import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { debounceTime } from 'rxjs';
import { ExecutionDashboard, GlobalDashboard, ProductDashboard, QualityApi, TrendPoint, readinessTone } from '../core/quality';
import { ChangesService, UiService } from '../core/ui.service';
import { ChartPoint, ChartSeries, BarChart, LineChart, SERIES, STATUS } from '../shared/charts';
import { HumanizePipe, StatusPill } from '../shared/display';
import { RunBar } from '../execution/run-bar';

const OUTCOME_SERIES: ChartSeries[] = [
  { key: 'passed', label: 'Passed', color: STATUS.passed },
  { key: 'failed', label: 'Failed', color: STATUS.failed },
  { key: 'blocked', label: 'Blocked', color: STATUS.blocked },
  { key: 'other', label: 'Skipped / not run / infra', color: STATUS.other },
];

function trendPoints(t: TrendPoint[]): ChartPoint[] {
  return t.map(p => ({ label: p.date.slice(5), values: { passed: p.passed, failed: p.failed, blocked: p.blocked, other: p.other } }));
}

const TABS = `
  <nav class="tabs">
    <a routerLink="/dashboard" [class.on]="tab === 'global'">Global quality</a>
    <a routerLink="/dashboard/execution" [class.on]="tab === 'execution'">Execution</a>
  </nav>`;

const STYLES = `
  .tabs { display: flex; gap: 4px; margin-bottom: 12px; } .tabs a { padding: 6px 12px; border-radius: 4px; text-decoration: none; color: var(--muted); }
  .tabs a.on { background: var(--selected); color: var(--text); font-weight: 600; }
  .tiles { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 8px; margin-bottom: 12px; }
  .tile { background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 10px 12px; text-decoration: none; color: inherit; display: block; }
  a.tile:hover { border-color: var(--accent); }
  .tile .v { font-size: 22px; font-weight: 600; font-variant-numeric: tabular-nums; } .tile .l { font-size: 12px; color: var(--muted); }
  .tile .s { font-size: 11px; color: var(--muted); }
  .two { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(420px, 100%), 1fr)); gap: 12px; } .two .panel + .panel { margin-top: 0; }
  .defs { font-size: 12px; color: var(--muted); } .defs p { margin: 2px 0; }
  .bar { height: 6px; background: var(--off-bg); border-radius: 3px; overflow: hidden; min-width: 60px; } .bar > i { display: block; height: 100%; background: var(--good); }
  td.n, th.n { text-align: right; font-variant-numeric: tabular-nums; }
`;

