using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Application.Security;
using Wings.Domain.Quality;

namespace Wings.Application.Quality;

public static class NotificationEvents
{
    public const string RunCompleted = "run.completed";
    public const string CriticalFailure = "run.criticalFailure";
    public const string HighFailureRate = "run.highFailureRate";
    public const string AgentOffline = "agent.offline";
    public const string EnvironmentUnavailable = "environment.unavailable";
    public const string GateFailed = "gate.failed";
    public const string ApprovalRequired = "approval.required";
    public const string DefectAssigned = "defect.assigned";
    public const string DefectRecurred = "defect.recurred";
    public const string BuildReceived = "build.received";
    public const string ReportDelivered = "report.delivered";

    public static readonly IReadOnlyList<(string Type, string Description)> All =
    [
        (RunCompleted, "A test run finished"),
        (CriticalFailure, "A P1 (business-critical) test failed"),
        (HighFailureRate, "A run's failure rate exceeded the configured threshold"),
        (AgentOffline, "An enabled execution agent stopped heartbeating"),
        (EnvironmentUnavailable, "A test environment failed its health check"),
        (GateFailed, "A release quality gate is failing"),
        (ApprovalRequired, "Something is waiting for review or sign-off"),
        (DefectAssigned, "A defect was assigned to you"),
        (DefectRecurred, "A resolved defect's failure happened again"),
        (BuildReceived, "A build was reported by CI with its change impact"),
        (ReportDelivered, "A scheduled report was delivered"),
    ];
}

public static class NotificationSeverity
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Critical = "Critical";
    public static int Rank(string? s) => s switch { Critical => 2, Warning => 1, _ => 0 };
    public static bool IsValid(string? s) => s is Info or Warning or Critical;
}

/// <summary>An event to publish. <paramref name="UserIds"/> are direct recipients (in-app) on top of everyone whose rule matches.</summary>
public sealed record NotificationMessage(string Type, string Severity, string Title, string? Body = null, string? Link = null, Guid? ProductId = null,
    IReadOnlyDictionary<string, string>? Data = null, IReadOnlyList<Guid>? UserIds = null);

public interface INotificationPublisher
{
    /// <summary>Stores the event, fills matching users' inboxes and queues channel/email deliveries. Saves pending changes of the context.</summary>
    Task<Guid> PublishAsync(NotificationMessage message, CancellationToken ct = default);
}

public static class NotificationTemplates
{
    public const string DefaultSubject = "[Wings QA] {{severity}}: {{title}}";
    public const string DefaultBody = "{{title}}\n\n{{body}}\n\nOpen in Wings Test Orchestrator: {{link}}";

    /// <summary>Replaces {{title}}, {{body}}, {{link}}, {{severity}}, {{type}}, {{time}} and {{data.key}} placeholders.</summary>
    public static string Render(string template, NotificationEvent ev, string? absoluteLink)
    {
        var data = string.IsNullOrEmpty(ev.DataJson) ? new Dictionary<string, string>() : JsonSerializer.Deserialize<Dictionary<string, string>>(ev.DataJson) ?? [];
        return Regex.Replace(template, @"\{\{\s*([\w.]+)\s*\}\}", m => m.Groups[1].Value switch
        {
            "title" => ev.Title,
            "body" => ev.Body ?? "",
            "link" => absoluteLink ?? "",
            "severity" => ev.Severity,
            "type" => ev.Type,
            "time" => ev.CreatedAt.ToString("yyyy-MM-dd HH:mm 'UTC'"),
            var k when k.StartsWith("data.") => data.GetValueOrDefault(k[5..]) ?? "",
            _ => m.Value,
        });
    }
}

public sealed class NotificationPublisher(IWingsDbContext db, IJobQueue jobs, IUserDirectory users, IChangeNotifier notifier, TimeProvider clock) : INotificationPublisher
{
    public const string DeliverJob = "notification.deliver";

