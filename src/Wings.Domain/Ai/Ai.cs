using Wings.Domain.Common;

namespace Wings.Domain.Ai;

public enum AiFeature { TestGeneration = 0, CodeGeneration = 1, FailureAnalysis = 2, TestSelection = 3, Maintenance = 4, TestRepair = 5 }
public enum AiInteractionStatus { Succeeded = 0, Failed = 1, Refused = 2 }

/// <summary>
/// One request to the AI provider: who asked, what was sent (size, hash, redactions; never the raw text), which model answered,
/// token usage, the structured result, and what people did with it. Suggestions only become data through normal, reviewed workflows.
/// </summary>
public class AiInteraction
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public AiFeature Feature { get; set; }
    public string UserName { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string? SubjectType { get; set; }
    public Guid? SubjectId { get; set; }
    public string? SubjectLabel { get; set; }
    public AiInteractionStatus Status { get; set; }
    public string? Error { get; set; }
    /// <summary>Number of secrets and sensitive values masked before sending.</summary>
    public int RedactionCount { get; set; }
    public string InputSha256 { get; set; } = "";
    public int InputChars { get; set; }
    public int ImageCount { get; set; }
    /// <summary>JSON list of the context sections sent (name and size), so reviewers can see what the model was given.</summary>
    public string? ContextManifestJson { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public long DurationMs { get; set; }
    /// <summary>Verified facts (computed by the platform) and AI suggestions, kept apart.</summary>
    public string? ResultJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Outcome { get; set; }
    public string? OutcomeBy { get; set; }
    public DateTimeOffset? OutcomeAt { get; set; }
    public string? OutcomeNote { get; set; }
    /// <summary>Records created from the suggestion (test cases, package versions, comments), as JSON.</summary>
    public string? AppliedJson { get; set; }
}

/// <summary>Administrator rules for the AI assistant (one row). Provider credentials live in configuration, not here.</summary>
public class AiPolicy : Entity, IAudited
{
    public bool IsEnabled { get; set; }
    /// <summary>Test source files (packages) may be sent for code generation, maintenance and repair.</summary>
    public bool AllowSourceCode { get; set; }
    /// <summary>Failure screenshots may be sent for analysis (binary evidence cannot be masked).</summary>
    public bool AllowScreenshots { get; set; }
    public bool MaskEmails { get; set; } = true;
    public bool MaskPhoneNumbers { get; set; } = true;
    /// <summary>Extra regular expressions (one per line) for business data to mask, e.g. customer or account numbers.</summary>
    public string? AdditionalRedactionPatterns { get; set; }
    public int DailyRequestsPerUser { get; set; } = 50;
    /// <summary>House conventions given to code generation (shared libraries, naming, page objects).</summary>
    public string? ProjectConventions { get; set; }
}
