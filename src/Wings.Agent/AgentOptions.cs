namespace Wings.Agent;

public sealed class AgentOptions
{
    /// <summary>Orchestrator base URL, e.g. https://wto.wings.local.</summary>
    public string ServerUrl { get; set; } = "http://localhost:5057";
    /// <summary>Key shown once when the agent was created in the orchestrator. Prefer the WINGS_AGENT__AGENTKEY environment variable.</summary>
    public string AgentKey { get; set; } = "";
    /// <summary>Desktop (interactive Windows session, FlaUI) or Browser (Playwright). Api and Database are reserved for later executors.</summary>
    public string Kind { get; set; } = "Browser";
    public int Slots { get; set; } = 1;
    /// <summary>Override the detected framework list (codes such as PLAYWRIGHT_TS, XUNIT, FLAUI). Empty = detect from installed tools.</summary>
    public string[] Frameworks { get; set; } = [];
    public string WorkDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "wings-agent");
    /// <summary>Keep extracted packages and outputs after a job (for debugging the agent itself).</summary>
    public bool KeepWorkDirectories { get; set; }
    /// <summary>Variables given to every test on this machine (e.g. WINGS_APP_PATH for the WinForms build installed here). Job variables override them.</summary>
    public Dictionary<string, string> Variables { get; set; } = [];
    /// <summary>Applications under test installed here (name → version), reported to the orchestrator.</summary>
    public Dictionary<string, string> Applications { get; set; } = [];
    /// <summary>Process names killed after each job so a crashed or hung application cannot affect the next one.</summary>
    public string[] CleanupProcessNames { get; set; } = [];
    public string DotnetCommand { get; set; } = "dotnet";
    public string NpmCommand { get; set; } = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
    public string NpxCommand { get; set; } = OperatingSystem.IsWindows() ? "npx.cmd" : "npx";
    public string PythonCommand { get; set; } = OperatingSystem.IsWindows() ? "python" : "python3";
    /// <summary>Run "npx playwright install" before Playwright jobs (needs internet access to the browser CDN).</summary>
    public bool InstallPlaywrightBrowsers { get; set; }
    /// <summary>Optional NuGet/npm cache directories shared across jobs to speed up restores.</summary>
    public string? PackageCacheDirectory { get; set; }
    public int PollSeconds { get; set; } = 3;
}
