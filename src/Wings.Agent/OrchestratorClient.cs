using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wings.Agent.Contracts;

namespace Wings.Agent;

/// <summary>Raised when the server says the job no longer belongs to this agent (lease lost, reassigned or finished).</summary>
public sealed class LeaseLostException(string message) : Exception(message);

public sealed class OrchestratorClient(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public Task<AgentRegistrationResponse> RegisterAsync(AgentRegistration r, CancellationToken ct) => Post<AgentRegistrationResponse>("api/v1/agent/register", r, ct);
    public Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest r, CancellationToken ct) => Post<HeartbeatResponse>("api/v1/agent/heartbeat", r, ct);
    public Task<ClaimResponse> ClaimAsync(int freeSlots, CancellationToken ct) => Post<ClaimResponse>("api/v1/agent/jobs/claim", new ClaimRequest(freeSlots), ct);
    public Task StartAsync(Guid jobId, CancellationToken ct) => Send($"api/v1/agent/jobs/{jobId}/start", new { }, ct);
    public Task LogsAsync(Guid jobId, LogBatch batch, CancellationToken ct) => Send($"api/v1/agent/jobs/{jobId}/logs", batch, ct);
    public Task ResultsAsync(Guid jobId, ResultBatch batch, CancellationToken ct) => Send($"api/v1/agent/jobs/{jobId}/results", batch, ct);
    public Task CompleteAsync(Guid jobId, JobCompletion c, CancellationToken ct) => Send($"api/v1/agent/jobs/{jobId}/complete", c, ct);

    public async Task DownloadPackageAsync(Guid versionId, string target, CancellationToken ct)
    {
        using var response = await http.GetAsync($"api/v1/agent/packages/{versionId}", HttpCompletionOption.ResponseHeadersRead, ct);
        await Ensure(response, ct);
        await using var file = File.Create(target);
        await response.Content.CopyToAsync(file, ct);
    }

    public async Task UploadArtifactAsync(Guid jobId, string path, string kind, string contentType, string? testIdentifier, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(stream);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "file", Path.GetFileName(path));
        form.Add(new StringContent(kind), "kind");
        if (testIdentifier is not null) form.Add(new StringContent(testIdentifier), "testIdentifier");
        using var response = await http.PostAsync($"api/v1/agent/jobs/{jobId}/artifacts", form, ct);
        await Ensure(response, ct);
    }

    private async Task<T> Post<T>(string url, object body, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(url, body, Json, ct);
        await Ensure(response, ct);
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private async Task Send(string url, object body, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(url, body, Json, ct);
        await Ensure(response, ct);
    }

    private static async Task Ensure(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.Conflict) throw new LeaseLostException(body);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException("The orchestrator rejected the agent key (unknown, rotated or agent disabled).");
        throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {body}", null, response.StatusCode);
    }
}
