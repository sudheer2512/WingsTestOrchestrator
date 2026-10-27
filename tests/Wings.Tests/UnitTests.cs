using System.Text;
using Wings.Application.Common;
using Wings.Application.Features;
using Wings.Application.Testing;
using Wings.Infrastructure.Packages;
using Wings.Infrastructure.Tabular;
using Xunit;

namespace Wings.Tests;

public sealed class UnitTests
{
    [Theory]
    [InlineData(7, 5, null, "00007")]
    [InlineData(125, 5, null, "00125")]
    [InlineData(1234567, 5, null, "1234567")]
    [InlineData(42, 5, "TC-", "TC-00042")]
    public void SequenceFormat_PadsForDisplayOnly(long value, int width, string? prefix, string expected)
        => Assert.Equal(expected, SequenceFormat.Format(value, width, prefix));

    [Fact]
    public void DependencyGraph_DetectsCycles()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        (Guid, Guid)[] edges = [(a, b), (b, c)];
        Assert.True(DependencyGraph.WouldCreateCycle(c, a, edges));
        Assert.True(DependencyGraph.WouldCreateCycle(a, a, edges));
        Assert.False(DependencyGraph.WouldCreateCycle(a, c, edges));
    }

    [Theory]
    [InlineData("src/Tests.cs", "src/Tests.cs")]
    [InlineData("src\\Tests.cs", "src/Tests.cs")]
    [InlineData("a//b.cs", "a/b.cs")]
    [InlineData("../x.cs", null)]
    [InlineData("a/../../x.cs", null)]
    [InlineData("/etc/passwd", null)]
    [InlineData("C:/Windows/x.dll", null)]
    [InlineData("", null)]
    public void ArchivePath_RejectsTraversal(string input, string? expected) => Assert.Equal(expected, ArchivePath.Normalize(input));

    [Fact]
    public void FeaturePath_RoundTrips()
    {
        var path = FeaturePath.Build(FeaturePath.Build(null, 125), 146);
        Assert.Equal("/125/146/", path);
        Assert.Equal([125L, 146L], FeaturePath.Parse(path));
    }

    [Fact]
    public void Csv_RoundTripsQuotesNewlinesAndNeutralisesFormulas()
    {
        var codec = new CsvCodec();
        var bytes = codec.Write(["Name", "Steps"], [["=HYPERLINK(\"x\")", "1. a, b\n2. \"c\""]], "x");
        var rows = codec.Read(new MemoryStream(bytes));
        Assert.Equal("'=HYPERLINK(\"x\")", rows[0]["Name"]);
        Assert.Equal("1. a, b\n2. \"c\"", rows[0]["Steps"]);
    }

    [Fact]
    public void LimitedReadStream_StopsAtLimit()
    {
        using var s = new LimitedReadStream(new MemoryStream(new byte[100]), 50);
        Assert.Throws<LimitExceededException>(() => s.CopyTo(Stream.Null));
    }

    [Fact]
    public async Task BuiltInScanner_FlagsNativeExecutables()
    {
        var pe = new byte[128];
        pe[0] = (byte)'M'; pe[1] = (byte)'Z'; pe[0x3C] = 64; pe[64] = (byte)'P'; pe[65] = (byte)'E';
        var findings = await new BuiltInContentScanner().ScanAsync("data.bin", pe, CancellationToken.None);
        Assert.Contains(findings, f => f.Message.Contains("Native executable"));
        Assert.Empty(await new BuiltInContentScanner().ScanAsync("notes.txt", "MZ is just text here"u8.ToArray(), CancellationToken.None));
    }

    [Fact]
    public void Analyzer_DiscoversPlaywrightNesting()
    {
        var analyzer = new PackageAnalyzer();
        analyzer.Analyze("e2e/invoice.spec.ts", """
            import { test } from '@playwright/test';
            test.describe('Invoices', () => {
              test.describe('GST { braces in title }', () => {
                test('intra-state @tc-42', async ({ page }) => { await page.goto('/'); });
              });
              test('list loads', async () => {});
            });
            test('outside', async () => {});
            """);
        Assert.Equal(
            ["e2e/invoice.spec.ts > Invoices > GST { braces in title } > intra-state @tc-42", "e2e/invoice.spec.ts > Invoices > list loads", "e2e/invoice.spec.ts > outside"],
            analyzer.Tests.Select(t => t.Identifier));
        Assert.Equal(42, analyzer.Tests[0].TestCaseNumberHint);
        Assert.Contains("PLAYWRIGHT_TS", analyzer.Frameworks);
    }

    [Fact]
    public void Analyzer_ParsesStepText()
    {
        var steps = TestCaseService.ParseSteps("1. Open form => Form opens\n2) Save\nVerify ledger => Balanced");
        Assert.Equal(["Open form", "Save", "Verify ledger"], steps.Select(s => s.Action));
        Assert.Equal(["Form opens", null, "Balanced"], steps.Select(s => s.ExpectedResult));
    }
}
