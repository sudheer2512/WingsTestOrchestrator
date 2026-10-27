using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cronos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Application.Execution;
using Wings.Domain.Orchestration;
using Wings.Domain.Testing;

namespace Wings.Application.Orchestration;

/// <summary>
/// Calendar settings. Simple triggers are turned into a cron expression: Hourly uses <see cref="Minute"/>; Daily, Weekly and
/// Monthly use <see cref="Time"/> ("HH:mm"); Weekly needs <see cref="DaysOfWeek"/> (0 = Sunday); Monthly needs <see cref="DayOfMonth"/>.
/// </summary>
public sealed record ScheduleUpsert
{
    public string Name { get; init; } = "";
    public bool IsEnabled { get; init; } = true;
    public TriggerType Trigger { get; init; }
    public string? CronExpression { get; init; }
    public string TimeZoneId { get; init; } = "UTC";
    public DateTimeOffset? RunOnceAt { get; init; }
    public int Minute { get; init; }
    public string? Time { get; init; }
    public IReadOnlyList<int> DaysOfWeek { get; init; } = [];
    public int? DayOfMonth { get; init; }
    public ScheduleTarget TargetType { get; init; }
    public Guid TargetId { get; init; }
    public Guid? EnvironmentId { get; init; }
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
    public int Priority { get; init; } = 50;
    public long? Version { get; init; }
}

public sealed record ScheduleDto(Guid Id, string Name, bool IsEnabled, TriggerType Trigger, string? CronExpression, string TimeZoneId, DateTimeOffset? RunOnceAt,
    ScheduleTarget TargetType, Guid TargetId, string? TargetName, Guid? EnvironmentId, string? EnvironmentName, IReadOnlyDictionary<string, string> Variables,
    int Priority, DateTimeOffset? NextRunAt, IReadOnlyList<DateTimeOffset> Upcoming, DateTimeOffset? LastTriggeredAt, Guid? LastRunId, string? LastError,
    string? WebhookTokenPrefix, string CreatedBy, long Version);

public sealed record WebhookTokenResult(ScheduleDto Schedule, string Token, string Url);

public sealed record TriggerRequest(string? BuildVersion = null, IReadOnlyDictionary<string, string>? Variables = null);

public interface IScheduleService
{
    Task<IReadOnlyList<ScheduleDto>> ListAsync(CancellationToken ct = default);
    Task<ScheduleDto> CreateAsync(ScheduleUpsert request, CancellationToken ct = default);
    Task<ScheduleDto> UpdateAsync(Guid id, ScheduleUpsert request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<WebhookTokenResult> RotateTokenAsync(Guid id, string baseUrl, CancellationToken ct = default);
    /// <summary>Runs the schedule now (manual trigger by a user).</summary>
    Task<IReadOnlyList<RunSummaryDto>> RunNowAsync(Guid id, CancellationToken ct = default);
    /// <summary>Called by CI/CD systems with the schedule's webhook token.</summary>
    Task<IReadOnlyList<RunSummaryDto>> TriggerAsync(Guid id, string token, TriggerRequest request, CancellationToken ct = default);
    /// <summary>Background: starts every due schedule once. Safe with several API instances.</summary>
    Task<int> RunDueAsync(CancellationToken ct = default);
}

public sealed class ScheduleService(IWingsDbContext db, IExecutionService runs, IPlanService plans, ICampaignService campaigns, ICurrentUser user,
    IPermissionService permissions, TimeProvider clock, IChangeNotifier notifier, ILogger<ScheduleService> log) : IScheduleService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ScheduleDto>> ListAsync(CancellationToken ct = default)
    {
        var all = await db.TestSchedules.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        var result = new List<ScheduleDto>();
        foreach (var s in all) result.Add(await ToDtoAsync(s, ct));
        return result;
    }

    public async Task<ScheduleDto> CreateAsync(ScheduleUpsert r, CancellationToken ct = default)
    {
        var s = new TestSchedule();
        await ApplyAsync(s, r, ct);
        db.TestSchedules.Add(s);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(s, ct);
    }

