using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Domain.Features;

namespace Wings.Application.Features;

public interface IProductService
{
    Task<IReadOnlyList<ProductDto>> ListAsync(CancellationToken ct = default);
    Task<ProductDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ProductDto> CreateAsync(ProductUpsert request, CancellationToken ct = default);
    Task<ProductDto> UpdateAsync(Guid id, ProductUpsert request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ProductVersionDto>> ListVersionsAsync(Guid productId, CancellationToken ct = default);
    Task<ProductVersionDto> CreateVersionAsync(Guid productId, ProductVersionUpsert request, CancellationToken ct = default);
    Task<ProductVersionDto> UpdateVersionAsync(Guid productId, Guid versionId, ProductVersionUpsert request, CancellationToken ct = default);
    Task<IReadOnlyList<SequenceScopeDto>> ListScopesAsync(CancellationToken ct = default);
}

public sealed class ProductService(IWingsDbContext db, IProductAccess access) : IProductService
{
    public async Task<IReadOnlyList<ProductDto>> ListAsync(CancellationToken ct = default)
    {
        var visible = await access.VisibleProductIdsAsync(ct);
        return await Project(db.Products.Where(p => visible == null || visible.Contains(p.Id)).OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name))
            .ToListAsync(ct);
    }

    public async Task<ProductDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        await access.EnsureCanViewAsync(id, ct);
        return await Project(db.Products.Where(p => p.Id == id)).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Product", id);
    }

    public async Task<ProductDto> CreateAsync(ProductUpsert request, CancellationToken ct = default)
    {
        Validate(request);
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Code == code, ct))
            throw new ValidationException("code", $"Product code '{code}' is already used.");

        SequenceScope scope;
        if (request.SequenceScopeKey.Clean() is { } key)
            scope = await db.SequenceScopes.FirstOrDefaultAsync(s => s.Key == key, ct) ?? throw new ValidationException("sequenceScopeKey", $"Sequence scope '{key}' does not exist.");
        else
        {
            scope = new SequenceScope { Key = $"PRODUCT:{code}", Name = $"{request.Name.Trim()} features" };
            db.SequenceScopes.Add(scope);
        }

        var product = new Product
        {
            Code = code, Name = request.Name.Trim(), Description = request.Description.Clean(), IsActive = request.IsActive,
            DisplayOrder = request.DisplayOrder ?? (await db.Products.MaxAsync(p => (int?)p.DisplayOrder, ct) ?? 0) + 10,
            SequenceScopeId = scope.Id,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return await GetAsync(product.Id, ct);
    }

    public async Task<ProductDto> UpdateAsync(Guid id, ProductUpsert request, CancellationToken ct = default)
    {
        Validate(request);
        await access.EnsureCanEditAsync(id, ct);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Product", id);
        if (request.Version is { } v) db.SetOriginalVersion(product, v);
        // The code and numbering scope are fixed after creation: sequence numbers and external references depend on them.
        product.Name = request.Name.Trim();
        product.Description = request.Description.Clean();
        product.IsActive = request.IsActive;
        if (request.DisplayOrder is { } order) product.DisplayOrder = order;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await access.EnsureCanEditAsync(id, ct);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Product", id);
        if (await db.Features.AnyAsync(f => f.ProductId == id, ct))
            throw new ConflictException("Delete or move the product's features before deleting the product.");
        product.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ProductVersionDto>> ListVersionsAsync(Guid productId, CancellationToken ct = default)
    {
        await access.EnsureCanViewAsync(productId, ct);
        return await db.ProductVersions.Where(v => v.ProductId == productId)
            .OrderByDescending(v => v.ReleaseDate).ThenByDescending(v => v.VersionLabel)
            .Select(v => new ProductVersionDto(v.Id, v.ProductId, v.VersionLabel, v.ReleaseDate, v.IsReleased, v.Notes, v.Version))
            .ToListAsync(ct);
    }

    public async Task<ProductVersionDto> CreateVersionAsync(Guid productId, ProductVersionUpsert request, CancellationToken ct = default)
    {
        new Validator().Required(request.VersionLabel, "versionLabel", 50).ThrowIfInvalid();
        await access.EnsureCanEditAsync(productId, ct);
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct)) throw new NotFoundException("Product", productId);
        var label = request.VersionLabel.Trim();
        if (await db.ProductVersions.AnyAsync(v => v.ProductId == productId && v.VersionLabel == label, ct))
            throw new ValidationException("versionLabel", $"Release '{label}' already exists for this product.");
        var version = new ProductVersion { ProductId = productId, VersionLabel = label, ReleaseDate = request.ReleaseDate, IsReleased = request.IsReleased, Notes = request.Notes.Clean(),
            Status = request.IsReleased ? Domain.Quality.ReleaseStatus.Released : Domain.Quality.ReleaseStatus.Planning };
        db.ProductVersions.Add(version);
        await db.SaveChangesAsync(ct);
        return new ProductVersionDto(version.Id, productId, version.VersionLabel, version.ReleaseDate, version.IsReleased, version.Notes, version.Version);
    }

    public async Task<ProductVersionDto> UpdateVersionAsync(Guid productId, Guid versionId, ProductVersionUpsert request, CancellationToken ct = default)
    {
        new Validator().Required(request.VersionLabel, "versionLabel", 50).ThrowIfInvalid();
        await access.EnsureCanEditAsync(productId, ct);
        var version = await db.ProductVersions.FirstOrDefaultAsync(v => v.Id == versionId && v.ProductId == productId, ct)
                      ?? throw new NotFoundException("Release", versionId);
        if (request.Version is { } rv) db.SetOriginalVersion(version, rv);
        version.VersionLabel = request.VersionLabel.Trim();
        version.ReleaseDate = request.ReleaseDate;
        version.IsReleased = request.IsReleased;
        // Keep the release-testing workspace's status in step with the product screen's "released" flag.
        if (request.IsReleased) version.Status = Domain.Quality.ReleaseStatus.Released;
        else if (version.Status == Domain.Quality.ReleaseStatus.Released) version.Status = Domain.Quality.ReleaseStatus.Testing;
        version.Notes = request.Notes.Clean();
        await db.SaveChangesAsync(ct);
        return new ProductVersionDto(version.Id, productId, version.VersionLabel, version.ReleaseDate, version.IsReleased, version.Notes, version.Version);
    }

    public async Task<IReadOnlyList<SequenceScopeDto>> ListScopesAsync(CancellationToken ct = default)
        => await db.SequenceScopes.OrderBy(s => s.Key)
            .Select(s => new SequenceScopeDto(s.Id, s.Key, s.Name, s.NextValue, s.DisplayWidth, s.Prefix)).ToListAsync(ct);

    private IQueryable<ProductDto> Project(IQueryable<Product> query)
        => query.Select(p => new ProductDto(p.Id, p.Code, p.Name, p.Description, p.DisplayOrder, p.IsActive, p.SequenceScopeId,
            p.SequenceScope!.Key, db.Features.Count(f => f.ProductId == p.Id), p.Version));

    private static void Validate(ProductUpsert r) => new Validator()
        .Required(r.Code, "code", 50)
        .Must(r.Code is null || System.Text.RegularExpressions.Regex.IsMatch(r.Code.Trim(), "^[A-Za-z0-9_-]+$"), "code", "Code may contain letters, digits, '-' and '_' only.")
        .Required(r.Name, "name", 200)
        .MaxLength(r.Description, "description", 4000)
        .ThrowIfInvalid();
}
