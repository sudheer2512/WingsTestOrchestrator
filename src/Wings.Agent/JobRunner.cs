using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Wings.Agent.Contracts;
using Wings.Agent.Executors;

namespace Wings.Agent;

/// <summary>
/// Executes one assigned job: download and verify the package, extract it into a private work directory, run the matching
/// executor, then send results, evidence and the completion status. All calls for a job go out in order through one
/// reporter so the server never sees concurrent updates for the same job.
/// </summary>
public sealed class JobRunner(JobAssignment job, AgentOptions options, OrchestratorClient client, IReadOnlyList<ITestExecutor> executors, ILogger log)
{
    private readonly ConcurrentQueue<LogLine> _lines = new();
    private readonly SemaphoreSlim _send = new(1, 1);

    public async Task RunAsync(CancellationToken cancel)
    {
        var work = Path.Combine(options.WorkDirectory, job.JobId.ToString("N"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(job.TimeoutMinutes));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);
        using var flushStop = new CancellationTokenSource();
        var flusher = FlushLoopAsync(flushStop.Token);
        var completion = new JobCompletion(JobCompletions.Completed, null, null);
        try
        {
            await Send(() => client.StartAsync(job.JobId, CancellationToken.None));
            Log("info", $"Job {job.JobId} attempt {job.Attempt}: {job.TestIdentifiers.Count} test(s) from {job.PackageName} v{job.PackageVersionNumber} ({job.Framework}) on {Environment.MachineName}");

            var executor = executors.FirstOrDefault(e => e.Frameworks.Contains(job.Framework))
                           ?? throw new PreparationException($"This agent has no executor for {job.Framework}.");
            if (job.Framework == "FLAUI" && !Capabilities.HasInteractiveDesktop())
                throw new PreparationException("FlaUI tests need an interactive Windows desktop session; this agent is running without one (e.g. as a service in session 0).");

            Directory.CreateDirectory(work);
            var package = await DownloadAsync(work, linked.Token);
            var root = Path.Combine(work, "package");
            Extract(package, root);
            var output = Directory.CreateDirectory(Path.Combine(work, "output")).FullName;
            var artifacts = Directory.CreateDirectory(Path.Combine(work, "artifacts")).FullName;
            var env = new Dictionary<string, string>(options.Variables);
            foreach (var (k, v) in job.Variables) env[k] = v;
            env["WINGS_ARTIFACTS_DIR"] = artifacts;
            env["WINGS_RUN_ID"] = job.RunId.ToString();
            env["WINGS_JOB_ID"] = job.JobId.ToString();
            // Reserved test data, per-test parameter sets and environment details for the tests to read.
            var contextFile = Path.Combine(work, "wings-context.json");
            await File.WriteAllTextAsync(contextFile, job.ContextJson, linked.Token);
            env["WINGS_CONTEXT_FILE"] = contextFile;

            var started = Stopwatch.StartNew();
            var result = await executor.RunAsync(new JobContext
            {
                Job = job, Options = options, PackageDirectory = root, OutputDirectory = output, ArtifactsDirectory = artifacts, Environment = env, Log = Log,
            }, linked.Token);
            Log("info", $"Runner finished in {started.Elapsed.TotalSeconds:F1}s with exit code {result.ExitCode}; {result.Results.Count} result(s), {result.Artifacts.Count} artifact(s).");

            foreach (var batch in result.Results.Select(MaskResult).Chunk(200)) await Send(() => client.ResultsAsync(job.JobId, new ResultBatch(batch), CancellationToken.None));
            foreach (var a in result.Artifacts.DistinctBy(a => a.Path))
            {
                try { MaskTextFile(a); await Send(() => client.UploadArtifactAsync(job.JobId, a.Path, a.Kind, a.ContentType, a.TestIdentifier, CancellationToken.None)); }
                catch (Exception ex) when (ex is not LeaseLostException) { Log("warn", $"Could not upload {Path.GetFileName(a.Path)}: {ex.Message}"); }
            }
            completion = new JobCompletion(JobCompletions.Completed, result.Error, result.ExitCode);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            completion = new JobCompletion(JobCompletions.TimedOut, $"Killed after the {job.TimeoutMinutes}-minute job timeout.", null);
        }
        catch (OperationCanceledException)
        {
            completion = new JobCompletion(JobCompletions.Cancelled, "Cancelled on request.", null);
        }
        catch (LeaseLostException ex)
        {
            log.LogWarning("Job {JobId}: lease lost ({Message}); abandoning without reporting", job.JobId, ex.Message);
            await flushStop.CancelAsync();
            Cleanup(work);
            return;
        }
        catch (Exception ex)
        {
            Log("error", ex is PreparationException ? ex.Message : ex.ToString());
            completion = new JobCompletion(JobCompletions.InfrastructureError, ex.Message, null);
        }

        KillLeftovers();
        await flushStop.CancelAsync();
        try { await flusher; } catch (OperationCanceledException) { }
        try
        {
            await FlushAsync();
            await Send(() => client.CompleteAsync(job.JobId, completion, CancellationToken.None));
            log.LogInformation("Job {JobId} finished: {Status}", job.JobId, completion.Status);
        }
        catch (Exception ex) { log.LogError(ex, "Job {JobId}: could not report completion; the server will expire the lease", job.JobId); }
        Cleanup(work);
    }

