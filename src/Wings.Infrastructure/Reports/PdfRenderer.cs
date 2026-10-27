using System.Globalization;
using System.IO.Compression;
using System.Text;
using Wings.Application.Abstractions;

namespace Wings.Infrastructure.Reports;

/// <summary>
/// Minimal PDF 1.4 writer for reports: the 14 standard fonts need no embedding, so output is small and has no third-party
/// dependency. Text is WinAnsi (Windows-1252); characters outside it are replaced with close ASCII equivalents.
/// </summary>
public sealed class PdfRenderer : IPdfRenderer
{
    private const double Margin = 40;

    public byte[] Render(PdfDocumentModel doc) => new Layout(doc).Build();

    private sealed class Layout
    {
        private readonly PdfDocumentModel _doc;
        private readonly double _w, _h;
        private readonly List<StringBuilder> _pages = [];
        private StringBuilder _page = null!;
        private double _y;

        public Layout(PdfDocumentModel doc)
        {
            _doc = doc;
            (_w, _h) = doc.Landscape ? (842d, 595d) : (595d, 842d);
        }

        private double Width => _w - 2 * Margin;

        public byte[] Build()
        {
            NewPage();
            Write(Margin, 16, true, _doc.Title, Width);
            if (_doc.Subtitle is { } sub) { _y -= 4; Gray(() => Write(Margin, 9, false, sub, Width)); }
            Rule(_y - 4);
            _y -= 14;
            foreach (var block in _doc.Blocks)
            {
                switch (block)
                {
                    case PdfBlock.Heading h:
                        Ensure(40);
                        _y -= 8;
                        Write(Margin, 12, true, h.Text, Width);
                        _y -= 4;
                        break;
                    case PdfBlock.Paragraph p:
                        Write(Margin, 9, false, p.Text, Width);
                        _y -= 3;
                        break;
                    case PdfBlock.KeyValues kv:
                        foreach (var (label, value) in kv.Rows) KeyValue(label, value ?? "-");
                        _y -= 4;
                        break;
                    case PdfBlock.Table t:
                        Table(t);
                        _y -= 6;
                        break;
                }
            }
            return Serialize();
        }

        private void NewPage()
        {
            _page = new StringBuilder();
            _pages.Add(_page);
            _y = _h - Margin;
        }

        private void Ensure(double needed)
        {
            if (_y - needed < Margin + 16) NewPage();
        }

        private void Gray(Action draw) { _page.Append("0.4 g\n"); draw(); _page.Append("0 g\n"); }

        private void Rule(double y) => _page.Append($"0.75 G 0.5 w {F(Margin)} {F(y)} m {F(_w - Margin)} {F(y)} l S 0 G\n");

        /// <summary>Writes wrapped text starting at the current line and moves the cursor below it.</summary>
        private void Write(double x, double size, bool bold, string text, double width)
        {
            foreach (var line in Wrap(text, width, size, bold))
            {
                Ensure(size + 2);
                _y -= size + 2;
                Text(x, _y, size, bold, line);
            }
        }

        private void KeyValue(string label, string value)
        {
            var labelWidth = Width * 0.36;
            var labels = Wrap(label, labelWidth - 6, 9, true);
            var values = Wrap(value, Width - labelWidth, 9, false);
            var lines = Math.Max(labels.Count, values.Count);
            Ensure(lines * 11 + 2);
            for (var i = 0; i < lines; i++)
            {
                _y -= 11;
                if (i < labels.Count) Text(Margin, _y, 9, true, labels[i]);
                if (i < values.Count) Text(Margin + labelWidth, _y, 9, false, values[i]);
            }
            _y -= 2;
        }

        private void Table(PdfBlock.Table t)
        {
            const double size = 7.5, pad = 3, line = 9.5;
            var cols = t.Headers.Count;
            if (cols == 0) return;
            var weights = t.Weights is { } w && w.Count == cols ? w.ToArray() : AutoWeights(t);
            var total = weights.Sum();
            var widths = weights.Select(x => Width * x / total).ToArray();

            void Header()
            {
                var cells = t.Headers.Select((h, i) => Wrap(h, widths[i] - 2 * pad, size, true)).ToList();
                var height = cells.Max(c => c.Count) * line + 2 * pad;
                Ensure(height + line * 2);
                _page.Append($"0.92 g {F(Margin)} {F(_y - height)} {F(Width)} {F(height)} re f 0 g\n");
                Cells(cells, widths, size, true, height, pad, line);
            }

            Header();
            if (t.Rows.Count == 0) { _y -= line; Gray(() => Text(Margin + pad, _y, size, false, "(no rows)")); _y -= 4; return; }
            foreach (var row in t.Rows)
            {
                var cells = Enumerable.Range(0, cols).Select(i => Wrap(i < row.Count ? row[i] ?? "" : "", widths[i] - 2 * pad, size, false).Take(12).ToList()).ToList();
                var height = Math.Max(1, cells.Max(c => c.Count)) * line + 2 * pad;
                if (_y - height < Margin + 16) { NewPage(); Header(); }
                Cells(cells, widths, size, false, height, pad, line);
                _page.Append($"0.85 G 0.3 w {F(Margin)} {F(_y)} m {F(_w - Margin)} {F(_y)} l S 0 G\n");
            }
        }

        private void Cells(List<List<string>> cells, double[] widths, double size, bool bold, double height, double pad, double line)
        {
            var x = Margin;
            for (var i = 0; i < cells.Count; i++)
            {
                for (var l = 0; l < cells[i].Count; l++) Text(x + pad, _y - pad - (l + 1) * line + 2, size, bold, cells[i][l]);
                x += widths[i];
            }
            _y -= height;
        }

