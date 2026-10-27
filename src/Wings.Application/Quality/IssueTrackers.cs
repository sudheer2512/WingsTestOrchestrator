using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Domain.Quality;

namespace Wings.Application.Quality;

public static class TrackerKinds
{
    public const string GitHub = "GitHub";
    public const string Jira = "Jira";
    public const string AzureDevOps = "AzureDevOps";
    /// <summary>Any internal system: the defect is POSTed as JSON; the response must contain "key" and may contain "url".</summary>
    public const string Webhook = "Webhook";
    public static readonly string[] All = [GitHub, Jira, AzureDevOps, Webhook];
}

/// <summary>Content pushed to an external tracker.</summary>
public sealed record ExternalIssue(string Title, string Markdown, string Html, DefectSeverity Severity, string Priority, JsonObject Payload);
public sealed record ExternalIssueRef(string Key, string? Url);

public interface IIssueTrackerAdapter
{
    string Kind { get; }
    Task<ExternalIssueRef> CreateAsync(HttpClient http, IssueTrackerConnection connection, string? token, ExternalIssue issue, CancellationToken ct);
}

public sealed class GitHubAdapter : IIssueTrackerAdapter
{
    public string Kind => TrackerKinds.GitHub;

    public async Task<ExternalIssueRef> CreateAsync(HttpClient http, IssueTrackerConnection c, string? token, ExternalIssue issue, CancellationToken ct)
    {
        var baseUrl = (c.BaseUrl ?? "https://api.github.com").TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/repos/{c.Target.Trim('/')}/issues")
        {
            Content = JsonContent.Create(new { title = issue.Title, body = issue.Markdown, labels = new[] { "wings-defect", $"severity:{issue.Severity.ToString().ToLowerInvariant()}" } }),
        };
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("WingsTestOrchestrator/1.0");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var json = await IssueTrackerService.SendAsync(http, request, ct);
        return new ExternalIssueRef($"{c.Target}#{json["number"]}", json["html_url"]?.GetValue<string>());
    }
}

public sealed class JiraAdapter : IIssueTrackerAdapter
{
    public string Kind => TrackerKinds.Jira;

    public async Task<ExternalIssueRef> CreateAsync(HttpClient http, IssueTrackerConnection c, string? token, ExternalIssue issue, CancellationToken ct)
    {
        var baseUrl = c.BaseUrl?.TrimEnd('/') ?? throw new ValidationException("baseUrl", "Jira needs its base URL (https://your-site.atlassian.net).");
        // API v2 accepts plain-text/wiki descriptions on both Jira Cloud and Data Center.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/rest/api/2/issue")
        {
            Content = JsonContent.Create(new { fields = new { project = new { key = c.Target }, summary = issue.Title, description = issue.Markdown, issuetype = new { name = "Bug" }, labels = new[] { "wings-defect" } } }),
        };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.Username}:{token}")));
        var json = await IssueTrackerService.SendAsync(http, request, ct);
        var key = json["key"]?.GetValue<string>() ?? throw new InvalidOperationException("Jira did not return an issue key.");
        return new ExternalIssueRef(key, $"{baseUrl}/browse/{key}");
    }
}

public sealed class AzureDevOpsAdapter : IIssueTrackerAdapter
{
    public string Kind => TrackerKinds.AzureDevOps;

    public async Task<ExternalIssueRef> CreateAsync(HttpClient http, IssueTrackerConnection c, string? token, ExternalIssue issue, CancellationToken ct)
    {
        var baseUrl = c.BaseUrl?.TrimEnd('/') ?? throw new ValidationException("baseUrl", "Azure DevOps needs the organization URL (https://dev.azure.com/your-org).");
        var severity = issue.Severity switch { DefectSeverity.Critical => "1 - Critical", DefectSeverity.Major => "2 - High", DefectSeverity.Minor => "3 - Medium", _ => "4 - Low" };
        var patch = new JsonArray
        {
            Op("/fields/System.Title", issue.Title),
            Op("/fields/Microsoft.VSTS.TCM.ReproSteps", issue.Html),
            Op("/fields/Microsoft.VSTS.Common.Severity", severity),
            Op("/fields/Microsoft.VSTS.Common.Priority", issue.Priority[1..]),
            Op("/fields/System.Tags", "wings-defect"),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/{Uri.EscapeDataString(c.Target)}/_apis/wit/workitems/$Bug?api-version=7.1")
        {
            Content = new StringContent(patch.ToJsonString(), Encoding.UTF8, "application/json-patch+json"),
        };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($":{token}")));
        var json = await IssueTrackerService.SendAsync(http, request, ct);
        return new ExternalIssueRef($"AB#{json["id"]}", json["_links"]?["html"]?["href"]?.GetValue<string>());
    }

    private static JsonObject Op(string path, string value) => new() { ["op"] = "add", ["path"] = path, ["value"] = value };
}

