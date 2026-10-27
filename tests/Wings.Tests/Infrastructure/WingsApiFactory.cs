using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Wings.Tests.Infrastructure;

/// <summary>
/// Hosts the real API (all middleware, auth, EF Core, background worker) against a fresh SQLite database file and a
/// temporary blob store per test class. Test users are created through configuration, exactly as in deployments.
/// </summary>
public class WingsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Integration-Test-Pw-1";
    public static readonly string[] UserNames = ["admin", "qa", "dev", "tester", "viewer"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "wings-tests", Guid.NewGuid().ToString("N"));

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Extra configuration for specialised fixtures (e.g. very short leases).</summary>
    protected virtual IEnumerable<KeyValuePair<string, string?>> ExtraSettings => [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_root);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Wings"] = $"Data Source={Path.Combine(_root, "wings.db")};Default Timeout=30",
                ["Storage:RootPath"] = Path.Combine(_root, "storage"),
                ["Auth:Jwt:SigningKey"] = "integration-tests-signing-key-0123456789abcdef",
                ["Seed:SampleData"] = "true",
                ["Auth:LoginAttemptsPerMinute"] = "10000",
            };
            string[] roles = ["System Administrator", "QA Administrator", "Test Developer", "Tester", "Read-only User"];
            for (var i = 0; i < UserNames.Length; i++)
            {
                settings[$"Seed:Users:{i}:UserName"] = UserNames[i];
                settings[$"Seed:Users:{i}:DisplayName"] = UserNames[i];
                settings[$"Seed:Users:{i}:Password"] = Password;
                settings[$"Seed:Users:{i}:Role"] = roles[i];
            }
            foreach (var (k, v) in ExtraSettings) settings[k] = v;
            cfg.AddInMemoryCollection(settings);
        });
    }

    public async Task<HttpClient> ClientFor(string userName)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName, password = Password });
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
        return client;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}

public static class HttpExtensions
{
    public static async Task<T> Read<T>(this HttpResponseMessage response, HttpStatusCode? expected = null)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (expected is { } e) Assert.True(response.StatusCode == e, $"Expected {e} but got {(int)response.StatusCode}: {body}");
        else Assert.True(response.IsSuccessStatusCode, $"Request failed {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, WingsApiFactory.Json)!;
    }

    public static Task<HttpResponseMessage> PostJson(this HttpClient c, string url, object body) => c.PostAsJsonAsync(url, body, WingsApiFactory.Json);
    public static Task<HttpResponseMessage> PutJson(this HttpClient c, string url, object body) => c.PutAsJsonAsync(url, body, WingsApiFactory.Json);

    public static async Task<string> Problem(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected} but got {(int)response.StatusCode}: {body}");
        return body;
    }
}
