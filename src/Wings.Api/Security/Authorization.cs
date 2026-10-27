using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Wings.Application.Abstractions;

namespace Wings.Api.Security;

/// <summary>Requires a permission code (e.g. "features.edit"). Permissions are resolved from the database per request, not from the token.</summary>
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(PermissionPolicyProvider.Prefix + permission);

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        => policyName.StartsWith(Prefix, StringComparison.Ordinal)
            ? new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(policyName[Prefix.Length..])).Build()
            : await base.GetPolicyAsync(policyName);
}

public sealed class PermissionHandler(IPermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return;
        if ((await permissions.GetPermissionsAsync(userId)).Contains(requirement.Permission)) context.Succeed(requirement);
    }
}

public sealed class CurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    private ClaimsPrincipal? Principal => http.HttpContext?.User;
    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string UserName => Principal?.FindFirstValue(JwtRegisteredClaimNames.UniqueName) ?? Principal?.Identity?.Name ?? "system";
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "wings-test-orchestrator";
    public string Audience { get; set; } = "wings-test-orchestrator";
    /// <summary>HMAC-SHA256 key, at least 32 characters. Supply through environment/secret store in non-development environments.</summary>
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 480;

    public SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(SigningKey));
}

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid userId, string userName, string displayName)
    {
        var o = options.Value;
        var expires = clock.GetUtcNow().AddMinutes(o.AccessTokenMinutes);
        var token = new JwtSecurityToken(o.Issuer, o.Audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, userName),
                new Claim("name", displayName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ],
            notBefore: clock.GetUtcNow().UtcDateTime, expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(o.Key(), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
