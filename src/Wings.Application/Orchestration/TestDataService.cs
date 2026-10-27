using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Domain.Orchestration;

namespace Wings.Application.Orchestration;

public sealed record DataSetDto(Guid Id, string Code, string Name, string? Description, string? Category, Guid? EnvironmentId, string? EnvironmentName,
    int DataVersion, int RecordCount, int ActiveRecords, int Reserved, string? SetupNotes, string? CleanupNotes, long Version, DateTimeOffset? ModifiedAt);

public sealed record DataSetUpsert(string Code, string Name, string? Description, string? Category, Guid? EnvironmentId, string? SetupNotes, string? CleanupNotes, long? Version = null);

public sealed record DataRecordDto(Guid Id, string Key, JsonElement Values, bool IsActive, bool IsReserved, string? ReservedByRun);

public sealed record DataRecordUpsert(string Key, JsonElement Values, bool IsActive = true);

public sealed record ReservationDto(Guid Id, string DataSetCode, string RecordKey, Guid RunId, string RunNumber, Guid JobId, DateTimeOffset ReservedAt, DateTimeOffset? ReleasedAt);

/// <summary>A parsed "DATASET[:count]" requirement.</summary>
public sealed record DataRequirement(string Code, int Count);

public interface ITestDataService
{
    Task<IReadOnlyList<DataSetDto>> ListAsync(CancellationToken ct = default);
    Task<DataSetDto> CreateAsync(DataSetUpsert request, CancellationToken ct = default);
    Task<DataSetDto> UpdateAsync(Guid id, DataSetUpsert request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<DataRecordDto>> RecordsAsync(Guid id, CancellationToken ct = default);
    Task<DataRecordDto> UpsertRecordAsync(Guid id, DataRecordUpsert request, CancellationToken ct = default);
    Task DeleteRecordAsync(Guid id, Guid recordId, CancellationToken ct = default);
    Task<ImportSummary> ImportAsync(Guid id, Stream content, string format, bool replace, CancellationToken ct = default);
    Task<IReadOnlyList<ReservationDto>> ReservationsAsync(bool activeOnly, CancellationToken ct = default);
    Task ReleaseAsync(Guid reservationId, CancellationToken ct = default);
}

public sealed record ImportSummary(int Created, int Updated, IReadOnlyList<string> Errors);

public sealed class TestDataService(IWingsDbContext db, IEnumerable<ITabularCodec> codecs, TimeProvider clock) : ITestDataService
{
    private static readonly Regex CodeRx = new("^[A-Z][A-Z0-9_]{0,49}$");

    /// <summary>Parses "CUSTOMERS:2, VEHICLES" into requirements; throws on malformed entries.</summary>
    public static IReadOnlyList<DataRequirement> Parse(string? text)
    {
        var list = new List<DataRequirement>();
        foreach (var part in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = part.Split(':', 2, StringSplitOptions.TrimEntries);
            var code = bits[0].ToUpperInvariant();
            var count = bits.Length == 2 ? int.TryParse(bits[1], out var n) ? n : -1 : 1;
            if (!CodeRx.IsMatch(code) || count is < 1 or > 100) throw new ValidationException("dataRequirements", $"'{part}' must be DATASET or DATASET:count (1–100).");
            list.Add(new DataRequirement(code, count));
        }
        return list.GroupBy(r => r.Code).Select(g => new DataRequirement(g.Key, g.Max(x => x.Count))).ToList();
    }

    public async Task<IReadOnlyList<DataSetDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.TestDataSets.AsNoTracking().OrderBy(d => d.Code).Select(d => new
        {
            d,
            Env = db.TestEnvironments.Where(e => e.Id == d.EnvironmentId).Select(e => e.Name).FirstOrDefault(),
            Count = db.TestDataRecords.Count(r => r.DataSetId == d.Id),
            Active = db.TestDataRecords.Count(r => r.DataSetId == d.Id && r.IsActive),
            Reserved = db.TestDataReservations.Count(r => r.DataSetId == d.Id && r.Active != null),
        }).ToListAsync(ct);
        return rows.Select(x => new DataSetDto(x.d.Id, x.d.Code, x.d.Name, x.d.Description, x.d.Category, x.d.EnvironmentId, x.Env, x.d.DataVersion, x.Count, x.Active,
            x.Reserved, x.d.SetupNotes, x.d.CleanupNotes, x.d.Version, x.d.ModifiedAt)).ToList();
    }