public sealed class WebhookTrackerAdapter : IIssueTrackerAdapter
{
    public string Kind => TrackerKinds.Webhook;

    public async Task<ExternalIssueRef> CreateAsync(HttpClient http, IssueTrackerConnection c, string? token, ExternalIssue issue, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, c.BaseUrl ?? throw new ValidationException("baseUrl", "The webhook URL is required."))
        {
            Content = new StringContent(issue.Payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var json = await IssueTrackerService.SendAsync(http, request, ct);
        return new ExternalIssueRef(json["key"]?.ToString() ?? throw new InvalidOperationException("The tracker response has no \"key\"."), json["url"]?.GetValue<string>());
    }
}

public sealed record TrackerDto(Guid Id, string Name, string Kind, string Target, string? BaseUrl, string? Username, bool HasToken, Guid? ProductId, bool IsEnabled, long Version);
/// <summary>Token is write-only: null keeps the stored token, "" clears it.</summary>
public sealed record TrackerUpsert(string Name, string Kind, string Target, string? BaseUrl, string? Username, string? Token, Guid? ProductId, bool IsEnabled = true, long? Version = null);

public interface IIssueTrackerService
{
    Task<IReadOnlyList<TrackerDto>> ListAsync(CancellationToken ct = default);
    Task<TrackerDto> SaveAsync(Guid? id, TrackerUpsert request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Creates the issue in the external tracker and records its key and URL on the defect.</summary>
    Task<DefectDetailDto> PushAsync(Guid defectId, Guid connectionId, CancellationToken ct = default);
}

public sealed class IssueTrackerService(IWingsDbContext db, ISecretProtector secrets, IHttpClientFactory httpFactory, IEnumerable<IIssueTrackerAdapter> adapters,
    IDefectService defects, ICurrentUser user, TimeProvider clock, Microsoft.Extensions.Options.IOptions<NotificationOptions> options) : IIssueTrackerService
{
    public const string HttpClientName = "outbound";

    public async Task<IReadOnlyList<TrackerDto>> ListAsync(CancellationToken ct = default)
        => (await db.IssueTrackerConnections.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct)).Select(ToDto).ToList();

    private static TrackerDto ToDto(IssueTrackerConnection c) => new(c.Id, c.Name, c.Kind, c.Target, c.BaseUrl, c.Username, c.ProtectedToken is not null, c.ProductId, c.IsEnabled, c.Version);

