using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wings.Application.Abstractions;
using Wings.Application.Quality;
using Wings.Domain.Quality;
using Wings.Infrastructure.Jobs;
using Wings.Infrastructure.Persistence;
using Wings.Infrastructure.Storage;

namespace Wings.Infrastructure.Notifications;

public sealed class SmtpOptions
{
    /// <summary>Empty: messages are written as .eml files to <see cref="PickupDirectory"/> (development and air-gapped installs).</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "wings-qa@localhost";
    /// <summary>Relative paths are under the storage root.</summary>
    public string PickupDirectory { get; set; } = "mail-outbox";
}

public sealed record OutgoingMessage(string Subject, string Body, string? Link, NotificationEvent Event, bool IsEscalation, string? AttachmentName, byte[]? Attachment);

public interface INotificationSender
{
    ChannelType Type { get; }
    Task SendAsync(NotificationChannel? channel, string? emailTo, OutgoingMessage message, CancellationToken ct);
}

public sealed class EmailSender(IOptions<SmtpOptions> options, IOptions<StorageOptions> storage) : INotificationSender
{
    public ChannelType Type => ChannelType.Email;

    public async Task SendAsync(NotificationChannel? channel, string? emailTo, OutgoingMessage m, CancellationToken ct)
    {
        var o = options.Value;
        var recipients = (emailTo ?? channel?.Recipients ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (recipients.Length == 0) throw new InvalidOperationException("The email has no recipients.");
        using var mail = new MailMessage { From = new MailAddress(o.From, "Wings Test Orchestrator"), Subject = (m.IsEscalation ? "[ESCALATED] " : "") + m.Subject, Body = m.Body, IsBodyHtml = false };
        foreach (var r in recipients) mail.To.Add(r);
        mail.Headers.Add("X-Wings-Event", m.Event.Type);
        if (m.Attachment is { } bytes) mail.Attachments.Add(new Attachment(new MemoryStream(bytes), m.AttachmentName ?? "report"));

        using var client = new SmtpClient();
        if (string.IsNullOrWhiteSpace(o.Host))
        {
            var dir = Path.IsPathRooted(o.PickupDirectory) ? o.PickupDirectory : Path.Combine(storage.Value.RootPath, o.PickupDirectory);
            Directory.CreateDirectory(dir);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = dir;
        }
        else
        {
            client.Host = o.Host;
            client.Port = o.Port;
            client.EnableSsl = o.EnableSsl;
            if (o.UserName is { Length: > 0 }) client.Credentials = new NetworkCredential(o.UserName, o.Password);
        }
        await client.SendMailAsync(mail, ct);
    }
}

/// <summary>Posts an Adaptive Card to a Teams incoming webhook / Workflows URL.</summary>
public sealed class TeamsSender(IHttpClientFactory http, ISecretProtector secrets) : INotificationSender
{
    public ChannelType Type => ChannelType.Teams;

    public async Task SendAsync(NotificationChannel? channel, string? emailTo, OutgoingMessage m, CancellationToken ct)
    {
        var url = secrets.Unprotect(channel?.ProtectedUrl ?? throw new InvalidOperationException("The Teams channel has no URL."));
        var color = m.Event.Severity switch { NotificationSeverity.Critical => "attention", NotificationSeverity.Warning => "warning", _ => "accent" };
        var body = new List<object>
        {
            new { type = "TextBlock", text = (m.IsEscalation ? "ESCALATED: " : "") + m.Event.Title, weight = "bolder", size = "medium", wrap = true, color },
            new { type = "TextBlock", text = $"{m.Event.Severity} · {m.Event.Type} · {m.Event.CreatedAt:yyyy-MM-dd HH:mm} UTC", isSubtle = true, spacing = "none", wrap = true },
        };
        if (!string.IsNullOrWhiteSpace(m.Event.Body)) body.Add(new { type = "TextBlock", text = m.Event.Body, wrap = true });
        var card = new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new Dictionary<string, object?>
                    {
                        ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json", ["type"] = "AdaptiveCard", ["version"] = "1.4", ["body"] = body,
                        ["actions"] = m.Link is null ? Array.Empty<object>() : new object[] { new { type = "Action.OpenUrl", title = "Open in Wings Test Orchestrator", url = m.Link } },
                    },
                },
            },
        };
        using var response = await http.CreateClient(IssueTrackerService.HttpClientName).PostAsJsonAsync(url, card, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Teams returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
    }
}

/// <summary>POSTs the event as JSON; with a signing secret, X-Wings-Signature: sha256=HMAC(body) lets the receiver verify it.</summary>
public sealed class WebhookSender(IHttpClientFactory http, ISecretProtector secrets) : INotificationSender
{
    public ChannelType Type => ChannelType.Webhook;

