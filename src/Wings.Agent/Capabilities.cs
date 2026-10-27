using System.Diagnostics;
using System.Runtime.InteropServices;
using Wings.Agent.Contracts;

namespace Wings.Agent;

/// <summary>Discovers what this machine can run: tools on PATH, desktop session, screen and DPI.</summary>
public static partial class Capabilities
{
    public static AgentRegistration Detect(AgentOptions options)
    {
        var tools = new Dictionary<string, string>();
        void Probe(string name, string cmd, params string[] args) { if (Version(cmd, args) is { } v) tools[name] = v; }
        Probe("dotnet", options.DotnetCommand, "--version");
        Probe("node", OperatingSystem.IsWindows() ? "node.exe" : "node", "--version");
        Probe("npm", options.NpmCommand, "--version");
        Probe("python", options.PythonCommand, "--version");

        var frameworks = options.Frameworks.Length > 0 ? options.Frameworks.ToList() : [];
        if (frameworks.Count == 0)
        {
            if (tools.ContainsKey("dotnet")) frameworks.AddRange(["XUNIT", "NUNIT", "MSTEST", "PLAYWRIGHT_CS"]);
            if (tools.ContainsKey("dotnet") && options.Kind == AgentKinds.Desktop && OperatingSystem.IsWindows()) frameworks.Add("FLAUI");
            if (tools.ContainsKey("npm")) frameworks.Add("PLAYWRIGHT_TS");
            if (tools.ContainsKey("python")) frameworks.AddRange(["PYTEST", "PLAYWRIGHT_PY"]);
        }

        string? resolution = null;
        int? dpi = null;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                resolution = $"{GetSystemMetrics(0)}x{GetSystemMetrics(1)}";
                dpi = (int)Math.Round(GetDpiForSystem() * 100 / 96.0);
            }
            catch (Exception) { /* older Windows without GetDpiForSystem */ }
        }

        return new AgentRegistration
        {
            MachineName = Environment.MachineName,
            OperatingSystem = RuntimeInformation.OSDescription,
            AgentVersion = typeof(Capabilities).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            Kind = options.Kind,
            Slots = options.Kind == AgentKinds.Desktop ? 1 : options.Slots,
            Frameworks = frameworks.Distinct().ToList(),
            InteractiveSession = HasInteractiveDesktop(),
            ProcessSessionId = Process.GetCurrentProcess().SessionId,
            ScreenResolution = resolution,
            DpiScalePercent = dpi,
            ProcessorCount = Environment.ProcessorCount,
            TotalMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024,
            Tools = tools,
            Applications = options.Applications,
            Browsers = ["chromium", "firefox", "webkit", .. OperatingSystem.IsWindows() ? new[] { "msedge" } : []],
        };
    }

    /// <summary>
    /// UI automation needs a real, unlocked desktop: a process in session 0 (Windows services) cannot see or drive windows.
    /// </summary>
    public static bool HasInteractiveDesktop()
        => OperatingSystem.IsWindows() && Environment.UserInteractive && Process.GetCurrentProcess().SessionId != 0;

    private static string? Version(string cmd, string[] args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(cmd, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (p is null || !p.WaitForExit(15_000)) return null;
            var text = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
            return p.ExitCode == 0 && text.Length > 0 ? text.Split('\n')[0].Replace("Python ", "").TrimStart('v').Trim() : null;
        }
        catch (Exception) { return null; }
    }

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();
}
