using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Domain.Testing;

namespace Wings.Application.Testing;

public interface ITestSuiteService
{
    Task<IReadOnlyList<SuiteSummaryDto>> ListAsync(Guid? productId, string? text, CancellationToken ct = default);
    Task<SuiteDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<SuiteDetailDto> CreateAsync(SuiteUpsert request, CancellationToken ct = default);
    Task<SuiteDetailDto> UpdateAsync(Guid id, SuiteUpsert request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<SuiteDetailDto> AddItemsAsync(Guid id, SuiteItemsRequest request, CancellationToken ct = default);
    Task<SuiteDetailDto> RemoveItemAsync(Guid id, Guid itemId, CancellationToken ct = default);
    Task<SuiteDetailDto> ReorderAsync(Guid id, IReadOnlyList<Guid> orderedItemIds, CancellationToken ct = default);
}

public sealed class TestSuiteService(IWingsDbContext db, ISequenceAllocator sequences, TestCaseService testCases) : ITestSuiteService
{
    public const string ScopeKey = "TESTSUITE";

    public async Task<IReadOnlyList<SuiteSummaryDto>> ListAsync(Guid? productId, string? text, CancellationToken ct = default)
    {
        var scope = await ScopeAsync(ct);
        var q = db.TestSuites.AsNoTracking();
        if (productId is { } pid) q = q.Where(s => s.ProductId == pid);
        if (text.Clean() is { } t) q = q.Where(s => EF.Functions.Like(s.Name, Search.Like(t), Search.Escape) || (s.Tags != null && EF.Functions.Like(s.Tags, Search.Like(t), Search.Escape)));
        var rows = await q.OrderBy(s => s.Name).Select(s => new
        {
            s, Count = s.Items.Count, Automated = s.Items.Count(i => i.TestCase!.AutomationStatus == AutomationStatus.Automated),
        }).ToListAsync(ct);
        return rows.Select(r => ToSummary(r.s, r.Count, r.Automated, scope)).ToList();
    }

    public async Task<SuiteDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var suite = await db.TestSuites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Test suite", id);
        var scope = await ScopeAsync(ct);
        var tcScope = await db.SequenceScopes.AsNoTracking().FirstAsync(s => s.Key == TestCaseService.ScopeKey, ct);
        var items = await db.TestSuiteItems.AsNoTracking().Where(i => i.SuiteId == id).OrderBy(i => i.Order).Select(i => new { i.Id, i.Order, i.TestCaseId }).ToListAsync(ct);
        var ids = items.Select(i => i.TestCaseId).ToList();
        var summaries = TestCaseService.WithNumbers(await testCases.Summaries(db.TestCases.Where(t => ids.Contains(t.Id)), tcScope).ToListAsync(ct), tcScope)
            .ToDictionary(s => s.Id);
        var list = items.Where(i => summaries.ContainsKey(i.TestCaseId)).Select(i => new SuiteItemDto(i.Id, i.Order, summaries[i.TestCaseId])).ToList();
        return new SuiteDetailDto(ToSummary(suite, list.Count, list.Count(i => i.TestCase.AutomationStatus == AutomationStatus.Automated), scope), list);
    }

