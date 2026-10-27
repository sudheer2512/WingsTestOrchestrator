using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Wings.Application.Testing;
using Wings.Domain.Features;

namespace Wings.Infrastructure.Packages;

public sealed record DiscoveredTest(string Identifier, string DisplayName, string FilePath, int Line, string Framework, Dictionary<string, List<string>> Traits)
{
    /// <summary>Feature sequence referenced by convention: [Trait("WingsFeature","00146")], [Category("WingsFeature:146")], @feature-00146.</summary>
    public long? FeatureSequenceHint => FirstNumber("WingsFeature");
    /// <summary>Test case number referenced by convention: [Trait("WingsTestCase","TC-00042")] or @tc-42.</summary>
    public long? TestCaseNumberHint => FirstNumber("WingsTestCase");

    private long? FirstNumber(string key)
        => Traits.TryGetValue(key, out var values) && values.Select(v => Regex.Match(v, @"\d+")).FirstOrDefault(m => m.Success) is { } m && long.TryParse(m.Value, out var n) ? n : null;
}

public static class FileKinds
{
    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "csharp", [".csproj"] = "xml", [".props"] = "xml", [".targets"] = "xml", [".config"] = "xml", [".runsettings"] = "xml", [".xml"] = "xml",
        [".resx"] = "xml", [".ts"] = "typescript", [".tsx"] = "typescript", [".js"] = "javascript", [".mjs"] = "javascript", [".cjs"] = "javascript",
        [".py"] = "python", [".json"] = "json", [".yaml"] = "yaml", [".yml"] = "yaml", [".md"] = "markdown", [".txt"] = "plaintext", [".sln"] = "plaintext",
        [".slnx"] = "xml", [".toml"] = "plaintext", [".cfg"] = "plaintext", [".ini"] = "plaintext", [".feature"] = "plaintext", [".sql"] = "sql",
        [".ps1"] = "powershell", [".sh"] = "shell", [".csv"] = "plaintext", [".html"] = "html", [".css"] = "css",
    };

    public static string LanguageFor(string path) => Languages.GetValueOrDefault(Path.GetExtension(path), "plaintext");

    /// <summary>Text = no NUL bytes in the first 8 KB and valid UTF-8 throughout.</summary>
    public static bool IsText(byte[] content)
    {
        if (content.AsSpan(0, Math.Min(content.Length, 8192)).IndexOf((byte)0) >= 0) return false;
        try { _ = new UTF8Encoding(false, true).GetCharCount(content); return true; }
        catch (DecoderFallbackException) { return false; }
    }

    public static string Decode(byte[] content) => Encoding.UTF8.GetString(content).TrimStart('﻿');
}

/// <summary>Static, non-executing analysis of package files: syntax checks, project-file review, framework detection and test discovery.</summary>
public sealed class PackageAnalyzer
{
    public List<DiscoveredTest> Tests { get; } = [];
    public HashSet<string> Frameworks { get; } = new(StringComparer.Ordinal);
    public List<ValidationFinding> SyntaxFindings { get; } = [];
    public List<ValidationFinding> ProjectFindings { get; } = [];
    public HashSet<string> Languages { get; } = [];
    public bool HasCSharp { get; private set; }
    public bool HasCsproj { get; private set; }
    public bool HasPackageJson { get; private set; }
    public bool HasJsTests { get; private set; }

    private static readonly HashSet<string> XunitAttrs = ["Fact", "Theory", "SkippableFact", "SkippableTheory", "StaFact", "WinFormsFact", "UIFact"];
    private static readonly HashSet<string> NunitAttrs = ["Test", "TestCase", "TestCaseSource", "Theory"];
    private static readonly HashSet<string> MstestAttrs = ["TestMethod", "DataTestMethod", "UITestMethod"];

