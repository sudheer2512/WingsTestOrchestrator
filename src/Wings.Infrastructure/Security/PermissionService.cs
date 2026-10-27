using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Application.Security;
using Wings.Infrastructure.Persistence;

namespace Wings.Infrastructure.Security;

/// <summary>Resolves permissions from role claims on every request (cached briefly) so role changes apply without re-login.</summary>
public sealed class PermissionService(WingsDbContext db, IMemoryCache cache) : IPermissionService
{
    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Key(userId), out IReadOnlySet<string>? cached) && cached is not null) return cached;

        var active = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.IsActive).FirstOrDefaultAsync(ct);
        IReadOnlySet<string> perms = !active
            ? new HashSet<string>()
            : (await (from ur in db.UserRoles
                      join rc in db.RoleClaims on ur.RoleId equals rc.RoleId
                      where ur.UserId == userId && rc.ClaimType == Permissions.ClaimType
                      select rc.ClaimValue!).Distinct().ToListAsync(ct)).ToHashSet();

        cache.Set(Key(userId), perms, TimeSpan.FromSeconds(30));
        return perms;
    }

    public void Invalidate(Guid userId) => cache.Remove(Key(userId));

    private static string Key(Guid id) => $"perms:{id}";
}

public sealed class ProductAccess(WingsDbContext db, ICurrentUser user, IPermissionService permissions) : IProductAccess
{
    private List<Domain.Administration.UserProductAccess>? _rows;

    public async Task<IReadOnlySet<Guid>?> VisibleProductIdsAsync(CancellationToken ct = default)
    {
        var rows = await RowsAsync(ct);
        return rows is null ? null : rows.Select(r => r.ProductId).ToHashSet();
    }

    public async Task EnsureCanViewAsync(Guid productId, CancellationToken ct = default)
    {
        var rows = await RowsAsync(ct);
        if (rows is not null && rows.All(r => r.ProductId != productId)) throw new NotFoundException("Product", productId);
    }

    public async Task EnsureCanEditAsync(Guid productId, CancellationToken ct = default)
    {
        var rows = await RowsAsync(ct);
        if (rows is null) return;
        var row = rows.FirstOrDefault(r => r.ProductId == productId) ?? throw new NotFoundException("Product", productId);
        if (!row.CanEdit) throw new ForbiddenException("You have read-only access to this product.");
    }

    /// <summary>Null when unrestricted: system administrators, or users with no product rows.</summary>
    private async Task<List<Domain.Administration.UserProductAccess>?> RowsAsync(CancellationToken ct)
    {
        if (user.UserId is not { } id) return null;
        if ((await permissions.GetPermissionsAsync(id, ct)).Contains(Permissions.UsersManage)) return null;
        _rows ??= await db.UserProductAccess.AsNoTracking().Where(r => r.UserId == id).ToListAsync(ct);
        return _rows.Count == 0 ? null : _rows;
    }
}