    public async Task<Guid> PublishAsync(NotificationMessage m, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var ev = new NotificationEvent
        {
            Type = m.Type, Severity = m.Severity, Title = Trim(m.Title, 400), Body = m.Body, Link = m.Link, ProductId = m.ProductId, CreatedAt = now,
            DataJson = m.Data is { Count: > 0 } ? JsonSerializer.Serialize(m.Data) : null,
        };
        db.NotificationEvents.Add(ev);

        var rules = (await db.NotificationRules.AsNoTracking().Where(r => r.IsEnabled).ToListAsync(ct))
            .Where(r => Matches(r.EventTypes, m.Type) && (r.ProductId is null || r.ProductId == m.ProductId) && NotificationSeverity.Rank(m.Severity) >= NotificationSeverity.Rank(r.MinSeverity))
            .ToList();

        var recipients = new HashSet<Guid>(m.UserIds ?? []);
        recipients.UnionWith(rules.Where(r => r.UserId is not null).Select(r => r.UserId!.Value));
        if (m.ProductId is { } pid && recipients.Count > 0)
        {
            // Users restricted to other products do not hear about this one.
            var restricted = await db.UserProductAccess.Where(a => recipients.Contains(a.UserId)).GroupBy(a => a.UserId)
                .Select(g => new { g.Key, Allowed = g.Any(a => a.ProductId == pid) }).ToListAsync(ct);
            recipients.ExceptWith(restricted.Where(r => !r.Allowed).Select(r => r.Key));
        }
        var contacts = (await users.FindAsync(recipients, ct)).Where(u => u.IsActive).ToDictionary(u => u.Id);
        recipients.IntersectWith(contacts.Keys);
        db.UserNotifications.AddRange(recipients.Select(u => new UserNotification { EventId = ev.Id, UserId = u }));

        var deliveries = rules.Where(r => r.ChannelId is not null).Select(r => r.ChannelId!.Value).Distinct()
            .Select(c => new NotificationDelivery { EventId = ev.Id, ChannelId = c, CreatedAt = now }).ToList();
        foreach (var email in rules.Where(r => r.UserId is { } u && r.EmailUser && recipients.Contains(u)).Select(r => contacts[r.UserId!.Value].Email)
                     .Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase))
            deliveries.Add(new NotificationDelivery { EventId = ev.Id, EmailTo = email, CreatedAt = now });
        db.NotificationDeliveries.AddRange(deliveries);
        foreach (var d in deliveries) await jobs.EnqueueAsync(DeliverJob, new { deliveryId = d.Id }, ct);

        await db.SaveChangesAsync(ct);
        if (recipients.Count > 0) await notifier.NotifyAsync("notificationsChanged", new { userIds = recipients }, ct);
        return ev.Id;
    }

    public static bool Matches(string eventTypes, string type)
        => eventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(t => t == "*" || t == type || (t.EndsWith(".*") && type.StartsWith(t[..^1])));

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

// ---- Inbox, subscriptions and administration ----

public sealed record InboxItemDto(Guid EventId, string Type, string Severity, string Title, string? Body, string? Link, DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt, DateTimeOffset? AcknowledgedAt, string? AcknowledgedBy, DateTimeOffset? EscalatedAt);

public sealed record InboxDto(int Unread, IReadOnlyList<InboxItemDto> Items);

public sealed record ChannelDto(Guid Id, string Name, ChannelType Type, string? Recipients, bool HasUrl, string? UrlHint, bool HasSigningSecret, bool IsEnabled, long Version);

/// <summary>Url and SigningSecret are write-only: null keeps the stored value, "" clears it.</summary>
public sealed record ChannelUpsert(string Name, ChannelType Type, string? Recipients, string? Url, string? SigningSecret, bool IsEnabled = true, long? Version = null);

public sealed record RuleDto(Guid Id, string Name, IReadOnlyList<string> EventTypes, Guid? ProductId, string? ProductName, string MinSeverity, Guid? UserId,
    string? UserName, bool EmailUser, Guid? ChannelId, string? ChannelName, bool IsEnabled, long Version);

