using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Wings.Application.Abstractions;

namespace Wings.Api.Infrastructure;

/// <summary>Pushes change notifications (tree edits, package validation results) to connected clients.</summary>
[Authorize]
public sealed class ChangesHub : Hub;

public sealed class SignalRChangeNotifier(IHubContext<ChangesHub> hub, ILogger<SignalRChangeNotifier> log) : IChangeNotifier
{
    public async Task NotifyAsync(string topic, object payload, CancellationToken ct = default)
    {
        try { await hub.Clients.All.SendAsync(topic, payload, ct); }
        catch (Exception ex) { log.LogWarning(ex, "Failed to broadcast {Topic}", topic); } // notifications are best-effort
    }
}
