using System.Text;
using ClosedXML.Excel;
using Wings.Application.Abstractions;

namespace Wings.Infrastructure.Tabular;

public sealed class CsvCodec : ITabularCodec
{
    public string Format => "csv";
    public string ContentType => "text/csv";

    public byte[] Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows, string sheetName)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Escape)));
        foreach (var row in rows) sb.AppendLine(string.Join(",", row.Select(Escape)));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    public IReadOnlyList<Dictionary<string, string>> Read(Stream input)
    {
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var records = Parse(reader.ReadToEnd()).ToList();
        if (records.Count == 0) return [];
        var headers = records[0].Select(h => h.Trim()).ToArray();
        return records.Skip(1).Where(r => r.Any(c => c.Length > 0))
            .Select(r => headers.Select((h, i) => (h, v: i < r.Count ? r[i] : "")).ToDictionary(x => x.h, x => x.v, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Formula-injection safe: cells beginning with = + - @ are prefixed so spreadsheets treat them as text.</summary>
    private static string Escape(string? value)
    {
        value ??= "";
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static IEnumerable<List<string>> Parse(string text)
    {
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                yield return row;
                row = [];
            }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); yield return row; }
    }
}

public sealed class XlsxCodec : ITabularCodec
{
    public string Format => "xlsx";
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public byte[] Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows, string sheetName)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);
        for (var c = 0; c < headers.Count; c++) ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Count; c++) ws.Cell(r, c + 1).SetValue(row[c] ?? "");
            r++;
        }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(r, 200), 10, 80);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public IReadOnlyList<Dictionary<string, string>> Read(Stream input)
    {
        using var wb = new XLWorkbook(input);
        var ws = wb.Worksheets.First();
        var used = ws.RangeUsed();
        if (used is null) return [];
        var headers = used.FirstRow().Cells().Select(c => c.GetString().Trim()).ToArray();
        return used.RowsUsed().Skip(1)
            .Select(row => headers.Select((h, i) => (h, v: row.Cell(i + 1).GetString())).ToDictionary(x => x.h, x => x.v, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
}
