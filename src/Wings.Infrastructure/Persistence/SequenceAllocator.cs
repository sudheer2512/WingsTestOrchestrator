using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;

namespace Wings.Infrastructure.Persistence;

/// <summary>
/// Allocates creation sequences with an atomic UPDATE inside the caller's transaction. On SQL Server the UPDATE takes an
/// exclusive row lock held to commit, so concurrent allocators for the same scope serialize; SQLite serializes all writers.
/// Values only ever increase, so a number is never handed out twice, even after the record that used it is deleted.
/// </summary>
public sealed class SequenceAllocator(WingsDbContext db) : ISequenceAllocator
{
    public async Task<long> NextAsync(Guid scopeId, CancellationToken ct = default)
    {
        EnsureTransaction();
        var updated = await db.SequenceScopes.Where(s => s.Id == scopeId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextValue, x => x.NextValue + 1), ct);
        if (updated != 1) throw new NotFoundException("Sequence scope", scopeId);
        var next = await db.SequenceScopes.AsNoTracking().Where(s => s.Id == scopeId).Select(s => s.NextValue).SingleAsync(ct);
        return next - 1;
    }

    public async Task ReserveAsync(Guid scopeId, long value, CancellationToken ct = default)
    {
        EnsureTransaction();
        if (value < 1) throw new ValidationException("sequenceNumber", "Sequence numbers start at 1.");
        await db.SequenceScopes.Where(s => s.Id == scopeId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextValue, x => x.NextValue > value ? x.NextValue : value + 1), ct);
    }

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Sequence allocation must run inside the caller's database transaction.");
    }
}
