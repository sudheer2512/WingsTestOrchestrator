using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Domain.Execution;
using Wings.Domain.Orchestration;

namespace Wings.Application.Orchestration;

public sealed record EnvironmentVariableDto(string Name, string? Value, bool IsSecret, bool HasValue, string? Description);

public sealed record EnvironmentDto(
    Guid Id, string Code, string Name, EnvironmentKind Kind, string? Description, bool IsProduction, IReadOnlyList<Guid> AllowedDestructiveTestCaseIds,
    string? BaseUrl, string? ApplicationBuildVersion, string? DatabaseReference, string? TenantCompany, string? AuthenticationNotes, string? Dependencies,
    IReadOnlyList<string> AgentPools, string? HealthCheckUrl, EnvironmentHealth Health, string? HealthMessage, DateTimeOffset? LastHealthCheckAt,
    bool InMaintenance, string? ResetProcedure, IReadOnlyList<EnvironmentVariableDto> Variables, int ActiveRuns, long Version, DateTimeOffset CreatedAt, string CreatedBy);

/// <summary>For a secret, a null <see cref="Value"/> keeps the stored value; an empty string clears it.</summary>
public sealed record EnvironmentVariableUpsert(string Name, string? Value, bool IsSecret, string? Description);

public sealed record EnvironmentUpsert
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public EnvironmentKind Kind { get; init; } = EnvironmentKind.QA;
    public string? Description { get; init; }
    public bool IsProduction { get; init; }
    public IReadOnlyList<Guid> AllowedDestructiveTestCaseIds { get; init; } = [];
    public string? BaseUrl { get; init; }
    public string? ApplicationBuildVersion { get; init; }
    public string? DatabaseReference { get; init; }
    public string? TenantCompany { get; init; }
    public string? AuthenticationNotes { get; init; }
    public string? Dependencies { get; init; }
    public IReadOnlyList<string> AgentPools { get; init; } = [];
    public string? HealthCheckUrl { get; init; }
    public bool InMaintenance { get; init; }
    public string? ResetProcedure { get; init; }
    public IReadOnlyList<EnvironmentVariableUpsert> Variables { get; init; } = [];
    public long? Version { get; init; }
}

public sealed record EnvironmentComparisonRow(string Item, string? Left, string? Right, bool Differs);

/// <summary>Everything a dispatched job needs from its environment, with secrets decrypted (never sent to browsers).</summary>
public sealed record ResolvedEnvironment(TestEnvironment Environment, IReadOnlyDictionary<string, string> Variables, IReadOnlyList<string> SecretNames);

public interface IEnvironmentService
{
    Task<IReadOnlyList<EnvironmentDto>> ListAsync(CancellationToken ct = default);
    Task<EnvironmentDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<EnvironmentDto> CreateAsync(EnvironmentUpsert request, CancellationToken ct = default);
    Task<EnvironmentDto> UpdateAsync(Guid id, EnvironmentUpsert request, CancellationToken ct = default);
    Task<EnvironmentDto> CloneAsync(Guid id, string code, string name, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<EnvironmentComparisonRow>> CompareAsync(Guid left, Guid right, CancellationToken ct = default);
    Task<EnvironmentDto> CheckHealthAsync(Guid id, CancellationToken ct = default);
    Task<int> CheckAllAsync(CancellationToken ct = default);
    Task<ResolvedEnvironment> ResolveAsync(Guid id, CancellationToken ct = default);
}

public sealed class EnvironmentService(IWingsDbContext db, ISecretProtector secrets, IHttpClientFactory http, TimeProvider clock, IChangeNotifier notifier,
    Quality.INotificationPublisher notifications) : IEnvironmentService
{
    public const string Mask = "••••••";

    public async Task<IReadOnlyList<EnvironmentDto>> ListAsync(CancellationToken ct = default)
    {
        var envs = await db.TestEnvironments.AsNoTracking().Include(e => e.Variables).OrderBy(e => e.Kind).ThenBy(e => e.Name).ToListAsync(ct);
        var active = await ActiveRunsAsync(ct);
        return envs.Select(e => ToDto(e, active.GetValueOrDefault(e.Id))).ToList();
    }

    public async Task<EnvironmentDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var e = await db.TestEnvironments.AsNoTracking().Include(x => x.Variables).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Environment", id);
        return ToDto(e, (await ActiveRunsAsync(ct)).GetValueOrDefault(id));
    }

