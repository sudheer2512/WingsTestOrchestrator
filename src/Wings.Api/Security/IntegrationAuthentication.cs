using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Wings.Application.Quality;

namespace Wings.Api.Security;

/// <summary>Authenticates CI/CD and other systems by an integration key (builds, release gate checks). Never grants user permissions.</summary>
public sealed class IntegrationKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IBuildService builds)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "IntegrationKey";
    public const string Policy = "Integration";
    public const string Header = "X-Wings-Integration-Key";
    public const string IntegrationClaim = "integration";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var key) || key.Count == 0) return AuthenticateResult.NoResult();
        var name = await builds.AuthenticateKeyAsync(key.ToString(), Context.RequestAborted);
        if (name is null) return AuthenticateResult.Fail("Unknown or disabled integration key.");
        var identity = new ClaimsIdentity([new Claim(IntegrationClaim, name), new Claim(ClaimTypes.Name, $"integration:{name}")], Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme));
    }
}
