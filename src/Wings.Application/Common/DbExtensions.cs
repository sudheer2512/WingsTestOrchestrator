using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;

namespace Wings.Application.Common;

public static class DbExtensions
{
    /// <summary>Runs work in a transaction through the provider's execution strategy (safe with retry-on-failure).</summary>
    public static Task<T> InTransactionAsync<T>(this IWingsDbContext db, Func<Task<T>> work, CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            if (db.Database.CurrentTransaction is not null) return await work();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        });
    }

    public static async Task InTransactionAsync(this IWingsDbContext db, Func<Task> work, CancellationToken ct = default)
        => await db.InTransactionAsync(async () => { await work(); return true; }, ct);

    public static string? Clean(this string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