/** Global quality dashboard: products, coverage, latest results, defects and release readiness. */
@Component({
  selector: 'app-global-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, StatusPill, BarChart, MatIconModule, MatTooltipModule],
  template: `
    <div class="page">
      ${TABS}
      @if (d(); as d) {
        <div class="tiles">
          <div class="tile"><div class="v">{{ d.products }}</div><div class="l">Products</div></div>
          <div class="tile" [matTooltip]="d.definitions['features']"><div class="v">{{ d.features }}</div><div class="l">Features</div>
            <div class="s">{{ d.featuresWithTests }} with tests · {{ d.featuresWithoutTests }} without</div></div>
          <div class="tile" [matTooltip]="d.definitions['featureCoverage']"><div class="v">{{ pct(d.featureCoverage) }}</div><div class="l">Feature coverage</div></div>
          <div class="tile" [matTooltip]="d.definitions['requirementCoverage']"><div class="v">{{ pct(d.requirementCoverage) }}</div><div class="l">Requirement coverage</div></div>
          <a class="tile" routerLink="/test-cases" [matTooltip]="d.definitions['automation']"><div class="v">{{ pct(d.automationCoverage) }}</div><div class="l">Test-case automation</div>
            <div class="s">{{ d.automatedTests }} automated · {{ d.manualTests }} manual</div></a>
          <a class="tile" routerLink="/runs" [matTooltip]="d.definitions['latest']"><div class="v">{{ d.latestPassed }}</div><div class="l">Latest passed</div></a>
          <a class="tile" routerLink="/defects" [queryParams]="{ tab: 'recurring' }"><div class="v" style="color: var(--bad)">{{ d.latestFailed }}</div><div class="l">Latest failed</div>
            <div class="s">{{ d.latestBlocked }} blocked · {{ d.neverExecuted }} never run</div></a>
          <a class="tile" routerLink="/impact" [matTooltip]="d.definitions['untestedChanged']"><div class="v">{{ d.untestedChangedFeatures }}</div><div class="l">Untested changed features</div></a>
          <a class="tile" routerLink="/defects" [queryParams]="{ openOnly: true }"><div class="v">{{ d.openDefects }}</div><div class="l">Open defects</div>
            <div class="s">{{ d.openDefectsBySeverity['Critical'] }} critical · {{ d.openDefectsBySeverity['Major'] }} major</div></a>
        </div>
        <div class="two">
          <div class="panel"><h3>Test results, last 30 days</h3>
            <app-bar-chart [series]="outcomeSeries" [points]="trend()" ariaLabel="Daily test outcomes for the last 30 days" /></div>
          <div class="panel"><h3>Release readiness</h3>
            <table class="data"><thead><tr><th>Release</th><th>Status</th><th>Readiness</th><th class="n">Gates</th></tr></thead><tbody>
              @for (r of d.releases; track r.id) {
                <tr class="clickable" [routerLink]="['/releases', r.id]"><td>{{ r.productName }} <b>{{ r.versionLabel }}</b></td><td>{{ r.status }}</td>
                  <td><app-status [value]="tone(r.readiness)" [label]="r.readiness" /></td><td class="n">{{ r.gatesPassed }}/{{ r.gatesTotal }}</td></tr>
              } @empty { <tr><td colspan="4" class="empty">No unreleased releases.</td></tr> }
            </tbody></table></div>
        </div>
        <div class="panel"><h3>Products</h3>
          <table class="data"><thead><tr><th>Product</th><th class="n">Features</th><th>Feature coverage</th><th class="n">Test cases</th><th class="n">Automated</th>
            <th class="n">Passed</th><th class="n">Failed</th><th class="n">Open defects</th><th>Next release</th></tr></thead><tbody>
            @for (p of d.productRows; track p.productId) {
              <tr class="clickable" [routerLink]="['/dashboard/products', p.productId]">
                <td><b>{{ p.code }}</b> {{ p.name }}</td><td class="n">{{ p.features }}</td>
                <td><div class="row"><div class="bar"><i [style.width.%]="p.features ? 100 * p.featuresWithTests / p.features : 0"></i></div>{{ p.featuresWithTests }}/{{ p.features }}</div></td>
                <td class="n">{{ p.testCases }}</td><td class="n">{{ pct(p.automationPercent) }}</td><td class="n">{{ p.latestPassed }}</td><td class="n">{{ p.latestFailed }}</td>
                <td class="n">{{ p.openDefects }}@if (p.criticalDefects) { <span class="pill" data-tone="bad">{{ p.criticalDefects }} critical</span> }</td>
                <td>@if (p.nextRelease) { {{ p.nextRelease }} <app-status [value]="tone(p.nextReleaseReadiness)" [label]="p.nextReleaseReadiness" /> }</td></tr>
            }
          </tbody></table></div>
        <div class="panel defs"><h3>Definitions</h3>@for (k of defKeys(d); track k) { <p>{{ d.definitions[k] }}</p> }</div>
      } @else { <div class="empty">Loading…</div> }
    </div>`,
  styles: STYLES,
})
export class GlobalDashboardPage {
  private api = inject(QualityApi);
  private ui = inject(UiService);
  tab = 'global';
  d = signal<GlobalDashboard | null>(null);
  outcomeSeries = OUTCOME_SERIES;
  trend = computed(() => trendPoints(this.d()?.trend ?? []));
  tone = readinessTone;

  constructor() {
    this.load();
    inject(ChangesService).events.pipe(debounceTime(3000), takeUntilDestroyed()).subscribe(e => { if (e.topic === 'runChanged') this.load(); });
  }

  load() { this.api.globalDashboard().subscribe({ next: d => this.d.set(d), error: e => this.ui.error(e) }); }
  pct(v?: number) { return v == null ? '–' : `${v}%`; }
  defKeys(d: GlobalDashboard) { return Object.keys(d.definitions); }
}