/// <summary>A rule targets either a user (in-app, optionally email) or a channel. Users without notifications.manage may only manage their own.</summary>
public sealed record RuleUpsert(string Name, IReadOnlyList<string> EventTypes, Guid? ProductId, string MinSeverity, Guid? UserId, bool EmailUser, Guid? ChannelId,
    bool IsEnabled = true, long? Version = null);

public sealed record EscalationDto(Guid Id, string Name, IReadOnlyList<string> EventTypes, int AfterMinutes, Guid ChannelId, string? ChannelName, bool IsEnabled, long Version);
public sealed record EscalationUpsert(string Name, IReadOnlyList<string> EventTypes, int AfterMinutes, Guid ChannelId, bool IsEnabled = true, long? Version = null);

public sealed record TemplateDto(string EventType, string Description, string Subject, string Body, bool IsCustomized);
public sealed record TemplateUpsert(string Subject, string Body);

public sealed record DeliveryDto(Guid Id, string Target, DeliveryStatus Status, int Attempts, string? Error, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, bool IsEscalation);
public sealed record EventLogDto(Guid Id, string Type, string Severity, string Title, string? Link, DateTimeOffset CreatedAt, int Recipients,
    DateTimeOffset? AcknowledgedAt, string? AcknowledgedBy, DateTimeOffset? EscalatedAt, IReadOnlyList<DeliveryDto> Deliveries);

public interface INotificationService
{
    Task<InboxDto> InboxAsync(bool unreadOnly, int take, CancellationToken ct = default);
    Task MarkReadAsync(IReadOnlyList<Guid>? eventIds, CancellationToken ct = default);
    Task AcknowledgeAsync(Guid eventId, CancellationToken ct = default);

    Task<IReadOnlyList<RuleDto>> RulesAsync(bool all, CancellationToken ct = default);
    Task<RuleDto> SaveRuleAsync(Guid? id, RuleUpsert request, CancellationToken ct = default);
    Task DeleteRuleAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ChannelDto>> ChannelsAsync(CancellationToken ct = default);
    Task<ChannelDto> SaveChannelAsync(Guid? id, ChannelUpsert request, CancellationToken ct = default);
    Task DeleteChannelAsync(Guid id, CancellationToken ct = default);
    Task<Guid> TestChannelAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<EscalationDto>> EscalationsAsync(CancellationToken ct = default);
    Task<EscalationDto> SaveEscalationAsync(Guid? id, EscalationUpsert request, CancellationToken ct = default);
    Task DeleteEscalationAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<TemplateDto>> TemplatesAsync(CancellationToken ct = default);
    Task<TemplateDto> SaveTemplateAsync(string eventType, TemplateUpsert request, CancellationToken ct = default);
    Task ResetTemplateAsync(string eventType, CancellationToken ct = default);

    Task<IReadOnlyList<EventLogDto>> EventLogAsync(int take, CancellationToken ct = default);
    /// <summary>Re-sends unacknowledged events to escalation channels once their policy's delay has passed. Returns the number escalated.</summary>
    Task<int> EscalateAsync(CancellationToken ct = default);
}