    public async Task<SuiteDetailDto> CreateAsync(SuiteUpsert request, CancellationToken ct = default)
    {
        Validate(request);
        var id = await db.InTransactionAsync(async () =>
        {
            var scope = await ScopeAsync(ct);
            var suite = new TestSuite { Number = await sequences.NextAsync(scope.Id, ct) };
            Apply(suite, request);
            db.TestSuites.Add(suite);
            await db.SaveChangesAsync(ct);
            return suite.Id;
        }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<SuiteDetailDto> UpdateAsync(Guid id, SuiteUpsert request, CancellationToken ct = default)
    {
        Validate(request);
        var suite = await db.TestSuites.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Test suite", id);
        if (request.Version is { } v) db.SetOriginalVersion(suite, v);
        Apply(suite, request);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var suite = await db.TestSuites.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Test suite", id);
        suite.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task<SuiteDetailDto> AddItemsAsync(Guid id, SuiteItemsRequest request, CancellationToken ct = default)
    {
        var suite = await db.TestSuites.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Test suite", id);
        var ids = request.TestCaseIds.Distinct().ToList();
        var existing = await db.TestCases.Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct);
        if (existing.Count != ids.Count) throw new ValidationException("testCaseIds", "One or more test cases do not exist.");
        var order = suite.Items.Count == 0 ? 0 : suite.Items.Max(i => i.Order);
        foreach (var tcId in ids.Where(tcId => suite.Items.All(i => i.TestCaseId != tcId)))
            suite.Items.Add(new TestSuiteItem { SuiteId = id, TestCaseId = tcId, Order = order += 10 });
        db.Touch(suite);
        db.PendingAuditSummary = $"Added {ids.Count} test case(s)";
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<SuiteDetailDto> RemoveItemAsync(Guid id, Guid itemId, CancellationToken ct = default)
    {
        var item = await db.TestSuiteItems.FirstOrDefaultAsync(i => i.Id == itemId && i.SuiteId == id, ct) ?? throw new NotFoundException("Suite item", itemId);
        db.TestSuiteItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<SuiteDetailDto> ReorderAsync(Guid id, IReadOnlyList<Guid> orderedItemIds, CancellationToken ct = default)
    {
        var items = await db.TestSuiteItems.Where(i => i.SuiteId == id).ToListAsync(ct);
        if (items.Count != orderedItemIds.Count || items.Any(i => !orderedItemIds.Contains(i.Id)))
            throw new ValidationException("orderedItemIds", "The list must contain every suite item exactly once.");
        for (var i = 0; i < orderedItemIds.Count; i++) items.Single(x => x.Id == orderedItemIds[i]).Order = (i + 1) * 10;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private static SuiteSummaryDto ToSummary(TestSuite s, int count, int automated, Domain.Features.SequenceScope scope)
        => new(s.Id, s.Number, SequenceFormat.Format(s.Number, scope.DisplayWidth, scope.Prefix), s.Name, s.Description, s.ProductId, s.Tags, s.OwnerUserName, count, automated, s.Version);

    private static void Validate(SuiteUpsert r) => new Validator().Required(r.Name, "name", 200).MaxLength(r.Tags, "tags", 400).ThrowIfInvalid();

    private static void Apply(TestSuite s, SuiteUpsert r)
    {
        s.Name = r.Name.Trim(); s.Description = r.Description.Clean(); s.ProductId = r.ProductId; s.Tags = r.Tags.Clean(); s.OwnerUserName = r.OwnerUserName.Clean();
    }

    private async Task<Domain.Features.SequenceScope> ScopeAsync(CancellationToken ct)
        => await db.SequenceScopes.AsNoTracking().FirstAsync(s => s.Key == ScopeKey, ct);
}

public interface ITestFolderService
{
    Task<IReadOnlyList<FolderDto>> ListAsync(CancellationToken ct = default);
    Task<FolderDto> CreateAsync(FolderUpsert request, CancellationToken ct = default);
    Task<FolderDto> UpdateAsync(Guid id, FolderUpsert request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class TestFolderService(IWingsDbContext db) : ITestFolderService
{
    public async Task<IReadOnlyList<FolderDto>> ListAsync(CancellationToken ct = default)
        => await db.TestFolders.AsNoTracking().OrderBy(f => f.DisplayOrder).ThenBy(f => f.Name)
            .Select(f => new FolderDto(f.Id, f.Name, f.ParentId, f.DisplayOrder, db.TestCases.Count(t => t.FolderId == f.Id))).ToListAsync(ct);

    public async Task<FolderDto> CreateAsync(FolderUpsert request, CancellationToken ct = default)
    {
        new Validator().Required(request.Name, "name", 200).ThrowIfInvalid();
        var parent = request.ParentId is { } pid ? await db.TestFolders.FirstOrDefaultAsync(f => f.Id == pid, ct) ?? throw new NotFoundException("Folder", pid) : null;
        var folder = new TestFolder { Name = request.Name.Trim(), ParentId = parent?.Id };
        folder.Path = $"{parent?.Path ?? "/"}{folder.Id:N}/";
        folder.DisplayOrder = (await db.TestFolders.Where(f => f.ParentId == folder.ParentId).MaxAsync(f => (int?)f.DisplayOrder, ct) ?? 0) + 10;
        db.TestFolders.Add(folder);
        await db.SaveChangesAsync(ct);
        return new FolderDto(folder.Id, folder.Name, folder.ParentId, folder.DisplayOrder, 0);
    }

    public async Task<FolderDto> UpdateAsync(Guid id, FolderUpsert request, CancellationToken ct = default)
    {
        new Validator().Required(request.Name, "name", 200).ThrowIfInvalid();
        var folder = await db.TestFolders.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("Folder", id);
        folder.Name = request.Name.Trim();
        if (request.ParentId != folder.ParentId)
        {
            var parent = request.ParentId is { } pid ? await db.TestFolders.FirstOrDefaultAsync(f => f.Id == pid, ct) ?? throw new NotFoundException("Folder", pid) : null;
            if (parent is not null && parent.Path.StartsWith(folder.Path)) throw new ValidationException("parentId", "A folder cannot be moved inside itself.");
            var oldPath = folder.Path;
            folder.ParentId = parent?.Id;
            folder.Path = $"{parent?.Path ?? "/"}{folder.Id:N}/";
            foreach (var d in await db.TestFolders.Where(f => f.Path.StartsWith(oldPath) && f.Id != id).ToListAsync(ct))
                d.Path = folder.Path + d.Path[oldPath.Length..];
        }
        await db.SaveChangesAsync(ct);
        return new FolderDto(folder.Id, folder.Name, folder.ParentId, folder.DisplayOrder, await db.TestCases.CountAsync(t => t.FolderId == id, ct));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var folder = await db.TestFolders.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("Folder", id);
        if (await db.TestFolders.AnyAsync(f => f.ParentId == id, ct) || await db.TestCases.AnyAsync(t => t.FolderId == id, ct))
            throw new ConflictException("Only empty folders can be deleted.");
        db.TestFolders.Remove(folder);
        await db.SaveChangesAsync(ct);
    }
}