    public async Task<EnvironmentDto> CreateAsync(EnvironmentUpsert r, CancellationToken ct = default)
    {
        Validate(r);
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.TestEnvironments.IgnoreQueryFilters().AnyAsync(e => e.Code == code, ct)) throw new ValidationException("code", $"Environment code '{code}' is already used.");
        var env = new TestEnvironment { Code = code };
        Apply(env, r);
        ApplyVariables(env, r.Variables);
        db.TestEnvironments.Add(env);
        await db.SaveChangesAsync(ct);
        return await GetAsync(env.Id, ct);
    }

    public async Task<EnvironmentDto> UpdateAsync(Guid id, EnvironmentUpsert r, CancellationToken ct = default)
    {
        Validate(r);
        var env = await db.TestEnvironments.Include(e => e.Variables).FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Environment", id);
        if (r.Version is { } v) db.SetOriginalVersion(env, v);
        Apply(env, r);
        ApplyVariables(env, r.Variables);
        db.Touch(env);
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("environmentsChanged", new { environmentId = id }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<EnvironmentDto> CloneAsync(Guid id, string code, string name, CancellationToken ct = default)
    {
        new Validator().Required(code, "code", 50).Required(name, "name", 200).ThrowIfInvalid();
        var src = await db.TestEnvironments.AsNoTracking().Include(e => e.Variables).FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Environment", id);
        var upper = code.Trim().ToUpperInvariant();
        if (await db.TestEnvironments.IgnoreQueryFilters().AnyAsync(e => e.Code == upper, ct)) throw new ValidationException("code", $"Environment code '{upper}' is already used.");
        var copy = new TestEnvironment
        {
            Code = upper, Name = name.Trim(), Kind = src.Kind == EnvironmentKind.Production ? EnvironmentKind.Preproduction : src.Kind, Description = src.Description,
            IsProduction = false, BaseUrl = src.BaseUrl, ApplicationBuildVersion = src.ApplicationBuildVersion, DatabaseReference = src.DatabaseReference,
            TenantCompany = src.TenantCompany, AuthenticationNotes = src.AuthenticationNotes, Dependencies = src.Dependencies, AgentPools = src.AgentPools,
            HealthCheckUrl = src.HealthCheckUrl, ResetProcedure = src.ResetProcedure,
            Variables = src.Variables.Select(v => new EnvironmentVariable { Name = v.Name, Value = v.Value, ProtectedValue = v.ProtectedValue, IsSecret = v.IsSecret, Description = v.Description }).ToList(),
        };
        db.TestEnvironments.Add(copy);
        db.PendingAuditSummary = $"Cloned from {src.Code}";
        await db.SaveChangesAsync(ct);
        return await GetAsync(copy.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var env = await db.TestEnvironments.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Environment", id);
        if ((await ActiveRunsAsync(ct)).GetValueOrDefault(id) > 0) throw new ConflictException("Runs are active against this environment.");
        env.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EnvironmentComparisonRow>> CompareAsync(Guid left, Guid right, CancellationToken ct = default)
    {
        var l = await db.TestEnvironments.AsNoTracking().Include(e => e.Variables).FirstOrDefaultAsync(e => e.Id == left, ct) ?? throw new NotFoundException("Environment", left);
        var r = await db.TestEnvironments.AsNoTracking().Include(e => e.Variables).FirstOrDefaultAsync(e => e.Id == right, ct) ?? throw new NotFoundException("Environment", right);
        var rows = new List<EnvironmentComparisonRow>();
        void Add(string item, string? a, string? b) => rows.Add(new(item, a, b, !string.Equals(a ?? "", b ?? "", StringComparison.Ordinal)));
        Add("Kind", l.Kind.ToString(), r.Kind.ToString());
        Add("Production", l.IsProduction ? "Yes" : "No", r.IsProduction ? "Yes" : "No");
        Add("Base URL", l.BaseUrl, r.BaseUrl);
        Add("Application build", l.ApplicationBuildVersion, r.ApplicationBuildVersion);
        Add("Database", l.DatabaseReference, r.DatabaseReference);
        Add("Tenant / company", l.TenantCompany, r.TenantCompany);
        Add("Agent pools", l.AgentPools, r.AgentPools);
        Add("Health check URL", l.HealthCheckUrl, r.HealthCheckUrl);
        foreach (var name in l.Variables.Select(v => v.Name).Union(r.Variables.Select(v => v.Name)).Order())
        {
            var a = l.Variables.FirstOrDefault(v => v.Name == name);
            var b = r.Variables.FirstOrDefault(v => v.Name == name);
            if (a?.IsSecret == true || b?.IsSecret == true)
            {
                // Secrets are compared, never shown.
                var same = a is not null && b is not null && Reveal(a) == Reveal(b);
                rows.Add(new($"${name}", a is null ? null : $"{Mask} (secret)", b is null ? null : $"{Mask} (secret)", !same));
            }
            else Add($"${name}", a?.Value, b?.Value);
        }
        return rows;
    }

    public async Task<EnvironmentDto> CheckHealthAsync(Guid id, CancellationToken ct = default)
    {
        var env = await db.TestEnvironments.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Environment", id);
        var before = env.Health;
        await ProbeAsync(env, ct);
        await db.SaveChangesAsync(ct);
        await NotifyUnavailableAsync([(env, before)], ct);
        await notifier.NotifyAsync("environmentsChanged", new { environmentId = id }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<int> CheckAllAsync(CancellationToken ct = default)
    {
        var envs = await db.TestEnvironments.Where(e => e.HealthCheckUrl != null).ToListAsync(ct);
        var before = envs.Select(e => (e, e.Health)).ToList();
        foreach (var env in envs) await ProbeAsync(env, ct);
        if (envs.Count > 0)
        {
            db.PendingAuditAction = "HealthChecked";
            await db.SaveChangesAsync(ct);
            await notifier.NotifyAsync("environmentsChanged", new { }, ct);
            await NotifyUnavailableAsync(before, ct);
        }
        return envs.Count;
    }

    private async Task NotifyUnavailableAsync(IEnumerable<(TestEnvironment Env, EnvironmentHealth Before)> checkedEnvs, CancellationToken ct)
    {
        foreach (var (env, before) in checkedEnvs.Where(x => x.Env.Health == EnvironmentHealth.Unavailable && x.Before != EnvironmentHealth.Unavailable && !x.Env.InMaintenance))
            await notifications.PublishAsync(new Quality.NotificationMessage(Quality.NotificationEvents.EnvironmentUnavailable,
                env.IsProduction ? Quality.NotificationSeverity.Critical : Quality.NotificationSeverity.Warning,
                $"Environment {env.Name} is unavailable", $"Health check failed: {env.HealthMessage}. Runs targeting it will not start until it recovers (or are started with \"ignore environment health\").",
                "/environments", null, new Dictionary<string, string> { ["environment"] = env.Code }), ct);
    }

    private async Task ProbeAsync(TestEnvironment env, CancellationToken ct)
    {
        if (env.HealthCheckUrl is not { } url) return;
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = http.CreateClient("health");
            client.Timeout = TimeSpan.FromSeconds(10);
            using var response = await client.GetAsync(url, ct);
            var ms = sw.ElapsedMilliseconds;
            (env.Health, env.HealthMessage) = response.IsSuccessStatusCode
                ? (ms > 3000 ? EnvironmentHealth.Degraded : EnvironmentHealth.Available, $"HTTP {(int)response.StatusCode} in {ms} ms")
                : (EnvironmentHealth.Unavailable, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} in {ms} ms");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            (env.Health, env.HealthMessage) = (EnvironmentHealth.Unavailable, ex is TaskCanceledException ? "Timed out after 10 s" : ex.Message);
        }
        env.LastHealthCheckAt = clock.GetUtcNow();
    }

    public async Task<ResolvedEnvironment> ResolveAsync(Guid id, CancellationToken ct = default)
    {
        var env = await db.TestEnvironments.AsNoTracking().Include(e => e.Variables).FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Environment", id);
        var vars = new Dictionary<string, string>();
        if (env.BaseUrl is { } baseUrl) vars["WINGS_BASE_URL"] = baseUrl;
        vars["WINGS_ENVIRONMENT"] = env.Code;
        if (env.ApplicationBuildVersion is { } build) vars["WINGS_APP_BUILD"] = build;
        foreach (var v in env.Variables) if (Reveal(v) is { } value) vars[v.Name] = value;
        return new ResolvedEnvironment(env, vars, env.Variables.Where(v => v.IsSecret).Select(v => v.Name).ToList());
    }

    private string? Reveal(EnvironmentVariable v) => v.IsSecret ? v.ProtectedValue is { } p ? secrets.Unprotect(p) : null : v.Value;

    private async Task<Dictionary<Guid, int>> ActiveRunsAsync(CancellationToken ct)
        => await db.ExecutionRuns.Where(r => r.EnvironmentId != null && (r.Status == RunStatus.Queued || r.Status == RunStatus.Running))
            .GroupBy(r => r.EnvironmentId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);

    private static EnvironmentDto ToDto(TestEnvironment e, int active) => new(
        e.Id, e.Code, e.Name, e.Kind, e.Description, e.IsProduction, ParseIds(e.AllowedDestructiveTestCaseIds), e.BaseUrl, e.ApplicationBuildVersion,
        e.DatabaseReference, e.TenantCompany, e.AuthenticationNotes, e.Dependencies, e.Pools, e.HealthCheckUrl, e.Health, e.HealthMessage, e.LastHealthCheckAt,
        e.InMaintenance, e.ResetProcedure,
        e.Variables.OrderBy(v => v.Name).Select(v => new EnvironmentVariableDto(v.Name, v.IsSecret ? null : v.Value, v.IsSecret, v.IsSecret ? v.ProtectedValue is not null : v.Value is not null, v.Description)).ToList(),
        active, e.Version, e.CreatedAt, e.CreatedBy);

    internal static IReadOnlyList<Guid> ParseIds(string? csv) => (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();

    private static void Validate(EnvironmentUpsert r)
    {
        var v = new Validator().Required(r.Code, "code", 50).Required(r.Name, "name", 200)
            .Must(r.Code is null || Regex.IsMatch(r.Code.Trim(), "^[A-Za-z0-9_-]+$"), "code", "Code may contain letters, digits, '-' and '_' only.")
            .Must(r.BaseUrl is null || Uri.TryCreate(r.BaseUrl, UriKind.Absolute, out _), "baseUrl", "Base URL must be absolute.")
            .Must(r.HealthCheckUrl is null || (Uri.TryCreate(r.HealthCheckUrl, UriKind.Absolute, out var h) && h.Scheme is "http" or "https"), "healthCheckUrl", "Health check URL must be an absolute http(s) URL.")
            .Must(r.Kind != EnvironmentKind.Production || r.IsProduction, "isProduction", "Production environments must be flagged as production.")
            .Must(r.Variables.Select(x => x.Name).Distinct().Count() == r.Variables.Count, "variables", "Variable names must be unique.");
        foreach (var x in r.Variables)
            v.Must(Regex.IsMatch(x.Name ?? "", "^[A-Z][A-Z0-9_]{0,63}$"), "variables", $"'{x.Name}' must be UPPER_SNAKE_CASE.");
        v.ThrowIfInvalid();
    }

    private static void Apply(TestEnvironment e, EnvironmentUpsert r)
    {
        e.Name = r.Name.Trim(); e.Kind = r.Kind; e.Description = r.Description.Clean(); e.IsProduction = r.IsProduction || r.Kind == EnvironmentKind.Production;
        e.AllowedDestructiveTestCaseIds = r.AllowedDestructiveTestCaseIds.Count == 0 ? null : string.Join(',', r.AllowedDestructiveTestCaseIds.Distinct());
        e.BaseUrl = r.BaseUrl.Clean(); e.ApplicationBuildVersion = r.ApplicationBuildVersion.Clean(); e.DatabaseReference = r.DatabaseReference.Clean();
        e.TenantCompany = r.TenantCompany.Clean(); e.AuthenticationNotes = r.AuthenticationNotes.Clean(); e.Dependencies = r.Dependencies.Clean();
        e.AgentPools = r.AgentPools.Count == 0 ? null : string.Join(',', r.AgentPools.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct());
        e.HealthCheckUrl = r.HealthCheckUrl.Clean(); e.InMaintenance = r.InMaintenance; e.ResetProcedure = r.ResetProcedure.Clean();
        if (e.HealthCheckUrl is null) { e.Health = EnvironmentHealth.Unknown; e.HealthMessage = null; }
    }

    private void ApplyVariables(TestEnvironment env, IReadOnlyList<EnvironmentVariableUpsert> incoming)
    {
        foreach (var gone in env.Variables.Where(v => incoming.All(i => i.Name != v.Name)).ToList()) env.Variables.Remove(gone);
        foreach (var i in incoming)
        {
            var v = env.Variables.FirstOrDefault(x => x.Name == i.Name);
            if (v is null) env.Variables.Add(v = new EnvironmentVariable { EnvironmentId = env.Id, Name = i.Name });
            v.Description = i.Description.Clean();
            if (i.IsSecret)
            {
                if (!v.IsSecret && i.Value is null && v.Value is { } plain) v.ProtectedValue = secrets.Protect(plain); // converting plain → secret keeps the value
                else if (i.Value is not null) v.ProtectedValue = i.Value.Length == 0 ? null : secrets.Protect(i.Value);
                v.Value = null;
            }
            else
            {
                v.Value = i.Value;
                v.ProtectedValue = null;
            }
            v.IsSecret = i.IsSecret;
        }
    }
}