    public void Analyze(string path, string text)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var file = Path.GetFileName(path);
        Languages.Add(FileKinds.LanguageFor(path));
        switch (ext)
        {
            case ".cs": HasCSharp = true; AnalyzeCSharp(path, text); break;
            case ".csproj": HasCsproj = true; AnalyzeCsproj(path, text); break;
            case ".props" or ".targets" or ".config" or ".runsettings" or ".slnx" or ".xml": CheckXml(path, text, file); break;
            case ".json": AnalyzeJson(path, text, file); break;
            case ".yaml" or ".yml": AnalyzeYaml(path, text); break;
            case ".ts" or ".js" or ".mjs" or ".cjs" or ".tsx": AnalyzeJs(path, text); break;
            case ".py": AnalyzePython(path, text); break;
        }
        if (file.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase) || file.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase))
        {
            if (Regex.IsMatch(text, @"(?im)^\s*[""']?pytest\b")) Frameworks.Add("PYTEST");
            if (Regex.IsMatch(text, @"(?im)^\s*[""']?(pytest-)?playwright\b")) Frameworks.Add("PLAYWRIGHT_PY");
        }
    }

    // ---- C# -----------------------------------------------------------------------------------------------------

    private void AnalyzeCSharp(string path, string text)
    {
        var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview), path);
        foreach (var d in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Take(50))
            SyntaxFindings.Add(new("error", $"{d.Id}: {d.GetMessage()}", path, d.Location.GetLineSpan().StartLinePosition.Line + 1));

        var root = tree.GetCompilationUnitRoot();
        if (root.Usings.Any(u => u.Name?.ToString().StartsWith("FlaUI", StringComparison.Ordinal) == true)) Frameworks.Add("FLAUI");
        if (root.Usings.Any(u => u.Name?.ToString().StartsWith("Microsoft.Playwright", StringComparison.Ordinal) == true)) Frameworks.Add("PLAYWRIGHT_CS");

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var attrs = method.AttributeLists.SelectMany(a => a.Attributes).ToList();
            var names = attrs.Select(AttrName).ToList();
            string? framework = names.Any(XunitAttrs.Contains) && !names.Contains("Test") ? "XUNIT"
                : names.Any(MstestAttrs.Contains) ? "MSTEST"
                : names.Any(NunitAttrs.Contains) ? "NUNIT" : null;
            if (framework is null || method.Parent is not TypeDeclarationSyntax type) continue;

            var ns = string.Join('.', method.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()));
            var typeChain = string.Join('.', method.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(t => t.Identifier.Text));
            var identifier = string.Join('.', new[] { ns, typeChain, method.Identifier.Text }.Where(s => s.Length > 0));

            var traits = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in method.AttributeLists.Concat(type.AttributeLists).SelectMany(l => l.Attributes))
            {
                var args = a.ArgumentList?.Arguments.Select(x => x.Expression is LiteralExpressionSyntax l ? l.Token.ValueText : null).ToList() ?? [];
                switch (AttrName(a))
                {
                    case "Trait" or "Property" when args.Count == 2 && args[0] is { } k && args[1] is { } v: AddTrait(traits, k, v); break;
                    case "Category" or "TestCategory" when args.Count == 1 && args[0] is { } c: AddCategory(traits, c); break;
                    case "WingsFeature" when args.Count >= 1 && args[0] is { } f: AddTrait(traits, "WingsFeature", f); break;
                    case "WingsTestCase" when args.Count >= 1 && args[0] is { } t: AddTrait(traits, "WingsTestCase", t); break;
                }
            }
            var display = attrs.Select(a => a.ArgumentList?.Arguments.FirstOrDefault(x => x.NameEquals?.Name.Identifier.Text is "DisplayName" or "Description"))
                .FirstOrDefault(x => x is not null)?.Expression is LiteralExpressionSyntax lit ? lit.Token.ValueText : Humanize(method.Identifier.Text);
            Frameworks.Add(framework);
            Tests.Add(new DiscoveredTest(identifier, display, path, method.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1, framework, traits));
        }
    }

    private static string AttrName(AttributeSyntax a)
    {
        var name = a.Name switch { QualifiedNameSyntax q => q.Right.Identifier.Text, IdentifierNameSyntax i => i.Identifier.Text, _ => a.Name.ToString() };
        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^9] : name;
    }

    private void AnalyzeCsproj(string path, string text)
    {
        XDocument doc;
        try { doc = XDocument.Parse(text, LoadOptions.SetLineInfo); }
        catch (System.Xml.XmlException ex) { SyntaxFindings.Add(new("error", $"Invalid project XML: {ex.Message}", path, ex.LineNumber)); return; }

        var refs = doc.Descendants().Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update") ?? "").ToList();
        if (!doc.Descendants().Any(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks"))
            ProjectFindings.Add(new("error", "No TargetFramework is declared.", path));
        if (refs.Any(r => r.StartsWith("xunit", StringComparison.OrdinalIgnoreCase))) Frameworks.Add("XUNIT");
        if (refs.Any(r => r.Equals("NUnit", StringComparison.OrdinalIgnoreCase))) Frameworks.Add("NUNIT");
        if (refs.Any(r => r.StartsWith("MSTest", StringComparison.OrdinalIgnoreCase))) Frameworks.Add("MSTEST");
        if (refs.Any(r => r.StartsWith("Microsoft.Playwright", StringComparison.OrdinalIgnoreCase))) Frameworks.Add("PLAYWRIGHT_CS");
        if (refs.Any(r => r.StartsWith("FlaUI", StringComparison.OrdinalIgnoreCase))) Frameworks.Add("FLAUI");
        if (!refs.Any(r => r.Equals("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)) && !refs.Any(r => r.StartsWith("MSTest.Sdk", StringComparison.OrdinalIgnoreCase))
            && !(doc.Root?.Attribute("Sdk")?.Value.StartsWith("MSTest.Sdk", StringComparison.OrdinalIgnoreCase) ?? false))
            ProjectFindings.Add(new("warning", "Microsoft.NET.Test.Sdk is not referenced; 'dotnet test' will not discover tests in this project.", path));

        // MSBuild can run arbitrary commands during build; flag anything a reviewer must look at before approval.
        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName is "Exec" or "UsingTask" or "DownloadFile" or "Copy" && e.Parent?.Name.LocalName == "Target" || e.Name.LocalName == "UsingTask"))
            ProjectFindings.Add(new("warning", $"Build customisation <{e.Name.LocalName}> runs during build. Review it before approval.", path, ((System.Xml.IXmlLineInfo)e).LineNumber));
        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference" && ((string?)e.Attribute("Version"))?.Contains('*') == true))
            ProjectFindings.Add(new("warning", $"Floating package version for {(string?)e.Attribute("Include")}; pin versions for reproducible runs.", path));
    }

    private void CheckXml(string path, string text, string file)
    {
        try
        {
            var doc = XDocument.Parse(text, LoadOptions.SetLineInfo);
            if (file.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
                if (doc.Descendants().Any(e => e.Name.LocalName is "Exec" or "UsingTask"))
                    ProjectFindings.Add(new("warning", "Build-wide MSBuild file contains Exec/UsingTask. Review it before approval.", path));
            if (file.Equals("nuget.config", StringComparison.OrdinalIgnoreCase))
                foreach (var src in doc.Descendants().Where(e => e.Name.LocalName == "add" && e.Parent?.Name.LocalName == "packageSources"))
                    ProjectFindings.Add(new("warning", $"Custom NuGet source '{(string?)src.Attribute("value")}'. Confirm it is an approved feed.", path));
        }
        catch (System.Xml.XmlException ex) { SyntaxFindings.Add(new("error", $"Invalid XML: {ex.Message}", path, ex.LineNumber)); }
    }

    // ---- JSON / YAML / structured definitions -------------------------------------------------------------------

    private void AnalyzeJson(string path, string text, string file)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { SyntaxFindings.Add(new("error", $"Invalid JSON: {ex.Message}", path, (int?)(ex.LineNumber + 1))); return; }
        using (doc)
        {
            var root = doc.RootElement;
            if (file.Equals("package.json", StringComparison.OrdinalIgnoreCase) && root.ValueKind == JsonValueKind.Object)
            {
                HasPackageJson = true;
                foreach (var section in new[] { "dependencies", "devDependencies" })
                    if (root.TryGetProperty(section, out var deps) && deps.ValueKind == JsonValueKind.Object)
                        foreach (var dep in deps.EnumerateObject())
                        {
                            if (dep.Name == "@playwright/test") Frameworks.Add("PLAYWRIGHT_TS");
                            var spec = dep.Value.ValueKind == JsonValueKind.String ? dep.Value.GetString() ?? "" : "";
                            if (Regex.IsMatch(spec, @"^(git|https?|file|github):|\.tgz$"))
                                ProjectFindings.Add(new("warning", $"Dependency '{dep.Name}' comes from '{spec}' rather than the registry.", path));
                        }
                if (root.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Object)
                    foreach (var s in scripts.EnumerateObject().Where(s => s.Name is "preinstall" or "install" or "postinstall" or "prepare" or "prepublish"))
                        ProjectFindings.Add(new("warning", $"Lifecycle script '{s.Name}' runs automatically on install: {s.Value}", path));
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "WingsTestDefinition")
                AddStructured(path, root.TryGetProperty("tests", out var tests) ? tests : default);
        }
    }

    private void AnalyzeYaml(string path, string text)
    {
        object? data;
        try { data = new YamlDotNet.Serialization.DeserializerBuilder().Build().Deserialize<object>(text); }
        catch (YamlDotNet.Core.YamlException ex) { SyntaxFindings.Add(new("error", $"Invalid YAML: {ex.Message}", path, (int)ex.Start.Line)); return; }
        if (data is Dictionary<object, object> map && map.TryGetValue("kind", out var k) && Equals(k, "WingsTestDefinition") && map.TryGetValue("tests", out var tests))
        {
            var json = JsonSerializer.Serialize(Plain(tests));
            using var doc = JsonDocument.Parse(json);
            AddStructured(path, doc.RootElement);
        }
    }

    /// <summary>YAML maps deserialize with object keys, which System.Text.Json cannot write; convert to string-keyed structures.</summary>
    private static object? Plain(object? node) => node switch
    {
        IDictionary<object, object> d => d.ToDictionary(kv => kv.Key.ToString() ?? "", kv => Plain(kv.Value)),
        IList<object> l => l.Select(Plain).ToList(),
        _ => node,
    };

    private void AddStructured(string path, JsonElement tests)
    {
        Frameworks.Add("STRUCTURED");
        if (tests.ValueKind != JsonValueKind.Array) { SyntaxFindings.Add(new("error", "A WingsTestDefinition must contain a 'tests' array.", path)); return; }
        var i = 0;
        foreach (var t in tests.EnumerateArray())
        {
            i++;
            string? Str(string name) => t.ValueKind == JsonValueKind.Object && t.TryGetProperty(name, out var v) ? v.ToString() : null;
            var id = Str("id");
            if (id is null) { SyntaxFindings.Add(new("error", $"Test #{i} has no 'id'.", path)); continue; }
            var traits = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (Str("feature") is { } f) AddTrait(traits, "WingsFeature", f);
            if (Str("testCase") is { } tc) AddTrait(traits, "WingsTestCase", tc);
            Tests.Add(new DiscoveredTest(id, Str("name") ?? id, path, 0, "STRUCTURED", traits));
        }
    }

    // ---- JavaScript / TypeScript (Playwright) -------------------------------------------------------------------

    private static readonly Regex DescribeRx = new(@"\b(?:test\.describe|describe)(?:\.(?:only|skip|serial|parallel|fixme))?\s*\(\s*(['""`])(?<name>(?:\\.|(?!\1).)*)\1", RegexOptions.Compiled);
    private static readonly Regex TestRx = new(@"(?<![\w.])(?:test|it)(?:\.(?:only|skip|fixme|fail|slow))?\s*\(\s*(['""`])(?<name>(?:\\.|(?!\1).)*)\1", RegexOptions.Compiled);
    private static readonly Regex TagRx = new(@"@(?<tag>[A-Za-z][\w:-]*)", RegexOptions.Compiled);

    private void AnalyzeJs(string path, string text)
    {
        var isSpec = Regex.IsMatch(Path.GetFileName(path), @"\.(spec|test)\.[cm]?[jt]sx?$", RegexOptions.IgnoreCase);
        if (!isSpec && !text.Contains("@playwright/test")) return;
        var describes = new List<(string Name, int Depth)>();
        var depth = 0;
        var lines = text.Split('\n');
        for (var n = 0; n < lines.Length; n++)
        {
            var raw = lines[n];
            if (raw.TrimStart().StartsWith("//")) continue;
            var d = DescribeRx.Match(raw);
            if (d.Success) describes.Add((d.Groups["name"].Value, depth));
            else if (TestRx.Match(raw) is { Success: true } t)
            {
                var title = t.Groups["name"].Value;
                var traits = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (Match tag in TagRx.Matches(raw + " " + string.Join(' ', describes.Select(x => x.Name)))) AddCategory(traits, tag.Groups["tag"].Value);
                var chain = describes.Select(x => x.Name).Append(title);
                Tests.Add(new DiscoveredTest($"{path} > {string.Join(" > ", chain)}", title, path, n + 1, "PLAYWRIGHT_TS", traits));
                HasJsTests = true;
            }
            var code = BlankStrings(raw);
            depth += code.Count(c => c == '{') - code.Count(c => c == '}');
            describes.RemoveAll(x => depth <= x.Depth);
        }
        if (HasJsTests && text.Contains("@playwright/test")) Frameworks.Add("PLAYWRIGHT_TS");
    }

    /// <summary>Blanks string literal contents so braces inside strings do not affect depth tracking.</summary>
    private static string BlankStrings(string line)
        => Regex.Replace(line, @"(['""`])(?:\\.|(?!\1).)*\1", m => new string('x', m.Length));

    // ---- Python (pytest) ----------------------------------------------------------------------------------------

    private static readonly Regex PyClassRx = new(@"^(?<indent>\s*)class\s+(?<name>Test\w*)\s*[\(:]", RegexOptions.Compiled);
    private static readonly Regex PyDefRx = new(@"^(?<indent>\s*)(?:async\s+)?def\s+(?<name>test\w*)\s*\(", RegexOptions.Compiled);
    private static readonly Regex PyMarkRx = new(@"^\s*@pytest\.mark\.(?<mark>\w+)(?:\((?<args>[^)]*)\))?", RegexOptions.Compiled);

    private void AnalyzePython(string path, string text)
    {
        var name = Path.GetFileName(path);
        if (!name.StartsWith("test_", StringComparison.Ordinal) && !name.EndsWith("_test.py", StringComparison.Ordinal)) return;
        string? cls = null;
        var clsIndent = -1;
        var marks = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Split('\n');
        for (var n = 0; n < lines.Length; n++)
        {
            var line = lines[n].TrimEnd('\r');
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;
            var indent = line.Length - line.TrimStart().Length;
            if (cls is not null && indent <= clsIndent && !line.TrimStart().StartsWith('@')) cls = null;
            if (PyMarkRx.Match(line) is { Success: true } mk)
            {
                var args = mk.Groups["args"].Value.Trim().Trim('"', '\'');
                AddTrait(marks, mk.Groups["mark"].Value.Equals("feature", StringComparison.OrdinalIgnoreCase) ? "WingsFeature" : "Category", args.Length > 0 ? args : mk.Groups["mark"].Value);
                continue;
            }
            if (PyClassRx.Match(line) is { Success: true } c) { cls = c.Groups["name"].Value; clsIndent = indent; marks = new(StringComparer.OrdinalIgnoreCase); continue; }
            if (PyDefRx.Match(line) is { Success: true } d && (cls is null ? indent == 0 : indent > clsIndent))
            {
                var test = d.Groups["name"].Value;
                Tests.Add(new DiscoveredTest(cls is null ? $"{path}::{test}" : $"{path}::{cls}::{test}", Humanize(test), path, n + 1, "PYTEST", marks));
                Frameworks.Add("PYTEST");
                marks = new(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    // ---- shared -------------------------------------------------------------------------------------------------

    private static void AddTrait(Dictionary<string, List<string>> traits, string key, string value)
    {
        if (!traits.TryGetValue(key, out var list)) traits[key] = list = [];
        list.Add(value);
    }

    /// <summary>Categories/tags like "WingsFeature:146", "feature-00146" or "tc-42" become structured traits.</summary>
    private static void AddCategory(Dictionary<string, List<string>> traits, string category)
    {
        var m = Regex.Match(category, @"^(?:wings)?(?<kind>feature|wf|testcase|tc)[:_-]?(?<id>[\w-]*\d+)$", RegexOptions.IgnoreCase);
        if (m.Success) AddTrait(traits, m.Groups["kind"].Value.ToLowerInvariant() is "feature" or "wf" ? "WingsFeature" : "WingsTestCase", m.Groups["id"].Value);
        else AddTrait(traits, "Category", category);
    }

    private static string Humanize(string identifier)
    {
        var s = identifier.Replace('_', ' ');
        s = Regex.Replace(s, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return s.Length == 0 ? identifier : char.ToUpperInvariant(s[0]) + s[1..];
    }

    public (string? Framework, string? Language, ApplicationType Platform) Summarize()
    {
        string[] priority = ["FLAUI", "PLAYWRIGHT_CS", "PLAYWRIGHT_TS", "PLAYWRIGHT_PY", "XUNIT", "NUNIT", "MSTEST", "PYTEST", "STRUCTURED"];
        var framework = priority.FirstOrDefault(Frameworks.Contains);
        var language = HasCSharp ? "csharp" : HasJsTests || HasPackageJson ? (Languages.Contains("typescript") ? "typescript" : "javascript")
            : Languages.Contains("python") ? "python" : Languages.Contains("json") || Languages.Contains("yaml") ? "structured" : Languages.FirstOrDefault();
        var platform = framework switch
        {
            "FLAUI" => ApplicationType.WinForms,
            "PLAYWRIGHT_CS" or "PLAYWRIGHT_TS" or "PLAYWRIGHT_PY" => ApplicationType.Browser,
            _ => ApplicationType.None,
        };
        return (framework, language, platform);
    }
}
