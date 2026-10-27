# Execution agent installation

`Wings.Agent` is a small .NET program that runs on a test machine, pulls jobs from the orchestrator over HTTPS, runs
uploaded test code, and streams back logs, results and evidence. It never accepts inbound connections.

| Agent kind | Runs | Machine |
|---|---|---|
| **Desktop** | FlaUI / WinForms UI tests (and other .NET tests) | Dedicated Windows 10/11 or Server VM with a **logged-in, unlocked desktop session** |
| **Browser** | Playwright (TypeScript/JavaScript, .NET, Python), xUnit/NUnit/MSTest, pytest | Windows, Linux or macOS |

Uploaded test code is executed **only** on agents, never on the web server. Treat agent machines as disposable test
infrastructure: a dedicated low-privilege local account, no access to production systems or secrets, and outbound network
limited to the orchestrator, package feeds and the environments under test.

## 1. Register the agent in the orchestrator

*Agents → Register agent*: enter a name (e.g. `QA-WIN-01`), choose the kind and pool. The **agent key is shown once** —
store it in your password vault. *Rotate key* replaces it; the old key stops working immediately.

## 2. Install prerequisites on the machine

| Needed for | Install |
|---|---|
| All agents | [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (SDK if the agent will build .NET test projects) |
| .NET test packages (FlaUI, xUnit…) | .NET 10 **SDK**; access to nuget.org or your internal feed |
| Playwright TS/JS | Node.js 20+ and npm; browsers via `npx playwright@<version> install chromium` (or set `InstallPlaywrightBrowsers=true`) |
| pytest | Python 3.10+ with pip |
| Desktop (WinForms) | The Wings application build under test installed on the machine |

## 3. Publish and configure the agent

On a build machine:

```bash
dotnet publish src/Wings.Agent -c Release -r win-x64 --self-contained false -o publish/agent-win
```

Copy `publish/agent-win` to the test machine (e.g. `C:\WingsAgent`). Configure it with environment variables
(preferred for the key) or `appsettings.json` next to `Wings.Agent.exe`:

```json
{
  "Agent": {
    "ServerUrl": "https://wto.wings.local",
    "Kind": "Desktop",
    "WorkDirectory": "C:\\WingsAgent\\work",
    "PackageCacheDirectory": "C:\\WingsAgent\\cache",
    "Variables": { "WINGS_APP_PATH": "C:\\Wings\\WingsAuto\\WingsAuto.exe" },
    "Applications": { "WingsAuto": "2026.2.0" },
    "CleanupProcessNames": [ "WingsAuto" ]
  }
}
```

| Setting | Meaning |
|---|---|
| `AgentKey` | Set as `WINGS_AGENT__AGENTKEY` (machine-level, readable only by the agent account). |
| `Kind` | `Desktop` or `Browser`. Desktop agents always run one job at a time. |
| `Slots` | Parallel jobs for browser agents. |
| `Frameworks` | Leave empty to detect from installed tools; set to restrict (e.g. `["PLAYWRIGHT_TS"]`). |
| `Variables` | Machine-specific values given to every test (application path, local URLs). Run variables override them. **Secrets** (test passwords) belong here or in the machine's credential store, not in run variables. |
| `CleanupProcessNames` | Killed after each job so a hung application cannot affect the next test. |
| `KeepWorkDirectories` | Keep extracted packages and raw outputs for troubleshooting. |

Every setting can also be given as `WINGS_AGENT__<NAME>` or `--Agent:<Name>=value`.

## 4. Run a desktop agent in the interactive session

UI Automation can only drive windows on a visible, unlocked desktop. **Do not install a desktop agent as a Windows
service** (services run in session 0 and cannot see the desktop). The agent detects this, reports
"interactive session: none", and the orchestrator will not send it UI jobs.

Recommended setup on a dedicated VM:

1. Create a local user `wings-agent` (standard user).
2. Configure automatic sign-in for that user (Sysinternals *Autologon* stores the password encrypted).
3. Disable the screen saver and screen lock, and power-saving sleep, for that user.
4. Fix the screen resolution and scaling (for example 1920×1080 at 100%): the agent reports both, and tests are most
   stable when they are identical on every desktop agent.
5. Start the agent at sign-in with a scheduled task:

   ```bat
   setx WINGS_AGENT__AGENTKEY "wta_…"
   schtasks /Create /TN "Wings Agent" /SC ONLOGON /RU wings-agent /RL LIMITED /TR "C:\WingsAgent\Wings.Agent.exe"
   ```

6. If you connect with Remote Desktop, **do not minimise or disconnect the RDP window** — that locks or hides the desktop.
   Use `tscon` to hand the session back to the console, or use a VM console/VNC instead.

Browser agents have no such restriction and can run as a service (`sc.exe create`, systemd or launchd).

## 5. Verify

The agent prints its detected capabilities and `Registered as "<name>"`. In *Agents* it shows a green dot, its
frameworks, slots and (for desktop agents) `interactive` with the screen resolution. Then execute a published package
from *Test code* or a feature's *Execute tests* button and follow it in *Runs*.

## Demonstrating the sample WinForms test (Scenario 4)

1. On the Windows agent machine, build and copy the sample application:
   `dotnet publish samples/apps/WingsAuto.SampleApp -c Release -r win-x64 -o C:\Wings\SampleApp`
2. Set the agent variable `WINGS_APP_PATH=C:\Wings\SampleApp\WingsAuto.SampleApp.exe` and
   `CleanupProcessNames=["WingsAuto.SampleApp"]`; start a **Desktop** agent as described above.
3. In the orchestrator, upload `samples/automation/WingsAuto.WinForms.UiTests` as a .zip with product *Wings Auto*;
   approve and publish it (two different users).
4. Open feature *Create Enquiry* and choose *Execute tests*. The job is dispatched only to the interactive desktop agent;
   it builds the test project, drives the form, and returns results, the TRX report and desktop screenshots.

## How it behaves under failure

| Situation | Outcome |
|---|---|
| Restore/build of the test project fails | Tests are **Blocked**; job completes with the build error in its log. |
| Missing tool, browser, desktop session, hash mismatch | Job ends as **InfrastructureError**; unfinished tests are re-queued (up to the run's retry count) for any compatible agent. |
| Agent crashes, loses network or is shut down | Its lease expires (90 s by default); the orchestrator records **InfrastructureError**, keeps the results and evidence already received, and re-queues the rest. Late messages from the old agent are rejected. |
| Run cancelled | Queued jobs are cancelled immediately; running jobs are killed (whole process tree) at the next heartbeat. |
| Job exceeds the run timeout | The agent kills it and reports **TimedOut**; the server enforces it too if the agent is unresponsive. |

## What tests receive from the agent

| Variable | Content |
|---|---|
| `WINGS_BASE_URL`, `WINGS_ENVIRONMENT`, `WINGS_APP_BUILD` | From the run's environment (base URL, code, application build) |
| Environment variables and secrets | Every variable of the run's environment; run variables override them |
| `WINGS_CONTEXT_FILE` | Path to a JSON file: `run` (id, number, build), `environment`, `data` (records reserved for this job, by data set code) and `parameters` (parameter sets of each test's test case) |
| `WINGS_ARTIFACTS_DIR` | Folder whose files are uploaded as evidence (files starting with a test's method name/title attach to that test) |
| `WINGS_RUN_ID`, `WINGS_JOB_ID` | Identifiers for correlating application logs |

Secret values are replaced with `***` in the job log, in error messages, stack traces and output, and in text reports
before upload. Binary evidence (screenshots, videos, traces) cannot be scanned, so tests must not display secrets on screen.
