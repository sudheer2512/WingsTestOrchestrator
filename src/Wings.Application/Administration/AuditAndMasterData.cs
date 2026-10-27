using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Application.Features;
using Wings.Domain.Features;
using Wings.Domain.Testing;

namespace Wings.Application.Administration;

public interface IAuditQueryService
{
    Task<PagedResult<AuditEntryDto>> SearchAsync(AuditQuery query, CancellationToken ct = default);
    /// <summary>History of an aggregate, including changes to its child records (requirements, mappings...).</summary>
    Task<PagedResult<AuditEntryDto>> ForEntityAsync(string rootType, Guid id, PageRequest page, CancellationToken ct = default);
}

public sealed class AuditQueryService(IWingsDbContext db) : IAuditQueryService
{
    public async Task<PagedResult<AuditEntryDto>> SearchAsync(AuditQuery query, CancellationToken ct = default)
    {
        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (query.EntityType.Clean() is { } type) q = q.Where(a => a.EntityType == type || a.RootEntityType == type);
        if (query.EntityId is { } id) q = q.Where(a => a.EntityId == id || a.RootEntityId == id);
        if (query.UserName.Clean() is { } user) q = q.Where(a => a.UserName == user);
        if (query.Action.Clean() is { } action) q = q.Where(a => a.Action == action);
        if (query.From is { } from) q = q.Where(a => a.Timestamp >= from);
        if (query.To is { } to) q = q.Where(a => a.Timestamp <= to);
        return await PageAsync(q, new PageRequest(query.Page, query.PageSize), ct);
    }

    public Task<PagedResult<AuditEntryDto>> ForEntityAsync(string rootType, Guid id, PageRequest page, CancellationToken ct = default)
        => PageAsync(db.AuditLogs.AsNoTracking().Where(a => a.RootEntityType == rootType && a.RootEntityId == id), page, ct);

    private static async Task<PagedResult<AuditEntryDto>> PageAsync(IQueryable<Domain.Administration.AuditLog> q, PageRequest page, CancellationToken ct)
    {
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.Id).Skip(page.Skip).Take(page.SafePageSize)
            .Select(a => new AuditEntryDto(a.Id, a.Timestamp, a.UserName, a.Action, a.EntityType, a.EntityId, a.ChangesJson, a.Summary))
            .ToListAsync(ct);
        return new PagedResult<AuditEntryDto>(items, total, page.SafePage, page.SafePageSize);
    }
}

public sealed record MasterItemUpsert(string Code, string Name, int SortOrder = 0);
public sealed record FrameworkDto(string Code, string Name, string Language, ApplicationType Platform);

public interface IMasterDataService
{
    Task<IReadOnlyList<LookupItem>> TestTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FrameworkDto>> FrameworksAsync(CancellationToken ct = default);
    Task<LookupItem> UpsertFeatureTypeAsync(MasterItemUpsert item, CancellationToken ct = default);
    Task<LookupItem> UpsertFeatureStatusAsync(MasterItemUpsert item, CancellationToken ct = default);
    Task<LookupItem> UpsertTestTypeAsync(MasterItemUpsert item, CancellationToken ct = default);
}

public sealed class MasterDataService(IWingsDbContext db) : IMasterDataService
{
    public async Task<IReadOnlyList<LookupItem>> TestTypesAsync(CancellationToken ct = default)
        => await db.TestTypes.OrderBy(t => t.SortOrder).ThenBy(t => t.Name).Select(t => new LookupItem(t.Code, t.Name, t.SortOrder)).ToListAsync(ct);

    public async Task<IReadOnlyList<FrameworkDto>> FrameworksAsync(CancellationToken ct = default)
        => await db.TestingFrameworks.OrderBy(f => f.Name).Select(f => new FrameworkDto(f.Code, f.Name, f.Language, f.Platform)).ToListAsync(ct);

    public async Task<LookupItem> UpsertFeatureTypeAsync(MasterItemUpsert item, CancellationToken ct = default)
    {
        var code = Validate(item);
        var row = await db.FeatureTypes.FirstOrDefaultAsync(t => t.Code == code, ct);
        if (row is null) db.FeatureTypes.Add(row = new FeatureType { Code = code });
        row.Name = item.Name.Trim(); row.SortOrder = item.SortOrder;
        await db.SaveChangesAsync(ct);
        return new LookupItem(row.Code, row.Name, row.SortOrder);
    }

    public async Task<LookupItem> UpsertFeatureStatusAsync(MasterItemUpsert item, CancellationToken ct = default)
    {
        var code = Validate(item);
        var row = await db.FeatureStatuses.FirstOrDefaultAsync(t => t.Code == code, ct);
        if (row is null) db.FeatureStatuses.Add(row = new FeatureStatus { Code = code });
        row.Name = item.Name.Trim(); row.SortOrder = item.SortOrder;
        await db.SaveChangesAsync(ct);
        return new LookupItem(row.Code, row.Name, row.SortOrder);
    }

    public async Task<LookupItem> UpsertTestTypeAsync(MasterItemUpsert item, CancellationToken ct = default)
    {
        var code = Validate(item);
        var row = await db.TestTypes.FirstOrDefaultAsync(t => t.Code == code, ct);
        if (row is null) db.TestTypes.Add(row = new TestType { Code = code, IsCustom = true });
        row.Name = item.Name.Trim(); row.SortOrder = item.SortOrder;
        await db.SaveChangesAsync(ct);
        return new LookupItem(row.Code, row.Name, row.SortOrder);
    }

    private static string Validate(MasterItemUpsert item)
    {
        new Validator().Required(item.Code, "code", 50).Required(item.Name, "name", 200)
            .Must(item.Code is null || System.Text.RegularExpressions.Regex.IsMatch(item.Code.Trim(), "^[A-Za-z0-9_]+$"), "code", "Code may contain letters, digits and '_' only.")
            .ThrowIfInvalid();
        return item.Code.Trim().ToUpperInvariant();
    }
}