    private void Log(string level, string message)
    {
        message = Mask(message)!;
        _lines.Enqueue(new LogLine(DateTimeOffset.UtcNow, level, message));
        log.Log(level == "error" ? LogLevel.Error : level == "warn" ? LogLevel.Warning : LogLevel.Information, "[{Job}] {Message}", job.JobId.ToString()[..8], message);
    }

    /// <summary>Secret values from the environment, longest first so overlapping values are fully hidden. Short values are not masked (too many false hits).</summary>
    private readonly string[] _secrets = job.SecretNames.Select(n => job.Variables.GetValueOrDefault(n)).Where(v => v is { Length: >= 4 }).Select(v => v!).OrderByDescending(v => v.Length).ToArray();

    private string? Mask(string? text)
    {
        if (text is null || _secrets.Length == 0) return text;
        foreach (var s in _secrets) text = text.Replace(s, "***", StringComparison.Ordinal);
        return text;
    }

    private TestResultReport MaskResult(TestResultReport r) => _secrets.Length == 0 ? r
        : r with { ErrorMessage = Mask(r.ErrorMessage), StackTrace = Mask(r.StackTrace), Output = Mask(r.Output) };

    /// <summary>Text evidence (reports, logs) is rewritten with secrets masked before upload; binary evidence cannot be scanned.</summary>
    private void MaskTextFile(ArtifactFile a)
    {
        if (_secrets.Length == 0 || a.Kind is not (ArtifactKinds.Report or ArtifactKinds.Log) || new FileInfo(a.Path).Length > 20 * 1024 * 1024) return;
        var text = File.ReadAllText(a.Path);
        var masked = Mask(text)!;
        if (!ReferenceEquals(text, masked) && text != masked) File.WriteAllText(a.Path, masked);
    }

    private async Task FlushLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct);
            try { await FlushAsync(); } catch (LeaseLostException) { return; } catch (Exception ex) { log.LogWarning("Log upload failed: {Message}", ex.Message); }
        }
    }

    private async Task FlushAsync()
    {
        var batch = new List<LogLine>();
        while (batch.Count < 1000 && _lines.TryDequeue(out var l)) batch.Add(l);
        if (batch.Count > 0) await Send(() => client.LogsAsync(job.JobId, new LogBatch(batch), CancellationToken.None));
        if (!_lines.IsEmpty) await FlushAsync();
    }

    private async Task Send(Func<Task> call)
    {
        await _send.WaitAsync();
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try { await call(); return; }
                catch (HttpRequestException ex) when (attempt < 4 && ex.StatusCode is null or >= System.Net.HttpStatusCode.InternalServerError)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2)); // transient network or server error
                }
            }
        }
        finally { _send.Release(); }
    }

    private async Task<string> DownloadAsync(string work, CancellationToken ct)
    {
        var target = Path.Combine(work, Path.GetFileName(job.PackageFileName) is { Length: > 0 } n ? n : "package.bin");
        Log("info", $"Downloading package ({job.PackageSha256[..12]}…)");
        await client.DownloadPackageAsync(job.PackageVersionId, target, ct);
        await using var s = File.OpenRead(target);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(s, ct));
        if (!string.Equals(hash, job.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new PreparationException($"Package hash mismatch: expected {job.PackageSha256}, got {hash}. The artifact was not executed.");
        return target;
    }

    /// <summary>Extracts with the same path rules the server enforced at upload (no traversal, no absolute paths).</summary>
    private static void Extract(string package, string root)
    {
        Directory.CreateDirectory(root);
        var full = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!package.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { File.Copy(package, Path.Combine(root, Path.GetFileName(package))); return; }
        using var zip = ZipFile.OpenRead(package);
        var names = zip.Entries.Where(e => e.Name.Length > 0).Select(e => e.FullName.Replace('\\', '/')).ToList();
        var prefix = names.Count > 0 && names.All(n => n.Contains('/')) && names.Select(n => n[..n.IndexOf('/')]).Distinct().Count() == 1 ? names[0][..(names[0].IndexOf('/') + 1)] : "";
        foreach (var e in zip.Entries.Where(e => e.Name.Length > 0))
        {
            var rel = e.FullName.Replace('\\', '/');
            if (rel.StartsWith(prefix, StringComparison.Ordinal)) rel = rel[prefix.Length..];
            var dest = Path.GetFullPath(Path.Combine(root, rel));
            if (!dest.StartsWith(full, StringComparison.Ordinal)) throw new PreparationException($"Unsafe path in package: {e.FullName}");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, overwrite: true);
        }
    }

    private void KillLeftovers()
    {
        foreach (var name in options.CleanupProcessNames)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(entireProcessTree: true); Log("warn", $"Killed leftover process {name} ({p.Id})."); }
                catch (Exception ex) { Log("warn", $"Could not kill {name} ({p.Id}): {ex.Message}"); }
                finally { p.Dispose(); }
            }
    }

    private void Cleanup(string work)
    {
        if (options.KeepWorkDirectories) return;
        try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); }
        catch (Exception ex) { log.LogWarning("Could not delete {Dir}: {Message}", work, ex.Message); }
    }
}
