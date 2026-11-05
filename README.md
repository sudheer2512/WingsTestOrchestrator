# Wings Test Orchestrator

Central platform for the Wings product feature inventory, test cases and automation code.
This repository delivers all six phases of the build specification: **Feature Repository**, **Test Repository**, **Execution
Infrastructure**, **Full Orchestration**, **Quality** (defects, release readiness and quality gates, dashboards, reports,
notifications, source and build integration) and the optional **AI testing assistant** (see [Roadmap](#roadmap)).

## What works today

| Area | Capabilities |
|---|---|
| Feature repository | Unlimited hierarchy (Product → Module → … → Operation) in one tree; immutable GUID identity; **creation sequence numbers** allocated transactionally per numbering scope, never reused, preserved on move/reorder, legacy numbers importable; separate display order; drag-and-drop reorder/move; duplicate, deprecate (with replacement), soft delete |
| Feature details | Identification, technical, business-behaviour fields; requirements; source mappings with developer approval; dependencies with cycle detection; attachments and links; full audit history including child records |
| Explorer | Lazy-loaded, virtual-scrolled tree with keyboard navigation and context menu; search by name or sequence; filters (status, type, application type, sequence range, created after, recent, since last release, untested, missing automation); coverage indicators and counts; Excel/CSV inventory export and import; live refresh over SignalR |
| Test cases | Numbered (`TC-00042`), versioned on every save with immutable snapshots and revision compare; steps with drag reorder and shared-step calls; parameter sets; tags; folders; templates; clone; bulk edit; multi-feature and requirement links; test-case dependencies with cycle detection; review workflow (author cannot approve own revision); CSV/Excel import/export |
| Suites | Reusable ordered collections (`TS-00001`), drag reorder |
| Test code | Upload a .zip project or single file (C#/.NET, Playwright TS/JS, pytest, JSON/YAML definitions). Background validation: archive safety (zip-slip, symlinks, bombs, size limits), malware/unsafe-content scan, SHA-256 per file, syntax checks (Roslyn for C#, XML/JSON/YAML), project-file review (MSBuild `Exec`, lifecycle scripts, floating versions, custom feeds), framework/platform detection, **test discovery** and automatic mapping to features/test cases by convention. Immutable versions; edit in the browser (CodeMirror: highlighting, folding, search/replace, side-by-side diff) to create a new version; Draft → Uploaded → Validating → Validated → Approved → Published → Deprecated with separation of duties |
| Execution | Start runs for test cases, a feature (with sub-features), a suite, a package version, features created after a sequence number, or failed tests of a previous run. Runs resolve to the latest **published** automated tests, grouped into jobs per package version; manual test cases are recorded as not run. Durable queue with atomic claims, priorities, pause/resume, cancellation, per-run timeouts and infrastructure retries |
| Agents | `Wings.Agent` pulls work over HTTPS with a per-agent key, heartbeats to hold **leases**, verifies the package SHA-256, runs **Playwright** (JSON report, screenshots, videos, traces), **dotnet test** (xUnit/NUnit/MSTest/**FlaUI**, TRX) or **pytest** (JUnit), and streams logs, results and evidence. Desktop (FlaUI) jobs go only to agents with an interactive Windows session, one at a time. Lost agents are detected by lease expiry; their results so far are kept and unfinished tests are re-queued |
| Monitoring | Live run monitor (SignalR): segmented progress, jobs and agents, streaming log, per-test errors, stack traces, inline screenshots and downloadable videos/traces/reports; runs list; agent registry with capabilities and status; execution history per feature and last result in the feature summary |
| Environments | Inventory of Development/QA/Staging/Pre-production/Automation/Production environments: base URL, build, database, tenant, agent pools, health-check URL (polled; unavailable or maintenance environments get no new runs or dispatch), variables and **secrets encrypted with ASP.NET Core Data Protection** (never returned to browsers, compared blind, decrypted only at dispatch, masked by agents in logs, results and text evidence). Clone and compare. **Production guard**: needs the `environments.production` permission; destructive test cases are skipped unless explicitly authorised for that environment; unlinked automated tests are skipped |
| Test data | Versioned data sets (CUSTOMERS, VEHICLES…) with records, CSV/Excel import, per-environment sets. Test cases declare `DATASET[:count]`; records are **reserved exclusively per job** (filtered unique index) and delivered in `WINGS_CONTEXT_FILE`, then released. Too few records is caught when the run starts |
| Ordering and locks | **Dependency waves** from test-case and feature prerequisites (never from creation sequence): a job starts only when the jobs it depends on finish; tests whose prerequisite did not pass are **Blocked**. Independent tests run in parallel. **Exclusive resource locks** (`db:AUTO-QA`, `user:sales01`) are taken atomically at dispatch and released with the job or on agent loss |
| Plans and campaigns | Test plans (scope as suites, release, build, environment, owners, testers, dates, entry/exit criteria, risk, defect acceptance rules; templates and cloning; Draft → Active → Completed → Approved with approval by a different person) with latest-outcome coverage per test case. Plan runs keep **manual test cases pending** for testers to record results. Campaigns run plans × environments for a build, with a live matrix |
| Scheduling | Once, hourly, daily, weekly, monthly or cron, **time-zone aware**, with the next occurrences shown; safe on several nodes. **Webhook triggers** for build and deployment pipelines (per-schedule token, build version and variables in the body) |
| Defects | Repository (`DEF-00001`) with product, feature, test case, failing run, build, environment, severity, priority, reproduction steps, expected/actual, evidence (screenshots, logs, videos, traces of the failing test), assignee, root cause, fix version, verification. Workflow Open → Assigned → In progress → Fixed → Ready for retest → Verified → Closed, plus Reopened, Duplicate, Rejected, Deferred (justification required). **Suggested from failed tests without duplicates**: each failure gets a signature (test + error with numbers, ids, timestamps and paths masked); a repeat attaches as another occurrence. Retest runs start from the workflow; a passing retest verifies the defect, a failing one reopens it, and a closed defect whose failure recurs is reopened. Failure **clusters** (same error across tests) and **recurring/flaky** analysis. Push to **GitHub, Jira, Azure DevOps** or any internal tracker (webhook) through adapters |
| Releases and gates | Release workspace per product version: features added (introduced in the release), modified (declared with a risk, or detected from builds), deprecated (in the window); required tests and why; completion, pass rate, failed P1 tests, unresolved critical defects, regression completion, environment coverage, known risks. Configurable **quality gates** (suite passed, no unresolved defects ≥ severity, changed features ≥ risk tested, minimum automation, tagged tests passed, minimum pass rate), blocking or warning-only, product-wide or per release, each with a written definition and denominator. **Exceptions** need `releases.approve` and a justification and are audited. Execute the release scope in one click. **Signed-off report**: PDF stored with its SHA-256 |
| Dashboards | Global (products, features with/without tests, feature, requirement and automation coverage, latest results, untested changed features, defects, release readiness, 30-day trend), product (module coverage → feature status → feature, failure and automation trends, high-risk untested areas, defects by module, build quality) and execution (running jobs, queue, agent utilization, durations, throughput, retries, environment failures). Drill down to features, runs and evidence |
| Reports | 17 reports (inventory, by sequence, between releases, untested, automation coverage, execution summary, history by feature, failed, flaky, defects by module, release comparison, tester productivity, agent utilization, durations, quality trends, business-critical coverage, changed-code coverage) with configurable columns, sorting, **Excel/CSV/PDF** export, saved and shared filters, **scheduled email delivery**, and permission-based visibility |
| Notifications | In-app inbox with live badge; email (SMTP, or `.eml` files to a pickup folder), **Microsoft Teams** (Adaptive Card) and **webhooks** (HMAC-SHA256 signed); personal subscriptions and administrator routing rules by event, product and severity; editable templates; **escalation** of unacknowledged events; delivery log with retries. Events: run completed, critical (P1) failure, unusually high failure rate, agent offline, environment unavailable, quality gate failed, approval required, defect assigned/recurred, build received |
| Source and builds | Git and **TFVC** repositories (server paths made repository-relative). CI reports builds with changed files and commits (`/api/v1/ci/builds`, integration key): files map to features through approved source mappings and the features' assemblies, classes, forms and database objects; commits referencing a feature link to it; features depending on changed ones are impacted too. Affected tests and suggested suites are listed and can **run automatically after the build**; the build's release is re-evaluated and CI can **block or warn** on its gates (`/api/v1/ci/gates`, 409 when blocked). Change impact screen with a what-if analysis |
| AI assistant (optional) | Claude (Opus 5.5 by default) through the official Anthropic SDK behind a provider interface. **Suggest test cases** (positive, negative, boundary, exception) from a feature's description, requirements, business/validation rules, UI metadata, API definitions, existing tests, past defects and, if allowed, linked test source; accepted ones become **draft** test cases tagged `ai-generated` that go through review. **Draft test code** for FlaUI/C#, Playwright/TS or C#, API and database-validation tests using the platform's conventions and approved published code as a style reference; the draft is uploaded as a package version that must be validated and approved by someone else. **Failure analysis** from the error, stack trace, text evidence, screenshots (if allowed), execution history and recent build changes, with probable causes, where to look and next steps; can be added to the linked defect as a labelled comment. **Test selection** ranks candidates by a documented risk score (changes, dependencies, release scope, criticality, defects, failure history) and the AI orders and explains within a time budget. **Maintenance** detects flaky, broken-selector, outdated, duplicate, unused and behaviour-changed tests and asks the AI for recommendations; **test repair** proposes a file change shown as a diff, applied only as a new package version that needs validation and approval. Every result shows **verified facts** (from the platform's records) apart from **AI suggestions**. Before anything is sent, environment secrets, passwords, tokens, keys, emails, phone numbers and administrator patterns are masked; video and trace archives are never sent. An administrator policy enables the assistant, controls whether source code and screenshots may be sent, and limits requests per user per day; every request is recorded (who, what was sent, masking count, model, tokens, result, what was done with it). Selection and maintenance still show their platform-computed facts when the AI is off |
| Security | ASP.NET Core Identity + JWT; 10 roles with an editable permission matrix resolved per request; product-level access restrictions (view/edit); login rate limiting and lockout; audit log of every change |

**Uploaded code is never compiled or executed by the web server.** Validation there is static only; code is built and run
only on execution agents. See [docs/agent-installation.md](docs/agent-installation.md).

## Repository layout

```
src/Wings.Domain           Entities (GUID ids, audit metadata, concurrency tokens)
src/Wings.Application      Service interfaces + implementations, DTOs, permissions
src/Wings.Infrastructure   EF Core (SQLite + SQL Server), migrations, sequence allocator, blob store,
                           durable job queue, package validation pipeline, Identity
src/Wings.Api              ASP.NET Core Web API (v1), auth, agent protocol, SignalR hub, serves the built Angular app
src/Wings.Agent            Execution agent (console/worker): Playwright, dotnet test (incl. FlaUI) and pytest executors
src/Wings.Agent.Contracts  Wire contract shared by the API and the agent
tests/Wings.Tests          Unit + API integration tests (real host, real database)
web/                       Angular 22 + Angular Material front end
database/sqlserver         Idempotent SQL Server schema script (schema.sql) generated from migrations
samples/apps               SAMPLE WinForms application under test (the sample web portal is served at /sample-portal/)
samples/automation         SAMPLE FlaUI (WinForms) and Playwright projects used for demos and tests
docs                       Agent installation and operations
```

## Running locally (macOS, Linux or Windows)

Prerequisites: .NET SDK 10 (LTS), Node.js 22+.

> .NET 8 leaves Microsoft support on 10 Nov 2026, so the solution targets .NET 10 LTS, which the specification allows ("or later supported LTS").

1. Create `src/Wings.Api/appsettings.Development.json` from `appsettings.Development.example.json`: generate a signing
   key (`openssl rand -base64 48`) and choose passwords for the seeded development users. The file is git-ignored.
2. Build the UI into the API's `wwwroot`, then run the API:

   ```bash
   cd web && npm ci && npx ng build && cd ..
   dotnet run --project src/Wings.Api --launch-profile http
   ```

   Open http://localhost:5057. In development the API uses SQLite (`src/Wings.Api/App_Data/wings-dev.db`), applies
   migrations, and seeds reference data, the dev users and clearly labelled sample Wings products.
3. For UI work with hot reload run `npx ng serve` in `web/` and open http://localhost:4200 (it proxies `/api` and `/hubs` to the API).

### Triggering runs from CI/CD

Create a schedule with trigger *Webhook*, then *Create token* (shown once) and call it after a build or deployment:

```bash
curl -X POST "https://wto.example.local/api/v1/triggers/schedules/<schedule-id>" -H "X-Wings-Trigger-Token: <token>" -H "Content-Type: application/json" -d '{"buildVersion": "2026.2.19"}'
```

### Build reporting and release gates from CI

Create an integration key under *Administration → Integrations* (shown once), then from the pipeline:

```bash
curl -X POST https://wto.example.local/api/v1/ci/builds -H "X-Wings-Integration-Key: <key>" -H "Content-Type: application/json" -d '{"product": "AUTO", "buildNumber": "2026.3.0.121", "repository": "WingsAuto", "release": "2026.3", "trigger": "affected", "environmentCode": "QA", "changedFiles": ["src/Sales/Invoicing/InvoiceCalculator.cs"], "commits": [{"id": "a1b2c3d", "message": "Fix GST rounding (feature 146)"}]}'
```

```bash
curl -f "https://wto.example.local/api/v1/ci/gates?product=AUTO&release=2026.3&enforce=true" -H "X-Wings-Integration-Key: <key>"
```

The second call answers 409 while a blocking gate fails, so the promotion step fails.

### Running a test locally end to end

1. *Agents → Register agent* (kind Browser) and copy the key.
2. Install a browser once: `npx playwright@1.56.1 install chromium`.
3. Start an agent:

   ```bash
   WINGS_AGENT__AGENTKEY=<key> WINGS_AGENT__SERVERURL=http://localhost:5057 dotnet run --project src/Wings.Agent
   ```

4. Upload `samples/automation/wings-portal-e2e` as a .zip (product *Wings Auto*), approve and publish it with two
   different users, then *Execute* it with the variable `WINGS_BASE_URL=http://localhost:5057/sample-portal/`.
   Two tests pass and one fails against a deliberate defect in the sample portal, with screenshot, video and trace.

### Tests

```bash
dotnet test tests/Wings.Tests
```

```bash
cd web && npx ng test --watch=false
```

The integration tests start the real API in-process against a fresh SQLite database per test class, sign in as users
with different roles and exercise the acceptance scenarios: feature creation and sequence allocation (including 20
concurrent creates), moves and reorders, legacy import, sequence filtering, permissions and product access, test-case
versioning and approval, CSV/Excel round trips, suites and shared steps, and package upload → validation → discovery →
mapping → approval → publication, plus rejection of zip-slip, EICAR, executables, nested archives and syntax errors.
Phase 5 tests cover defects from failures without duplicates, automatic retest verification and recurrence reopening,
tracker push, release gates with exceptions, CI gate checks and signed reports, TFVC build impact with an automatically
started affected-test run, signed webhook/Teams/email deliveries, escalation, agent-offline detection, dashboards, report
exports and scheduled report emails. Phase 6 tests use a scripted model and cover the policy and daily limit, masking of
secrets and personal data, refusal and malformed-answer handling, applying suggestions through the normal workflows, invented
identifiers being dropped, facts-only mode, and the exact request the Anthropic SDK sends (against a local HTTP stub).

## Production deployment (Windows Server, IIS, SQL Server)

1. **Database.** Create an empty database and run `database/sqlserver/schema.sql` (idempotent), or leave
   `Database:MigrateOnStartup=true` and grant the app pool identity `db_ddladmin` for the first start. Regenerate the
   script after model changes with
   `dotnet ef migrations add <Name> --project src/Wings.Infrastructure --startup-project src/Wings.Infrastructure --context SqlServerWingsDbContext -o Persistence/Migrations/SqlServer`
   (and the same for `SqliteWingsDbContext`).
2. **Build the package** on any machine with the .NET 10 SDK and Node.js: `./deploy/publish-iis.sh` produces
   `publish/WingsTestOrchestrator-iis-<date>-<commit>.zip`. It refuses to package local settings or data.
3. **Install.** Install the ASP.NET Core 10 Hosting Bundle, extract the zip on the server and run `Install-Wings.ps1` as
   Administrator. It sets up IIS, the database, an always-running app pool (the job queue and schedulers live in that
   process), permissions and the first administrator. See [deploy/iis/INSTALL.md](deploy/iis/INSTALL.md) for options,
   upgrades and troubleshooting.
4. **Configuration.** The installer writes `appsettings.Production.json`; these settings can also come from environment
   variables or a secret store, and are never committed:
   - `ConnectionStrings__Wings` — SQL Server connection string (`Database:Provider` defaults to `SqlServer`)
   - `Auth__Jwt__SigningKey` — ≥ 32 random characters; startup fails without it
   - `Storage__RootPath` — local folder or UNC share for blobs (the app pool identity needs modify rights)
   - `Scanning__ClamAv__Host` / `Port` — clamd endpoint; without it only built-in checks run and every report says so
   - `Seed__Users__0__UserName`, `…__Password`, `…__Role` — bootstrap the first administrator once, then remove
   - Data Protection keys for environment secrets are stored under `Storage__RootPath/keys`. Back that folder up with the
     database (without it secrets cannot be decrypted) and, on Windows, protect it with DPAPI or a certificate
     (`ProtectKeysWithDpapi` / `ProtectKeysWithCertificate`).
   - `Notifications__PublicUrl` — the site's public URL, used for links in emails, Teams cards, webhooks and tracker issues
   - `Notifications__Smtp__Host`, `Port`, `EnableSsl`, `UserName`, `Password`, `From` — SMTP relay; without a host, emails
     are written as `.eml` files to `Storage__RootPath/mail-outbox`
   - `Notifications__HighFailureRatePercent` (30) and `HighFailureRateMinTests` (5) — when a run counts as unusually failing
   - `ANTHROPIC_API_KEY` — enables the AI assistant (or `Ai__ApiKey` from a secret store); then switch it on under
     *Administration → AI assistant policy*. Optional: `Ai__Model` (default `claude-opus-5-5`), `Ai__Effort` (`medium`),
     `Ai__MaxTokens`, `Ai__RefusalFallbacks` (default on: a refused request is re-served on a fallback model server-side),
     `Ai__Provider=None` to turn the integration off entirely
   - `Jobs__RunWorker=false` on extra web nodes if only one node should process validation jobs (the queue is safe to share)
5. Health endpoint: `GET /health`.

## Conventions for linking automation to features

Discovered tests are mapped automatically when they carry the feature's creation sequence (within the package's product):

| Framework | Convention |
|---|---|
| xUnit | `[Trait("WingsFeature", "00146")]`, `[Trait("WingsTestCase", "TC-00042")]` |
| NUnit / MSTest | `[Category("WingsFeature:146")]` / `[TestCategory("WingsFeature:146")]` |
| Playwright | `@feature-00146` or `@tc-42` in a test or describe title, or `tag` option |
| pytest | `@pytest.mark.feature("00146")` |
| JSON/YAML | `kind: WingsTestDefinition` with `tests: [{ id, name, feature, testCase }]` |

Mappings carry forward to new versions by test identifier and can be changed in the UI.

## Design notes

- **Sequence numbers** live in `SequenceScopes`; allocation is an atomic `UPDATE … SET NextValue = NextValue + 1` in the
  caller's transaction, backed by a unique index on `(SequenceScopeId, SequenceNumber)`. Deleted features are soft-deleted
  so their numbers stay taken. The tree's materialized path uses sequence numbers (`/1/2/3/`), which keeps subtree queries
  index-friendly and allows about 100 levels.
- Modules, submodules, features and operations are all `Features` rows typed by the `FeatureTypes` master table rather
  than separate `Modules` tables, so administrators can add levels without schema changes.
- Every entity has a `Version` concurrency token; stale edits return HTTP 409.
- Blobs are content-addressed (SHA-256) and immutable, so identical files are stored once and historical artifacts cannot change.
- Validation runs on a durable, lease-based job queue in the database (the "Hangfire or equivalent" option), with retries and back-off.
- Coverage figures have explicit denominators (shown as tooltips and report definitions): feature coverage, requirement
  coverage and test-case automation percentage are distinct, and code coverage is not claimed (it comes from build tools).
- Release readiness is computed from the latest execution of each test case inside the release's testing window
  (testing start date, else the previous release's date, else when the release was created).
- Reports and sign-off PDFs come from a small built-in PDF writer (standard fonts, no third-party licence).
- The AI assistant never writes data directly: suggestions are stored with the request record, and a person applies them
  through the existing services (test-case review, package validation and approval with separation of duties, defect
  comments). Answers are constrained to a JSON schema, identifiers the model returns are checked against the context, and
  context is selected to fit rather than silently cut.
- Permissions are read from the database per request (30 s cache), so role changes and deactivations apply without waiting for token expiry.

## Roadmap

| Phase | Status |
|---|---|
| 1 Feature repository | Done |
| 2 Test repository and test code | Done (compilation deferred to agents) |
| 3 Execution infrastructure: agents, registration, leases, dispatch, live monitor, evidence storage | Done (Playwright run verified here; FlaUI agent to be verified on Windows) |
| 4 Orchestration: schedules and triggers, plans/campaigns, environments and secrets, test data, resource locks, dependency-ordered execution, manual results | Done |
| 5 Quality: defects, releases, quality gates, dashboards, reports, notifications, source/build integration | Done (Teams, Jira, GitHub and Azure DevOps verified against recorded HTTP, not live tenants) |
| 6 AI assistance: test suggestions, code generation, failure analysis, risk-based selection, maintenance and repair proposals | Done (verified with a scripted model and a wire-level SDK test; needs an Anthropic API key for live use) |

Not yet implemented: Microsoft Entra ID sign-in (Identity + JWT only), an S3-compatible blob store (the `IFileStore`
interface is ready), evidence retention policies, API/database agent executors, two-way status sync with external issue
trackers (defects are pushed one way), and pulling commits directly from Git/TFVC servers (CI pushes them).

The `samples/automation/WingsAuto.WinForms.UiTests` project targets `net10.0-windows` and drives a sample WinForms app
through FlaUI; it is uploaded and validated here but can only be built and run on a Windows agent with an interactive
desktop session.
