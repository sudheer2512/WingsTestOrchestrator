using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Wings.Agent.Contracts;
using Wings.Application.Execution;

namespace Wings.Api.Security;

/// <summary>Authenticates execution agents by the key in <see cref="AgentHeaders.Key"/>. Users can never use this scheme.</summary>
public sealed class AgentKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IAgentAdminService agents)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "AgentKey";
    public const string Policy = "Agent";
    public const string AgentIdClaim = "agent_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AgentHeaders.Key, out var key) || key.Count == 0) return AuthenticateResult.NoResult();
        var agent = await agents.AuthenticateAsync(key.ToString(), Context.RequestAborted);
        if (agent is null) return AuthenticateResult.Fail("Unknown or disabled agent key.");
        var identity = new ClaimsIdentity([new Claim(AgentIdClaim, agent.Id.ToString()), new Claim(ClaimTypes.Name, $"agent:{agent.Name}")], Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme));
    }
}