    public async Task<ScheduleDto> UpdateAsync(Guid id, ScheduleUpsert r, CancellationToken ct = default)
    {
        var s = await db.TestSchedules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Schedule", id);
        if (r.Version is { } v) db.SetOriginalVersion(s, v);
        await ApplyAsync(s, r, ct);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(s, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var s = await db.TestSchedules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Schedule", id);
        db.TestSchedules.Remove(s);
        await db.SaveChangesAsync(ct);
    }

    public async Task<WebhookTokenResult> RotateTokenAsync(Guid id, string baseUrl, CancellationToken ct = default)
    {
        var s = await db.TestSchedules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Schedule", id);
        if (s.Trigger != TriggerType.Webhook) throw new ConflictException("Only webhook-triggered schedules have a token.");
        var token = "wtt_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        s.WebhookTokenHash = Hash(token);
        s.WebhookTokenPrefix = token[..12];
        db.PendingAuditAction = "WebhookTokenRotated";
        await db.SaveChangesAsync(ct);
        return new WebhookTokenResult(await ToDtoAsync(s, ct), token, $"{baseUrl.TrimEnd('/')}/api/v1/triggers/schedules/{s.Id}");
    }

    public async Task<IReadOnlyList<RunSummaryDto>> RunNowAsync(Guid id, CancellationToken ct = default)
    {
        var s = await db.TestSchedules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Schedule", id);
        return await StartAsync(s, new RunOrigin(user.UserName, ScheduleId: s.Id), null, null, ct);
    }

    public async Task<IReadOnlyList<RunSummaryDto>> TriggerAsync(Guid id, string token, TriggerRequest request, CancellationToken ct = default)
    {
        var s = await db.TestSchedules.FirstOrDefaultAsync(x => x.Id == id, ct);
        // Same error for unknown schedule, wrong token and disabled schedule, so ids cannot be probed.
        if (s is null || s.Trigger != TriggerType.Webhook || !s.IsEnabled || s.WebhookTokenHash is null
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(s.WebhookTokenHash), Encoding.ASCII.GetBytes(Hash(token ?? ""))))
            throw new ForbiddenException("Invalid trigger.");
        if (request.Variables?.Keys.Any(k => !System.Text.RegularExpressions.Regex.IsMatch(k, "^[A-Z][A-Z0-9_]{0,63}$")) == true)
            throw new ValidationException("variables", "Variable names must be UPPER_SNAKE_CASE.");
        return await StartAsync(s, new RunOrigin($"webhook:{s.Name}", ScheduleId: s.Id, CheckPermissions: false), request.BuildVersion, request.Variables, ct);
    }