    public async Task SendAsync(NotificationChannel? channel, string? emailTo, OutgoingMessage m, CancellationToken ct)
    {
        var url = secrets.Unprotect(channel?.ProtectedUrl ?? throw new InvalidOperationException("The webhook channel has no URL."));
        var e = m.Event;
        var json = JsonSerializer.Serialize(new
        {
            id = e.Id, type = e.Type, severity = e.Severity, title = e.Title, body = e.Body, link = m.Link, productId = e.ProductId, createdAt = e.CreatedAt, escalation = m.IsEscalation,
            data = string.IsNullOrEmpty(e.DataJson) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(e.DataJson),
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Wings-Event", e.Type);
        request.Headers.Add("X-Wings-Delivery", e.Id.ToString());
        if (channel.ProtectedSigningSecret is { } s)
            request.Headers.Add("X-Wings-Signature", "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secrets.Unprotect(s)), Encoding.UTF8.GetBytes(json))));
        using var response = await http.CreateClient(IssueTrackerService.HttpClientName).SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"The webhook returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
    }
}

/// <summary>Sends one queued delivery; the job queue retries failures with back-off and the final failure is recorded.</summary>
public sealed class NotificationDeliveryJob(WingsDbContext db, IEnumerable<INotificationSender> senders, IFileStore files, TimeProvider clock,
    IOptions<NotificationOptions> options, ILogger<NotificationDeliveryJob> log) : IFailureAwareJobHandler
{
    public string JobType => NotificationPublisher.DeliverJob;

    private sealed record Payload(Guid DeliveryId);

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var id = JsonSerializer.Deserialize<Payload>(payloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.DeliveryId;
        var d = await db.NotificationDeliveries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null || d.Status == DeliveryStatus.Sent) return;
        var ev = await db.NotificationEvents.AsNoTracking().FirstAsync(e => e.Id == d.EventId, ct);
        var channel = d.ChannelId is { } cid ? await db.NotificationChannels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, ct) : null;
        if (d.ChannelId is not null && (channel is null || !channel.IsEnabled))
        {
            d.Status = DeliveryStatus.Failed;
            d.Error = channel is null ? "The channel was deleted." : "The channel is disabled.";
            await db.SaveChangesAsync(ct);
            return;
        }

        var template = await db.NotificationTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.EventType == ev.Type, ct);
        var link = ev.Link is null ? null : (options.Value.PublicUrl ?? "http://localhost:5057").TrimEnd('/') + ev.Link;
        byte[]? attachment = null;
        if (d.AttachmentKey is { } key)
        {
            await using var stream = await files.OpenReadAsync(key, ct);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            attachment = ms.ToArray();
        }
        var message = new OutgoingMessage(NotificationTemplates.Render(template?.Subject ?? NotificationTemplates.DefaultSubject, ev, link),
            NotificationTemplates.Render(template?.Body ?? NotificationTemplates.DefaultBody, ev, link), link, ev, d.IsEscalation, d.AttachmentName, attachment);
        var type = channel?.Type ?? ChannelType.Email;
        var sender = senders.First(s => s.Type == type);

        d.Attempts++;
        try
        {
            await sender.SendAsync(channel, d.EmailTo, message, ct);
            d.Status = DeliveryStatus.Sent;
            d.SentAt = clock.GetUtcNow();
            d.Error = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            d.Error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            await db.SaveChangesAsync(ct);
            log.LogWarning(ex, "Notification delivery {DeliveryId} via {Type} failed (attempt {Attempt})", d.Id, type, d.Attempts);
            throw;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task OnFinalFailureAsync(string payloadJson, Exception error, CancellationToken ct)
    {
        var id = JsonSerializer.Deserialize<Payload>(payloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.DeliveryId;
        await db.NotificationDeliveries.Where(x => x.Id == id && x.Status != DeliveryStatus.Sent).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, DeliveryStatus.Failed), ct);
    }
}

public sealed class UserDirectory(WingsDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyList<UserContact>> ListAsync(CancellationToken ct = default)
        => await db.Users.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.UserName).Select(u => new UserContact(u.Id, u.UserName!, u.DisplayName, u.Email, u.IsActive)).ToListAsync(ct);

    public async Task<IReadOnlyList<UserContact>> FindAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).Select(u => new UserContact(u.Id, u.UserName!, u.DisplayName, u.Email, u.IsActive)).ToListAsync(ct);
    }

    public async Task<UserContact?> FindByUserNameAsync(string userName, CancellationToken ct = default)
    {
        var normalized = userName.Trim().ToUpperInvariant();
        return await db.Users.AsNoTracking().Where(u => u.NormalizedUserName == normalized).Select(u => new UserContact(u.Id, u.UserName!, u.DisplayName, u.Email, u.IsActive)).FirstOrDefaultAsync(ct);
    }
}

/// <summary>Runs <see cref="OperationsWatch"/> periodically: offline agents, escalations and scheduled reports.</summary>
public sealed class QualityMonitor(IServiceScopeFactory scopes, IConfiguration config, ILogger<QualityMonitor> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Jobs:RunWorker", true)) return;
        var interval = TimeSpan.FromSeconds(config.GetValue("Notifications:MonitorSeconds", 30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(interval, stoppingToken); } catch (OperationCanceledException) { break; }
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OperationsWatch>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Quality monitor pass failed"); }
        }
    }
}
