namespace Wings.Domain.Administration;

public class AuditLog
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string UserName { get; set; } = "";
    /// <summary>Created, Updated, Deleted, or a domain action such as Moved, Approved, Published.</summary>
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    /// <summary>The aggregate this change belongs to (e.g. the feature that owns a requirement), so histories include child changes.</summary>
    public string? RootEntityType { get; set; }
    public Guid? RootEntityId { get; set; }
    /// <summary>JSON object of changed properties: {"Name":{"old":"A","new":"B"}}.</summary>
    public string? ChangesJson { get; set; }
    public string? Summary { get; set; }
    public string? CorrelationId { get; set; }
}

public enum BackgroundJobStatus { Pending = 0, Running = 1, Succeeded = 2, Failed = 3 }

/// <summary>Durable, database-backed job queue entry processed by background workers.</summary>
public class BackgroundJob
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Type { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public BackgroundJobStatus Status { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Restricts a user to specific products. A user with no rows has access to every product.</summary>
public class UserProductAccess
{
    public Guid UserId { get; set; }
    public Guid ProductId { get; set; }
    public bool CanEdit { get; set; }
}
