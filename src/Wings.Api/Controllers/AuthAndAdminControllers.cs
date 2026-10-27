using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Wings.Api.Security;
using Wings.Application.Abstractions;
using Wings.Application.Administration;
using Wings.Application.Common;
using Wings.Application.Features;
using Wings.Application.Security;
using Wings.Domain.Administration;
using Wings.Infrastructure.Identity;
using Wings.Infrastructure.Persistence;

namespace Wings.Api.Controllers;

public sealed record LoginRequest(string UserName, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record SessionDto(string Token, DateTimeOffset ExpiresAt, UserSummaryDto User, IReadOnlyCollection<string> Permissions);
public sealed record UserSummaryDto(Guid Id, string UserName, string DisplayName, string? Email, bool IsActive, IReadOnlyList<string> Roles,
    IReadOnlyList<ProductAccessDto> ProductAccess, DateTimeOffset? LastLoginAt, bool IsLockedOut);
public sealed record ProductAccessDto(Guid ProductId, bool CanEdit);
public sealed record CreateUserRequest(string UserName, string DisplayName, string? Email, string Password, IReadOnlyList<string> Roles);
public sealed record UpdateUserRequest(string DisplayName, string? Email, bool IsActive, IReadOnlyList<string> Roles);
public sealed record ResetPasswordRequest(string NewPassword);
public sealed record RoleDto(Guid Id, string Name, IReadOnlyList<string> Permissions, int UserCount);
public sealed record PermissionDto(string Code, string Description);

[ApiController, Route("api/v1/auth")]
public sealed class AuthController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, TokenService tokens,
    IPermissionService permissions, WingsDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<ActionResult<SessionDto>> Login(LoginRequest request)
    {
        var user = await users.FindByNameAsync(request.UserName ?? "");
        // Same response for unknown users, wrong passwords and disabled accounts, so usernames cannot be probed.
        if (user is null || !user.IsActive) return Problem(statusCode: 401, title: "Invalid user name or password.");
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password ?? "", lockoutOnFailure: true);
        if (result.IsLockedOut) return Problem(statusCode: 423, title: "The account is temporarily locked after repeated failed sign-ins. Try again later.");
        if (!result.Succeeded) return Problem(statusCode: 401, title: "Invalid user name or password.");

        user.LastLoginAt = clock.GetUtcNow();
        await users.UpdateAsync(user);
        db.AuditLogs.Add(new AuditLog { Timestamp = clock.GetUtcNow(), UserName = user.UserName!, Action = "SignedIn", EntityType = "User", EntityId = user.Id });
        await db.SaveChangesAsync();
        return await SessionFor(user);
    }

    [HttpGet("me")]
    public async Task<ActionResult<SessionDto>> Me()
    {
        var user = await users.FindByIdAsync(User.FindFirstValue("sub") ?? "");
        if (user is null || !user.IsActive) return Unauthorized();
        return await SessionFor(user, reuseToken: true);
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await users.FindByIdAsync(User.FindFirstValue("sub") ?? "") ?? throw new ForbiddenException();
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded) throw new ValidationException(new Dictionary<string, string[]> { ["newPassword"] = result.Errors.Select(e => e.Description).ToArray() });
        return NoContent();
    }

    private async Task<SessionDto> SessionFor(ApplicationUser user, bool reuseToken = false)
    {
        var perms = await permissions.GetPermissionsAsync(user.Id);
        var roleNames = (await users.GetRolesAsync(user)).ToList();
        var (token, expires) = reuseToken && Request.Headers.Authorization.ToString() is { Length: > 7 } h
            ? (h[7..], DateTimeOffset.FromUnixTimeSeconds(long.Parse(User.FindFirstValue("exp") ?? "0")))
            : tokens.Issue(user.Id, user.UserName!, user.DisplayName);
        return new SessionDto(token, expires, new UserSummaryDto(user.Id, user.UserName!, user.DisplayName, user.Email, user.IsActive, roleNames, [], user.LastLoginAt, false), perms);
    }
}

