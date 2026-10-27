namespace Wings.Application.Common;

public class AppException(string message) : Exception(message);

public sealed class NotFoundException(string entity, object key) : AppException($"{entity} '{key}' was not found.");

public sealed class ConflictException(string message) : AppException(message);

public sealed class ForbiddenException(string message = "You do not have permission to perform this operation.") : AppException(message);

public sealed class ValidationException : AppException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(string field, string message) : base(message)
        => Errors = new Dictionary<string, string[]> { [field] = [message] };

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.") => Errors = errors;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record PageRequest(int Page = 1, int PageSize = 50)
{
    public int SafePage => Math.Max(1, Page);
    public int SafePageSize => Math.Clamp(PageSize, 1, 500);
    public int Skip => (SafePage - 1) * SafePageSize;
}

public static class SequenceFormat
{
    /// <summary>Zero-pads for display only; values wider than the width print in full, so capacity is unbounded.</summary>
    public static string Format(long value, int width = 5, string? prefix = null)
        => $"{prefix}{value.ToString().PadLeft(width, '0')}";
}

/// <summary>Collects field errors and throws a single <see cref="ValidationException"/>.</summary>
public sealed class Validator
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public Validator Required(string? value, string field, int maxLength = 0)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(field, $"{field} is required.");
        else if (maxLength > 0 && value.Length > maxLength) Add(field, $"{field} must be at most {maxLength} characters.");
        return this;
    }

    public Validator MaxLength(string? value, string field, int maxLength)
    {
        if (value is not null && value.Length > maxLength) Add(field, $"{field} must be at most {maxLength} characters.");
        return this;
    }

    public Validator Must(bool condition, string field, string message)
    {
        if (!condition) Add(field, message);
        return this;
    }

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
        list.Add(message);
    }

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0) throw new ValidationException(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }
}

/// <summary>Cycle detection over a directed "depends on" graph.</summary>
public static class DependencyGraph
{
    /// <summary>True if adding edge from → to would create a cycle (i.e. "to" already reaches "from").</summary>
    public static bool WouldCreateCycle(Guid from, Guid to, IEnumerable<(Guid From, Guid To)> edges)
    {
        if (from == to) return true;
        var adjacency = edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.Select(e => e.To).ToList());
        var stack = new Stack<Guid>([to]);
        var seen = new HashSet<Guid>();
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node == from) return true;
            if (!seen.Add(node) || !adjacency.TryGetValue(node, out var next)) continue;
            foreach (var n in next) stack.Push(n);
        }
        return false;
    }
}

/// <summary>
/// Case-insensitive "contains" that behaves the same on SQL Server and SQLite (string.Contains maps to the case-sensitive
/// instr() on SQLite). Use as EF.Functions.Like(column, Search.Like(text), Search.Escape).
/// </summary>
public static class Search
{
    public const string Escape = "\\";
    public static string Like(string text) => "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