    public async Task<DataSetDto> CreateAsync(DataSetUpsert r, CancellationToken ct = default)
    {
        var code = Validate(r);
        if (await db.TestDataSets.IgnoreQueryFilters().AnyAsync(d => d.Code == code, ct)) throw new ValidationException("code", $"Data set '{code}' already exists.");
        var set = new TestDataSet { Code = code };
        Apply(set, r);
        db.TestDataSets.Add(set);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).Single(d => d.Id == set.Id);
    }

    public async Task<DataSetDto> UpdateAsync(Guid id, DataSetUpsert r, CancellationToken ct = default)
    {
        Validate(r);
        var set = await Load(id, ct);
        if (r.Version is { } v) db.SetOriginalVersion(set, v);
        Apply(set, r); // the code is fixed: test cases refer to it
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).Single(d => d.Id == id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var set = await Load(id, ct);
        if (await db.TestDataReservations.AnyAsync(r => r.DataSetId == id && r.Active != null, ct)) throw new ConflictException("Records of this data set are reserved by running jobs.");
        set.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DataRecordDto>> RecordsAsync(Guid id, CancellationToken ct = default)
    {
        await Load(id, ct);
        var rows = await db.TestDataRecords.AsNoTracking().Where(r => r.DataSetId == id).OrderBy(r => r.Key).Select(r => new
        {
            r,
            Run = db.TestDataReservations.Where(x => x.RecordId == r.Id && x.Active != null)
                .Join(db.ExecutionRuns, x => x.RunId, run => run.Id, (x, run) => (long?)run.Number).FirstOrDefault(),
        }).ToListAsync(ct);
        return rows.Select(x => new DataRecordDto(x.r.Id, x.r.Key, JsonDocument.Parse(x.r.ValuesJson).RootElement.Clone(), x.r.IsActive, x.Run is not null,
            x.Run is { } n ? Execution.ExecutionService.Format(n) : null)).ToList();
    }

    public async Task<DataRecordDto> UpsertRecordAsync(Guid id, DataRecordUpsert r, CancellationToken ct = default)
    {
        var set = await Load(id, ct);
        new Validator().Required(r.Key, "key", 200).Must(r.Values.ValueKind == JsonValueKind.Object, "values", "Values must be a JSON object.").ThrowIfInvalid();
        var key = r.Key.Trim();
        var record = await db.TestDataRecords.FirstOrDefaultAsync(x => x.DataSetId == id && x.Key == key, ct);
        if (record is null) db.TestDataRecords.Add(record = new TestDataRecord { DataSetId = id, Key = key, CreatedAt = clock.GetUtcNow() });
        record.ValuesJson = r.Values.GetRawText();
        record.IsActive = r.IsActive;
        Bump(set);
        await db.SaveChangesAsync(ct);
        return (await RecordsAsync(id, ct)).Single(x => x.Id == record.Id);
    }

    public async Task DeleteRecordAsync(Guid id, Guid recordId, CancellationToken ct = default)
    {
        var set = await Load(id, ct);
        var record = await db.TestDataRecords.FirstOrDefaultAsync(r => r.Id == recordId && r.DataSetId == id, ct) ?? throw new NotFoundException("Record", recordId);
        if (await db.TestDataReservations.AnyAsync(r => r.RecordId == recordId && r.Active != null, ct)) throw new ConflictException("The record is reserved by a running job.");
        db.TestDataRecords.Remove(record);
        Bump(set);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Rows need a Key column; other columns become field values. With replace, records missing from the file are deactivated.</summary>
    public async Task<ImportSummary> ImportAsync(Guid id, Stream content, string format, bool replace, CancellationToken ct = default)
    {
        var set = await Load(id, ct);
        var codec = codecs.FirstOrDefault(c => c.Format.Equals(format, StringComparison.OrdinalIgnoreCase)) ?? throw new ValidationException("format", "Supported formats are csv and xlsx.");
        var rows = codec.Read(content);
        var errors = new List<string>();
        if (rows.Count > 0 && !rows[0].ContainsKey("Key")) return new ImportSummary(0, 0, ["The file needs a 'Key' column."]);
        var keys = rows.Select((r, i) => (Key: r.GetValueOrDefault("Key")?.Trim(), Row: i + 2)).ToList();
        errors.AddRange(keys.Where(k => string.IsNullOrEmpty(k.Key)).Select(k => $"Row {k.Row}: Key is empty."));
        errors.AddRange(keys.Where(k => !string.IsNullOrEmpty(k.Key)).GroupBy(k => k.Key).Where(g => g.Count() > 1).Select(g => $"Key '{g.Key}' appears more than once."));
        if (errors.Count > 0) return new ImportSummary(0, 0, errors);

        var existing = await db.TestDataRecords.Where(r => r.DataSetId == id).ToDictionaryAsync(r => r.Key, ct);
        int created = 0, updated = 0;
        foreach (var row in rows)
        {
            var key = row["Key"].Trim();
            var values = JsonSerializer.Serialize(row.Where(kv => !kv.Key.Equals("Key", StringComparison.OrdinalIgnoreCase)).ToDictionary(kv => kv.Key, kv => kv.Value));
            if (existing.TryGetValue(key, out var rec)) { rec.ValuesJson = values; rec.IsActive = true; updated++; }
            else { db.TestDataRecords.Add(new TestDataRecord { DataSetId = id, Key = key, ValuesJson = values, CreatedAt = clock.GetUtcNow() }); created++; }
        }
        if (replace)
        {
            var incoming = rows.Select(r => r["Key"].Trim()).ToHashSet();
            foreach (var rec in existing.Values.Where(r => !incoming.Contains(r.Key))) rec.IsActive = false;
        }
        Bump(set);
        db.PendingAuditSummary = $"Imported {created} new and {updated} updated record(s)";
        await db.SaveChangesAsync(ct);
        return new ImportSummary(created, updated, []);
    }

    public async Task<IReadOnlyList<ReservationDto>> ReservationsAsync(bool activeOnly, CancellationToken ct = default)
    {
        var q = db.TestDataReservations.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(r => r.Active != null);
        var rows = await q.OrderByDescending(r => r.ReservedAt).Take(500)
            .Join(db.TestDataRecords, r => r.RecordId, rec => rec.Id, (r, rec) => new { r, rec.Key })
            .Join(db.TestDataSets.IgnoreQueryFilters(), x => x.r.DataSetId, d => d.Id, (x, d) => new { x.r, x.Key, d.Code })
            .Join(db.ExecutionRuns, x => x.r.RunId, run => run.Id, (x, run) => new { x.r, x.Key, x.Code, run.Number })
            .ToListAsync(ct);
        return rows.Select(x => new ReservationDto(x.r.Id, x.Code, x.Key, x.r.RunId, Execution.ExecutionService.Format(x.Number), x.r.JobId, x.r.ReservedAt, x.r.ReleasedAt)).ToList();
    }

    public async Task ReleaseAsync(Guid reservationId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var n = await db.TestDataReservations.Where(r => r.Id == reservationId && r.Active != null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Active, (int?)null).SetProperty(r => r.ReleasedAt, now), ct);
        if (n == 0) throw new NotFoundException("Active reservation", reservationId);
    }

    private async Task<TestDataSet> Load(Guid id, CancellationToken ct) => await db.TestDataSets.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Data set", id);

    private static void Bump(TestDataSet set) => set.DataVersion++;

    private static string Validate(DataSetUpsert r)
    {
        var code = (r.Code ?? "").Trim().ToUpperInvariant();
        new Validator().Required(r.Name, "name", 200).Must(CodeRx.IsMatch(code), "code", "Code must be UPPER_SNAKE_CASE (e.g. CUSTOMERS).").ThrowIfInvalid();
        return code;
    }

    private static void Apply(TestDataSet s, DataSetUpsert r)
    {
        s.Name = r.Name.Trim(); s.Description = r.Description.Clean(); s.Category = r.Category.Clean(); s.EnvironmentId = r.EnvironmentId;
        s.SetupNotes = r.SetupNotes.Clean(); s.CleanupNotes = r.CleanupNotes.Clean();
    }
}