public sealed class NotificationService(IWingsDbContext db, ICurrentUser user, IPermissionService permissions, ISecretProtector secrets, IJobQueue jobs,
    IUserDirectory users, IChangeNotifier notifier, TimeProvider clock) : INotificationService
{
    private Guid Me => user.UserId ?? throw new ForbiddenException();

    public async Task<InboxDto> InboxAsync(bool unreadOnly, int take, CancellationToken ct = default)
    {
        var me = Me;
        var mine = db.UserNotifications.Where(n => n.UserId == me);
        var unread = await mine.CountAsync(n => n.ReadAt == null, ct);
        var q = from n in mine.Where(n => !unreadOnly || n.ReadAt == null)
                join e in db.NotificationEvents on n.EventId equals e.Id
                orderby e.CreatedAt descending
                select new InboxItemDto(e.Id, e.Type, e.Severity, e.Title, e.Body, e.Link, e.CreatedAt, n.ReadAt, e.AcknowledgedAt, e.AcknowledgedBy, e.EscalatedAt);
        return new InboxDto(unread, await q.Take(Math.Clamp(take, 1, 200)).ToListAsync(ct));
    }

    public async Task MarkReadAsync(IReadOnlyList<Guid>? eventIds, CancellationToken ct = default)
    {
        var me = Me;
        var q = db.UserNotifications.Where(n => n.UserId == me && n.ReadAt == null);
        if (eventIds is { Count: > 0 }) q = q.Where(n => eventIds.Contains(n.EventId));
        await q.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, clock.GetUtcNow()), ct);
        await notifier.NotifyAsync("notificationsChanged", new { userIds = new[] { me } }, ct);
    }

    public async Task AcknowledgeAsync(Guid eventId, CancellationToken ct = default)
    {
        var me = Me;
        var ev = await db.NotificationEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct) ?? throw new NotFoundException("Notification", eventId);
        if (!await db.UserNotifications.AnyAsync(n => n.EventId == eventId && n.UserId == me, ct) && !await CanManageAsync(ct))
            throw new ForbiddenException("Only recipients can acknowledge a notification.");
        if (ev.AcknowledgedAt is not null) return;
        ev.AcknowledgedAt = clock.GetUtcNow();
        ev.AcknowledgedBy = user.UserName;
        await db.UserNotifications.Where(n => n.EventId == eventId && n.UserId == me && n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, ev.AcknowledgedAt), ct);
        await db.SaveChangesAsync(ct);
    }

    // ---- rules (subscriptions) ----

    public async Task<IReadOnlyList<RuleDto>> RulesAsync(bool all, CancellationToken ct = default)
    {
        var q = db.NotificationRules.AsNoTracking();
        if (!all || !await CanManageAsync(ct)) { var me = Me; q = q.Where(r => r.UserId == me); }
        var rows = await q.OrderBy(r => r.Name).ToListAsync(ct);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var channels = await db.NotificationChannels.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var names = (await users.FindAsync(rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct(), ct)).ToDictionary(u => u.Id, u => u.UserName);
        return rows.Select(r => new RuleDto(r.Id, r.Name, Split(r.EventTypes), r.ProductId, r.ProductId is { } p ? products.GetValueOrDefault(p) : null, r.MinSeverity,
            r.UserId, r.UserId is { } u ? names.GetValueOrDefault(u) : null, r.EmailUser, r.ChannelId, r.ChannelId is { } c ? channels.GetValueOrDefault(c) : null,
            r.IsEnabled, r.Version)).ToList();
    }

    public async Task<RuleDto> SaveRuleAsync(Guid? id, RuleUpsert r, CancellationToken ct = default)
    {
        var manage = await CanManageAsync(ct);
        var targetUser = r.ChannelId is null ? r.UserId ?? Me : r.UserId;
        new Validator()
            .Required(r.Name, "name", 200)
            .Must(r.EventTypes.Count > 0, "eventTypes", "Choose at least one event type.")
            .Must(r.EventTypes.All(KnownType), "eventTypes", "Unknown event type.")
            .Must(NotificationSeverity.IsValid(r.MinSeverity), "minSeverity", "Use Info, Warning or Critical.")
            .Must((targetUser is null) != (r.ChannelId is null), "target", "A rule notifies either a user or a channel.")
            .ThrowIfInvalid();
        if (!manage && (r.ChannelId is not null || targetUser != Me))
            throw new ForbiddenException("You can only manage your own subscriptions.");
        if (r.ChannelId is { } cid && !await db.NotificationChannels.AnyAsync(c => c.Id == cid, ct)) throw new NotFoundException("Channel", cid);

        NotificationRule rule;
        if (id is { } existing)
        {
            rule = await db.NotificationRules.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Notification rule", existing);
            if (!manage && rule.UserId != Me) throw new ForbiddenException("You can only manage your own subscriptions.");
            if (r.Version is { } v) db.SetOriginalVersion(rule, v);
        }
        else db.NotificationRules.Add(rule = new NotificationRule());
        rule.Name = r.Name.Trim();
        rule.EventTypes = string.Join(',', r.EventTypes.Distinct());
        rule.ProductId = r.ProductId;
        rule.MinSeverity = r.MinSeverity;
        rule.UserId = targetUser;
        rule.EmailUser = targetUser is not null && r.EmailUser;
        rule.ChannelId = r.ChannelId;
        rule.IsEnabled = r.IsEnabled;
        await db.SaveChangesAsync(ct);
        return (await RulesAsync(true, ct)).Single(x => x.Id == rule.Id);
    }

    public async Task DeleteRuleAsync(Guid id, CancellationToken ct = default)
    {
        var rule = await db.NotificationRules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Notification rule", id);
        if (rule.UserId != Me && !await CanManageAsync(ct)) throw new ForbiddenException("You can only manage your own subscriptions.");
        db.NotificationRules.Remove(rule);
        await db.SaveChangesAsync(ct);
    }

    // ---- channels ----

    public async Task<IReadOnlyList<ChannelDto>> ChannelsAsync(CancellationToken ct = default)
        => (await db.NotificationChannels.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct)).Select(ToDto).ToList();

    private ChannelDto ToDto(NotificationChannel c)
    {
        string? hint = null;
        if (c.ProtectedUrl is not null)
            try { hint = Uri.TryCreate(secrets.Unprotect(c.ProtectedUrl), UriKind.Absolute, out var u) ? $"{u.Scheme}://{u.Host}/…" : "…"; }
            catch { hint = "(unreadable: re-enter the URL)"; }
        return new ChannelDto(c.Id, c.Name, c.Type, c.Recipients, c.ProtectedUrl is not null, hint, c.ProtectedSigningSecret is not null, c.IsEnabled, c.Version);
    }

    public async Task<ChannelDto> SaveChannelAsync(Guid? id, ChannelUpsert r, CancellationToken ct = default)
    {
        NotificationChannel c;
        if (id is { } existing)
        {
            c = await db.NotificationChannels.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Channel", existing);
            if (r.Version is { } v) db.SetOriginalVersion(c, v);
        }
        else db.NotificationChannels.Add(c = new NotificationChannel());

        var v2 = new Validator().Required(r.Name, "name", 200);
        if (r.Url is { Length: > 0 } url)
            v2.Must(Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "https" || (u.Scheme == "http" && u.IsLoopback)), "url", "Use an https URL (http is allowed only for localhost).");
        var hasUrl = r.Url is null ? c.ProtectedUrl is not null : r.Url.Length > 0;
        if (r.Type == ChannelType.Email)
            v2.Must(Split(r.Recipients ?? "").Count > 0 && Split(r.Recipients ?? "").All(e => Regex.IsMatch(e, @"^[^@\s]+@[^@\s]+$")), "recipients", "Enter one or more email addresses, comma separated.");
        else v2.Must(hasUrl, "url", "The channel's URL is required.");
        v2.ThrowIfInvalid();

        c.Name = r.Name.Trim();
        c.Type = r.Type;
        c.Recipients = r.Type == ChannelType.Email ? string.Join(", ", Split(r.Recipients!)) : null;
        if (r.Url is not null) c.ProtectedUrl = r.Url.Length == 0 || r.Type == ChannelType.Email ? null : secrets.Protect(r.Url);
        if (r.SigningSecret is not null) c.ProtectedSigningSecret = r.SigningSecret.Length == 0 ? null : secrets.Protect(r.SigningSecret);
        c.IsEnabled = r.IsEnabled;
        await db.SaveChangesAsync(ct);
        return ToDto(c);
    }

    public async Task DeleteChannelAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.NotificationChannels.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Channel", id);
        db.NotificationChannels.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> TestChannelAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.NotificationChannels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Channel", id);
        var now = clock.GetUtcNow();
        var ev = new NotificationEvent { Type = "channel.test", Severity = NotificationSeverity.Info, Title = $"Test message for channel \"{c.Name}\"", Body = $"Sent by {user.UserName}.", Link = "/notifications", CreatedAt = now, AcknowledgedAt = now, AcknowledgedBy = user.UserName };
        var delivery = new NotificationDelivery { EventId = ev.Id, ChannelId = c.Id, CreatedAt = now };
        db.NotificationEvents.Add(ev);
        db.NotificationDeliveries.Add(delivery);
        await jobs.EnqueueAsync(NotificationPublisher.DeliverJob, new { deliveryId = delivery.Id }, ct);
        await db.SaveChangesAsync(ct);
        return ev.Id;
    }

    // ---- escalation ----

    public async Task<IReadOnlyList<EscalationDto>> EscalationsAsync(CancellationToken ct = default)
    {
        var channels = await db.NotificationChannels.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return (await db.EscalationPolicies.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct))
            .Select(p => new EscalationDto(p.Id, p.Name, Split(p.EventTypes), p.AfterMinutes, p.ChannelId, channels.GetValueOrDefault(p.ChannelId), p.IsEnabled, p.Version)).ToList();
    }

    public async Task<EscalationDto> SaveEscalationAsync(Guid? id, EscalationUpsert r, CancellationToken ct = default)
    {
        new Validator().Required(r.Name, "name", 200)
            .Must(r.EventTypes.Count > 0 && r.EventTypes.All(KnownType), "eventTypes", "Choose known event types.")
            .Must(r.AfterMinutes is >= 1 and <= 7 * 24 * 60, "afterMinutes", "Escalate after 1 minute to 7 days.")
            .ThrowIfInvalid();
        if (!await db.NotificationChannels.AnyAsync(c => c.Id == r.ChannelId, ct)) throw new NotFoundException("Channel", r.ChannelId);
        EscalationPolicy p;
        if (id is { } existing)
        {
            p = await db.EscalationPolicies.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Escalation policy", existing);
            if (r.Version is { } v) db.SetOriginalVersion(p, v);
        }
        else db.EscalationPolicies.Add(p = new EscalationPolicy());
        p.Name = r.Name.Trim();
        p.EventTypes = string.Join(',', r.EventTypes.Distinct());
        p.AfterMinutes = r.AfterMinutes;
        p.ChannelId = r.ChannelId;
        p.IsEnabled = r.IsEnabled;
        await db.SaveChangesAsync(ct);
        return (await EscalationsAsync(ct)).Single(x => x.Id == p.Id);
    }

    public async Task DeleteEscalationAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.EscalationPolicies.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Escalation policy", id);
        db.EscalationPolicies.Remove(p);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> EscalateAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var policies = await db.EscalationPolicies.AsNoTracking().Where(p => p.IsEnabled).ToListAsync(ct);
        if (policies.Count == 0) return 0;
        var oldest = now.AddMinutes(-policies.Min(p => p.AfterMinutes));
        var candidates = await db.NotificationEvents.Where(e => e.AcknowledgedAt == null && e.EscalatedAt == null && e.CreatedAt <= oldest && e.CreatedAt >= now.AddDays(-7))
            .OrderBy(e => e.CreatedAt).Take(200).ToListAsync(ct);
        var escalated = 0;
        foreach (var ev in candidates)
        {
            var due = policies.Where(p => NotificationPublisher.Matches(p.EventTypes, ev.Type) && ev.CreatedAt <= now.AddMinutes(-p.AfterMinutes)).ToList();
            if (due.Count == 0) continue;
            ev.EscalatedAt = now;
            foreach (var channel in due.Select(p => p.ChannelId).Distinct())
            {
                var d = new NotificationDelivery { EventId = ev.Id, ChannelId = channel, CreatedAt = now, IsEscalation = true };
                db.NotificationDeliveries.Add(d);
                await jobs.EnqueueAsync(NotificationPublisher.DeliverJob, new { deliveryId = d.Id }, ct);
            }
            escalated++;
        }
        if (escalated > 0) await db.SaveChangesAsync(ct);
        return escalated;
    }

    // ---- templates and log ----

    public async Task<IReadOnlyList<TemplateDto>> TemplatesAsync(CancellationToken ct = default)
    {
        var custom = await db.NotificationTemplates.AsNoTracking().ToDictionaryAsync(t => t.EventType, ct);
        return NotificationEvents.All.Select(e => custom.TryGetValue(e.Type, out var t)
            ? new TemplateDto(e.Type, e.Description, t.Subject, t.Body, true)
            : new TemplateDto(e.Type, e.Description, NotificationTemplates.DefaultSubject, NotificationTemplates.DefaultBody, false)).ToList();
    }

    public async Task<TemplateDto> SaveTemplateAsync(string eventType, TemplateUpsert r, CancellationToken ct = default)
    {
        new Validator().Must(NotificationEvents.All.Any(e => e.Type == eventType), "eventType", "Unknown event type.")
            .Required(r.Subject, "subject", 400).Required(r.Body, "body", 8000).ThrowIfInvalid();
        var t = await db.NotificationTemplates.FirstOrDefaultAsync(x => x.EventType == eventType, ct);
        if (t is null) db.NotificationTemplates.Add(t = new NotificationTemplate { EventType = eventType });
        t.Subject = r.Subject.Trim();
        t.Body = r.Body;
        await db.SaveChangesAsync(ct);
        return (await TemplatesAsync(ct)).Single(x => x.EventType == eventType);
    }

    public async Task ResetTemplateAsync(string eventType, CancellationToken ct = default)
        => await db.NotificationTemplates.Where(t => t.EventType == eventType).ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<EventLogDto>> EventLogAsync(int take, CancellationToken ct = default)
    {
        var events = await db.NotificationEvents.AsNoTracking().OrderByDescending(e => e.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);
        var ids = events.Select(e => e.Id).ToList();
        var deliveries = (await db.NotificationDeliveries.AsNoTracking().Where(d => ids.Contains(d.EventId)).ToListAsync(ct)).ToLookup(d => d.EventId);
        var counts = await db.UserNotifications.Where(n => ids.Contains(n.EventId)).GroupBy(n => n.EventId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var channels = await db.NotificationChannels.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return events.Select(e => new EventLogDto(e.Id, e.Type, e.Severity, e.Title, e.Link, e.CreatedAt, counts.GetValueOrDefault(e.Id), e.AcknowledgedAt, e.AcknowledgedBy, e.EscalatedAt,
            deliveries[e.Id].OrderBy(d => d.CreatedAt).Select(d => new DeliveryDto(d.Id, d.ChannelId is { } c ? channels.GetValueOrDefault(c, "(deleted channel)") : d.EmailTo ?? "",
                d.Status, d.Attempts, d.Error, d.CreatedAt, d.SentAt, d.IsEscalation)).ToList())).ToList();
    }

    /// <summary>An exact event type, "*", or a prefix wildcard such as "run.*".</summary>
    private static bool KnownType(string t) => t == "*" || NotificationEvents.All.Any(e => e.Type == t || (t.EndsWith(".*") && e.Type.StartsWith(t[..^1])));

    private async Task<bool> CanManageAsync(CancellationToken ct)
        => user.UserId is { } id && (await permissions.GetPermissionsAsync(id, ct)).Contains(Permissions.NotificationsManage);

    private static List<string> Split(string csv) => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