/** Product dashboard: module coverage → feature status → test cases, trends, risk areas, defects and build quality. */
@Component({
  selector: 'app-product-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DatePipe, StatusPill, BarChart, LineChart, MatButtonModule, MatIconModule, RunBar],
  template: `
    <div class="page">
      ${TABS}
      @if (d(); as d) {
        <div class="page-head"><a routerLink="/dashboard" class="muted">Global</a> › <h1>{{ d.productName }}</h1><span class="spacer"></span>
          <a mat-stroked-button routerLink="/reports"><mat-icon>summarize</mat-icon>Reports</a></div>
        <div class="tiles">
          <div class="tile"><div class="v">{{ d.features }}</div><div class="l">Features</div><div class="s">{{ d.featuresWithTests }} with tests</div></div>
          <div class="tile"><div class="v">{{ d.testCases }}</div><div class="l">Test cases</div><div class="s">{{ d.automated }} automated ({{ d.automationPercent ?? 0 }}%)</div></div>
          @for (s of statuses(d); track s[0]) { <a class="tile" routerLink="/defects" [queryParams]="{ productId: d.productId, status: s[0] }"><div class="v">{{ s[1] }}</div><div class="l">Defects {{ s[0] }}</div></a> }
        </div>
        <div class="panel"><h3>Module coverage</h3>
          <table class="data"><thead><tr><th>Module</th><th class="n">Features</th><th>Feature coverage</th><th class="n">Test cases</th><th class="n">Automated</th><th class="n">Passed</th>
            <th class="n">Failed</th><th class="n">Not executed</th><th class="n">Pass rate</th><th class="n">Open defects</th></tr></thead><tbody>
            @for (m of d.modules; track m.featureId) {
              <tr class="clickable" [class.selected]="module() === m.featureId" (click)="module.set(module() === m.featureId ? null : m.featureId)">
                <td><span class="mono muted">{{ m.sequenceNumber }}</span> {{ m.name }}</td><td class="n">{{ m.features }}</td>
                <td><div class="row"><div class="bar"><i [style.width.%]="m.featureCoverage ?? 0"></i></div>{{ m.featureCoverage ?? 0 }}%</div></td>
                <td class="n">{{ m.testCases }}</td><td class="n">{{ m.automated }}</td><td class="n">{{ m.passed }}</td><td class="n">{{ m.failed }}</td><td class="n">{{ m.notExecuted }}</td>
                <td class="n">{{ m.passRate == null ? '–' : m.passRate + '%' }}</td><td class="n">{{ m.openDefects }}</td></tr>
            }
          </tbody></table></div>
        <div class="panel"><div class="row"><h3>Feature status</h3><span class="muted">{{ module() ? 'in the selected module' : 'all modules' }} · click a feature to open it</span></div>
          <table class="data"><thead><tr><th>Seq</th><th>Feature</th><th>Coverage</th><th class="n">Test cases</th><th>Latest result</th><th>Last executed</th><th class="n">Open defects</th></tr></thead><tbody>
            @for (f of features(); track f.featureId) {
              <tr class="clickable" [routerLink]="['/features', f.featureId]"><td class="num">{{ f.sequenceNumber }}</td><td [style.padding-left.px]="8 + 12 * f.depth">{{ f.name }}</td>
                <td><app-status [value]="f.coverage" /></td><td class="n">{{ f.testCases }}</td><td>@if (f.latestOutcome) { <app-status [value]="f.latestOutcome" /> } @else { <span class="muted">–</span> }</td>
                <td>{{ f.lastExecutedAt | date: 'short' }}</td><td class="n">{{ f.openDefects || '' }}</td></tr>
            }
          </tbody></table></div>
        <div class="two">
          <div class="panel"><h3>Test failure trend, 30 days</h3><app-bar-chart [series]="outcomeSeries" [points]="failureTrend()" ariaLabel="Daily outcomes for this product" /></div>
          <div class="panel"><h3>Automation trend, 12 weeks</h3><app-line-chart [series]="autoSeries" [points]="automationTrend()" ariaLabel="Test cases and automated test cases per week" /></div>
        </div>
        <div class="two">
          <div class="panel"><h3>High-risk untested areas</h3>
            <table class="data"><tbody>@for (r of d.riskAreas; track r.featureId) {
              <tr class="clickable" [routerLink]="['/features', r.featureId]"><td class="num">{{ r.sequenceNumber }}</td><td>{{ r.name }}</td><td class="muted">{{ r.reason }}</td><td class="n">{{ r.score }}</td></tr>
            } @empty { <tr><td class="empty">No risk areas: every feature has passing tests and no open defects.</td></tr> }</tbody></table></div>
          <div class="panel"><h3>Open defects by module</h3>
            <table class="data"><thead><tr><th>Module</th><th class="n">Critical</th><th class="n">Major</th><th class="n">Minor</th><th class="n">Trivial</th></tr></thead><tbody>
              @for (b of d.defectsByModule; track b.key) { <tr><td>{{ b.label }}</td><td class="n">{{ b.critical }}</td><td class="n">{{ b.major }}</td><td class="n">{{ b.minor }}</td><td class="n">{{ b.trivial }}</td></tr> }
              @empty { <tr><td colspan="5" class="empty">No open defects.</td></tr> }</tbody></table></div>
        </div>
        <div class="two">
          <div class="panel"><h3>Latest regression runs</h3>
            @for (r of d.latestRegression; track r.id) { <a [routerLink]="['/runs', r.id]" class="runrow"><span class="mono">{{ r.numberDisplay }}</span> {{ r.name }}</a><app-run-bar [run]="r" /> }
            @empty { <p class="muted">No completed runs yet.</p> }</div>
          <div class="panel"><h3>Build quality</h3>
            @if (d.buildQuality.length) { <app-bar-chart [series]="buildSeries" [points]="builds()" ariaLabel="Passed and failed tests per build" /> } @else { <p class="muted">No builds reported for this product.</p> }</div>
        </div>
      } @else { <div class="empty">Loading…</div> }
    </div>`,
  styles: STYLES + `.runrow { display: block; margin-top: 8px; text-decoration: none; }`,
})
export class ProductDashboardPage {
  private api = inject(QualityApi);
  private ui = inject(UiService);
  id = input.required<string>();
  tab = 'product';
  d = signal<ProductDashboard | null>(null);
  module = signal<string | null>(null);
  outcomeSeries = OUTCOME_SERIES;
  autoSeries: ChartSeries[] = [{ key: 'testCases', label: 'Test cases', color: SERIES[0] }, { key: 'automated', label: 'Automated', color: SERIES[1] }];
  buildSeries: ChartSeries[] = [{ key: 'passed', label: 'Passed', color: STATUS.passed }, { key: 'failed', label: 'Failed', color: STATUS.failed }];
  failureTrend = computed(() => trendPoints(this.d()?.failureTrend ?? []));
  automationTrend = computed(() => (this.d()?.automationTrend ?? []).map(p => ({ label: p.week.slice(5), values: { testCases: p.testCases, automated: p.automated } })));
  builds = computed(() => (this.d()?.buildQuality ?? []).map(b => ({ label: b.buildNumber, values: { passed: b.passed, failed: b.failed } })));
  features = computed(() => (this.d()?.featureStatus ?? []).filter(f => !this.module() || f.moduleId === this.module()));