[ApiController, Route("api/v1/admin"), HasPermission(Permissions.UsersManage)]
public sealed class AdminController(UserManager<ApplicationUser> users, RoleManager<ApplicationRole> roles, WingsDbContext db, IPermissionService permissions, TimeProvider clock) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IReadOnlyList<UserSummaryDto>> ListUsers()
    {
        var all = await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync();
        var userRoles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id select new { ur.UserId, r.Name }).ToListAsync();
        var access = await db.UserProductAccess.AsNoTracking().ToListAsync();
        var now = clock.GetUtcNow();
        return all.Select(u => new UserSummaryDto(u.Id, u.UserName!, u.DisplayName, u.Email, u.IsActive,
            userRoles.Where(r => r.UserId == u.Id).Select(r => r.Name!).OrderBy(n => n).ToList(),
            access.Where(a => a.UserId == u.Id).Select(a => new ProductAccessDto(a.ProductId, a.CanEdit)).ToList(), u.LastLoginAt, u.LockoutEnd > now)).ToList();
    }

    [HttpPost("users")]
    public async Task<ActionResult<UserSummaryDto>> CreateUser(CreateUserRequest request)
    {
        new Validator().Required(request.UserName, "userName", 100).Required(request.DisplayName, "displayName", 200).ThrowIfInvalid();
        await EnsureRolesExist(request.Roles);
        var user = new ApplicationUser { UserName = request.UserName.Trim(), DisplayName = request.DisplayName.Trim(), Email = request.Email.Clean(), CreatedAt = clock.GetUtcNow() };
        Check(await users.CreateAsync(user, request.Password), "password");
        if (request.Roles.Count > 0) Check(await users.AddToRolesAsync(user, request.Roles), "roles");
        Audit("Created", user.Id, $"User {user.UserName} created with roles {string.Join(", ", request.Roles)}");
        await db.SaveChangesAsync();
        return (await ListUsers()).Single(u => u.Id == user.Id);
    }

    [HttpPut("users/{id:guid}")]
    public async Task<UserSummaryDto> UpdateUser(Guid id, UpdateUserRequest request)
    {
        new Validator().Required(request.DisplayName, "displayName", 200).ThrowIfInvalid();
        await EnsureRolesExist(request.Roles);
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("User", id);
        if (user.Id.ToString() == User.FindFirstValue("sub") && (!request.IsActive || !request.Roles.Contains(Roles.SystemAdministrator) && (await users.GetRolesAsync(user)).Contains(Roles.SystemAdministrator)))
            throw new ValidationException("roles", "You cannot deactivate yourself or remove your own administrator role.");
        user.DisplayName = request.DisplayName.Trim();
        user.Email = request.Email.Clean();
        user.IsActive = request.IsActive;
        Check(await users.UpdateAsync(user), "user");
        var current = await users.GetRolesAsync(user);
        Check(await users.RemoveFromRolesAsync(user, current.Except(request.Roles)), "roles");
        Check(await users.AddToRolesAsync(user, request.Roles.Except(current)), "roles");
        if (!request.IsActive) await users.UpdateSecurityStampAsync(user);
        permissions.Invalidate(id);
        Audit("Updated", id, $"Roles: {string.Join(", ", request.Roles)}; active: {request.IsActive}");
        await db.SaveChangesAsync();
        return (await ListUsers()).Single(u => u.Id == id);
    }

    [HttpPost("users/{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("User", id);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        Check(await users.ResetPasswordAsync(user, token, request.NewPassword), "newPassword");
        await users.SetLockoutEndDateAsync(user, null);
        Audit("PasswordReset", id, null);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("users/{id:guid}/product-access")]
    public async Task<UserSummaryDto> SetProductAccess(Guid id, IReadOnlyList<ProductAccessDto> access)
    {
        if (await users.FindByIdAsync(id.ToString()) is null) throw new NotFoundException("User", id);
        var ids = access.Select(a => a.ProductId).Distinct().ToList();
        if (await db.Products.CountAsync(p => ids.Contains(p.Id)) != ids.Count) throw new ValidationException("productId", "Unknown product.");
        db.UserProductAccess.RemoveRange(db.UserProductAccess.Where(a => a.UserId == id));
        db.UserProductAccess.AddRange(access.DistinctBy(a => a.ProductId).Select(a => new UserProductAccess { UserId = id, ProductId = a.ProductId, CanEdit = a.CanEdit }));
        Audit("ProductAccessChanged", id, access.Count == 0 ? "Unrestricted" : $"{access.Count} product(s)");
        await db.SaveChangesAsync();
        return (await ListUsers()).Single(u => u.Id == id);
    }

    [HttpGet("roles")]
    public async Task<IReadOnlyList<RoleDto>> ListRoles()
    {
        var all = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
        var claims = await db.RoleClaims.AsNoTracking().Where(c => c.ClaimType == Permissions.ClaimType).ToListAsync();
        var counts = await db.UserRoles.GroupBy(r => r.RoleId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);
        return all.Select(r => new RoleDto(r.Id, r.Name!, claims.Where(c => c.RoleId == r.Id).Select(c => c.ClaimValue!).OrderBy(c => c).ToList(), counts.GetValueOrDefault(r.Id))).ToList();
    }

    [HttpGet("permissions")]
    public IReadOnlyList<PermissionDto> ListPermissions() => Permissions.All.Select(p => new PermissionDto(p.Code, p.Description)).ToList();

    [HttpPut("roles/{id:guid}/permissions")]
    public async Task<RoleDto> SetRolePermissions(Guid id, IReadOnlyList<string> codes)
    {
        var role = await roles.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("Role", id);
        var unknown = codes.Except(Permissions.All.Select(p => p.Code)).ToList();
        if (unknown.Count > 0) throw new ValidationException("permissions", $"Unknown permission(s): {string.Join(", ", unknown)}");
        if (role.Name == Roles.SystemAdministrator && !codes.Contains(Permissions.UsersManage))
            throw new ValidationException("permissions", "The System Administrator role must keep users.manage.");
        var existing = (await roles.GetClaimsAsync(role)).Where(c => c.Type == Permissions.ClaimType).ToList();
        foreach (var c in existing.Where(c => !codes.Contains(c.Value))) await roles.RemoveClaimAsync(role, c);
        foreach (var code in codes.Except(existing.Select(c => c.Value))) await roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, code));
        foreach (var uid in await db.UserRoles.Where(r => r.RoleId == id).Select(r => r.UserId).ToListAsync()) permissions.Invalidate(uid);
        Audit("PermissionsChanged", id, string.Join(", ", codes), "Role");
        await db.SaveChangesAsync();
        return (await ListRoles()).Single(r => r.Id == id);
    }

    private async Task EnsureRolesExist(IReadOnlyList<string> names)
    {
        foreach (var n in names) if (!await roles.RoleExistsAsync(n)) throw new ValidationException("roles", $"Unknown role '{n}'.");
    }

    private void Audit(string action, Guid id, string? summary, string type = "User")
        => db.AuditLogs.Add(new AuditLog { Timestamp = clock.GetUtcNow(), UserName = User.FindFirstValue("unique_name") ?? "system", Action = action, EntityType = type, EntityId = id, RootEntityType = type, RootEntityId = id, Summary = summary });

    private static void Check(IdentityResult result, string field)
    {
        if (!result.Succeeded) throw new ValidationException(new Dictionary<string, string[]> { [field] = result.Errors.Select(e => e.Description).ToArray() });
    }
}

