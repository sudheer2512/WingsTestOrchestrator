using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Wings.Agent;
using Wings.Agent.Contracts;
using Wings.Agent.Executors;

// Wings Test Orchestrator execution agent.
// Configuration: appsettings.json next to the executable, then environment variables prefixed WINGS_AGENT__
// (e.g. WINGS_AGENT__AGENTKEY), then command-line arguments (--Agent:ServerUrl=https://...).
// Content root = the executable folder, so appsettings.json is found whatever directory the agent is started from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Configuration.AddEnvironmentVariables("WINGS_");
builder.Services.AddOptions<AgentOptions>().BindConfiguration("Agent")
    .Validate(o => !string.IsNullOrWhiteSpace(o.AgentKey), "Agent:AgentKey is required (create the agent in the orchestrator's Agents screen).")
    .Validate(o => Uri.TryCreate(o.ServerUrl, UriKind.Absolute, out _), "Agent:ServerUrl must be an absolute URL.")
    .ValidateOnStart();
builder.Services.AddHttpClient<OrchestratorClient>((sp, http) =>
{
    var o = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    http.BaseAddress = new Uri(o.ServerUrl.TrimEnd('/') + "/");
    http.DefaultRequestHeaders.Add(AgentHeaders.Key, o.AgentKey);
    http.Timeout = TimeSpan.FromMinutes(30); // large package downloads and artifact uploads
});
builder.Services.AddSingleton<ITestExecutor, DotNetTestExecutor>();
builder.Services.AddSingleton<ITestExecutor, PlaywrightExecutor>();
builder.Services.AddSingleton<ITestExecutor, PytestExecutor>();
builder.Services.AddHostedService<AgentWorker>();
// Runs as a console app in the logged-in user's session (required for desktop UI automation); Ctrl+C stops it gracefully.
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(60));

await builder.Build().RunAsync();
