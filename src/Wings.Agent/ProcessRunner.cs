using System.Diagnostics;

namespace Wings.Agent;

public sealed record ProcessResult(int ExitCode, TimeSpan Duration);

/// <summary>Runs a child process, streams its output to the job log and kills the whole process tree on cancellation.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> args, string workingDirectory,
        IReadOnlyDictionary<string, string> environment, Action<string, string> log, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var (k, v) in environment) psi.Environment[k] = v;

        log("info", $"$ {fileName} {string.Join(' ', psi.ArgumentList.Select(Quote))}");
        var started = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) log("info", Clean(e.Data)); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log("warn", Clean(e.Data)); };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception ex) { throw new PreparationException($"Could not start '{fileName}': {ex.Message}. Is it installed and on PATH?"); }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            log("warn", $"Killed {fileName} (process tree) after cancellation or timeout.");
            throw;
        }
        process.WaitForExit(); // flush redirected output
        return new ProcessResult(process.ExitCode, started.Elapsed);
    }

    /// <summary>Removes terminal colour and cursor-movement codes so logs read cleanly in the browser.</summary>
    private static string Clean(string line) => System.Text.RegularExpressions.Regex.Replace(line, @"\x1B\[[0-9;?]*[A-Za-z]", "");

    private static string Quote(string a) => a.Contains(' ') ? $"\"{a}\"" : a;
}

/// <summary>Preparation failed (tooling, download, restore, desktop session). Reported as an infrastructure outcome, not a test failure.</summary>
public sealed class PreparationException(string message) : Exception(message);
