namespace Wings.Domain.Common;

/// <summary>Base for every business entity: immutable GUID identity, audit metadata and optimistic concurrency.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }

    /// <summary>Concurrency token. Incremented on every update; clients send it back to detect lost updates.</summary>
    public long Version { get; set; }
}

/// <summary>Entities that are never physically deleted.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}

/// <summary>Entities whose changes are written to the audit log.</summary>
public interface IAudited;
