using Wings.Domain.Common;

namespace Wings.Domain.Features;

public class Product : Entity, IAudited, ISoftDeletable
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Numbering scope used to allocate feature creation sequences for this product.</summary>
    public Guid SequenceScopeId { get; set; }
    public SequenceScope? SequenceScope { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public List<ProductVersion> Versions { get; set; } = [];
}

/// <summary>A release of a product (e.g. "Wings Auto 2026.3"). Features record the release that introduced them.</summary>
public class ProductVersion : Entity, IAudited
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public string VersionLabel { get; set; } = "";
    public DateOnly? ReleaseDate { get; set; }
    public bool IsReleased { get; set; }
    public string? Notes { get; set; }

    // Release testing workspace
    public Quality.ReleaseStatus Status { get; set; }
    /// <summary>Start of the release's testing window; defaults to the previous release's date.</summary>
    public DateOnly? TestingStartDate { get; set; }
    /// <summary>Release candidate build under test.</summary>
    public string? BuildVersion { get; set; }
    public string? KnownRisks { get; set; }
}

/// <summary>
/// A numbering scope for creation sequences. Values are allocated transactionally,
/// only ever increase, and are never reused even when the owning record is deleted.
/// </summary>
public class SequenceScope
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>The next value that will be handed out.</summary>
    public long NextValue { get; set; } = 1;
    public int DisplayWidth { get; set; } = 5;
    public string? Prefix { get; set; }
}