        private static double[] AutoWeights(PdfBlock.Table t)
            => Enumerable.Range(0, t.Headers.Count).Select(i =>
            {
                var lengths = t.Rows.Take(200).Select(r => i < r.Count ? (r[i] ?? "").Length : 0).Append(t.Headers[i].Length).ToList();
                return Math.Clamp(lengths.Average() * 0.7 + lengths.Max() * 0.3, 4, 60);
            }).ToArray();

        private void Text(double x, double y, double size, bool bold, string text)
            => _page.Append($"BT /{(bold ? "F2" : "F1")} {F(size)} Tf {F(x)} {F(y)} Td ({Escape(text)}) Tj ET\n");

        // ---- serialization ----

        private byte[] Serialize()
        {
            using var ms = new MemoryStream();
            var offsets = new List<long>();
            void Raw(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b); }
            void Obj(string body) { offsets.Add(ms.Position); Raw($"{offsets.Count} 0 obj\n{body}\nendobj\n"); }

            Raw("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
            var pageCount = _pages.Count;
            // Object numbers: 1 catalog, 2 pages, 3-4 fonts, 5 info, then (page, content) pairs.
            var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{6 + 2 * i} 0 R"));
            Obj("<< /Type /Catalog /Pages 2 0 R >>");
            Obj($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");
            Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
            Obj($"<< /Title ({Escape(_doc.Title)}) /Producer (Wings Test Orchestrator) /CreationDate (D:{DateTime.UtcNow:yyyyMMddHHmmss}Z) >>");
            for (var i = 0; i < pageCount; i++)
            {
                var content = _pages[i];
                var footer = $"{_doc.Footer}{(_doc.Footer is null ? "" : "   ·   ")}Page {i + 1} of {pageCount}";
                content.Append($"0.5 g BT /F1 7 Tf {F(Margin)} {F(Margin - 14)} Td ({Escape(footer)}) Tj ET 0 g\n");
                Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(_w)} {F(_h)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {7 + 2 * i} 0 R >>");
                var bytes = Deflate(Encoding.Latin1.GetBytes(content.ToString()));
                offsets.Add(ms.Position);
                Raw($"{offsets.Count} 0 obj\n<< /Length {bytes.Length} /Filter /FlateDecode >>\nstream\n");
                ms.Write(bytes);
                Raw("\nendstream\nendobj\n");
            }
            var xref = ms.Position;
            Raw($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
            foreach (var o in offsets) Raw($"{o:D10} 00000 n \n");
            Raw($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return ms.ToArray();
        }

        private static byte[] Deflate(byte[] data)
        {
            using var output = new MemoryStream();
            using (var z = new ZLibStream(output, CompressionLevel.Optimal)) z.Write(data);
            return output.ToArray();
        }
    }

    // ---- text helpers ----

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Maps to Windows-1252 code points (as Latin-1 chars for writing) and escapes PDF string delimiters.</summary>
    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in Normalize(s))
            sb.Append(ch switch { '(' => "\\(", ')' => "\\)", '\\' => "\\\\", _ => ch.ToString() });
        return sb.ToString();
    }

    private static readonly Dictionary<char, string> Replacements = new()
    {
        ['→'] = "->", ['←'] = "<-", ['↔'] = "<->", ['≥'] = ">=", ['≤'] = "<=", ['✓'] = "v", ['✗'] = "x", ['\t'] = "    ", [' '] = " ",
    };

    private static readonly Dictionary<char, char> WinAnsi = new()
    {
        ['€'] = '\x80', ['‚'] = '\x82', ['„'] = '\x84', ['…'] = '\x85', ['‘'] = '\x91', ['’'] = '\x92', ['“'] = '\x93', ['”'] = '\x94', ['•'] = '\x95',
        ['–'] = '\x96', ['—'] = '\x97', ['™'] = '\x99', ['›'] = '\x9B', ['‹'] = '\x8B',
    };

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (Replacements.TryGetValue(ch, out var rep)) sb.Append(rep);
            else if (WinAnsi.TryGetValue(ch, out var w)) sb.Append(w);
            else if (ch is >= ' ' and <= '~' or >= '\xA0' and <= '\xFF') sb.Append(ch);
            else if (ch is '\r' or '\n') sb.Append(' ');
            else sb.Append('?');
        }
        return sb.ToString();
    }

    private static List<string> Wrap(string text, double width, double size, bool bold)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var current = new StringBuilder();
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (Measure(candidate, size, bold) <= width) { current.Clear().Append(candidate); continue; }
                if (current.Length > 0) { lines.Add(current.ToString()); current.Clear(); }
                // A single word wider than the column is broken by characters.
                var w = word;
                while (Measure(w, size, bold) > width && w.Length > 1)
                {
                    var n = w.Length - 1;
                    while (n > 1 && Measure(w[..n], size, bold) > width) n--;
                    lines.Add(w[..n]);
                    w = w[n..];
                }
                current.Append(w);
            }
            lines.Add(current.ToString());
        }
        return lines;
    }

    private static double Measure(string s, double size, bool bold)
    {
        var widths = bold ? BoldWidths : RegularWidths;
        double total = 0;
        foreach (var ch in Normalize(s)) total += ch is >= ' ' and <= '~' ? widths[ch - 32] : 556;
        return total * size / 1000;
    }

    // Adobe AFM advance widths for code points 32–126.
    private static readonly int[] RegularWidths =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    private static readonly int[] BoldWidths =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611, 611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    ];
}