    public async Task<TrackerDto> SaveAsync(Guid? id, TrackerUpsert r, CancellationToken ct = default)
    {
        new Validator()
            .Required(r.Name, "name", 200)
            .Must(TrackerKinds.All.Contains(r.Kind), "kind", $"Kind must be one of {string.Join(", ", TrackerKinds.All)}.")
            .Must(r.Kind == TrackerKinds.Webhook || !string.IsNullOrWhiteSpace(r.Target), "target", "Enter the repository (owner/repo), Jira project key or Azure DevOps project.")
            .Must(r.BaseUrl.Clean() is null || (Uri.TryCreate(r.BaseUrl, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.IsLoopback)), "baseUrl", "Use an https URL.")
            .Must(r.Kind is TrackerKinds.GitHub || r.BaseUrl.Clean() is not null, "baseUrl", "This tracker needs its base URL.")
            .ThrowIfInvalid();
        IssueTrackerConnection c;
        if (id is { } existing)
        {
            c = await db.IssueTrackerConnections.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Issue tracker", existing);
            if (r.Version is { } v) db.SetOriginalVersion(c, v);
        }
        else db.IssueTrackerConnections.Add(c = new IssueTrackerConnection());
        c.Name = r.Name.Trim();
        c.Kind = r.Kind;
        c.Target = r.Target?.Trim() ?? "";
        c.BaseUrl = r.BaseUrl.Clean();
        c.Username = r.Username.Clean();
        if (r.Token is not null) c.ProtectedToken = r.Token.Length == 0 ? null : secrets.Protect(r.Token);
        c.ProductId = r.ProductId;
        c.IsEnabled = r.IsEnabled;
        await db.SaveChangesAsync(ct);
        return ToDto(c);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.IssueTrackerConnections.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Issue tracker", id);
        db.IssueTrackerConnections.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    public async Task<DefectDetailDto> PushAsync(Guid defectId, Guid connectionId, CancellationToken ct = default)
    {
        var detail = await defects.GetAsync(defectId, ct);
        if (detail.Summary.ExternalKey is { } key) throw new ConflictException($"The defect is already linked to {key}.");
        var c = await db.IssueTrackerConnections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == connectionId && x.IsEnabled, ct) ?? throw new NotFoundException("Issue tracker", connectionId);
        var adapter = adapters.FirstOrDefault(a => a.Kind == c.Kind) ?? throw new InvalidOperationException($"No adapter for {c.Kind}.");

        var link = $"{(options.Value.PublicUrl ?? "http://localhost:5057").TrimEnd('/')}/defects/{defectId}";
        var issue = Build(detail, link);
        ExternalIssueRef created;
        try
        {
            created = await adapter.CreateAsync(httpFactory.CreateClient(HttpClientName), c, c.ProtectedToken is { } t ? secrets.Unprotect(t) : null, issue, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ConflictException($"{c.Kind} rejected the issue: {ex.Message}");
        }

        var d = await db.Defects.FirstAsync(x => x.Id == defectId, ct);
        d.ExternalSystem = c.Kind;
        d.ExternalKey = created.Key;
        d.ExternalUrl = created.Url;
        db.DefectEvents.Add(new DefectEvent { DefectId = d.Id, Timestamp = clock.GetUtcNow(), UserName = user.UserName, Kind = "External", Text = $"Pushed to {c.Name} as {created.Key}." });
        db.PendingAuditAction = "PushedToTracker";
        await db.SaveChangesAsync(ct);
        return await defects.GetAsync(defectId, ct);
    }

    private static ExternalIssue Build(DefectDetailDto d, string link)
    {
        var s = d.Summary;
        var sections = new List<(string Title, string? Text)>
        {
            ("Description", d.Description), ("Steps to reproduce", d.ReproductionSteps), ("Expected result", d.ExpectedResult), ("Actual result", d.ActualResult),
        };
        var facts = new List<(string, string?)>
        {
            ("Defect", s.NumberDisplay), ("Severity", s.Severity.ToString()), ("Priority", s.Priority), ("Product", s.ProductName),
            ("Feature", s.FeatureName is null ? null : $"[{s.FeatureSequence}] {s.FeatureName}"), ("Test case", s.TestCaseNumber), ("Failing run", d.FailingRunNumber),
            ("Build", d.BuildVersion), ("Environment", d.EnvironmentName),
        };
        var md = new StringBuilder();
        foreach (var (k, v) in facts.Where(f => f.Item2 is not null)) md.AppendLine($"- **{k}:** {v}");
        foreach (var (title, text) in sections.Where(x => x.Text is not null)) md.AppendLine().AppendLine($"### {title}").AppendLine(text);
        if (d.Evidence.Count > 0) md.AppendLine().AppendLine($"Evidence ({d.Evidence.Count} files: screenshots, logs, traces) is attached to the defect in Wings Test Orchestrator.");
        md.AppendLine().AppendLine($"[Open {s.NumberDisplay} in Wings Test Orchestrator]({link})");

        var html = new StringBuilder("<ul>");
        foreach (var (k, v) in facts.Where(f => f.Item2 is not null)) html.Append($"<li><b>{Enc(k)}:</b> {Enc(v)}</li>");
        html.Append("</ul>");
        foreach (var (title, text) in sections.Where(x => x.Text is not null)) html.Append($"<h3>{Enc(title)}</h3><pre>{Enc(text)}</pre>");
        html.Append($"<p><a href=\"{Enc(link)}\">Open {Enc(s.NumberDisplay)} in Wings Test Orchestrator</a></p>");

        var payload = JsonSerializer.SerializeToNode(new
        {
            id = s.Id, number = s.NumberDisplay, title = s.Title, severity = s.Severity.ToString(), priority = s.Priority, status = s.Status.ToString(), product = s.ProductName,
            feature = s.FeatureName, testCase = s.TestCaseNumber, run = d.FailingRunNumber, build = d.BuildVersion, environment = d.EnvironmentName,
            description = d.Description, reproductionSteps = d.ReproductionSteps, expectedResult = d.ExpectedResult, actualResult = d.ActualResult, url = link,
        })!.AsObject();
        return new ExternalIssue($"[{s.NumberDisplay}] {s.Title}", md.ToString(), html.ToString(), s.Severity, s.Priority, payload);
    }

    private static string Enc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

    internal static async Task<JsonNode> SendAsync(HttpClient http, HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {(body.Length > 300 ? body[..300] : body)}");
        return JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body) ?? new JsonObject();
    }
}