[ApiController, Route("api/v1")]
public sealed class LookupsController(IFeatureQueryService features, IMasterDataService master, IProductService products) : ControllerBase
{
    [HttpGet("lookups"), HasPermission(Permissions.FeaturesView)]
    public async Task<object> Get(CancellationToken ct) => new
    {
        featureTypes = await features.FeatureTypesAsync(ct),
        featureStatuses = await features.FeatureStatusesAsync(ct),
        testTypes = await master.TestTypesAsync(ct),
        frameworks = await master.FrameworksAsync(ct),
        sequenceScopes = await products.ListScopesAsync(ct),
        applicationTypes = Enum.GetNames<Wings.Domain.Features.ApplicationType>(),
        priorities = new[] { "P1", "P2", "P3", "P4" },
        severities = new[] { "CRITICAL", "MAJOR", "MINOR", "TRIVIAL" },
        dependencyTypes = Enum.GetNames<Wings.Domain.Features.DependencyType>(),
        sourceMappingKinds = Enum.GetNames<Wings.Domain.Features.SourceMappingKind>(),
    };

    [HttpPut("master/feature-types"), HasPermission(Permissions.MasterDataManage)]
    public Task<LookupItem> UpsertFeatureType(MasterItemUpsert item, CancellationToken ct) => master.UpsertFeatureTypeAsync(item, ct);

    [HttpPut("master/feature-statuses"), HasPermission(Permissions.MasterDataManage)]
    public Task<LookupItem> UpsertFeatureStatus(MasterItemUpsert item, CancellationToken ct) => master.UpsertFeatureStatusAsync(item, ct);

    [HttpPut("master/test-types"), HasPermission(Permissions.MasterDataManage)]
    public Task<LookupItem> UpsertTestType(MasterItemUpsert item, CancellationToken ct) => master.UpsertTestTypeAsync(item, ct);
}

[ApiController, Route("api/v1/audit"), HasPermission(Permissions.AuditView)]
public sealed class AuditController(IAuditQueryService audit) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AuditEntryDto>> Search([FromQuery] AuditQuery query, CancellationToken ct) => audit.SearchAsync(query, ct);
}