  constructor() {
    effect(() => { const id = this.id(); this.d.set(null); this.api.productDashboard(id).subscribe({ next: d => this.d.set(d), error: e => this.ui.error(e) }); });
  }

  statuses(d: ProductDashboard) { return Object.entries(d.defectsByStatus).filter(([k]) => ['Open', 'Assigned', 'InProgress', 'Reopened', 'ReadyForRetest'].includes(k)); }
}

/** Execution dashboard: running jobs, queue, agents and utilization, durations, throughput, retries and environment failures. */
@Component({
  selector: 'app-execution-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DatePipe, StatusPill, BarChart, HumanizePipe, RunBar],
  template: `
    <div class="page">
      ${TABS}
      @if (d(); as d) {
        <div class="tiles">
          <div class="tile"><div class="v">{{ d.running.length }}</div><div class="l">Running jobs</div></div>
          <div class="tile"><div class="v">{{ queued(d) }}</div><div class="l">Pending queue</div></div>
          <a class="tile" routerLink="/agents"><div class="v">{{ d.agentsOnline }}/{{ d.agentsTotal }}</div><div class="l">Agents online</div><div class="s">{{ d.agentsBusy }} busy</div></a>
          <div class="tile"><div class="v">{{ d.slotUtilizationNow == null ? '–' : d.slotUtilizationNow + '%' }}</div><div class="l">Slot utilization now</div></div>
          <div class="tile"><div class="v">{{ d.retryRate == null ? '–' : d.retryRate + '%' }}</div><div class="l">Retry frequency (7 days)</div><div class="s">{{ d.retries7d }} of {{ d.jobs7d }} jobs</div></div>
        </div>
        <div class="two">
          <div class="panel"><h3>Test throughput, last 24 hours</h3><app-bar-chart [series]="throughputSeries" [points]="throughput()" ariaLabel="Tests completed per hour" /></div>
          <div class="panel"><h3>Running jobs</h3>
            <table class="data"><thead><tr><th>Run</th><th>Framework</th><th>Agent</th><th>Status</th><th>Since</th></tr></thead><tbody>
              @for (j of d.running; track j.jobId) { <tr class="clickable" [routerLink]="['/runs', j.runId]"><td class="num">{{ j.runNumber }}</td><td>{{ j.framework }}</td><td>{{ j.agentName }}</td>
                <td><app-status [value]="j.status" /></td><td>{{ j.startedAt | date: 'shortTime' }}</td></tr> }
              @empty { <tr><td colspan="5" class="empty">Nothing running.</td></tr> }</tbody></table>
            <h3>Queue</h3>
            <table class="data"><tbody>@for (q of d.queue; track q.framework + q.requiresDesktop) { <tr><td>{{ q.framework }}{{ q.requiresDesktop ? ' (desktop)' : '' }}</td><td class="n">{{ q.queued }}</td>
              <td class="muted">oldest {{ q.oldestQueuedAt | date: 'short' }}</td></tr> } @empty { <tr><td class="empty">Queue empty.</td></tr> }</tbody></table></div>
        </div>
        <div class="two">
          <div class="panel"><h3>Agent utilization, last 24 hours</h3>
            <table class="data"><thead><tr><th>Agent</th><th>Kind</th><th>State</th><th class="n">Slots</th><th class="n">Active</th><th class="n">Busy h</th><th>Utilization</th></tr></thead><tbody>
              @for (a of d.agents; track a.agentId) { <tr><td>{{ a.name }}</td><td>{{ a.kind }}</td><td><app-status [value]="a.inMaintenance ? 'Warning' : a.isOnline ? 'Active' : 'Failed'" [label]="a.inMaintenance ? 'Maintenance' : a.isOnline ? 'Online' : 'Offline'" /></td>
                <td class="n">{{ a.slots }}</td><td class="n">{{ a.activeJobs }}</td><td class="n">{{ a.busyHours24h }}</td>
                <td><div class="row"><div class="bar"><i [style.width.%]="a.utilization24h ?? 0"></i></div>{{ a.utilization24h ?? 0 }}%</div></td></tr> }</tbody></table></div>
          <div class="panel"><h3>Average execution duration (7 days)</h3>
            <table class="data"><thead><tr><th>Framework</th><th class="n">Jobs</th><th class="n">Average</th><th class="n">Max</th></tr></thead><tbody>
              @for (x of d.durations; track x.framework) { <tr><td>{{ x.framework }}</td><td class="n">{{ x.jobs }}</td><td class="n">{{ secs(x.averageSeconds) }}</td><td class="n">{{ secs(x.maxSeconds) }}</td></tr> }
              @empty { <tr><td colspan="4" class="empty">No completed jobs.</td></tr> }</tbody></table>
            <h3>Environment-related failures (7 days)</h3>
            <table class="data"><tbody>@for (e of d.environmentFailures; track e.name) { <tr><td>{{ e.name }}</td><td class="n">{{ e.infrastructureErrors }} infra errors</td><td class="n">{{ e.runs }} runs</td>
              <td><app-status [value]="e.health === 'Available' ? 'Passed' : e.health === 'Unavailable' ? 'Failed' : 'Warning'" [label]="e.health | humanize" /></td></tr> }
              @empty { <tr><td class="empty">No infrastructure errors.</td></tr> }</tbody></table></div>
        </div>
        <div class="panel"><h3>Failed runs (7 days)</h3>
          @for (r of d.failedRuns; track r.id) { <a [routerLink]="['/runs', r.id]" class="runrow"><span class="mono">{{ r.numberDisplay }}</span> {{ r.name }}</a><app-run-bar [run]="r" /> }
          @empty { <p class="muted">No failed runs.</p> }</div>
      } @else { <div class="empty">Loading…</div> }
    </div>`,
  styles: STYLES + `.runrow { display: block; margin-top: 8px; text-decoration: none; } h3 { margin-top: 12px; }`,
})
export class ExecutionDashboardPage {
  private api = inject(QualityApi);
  private ui = inject(UiService);
  tab = 'execution';
  d = signal<ExecutionDashboard | null>(null);
  throughputSeries: ChartSeries[] = [{ key: 'passedish', label: 'Completed (not failed)', color: STATUS.passed }, { key: 'failed', label: 'Failed', color: STATUS.failed }];
  throughput = computed(() => (this.d()?.throughput ?? []).map(t => ({ label: t.hour.slice(11, 16), values: { passedish: t.tests - t.failed, failed: t.failed } })));

  constructor() {
    this.load();
    inject(ChangesService).events.pipe(debounceTime(2000), takeUntilDestroyed()).subscribe(e => { if (['runChanged', 'agentsChanged'].includes(e.topic)) this.load(); });
  }

  load() { this.api.executionDashboard().subscribe({ next: d => this.d.set(d), error: e => this.ui.error(e) }); }
  queued(d: ExecutionDashboard) { return d.queue.reduce((s, q) => s + q.queued, 0); }
  secs(s: number) { return s >= 90 ? `${(s / 60).toFixed(1)} min` : `${Math.round(s)} s`; }
}