    public async Task<int> RunDueAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var due = await db.TestSchedules.AsNoTracking()
            .Where(s => s.IsEnabled && s.Trigger != TriggerType.Webhook && s.NextRunAt != null && s.NextRunAt <= now).ToListAsync(ct);
        var started = 0;
        foreach (var s in due)
        {
            var next = NextOccurrence(s, now);
            // Claim this occurrence atomically: only the instance that moves NextRunAt forward starts the run.
            var claimed = await db.TestSchedules.Where(x => x.Id == s.Id && x.NextRunAt == s.NextRunAt)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.NextRunAt, next).SetProperty(x => x.IsEnabled, s.Trigger != TriggerType.Once && s.IsEnabled), ct);
            if (claimed != 1) continue;
            try
            {
                await StartAsync(s, new RunOrigin($"schedule:{s.Name}", ScheduleId: s.Id, CheckPermissions: false), null, null, ct);
                started++;
            }
            catch (Exception ex) when (ex is AppException)
            {
                log.LogWarning("Schedule {Name} could not start a run: {Message}", s.Name, ex.Message);
                await db.TestSchedules.Where(x => x.Id == s.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LastError, ex.Message).SetProperty(x => x.LastTriggeredAt, now), ct);
            }
        }
        return started;
    }

    private async Task<IReadOnlyList<RunSummaryDto>> StartAsync(TestSchedule s, RunOrigin origin, string? build, IReadOnlyDictionary<string, string>? extraVars, CancellationToken ct)
    {
        var vars = JsonSerializer.Deserialize<Dictionary<string, string>>(s.VariablesJson ?? "{}") ?? [];
        foreach (var (k, v) in extraVars ?? new Dictionary<string, string>()) vars[k] = v;
        IReadOnlyList<RunSummaryDto> started;
        try
        {
            started = s.TargetType switch
            {
                ScheduleTarget.Plan => [await plans.ExecuteAsync(s.TargetId, new ExecutePlanRequest(s.EnvironmentId, build, vars, s.Priority), origin, ct)],
                ScheduleTarget.Campaign => await campaigns.ExecuteAsync(s.TargetId, origin, ct),
                _ => [await runs.StartAsync(await RequestFor(s, build, vars, ct), origin, ct)],
            };
        }
        catch (AppException ex)
        {
            await db.TestSchedules.Where(x => x.Id == s.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LastError, ex.Message).SetProperty(x => x.LastTriggeredAt, clock.GetUtcNow()), ct);
            throw;
        }
        var last = started.LastOrDefault()?.Id;
        await db.TestSchedules.Where(x => x.Id == s.Id).ExecuteUpdateAsync(u => u
            .SetProperty(x => x.LastTriggeredAt, clock.GetUtcNow()).SetProperty(x => x.LastRunId, last).SetProperty(x => x.LastError, (string?)null), ct);
        await notifier.NotifyAsync("schedulesChanged", new { scheduleId = s.Id }, ct);
        return started;
    }

    private async Task<StartRunRequest> RequestFor(TestSchedule s, string? build, Dictionary<string, string> vars, CancellationToken ct)
    {
        var request = new StartRunRequest { Name = $"Scheduled: {s.Name}", EnvironmentId = s.EnvironmentId, BuildVersion = build, Variables = vars, Priority = s.Priority };
        return s.TargetType switch
        {
            ScheduleTarget.Suite => request with { SuiteIds = [s.TargetId] },
            ScheduleTarget.Feature => request with { FeatureId = s.TargetId, IncludeSubfeatures = true },
            ScheduleTarget.Package => request with
            {
                PackageVersionId = await db.TestPackageVersions.Where(v => v.PackageId == s.TargetId && v.Status == PackageStatus.Published)
                    .OrderByDescending(v => v.VersionNumber).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct)
                    ?? throw new ConflictException("The package has no published version."),
            },
            _ => throw new ValidationException("targetType", "Unsupported target."),
        };
    }

    private async Task ApplyAsync(TestSchedule s, ScheduleUpsert r, CancellationToken ct)
    {
        var v = new Validator().Required(r.Name, "name", 200).Must(r.Priority is >= 0 and <= 100, "priority", "Priority must be between 0 and 100.");
        TimeZoneInfo? zone = null;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(r.TimeZoneId); } catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { v.Add("timeZoneId", $"Unknown time zone '{r.TimeZoneId}'."); }
        string? cron = null;
        try { cron = BuildCron(r); } catch (ValidationException ex) { foreach (var (k, msgs) in ex.Errors) foreach (var m in msgs) v.Add(k, m); }
        if (r.Trigger == TriggerType.Once && r.RunOnceAt is null) v.Add("runOnceAt", "Choose when to run.");
        var targetExists = r.TargetType switch
        {
            ScheduleTarget.Suite => await db.TestSuites.AnyAsync(x => x.Id == r.TargetId, ct),
            ScheduleTarget.Plan => await db.TestPlans.AnyAsync(x => x.Id == r.TargetId && !x.IsTemplate, ct),
            ScheduleTarget.Campaign => await db.TestCampaigns.AnyAsync(x => x.Id == r.TargetId, ct),
            ScheduleTarget.Feature => await db.Features.AnyAsync(x => x.Id == r.TargetId, ct),
            ScheduleTarget.Package => await db.TestPackages.AnyAsync(x => x.Id == r.TargetId, ct),
            _ => false,
        };
        if (!targetExists) v.Add("targetId", "The target does not exist.");
        if (r.EnvironmentId is { } envId)
        {
            var env = await db.TestEnvironments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == envId, ct);
            if (env is null) v.Add("environmentId", "Unknown environment.");
            // Schedules run unattended as the system, so the person creating one must be allowed to target production.
            else if (env.IsProduction && (user.UserId is not { } uid || !(await permissions.GetPermissionsAsync(uid, ct)).Contains(Security.Permissions.ProductionRuns)))
                v.Add("environmentId", "Scheduling runs against production needs the environments.production permission.");
        }
        v.ThrowIfInvalid();

        s.Name = r.Name.Trim(); s.IsEnabled = r.IsEnabled; s.Trigger = r.Trigger; s.CronExpression = cron; s.TimeZoneId = r.TimeZoneId; s.RunOnceAt = r.RunOnceAt;
        s.TargetType = r.TargetType; s.TargetId = r.TargetId; s.EnvironmentId = r.EnvironmentId; s.Priority = r.Priority;
        s.VariablesJson = r.Variables.Count == 0 ? null : JsonSerializer.Serialize(r.Variables, Json);
        s.NextRunAt = s.IsEnabled ? NextOccurrence(s, clock.GetUtcNow()) : null;
        _ = zone;
    }

    internal static string? BuildCron(ScheduleUpsert r)
    {
        (int H, int M) Time()
        {
            if (r.Time is null || !TimeOnly.TryParseExact(r.Time, "HH:mm", out var t)) throw new ValidationException("time", "Time must be HH:mm (24-hour).");
            return (t.Hour, t.Minute);
        }
        switch (r.Trigger)
        {
            case TriggerType.Once or TriggerType.Webhook: return null;
            case TriggerType.Hourly:
                if (r.Minute is < 0 or > 59) throw new ValidationException("minute", "Minute must be 0–59.");
                return $"{r.Minute} * * * *";
            case TriggerType.Daily: { var (h, m) = Time(); return $"{m} {h} * * *"; }
            case TriggerType.Weekly:
                {
                    var (h, m) = Time();
                    if (r.DaysOfWeek.Count == 0 || r.DaysOfWeek.Any(d => d is < 0 or > 6)) throw new ValidationException("daysOfWeek", "Choose one or more days (0 = Sunday … 6 = Saturday).");
                    return $"{m} {h} * * {string.Join(',', r.DaysOfWeek.Distinct().Order())}";
                }
            case TriggerType.Monthly:
                {
                    var (h, m) = Time();
                    if (r.DayOfMonth is not (>= 1 and <= 28)) throw new ValidationException("dayOfMonth", "Day of month must be 1–28 so it exists in every month.");
                    return $"{m} {h} {r.DayOfMonth} * *";
                }
            default:
                try { CronExpression.Parse(r.CronExpression ?? ""); }
                catch (CronFormatException ex) { throw new ValidationException("cronExpression", $"Invalid cron expression: {ex.Message}"); }
                return r.CronExpression!.Trim();
        }
    }

    internal static DateTimeOffset? NextOccurrence(TestSchedule s, DateTimeOffset after)
    {
        if (!s.IsEnabled) return null;
        if (s.Trigger == TriggerType.Webhook) return null;
        if (s.Trigger == TriggerType.Once) return s.RunOnceAt is { } at && (s.LastTriggeredAt is null || at > s.LastTriggeredAt) && at > after.AddMinutes(-1) ? at : null;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId);
        return CronExpression.Parse(s.CronExpression!).GetNextOccurrence(after, zone);
    }

    private static IEnumerable<DateTimeOffset> Upcoming(TestSchedule s, DateTimeOffset from, int count)
    {
        if (!s.IsEnabled || s.Trigger is TriggerType.Webhook or TriggerType.Once || s.CronExpression is null) yield break;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId);
        var cron = CronExpression.Parse(s.CronExpression);
        var t = from;
        for (var i = 0; i < count; i++)
        {
            if (cron.GetNextOccurrence(t, zone) is not { } next) yield break;
            yield return next;
            t = next;
        }
    }

    private async Task<ScheduleDto> ToDtoAsync(TestSchedule s, CancellationToken ct)
    {
        string? target = s.TargetType switch
        {
            ScheduleTarget.Suite => await db.TestSuites.Where(x => x.Id == s.TargetId).Select(x => x.Name).FirstOrDefaultAsync(ct),
            ScheduleTarget.Plan => await db.TestPlans.Where(x => x.Id == s.TargetId).Select(x => x.Name).FirstOrDefaultAsync(ct),
            ScheduleTarget.Campaign => await db.TestCampaigns.Where(x => x.Id == s.TargetId).Select(x => x.Name).FirstOrDefaultAsync(ct),
            ScheduleTarget.Feature => await db.Features.Where(x => x.Id == s.TargetId).Select(x => x.Name).FirstOrDefaultAsync(ct),
            ScheduleTarget.Package => await db.TestPackages.Where(x => x.Id == s.TargetId).Select(x => x.Name).FirstOrDefaultAsync(ct),
            _ => null,
        };
        var env = s.EnvironmentId is { } e ? await db.TestEnvironments.Where(x => x.Id == e).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        return new ScheduleDto(s.Id, s.Name, s.IsEnabled, s.Trigger, s.CronExpression, s.TimeZoneId, s.RunOnceAt, s.TargetType, s.TargetId, target, s.EnvironmentId, env,
            JsonSerializer.Deserialize<Dictionary<string, string>>(s.VariablesJson ?? "{}") ?? [], s.Priority, s.NextRunAt, Upcoming(s, clock.GetUtcNow(), 5).ToList(),
            s.LastTriggeredAt, s.LastRunId, s.LastError, s.WebhookTokenPrefix, s.CreatedBy, s.Version);
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
