using System.Text.Json;
using System.Text.RegularExpressions;
using Wings.Application.Common;

namespace Wings.Application.Ai;

public sealed class AiOptions
{
    /// <summary>"Anthropic" or "None". Other providers plug in by implementing <see cref="IAiModelClient"/>.</summary>
    public string Provider { get; set; } = "Anthropic";
    public string Model { get; set; } = "claude-opus-5-5";
    /// <summary>low, medium, high, xhigh or max.</summary>
    public string Effort { get; set; } = "medium";
    public int MaxTokens { get; set; } = 16000;
    /// <summary>Optional; otherwise the ANTHROPIC_API_KEY environment variable. Keep it in a secret store.</summary>
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
    /// <summary>Re-serve a refused request on a fallback model (server-side, routed by refusal category).</summary>
    public bool RefusalFallbacks { get; set; } = true;
    /// <summary>Upper bound on the text sent per request; context is selected to fit, never cut silently.</summary>
    public int MaxInputChars { get; set; } = 400_000;
}

public sealed record AiImage(string MediaType, byte[] Data, string Label);

/// <summary>One structured request: instructions, the context document, optional images, and the JSON schema the answer must follow.</summary>
public sealed record AiModelRequest(string System, string UserText, JsonElement Schema, IReadOnlyList<AiImage> Images, int? MaxTokens = null);

public sealed record AiModelResponse(string Json, string Model, long? InputTokens, long? OutputTokens);

/// <summary>A configurable LLM provider. The platform never sends a request without going through <see cref="AiAssistant"/>'s policy and redaction.</summary>
public interface IAiModelClient
{
    string Provider { get; }
    string Model { get; }
    /// <summary>Why the provider cannot be used (missing key, disabled), or null when ready.</summary>
    string? Unavailable { get; }
    Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken ct);
}

public sealed class AiRefusedException(string message) : AppException(message);
public sealed class AiProviderException(string message) : AppException(message);

/// <summary>Used when no provider is configured: every request explains how to configure one.</summary>
public sealed class UnconfiguredAiModelClient(string reason) : IAiModelClient
{
    public string Provider => "None";
    public string Model => "";
    public string? Unavailable => reason;
    public Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken ct) => throw new AiProviderException(reason);
}

/// <summary>
/// Masks secrets and sensitive business data before text leaves the platform: environment secret values, tokens and keys,
/// passwords in connection strings, JWTs, private keys, optionally emails and phone numbers, and administrator patterns.
/// </summary>
public sealed partial class AiRedactor
{
    private readonly List<string> _literals;
    private readonly List<(Regex Pattern, string Label)> _patterns = [];
    public int Count { get; private set; }

    public AiRedactor(IEnumerable<string> secretValues, bool maskEmails, bool maskPhones, string? additionalPatterns)
    {
        // Longest first, so a secret containing another is masked whole.
        _literals = secretValues.Where(s => s.Length >= 4).Distinct().OrderByDescending(s => s.Length).ToList();
        _patterns.Add((PrivateKey(), "PRIVATE-KEY"));
        _patterns.Add((Jwt(), "TOKEN"));
        _patterns.Add((KnownKeys(), "KEY"));
        _patterns.Add((Bearer(), "TOKEN"));
        _patterns.Add((AssignedSecret(), "SECRET"));
        if (maskEmails) _patterns.Add((Email(), "EMAIL"));
        if (maskPhones) _patterns.Add((Phone(), "PHONE"));
        foreach (var line in (additionalPatterns ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            try { _patterns.Add((new Regex(line, RegexOptions.Compiled, TimeSpan.FromMilliseconds(250)), "MASKED")); } catch (ArgumentException) { }
    }

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        foreach (var s in _literals)
        {
            var n = CountOf(text, s);
            if (n == 0) continue;
            Count += n;
            text = text.Replace(s, "[REDACTED:SECRET]", StringComparison.Ordinal);
        }
        foreach (var (pattern, label) in _patterns)
            text = pattern.Replace(text, m =>
            {
                Count++;
                // Keep the key name of "password=…" style assignments so the context still reads naturally.
                return m.Groups["key"].Success ? $"{m.Groups["key"].Value}[REDACTED:{label}]" : $"[REDACTED:{label}]";
            });
        return text;
    }

    private static int CountOf(string text, string value)
    {
        int count = 0, i = 0;
        while ((i = text.IndexOf(value, i, StringComparison.Ordinal)) >= 0) { count++; i += value.Length; }
        return count;
    }

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----")] private static partial Regex PrivateKey();
    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}")] private static partial Regex Jwt();
    [GeneratedRegex(@"\b(?:sk-ant-[A-Za-z0-9_-]{10,}|sk-[A-Za-z0-9_-]{20,}|ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|wta_[A-Za-z0-9_-]{10,}|wik_[A-Za-z0-9_-]{10,}|AKIA[0-9A-Z]{16}|xox[baprs]-[A-Za-z0-9-]{10,})")] private static partial Regex KnownKeys();
    [GeneratedRegex(@"(?<key>\b(?:Bearer|Basic)\s+)[A-Za-z0-9._~+/=-]{12,}", RegexOptions.IgnoreCase)] private static partial Regex Bearer();
    [GeneratedRegex(@"(?<key>\b(?:password|passwd|pwd|secret|api[_-]?key|access[_-]?token|client[_-]?secret)\s*[=:]\s*[""']?)[^\s;""',]{3,}", RegexOptions.IgnoreCase)] private static partial Regex AssignedSecret();
    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")] private static partial Regex Email();
    // International (+country), separated (555 123 4567 / (555) 123-4567) or 10-digit mobile numbers; plain amounts and durations are left alone.
    [GeneratedRegex(@"\+\d{1,3}[ -]?\d{2,5}[ -]?\d{3,5}[ -]?\d{3,5}\b|\(?\b\d{3}\)?[ -]\d{3}[ -]\d{4}\b|\b[6-9]\d{9}\b")] private static partial Regex Phone();
}

/// <summary>Builds JSON Schemas for structured outputs: every property required, no additional properties.</summary>
public static class AiSchema
{
    public static object Str(string? description = null) => description is null ? new { type = "string" } : new { type = "string", description };
    public static object Num(string? description = null) => description is null ? new { type = "number" } : new { type = "number", description };
    public static object Bool() => new { type = "boolean" };
    public static object Enum(params string[] values) => new { type = "string", @enum = values };
    public static object Arr(object items) => new { type = "array", items };
    public static object Obj(params (string Name, object Schema)[] properties)
        => new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties.ToDictionary(p => p.Name, p => p.Schema),
            ["required"] = properties.Select(p => p.Name).ToArray(),
            ["additionalProperties"] = false,
        };
    public static JsonElement Build(object schema) => JsonSerializer.SerializeToElement(schema);
}
