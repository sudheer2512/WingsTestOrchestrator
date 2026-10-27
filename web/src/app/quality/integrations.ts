import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { debounceTime, firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Product } from '../core/models';
import { BuildImpact, BuildSummary, IntegrationKey, QualityApi, Repository, Tracker, VcsType } from '../core/quality';
import { RunLauncher } from '../execution/start-run';
import { ChangesService, UiService } from '../core/ui.service';
import { HumanizePipe, StatusPill } from '../shared/display';
import { RunBar } from '../execution/run-bar';

/** Impact panel shared by the what-if analysis and a recorded build. */
@Component({
  selector: 'app-impact-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatIconModule, StatusPill, HumanizePipe, RunBar],
  template: `
    @if (impact(); as i) {
      @for (w of i.warnings; track w) { <p class="warn"><mat-icon class="inline">warning</mat-icon> {{ w }}</p> }
      @if (i.triggeredRun; as r) { <div class="panel"><h3>Triggered run</h3><a [routerLink]="['/runs', r.id]" class="mono">{{ r.numberDisplay }}</a> {{ r.name }}<app-run-bar [run]="r" /></div> }
      <div class="two">
        <div class="panel"><h3>Impacted features ({{ i.features.length }})</h3>
          <table class="data"><tbody>@for (f of i.features; track f.featureId) {
            <tr><td class="num">{{ f.sequenceNumber }}</td><td><a [routerLink]="['/features', f.featureId]">{{ f.name }}</a><div class="muted small">{{ f.evidence }}</div></td>
              <td><span class="pill" [attr.data-tone]="f.reason === 'Dependency' ? 'warn' : 'neutral'">{{ f.reason | humanize }}</span></td></tr>
          } @empty { <tr><td class="empty">No feature matched.</td></tr> }</tbody></table></div>
        <div class="panel"><div class="row"><h3>Affected tests ({{ i.tests.length }})</h3><span class="spacer"></span>
          @if (i.tests.length && canRun()) { <button mat-stroked-button (click)="runTests()"><mat-icon>play_arrow</mat-icon>Run affected tests</button> }</div>
          <table class="data"><tbody>@for (t of i.tests; track t.testCaseId) {
            <tr><td><a [routerLink]="['/test-cases', t.testCaseId]">{{ t.numberDisplay }}</a> {{ t.name }}</td><td>{{ t.priority }}</td><td>{{ t.isAutomated ? 'Automated' : 'Manual' }}</td>
              <td>@if (t.latestOutcome) { <app-status [value]="t.latestOutcome" /> }</td></tr>
          } @empty { <tr><td class="empty">No test cases cover these features.</td></tr> }</tbody></table>
          @if (i.suites.length) { <h3>Suggested suites</h3>@for (s of i.suites; track s.suiteId) { <div><a [routerLink]="['/suites', s.suiteId]">{{ s.numberDisplay }}</a> {{ s.name }}
            <span class="muted">{{ s.matching }} of {{ s.total }} tests affected</span></div> } }</div>
      </div>
      <div class="two">
        <div class="panel"><h3>Changed files ({{ i.changedFiles.length }})</h3><div class="files">@for (f of i.changedFiles; track f) { <div class="mono" [class.muted]="i.unmappedFiles.includes(f)">{{ f }}</div> }</div>
          @if (i.unmappedFiles.length) { <p class="muted small">Greyed files match no approved mapping. Declare and approve source mappings on the feature's technical tab.</p> }</div>
        <div class="panel"><h3>Commits ({{ i.commits.length }})</h3>@for (c of i.commits; track c.id) { <div class="commit"><span class="mono">{{ c.id.slice(0, 10) }}</span> {{ c.message }} <span class="muted">{{ c.author }}</span></div> }
          @empty { <p class="muted">None reported.</p> }</div>
      </div>
    }`,
  styles: `
    .two { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(420px, 100%), 1fr)); gap: 12px; margin-top: 12px; } .two .panel + .panel { margin-top: 0; }
    .warn { color: var(--warn); margin: 4px 0; } .small { font-size: 11px; } .files { max-height: 260px; overflow: auto; font-size: 12px; } .commit { margin-bottom: 4px; } h3 { margin-top: 8px; }
  `,
})
export class ImpactView {
  impact = input<BuildImpact | null>(null);
  private launcher = inject(RunLauncher);
  private auth = inject(AuthService);
  canRun = () => this.auth.can('tests.execute');
  runTests() {
    const i = this.impact()!;
    const what = i.build ? `build ${i.build.buildNumber}` : 'the analysed changes';
    this.launcher.launch({ title: `Run tests affected by ${what}`, description: `${i.tests.length} test case(s) linked to ${i.features.length} impacted feature(s).`,
      scope: { name: `Tests affected by ${what}`, testCaseIds: i.tests.map(t => t.testCaseId), buildVersion: i.build?.buildNumber, includeManual: false } });
  }
}

/** Change impact: builds reported by CI and a what-if analysis for a list of changed files or commits. */
@Component({
  selector: 'app-impact',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, DatePipe, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, MatTooltipModule, StatusPill, ImpactView],
  template: `
    <div class="layout">
      <aside class="list">
        <div class="row"><h2>Builds</h2><span class="spacer"></span><a mat-icon-button routerLink="/impact" matTooltip="What-if analysis"><mat-icon>science</mat-icon></a></div>
        <mat-form-field class="full"><mat-label>Product</mat-label><mat-select [(ngModel)]="productId" (ngModelChange)="loadBuilds()"><mat-option [value]="undefined">All</mat-option>
          @for (p of products(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }</mat-select></mat-form-field>
        @for (b of builds(); track b.id) {
          <a class="item" [class.on]="id() === b.id" [routerLink]="['/impact/builds', b.id]"><b class="mono">{{ b.buildNumber }}</b>
            @if (b.gateStatus) { <app-status [value]="b.gateStatus === 'Ready' ? 'Passed' : b.gateStatus === 'Blocked' ? 'Failed' : 'Warning'" [label]="b.gateStatus" /> }
            <span class="sub">{{ b.productName }}{{ b.releaseLabel ? ' ' + b.releaseLabel : '' }} · {{ b.impactedFeatureCount }} feature(s) · {{ b.createdAt | date: 'short' }}</span></a>
        } @empty { <p class="muted small">No builds yet. CI posts them to /api/v1/ci/builds with an integration key.</p> }
      </aside>
      <section class="page">
        @if (id()) {
          @if (build(); as b) {
            <div class="page-head"><h1>Build {{ b.build!.buildNumber }}</h1><span class="muted">{{ b.build!.productName }} · {{ b.build!.branch }} · {{ b.build!.repositoryName }}</span>
              <span class="spacer"></span>@if (b.build!.buildUrl) { <a mat-stroked-button [href]="b.build!.buildUrl" target="_blank" rel="noopener">CI build</a> }
              @if (b.build!.releaseId) { <a mat-stroked-button [routerLink]="['/releases', b.build!.releaseId]">Release {{ b.build!.releaseLabel }}</a> }</div>
            <app-impact-view [impact]="b" />
          }
        } @else {
          <div class="page-head"><h1>Change impact analysis</h1></div>
          <p class="muted">Paste changed files (and optionally commit messages) to see which features are impacted, through approved source mappings, commit references
            (“feature 27”, “[F27]”, “AUTO-27”) and feature dependencies, and which tests to run.</p>
          <div class="panel">
            <div class="grid-3">
              <mat-form-field><mat-label>Product</mat-label><mat-select [(ngModel)]="report.product">@for (p of products(); track p.id) { <mat-option [value]="p.code">{{ p.name }}</mat-option> }</mat-select></mat-form-field>
              <mat-form-field><mat-label>Version control</mat-label><mat-select [(ngModel)]="report.vcs"><mat-option value="git">Git</mat-option><mat-option value="tfvc">TFVC</mat-option></mat-select></mat-form-field>
              <mat-form-field><mat-label>Repository</mat-label><mat-select [(ngModel)]="report.repository"><mat-option [value]="undefined">–</mat-option>
                @for (r of repos(); track r.id) { <mat-option [value]="r.name">{{ r.name }}</mat-option> }</mat-select></mat-form-field>
            </div>
            <mat-form-field class="full"><mat-label>Changed files, one per line</mat-label><textarea matInput rows="6" [(ngModel)]="files" placeholder="src/Sales/Invoicing/InvoiceCalculator.cs"></textarea></mat-form-field>
            <mat-form-field class="full"><mat-label>Commit messages, one per line (optional)</mat-label><textarea matInput rows="3" [(ngModel)]="messages"></textarea></mat-form-field>
            <mat-checkbox [(ngModel)]="report.includeDependents">Include features that depend on changed features</mat-checkbox>
            <div><button mat-flat-button [disabled]="!report.product" (click)="analyze()"><mat-icon>travel_explore</mat-icon>Analyse</button></div>
          </div>
          <app-impact-view [impact]="whatIf()" />
        }
      </section>
    </div>`,
  styles: `
    .layout { display: grid; grid-template-columns: 300px 1fr; height: 100%; }
    @media (max-width: 900px) { .layout { grid-template-columns: 1fr; height: auto; } .list { max-height: 220px; border-right: 0; border-bottom: 1px solid var(--border); } }
    .list { background: var(--surface); border-right: 1px solid var(--border); padding: 8px; overflow: auto; }
    .item { display: grid; grid-template-columns: 1fr auto; gap: 2px 8px; padding: 6px 8px; border-radius: 4px; cursor: pointer; text-decoration: none; color: inherit; }
    .item .sub { grid-column: 1 / -1; font-size: 12px; color: var(--muted); } .item:hover { background: var(--hover); } .item.on { background: var(--selected); } .small { font-size: 11px; }
  `,
})
export class Impact {
  private q = inject(QualityApi);
  private api = inject(ApiService);
  private ui = inject(UiService);
  id = input<string>();
  products = signal<Product[]>([]);
  repos = signal<Repository[]>([]);
  builds = signal<BuildSummary[]>([]);
  build = signal<BuildImpact | null>(null);
  whatIf = signal<BuildImpact | null>(null);
  productId?: string;
  files = '';
  messages = '';
  report = { product: '', vcs: 'git', repository: undefined as string | undefined, includeDependents: true };

  constructor() {
    this.api.products().subscribe(p => { this.products.set(p); this.report.product ||= p[0]?.code ?? ''; });
    this.q.repositories().subscribe(r => this.repos.set(r));
    this.loadBuilds();
    effect(() => { const id = this.id(); this.build.set(null); if (id) this.q.build(id).subscribe({ next: b => this.build.set(b), error: e => this.ui.error(e) }); });
    inject(ChangesService).events.pipe(debounceTime(1000), takeUntilDestroyed()).subscribe(e => { if (e.topic === 'buildsChanged') this.loadBuilds(); });
  }

  loadBuilds() { this.q.builds(this.productId).subscribe({ next: b => this.builds.set(b), error: e => this.ui.error(e) }); }

  async analyze() {
    const lines = (s: string) => s.split('\n').map(x => x.trim()).filter(Boolean);
    try {
      this.whatIf.set(await firstValueFrom(this.q.analyze({ ...this.report, buildNumber: 'what-if', changedFiles: lines(this.files),
        commits: lines(this.messages).map((m, i) => ({ id: `msg-${i + 1}`, message: m })), trigger: 'none' })));
    } catch (e) { this.ui.error(e); }
  }
}

/** Integration settings: source repositories, CI integration keys and issue trackers. */
@Component({
  selector: 'app-integrations',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, MatTooltipModule],
  template: `
    <div class="page">
      <div class="page-head"><h1>Integrations</h1></div>
      <div class="panel">
        <h3>Source repositories</h3>
        <table class="data"><thead><tr><th>Name</th><th>Type</th><th>URL / server path</th><th>Product</th><th></th></tr></thead><tbody>
          @for (r of repos(); track r.id) { <tr><td>{{ r.name }}</td><td>{{ r.type }}</td><td class="mono">{{ r.url }}</td><td>{{ productName(r.productId) }}</td>
            <td><button mat-icon-button (click)="deleteRepo(r)"><mat-icon>delete</mat-icon></button></td></tr> }
          @empty { <tr><td colspan="5" class="empty">No repositories.</td></tr> }</tbody></table>
        <div class="wrap-row">
          <mat-form-field><mat-label>Name</mat-label><input matInput [(ngModel)]="repo.name" placeholder="WingsAuto" /></mat-form-field>
          <mat-form-field><mat-label>Type</mat-label><mat-select [(ngModel)]="repo.type"><mat-option value="Git">Git</mat-option><mat-option value="Tfvc">TFVC</mat-option></mat-select></mat-form-field>
          <mat-form-field class="wide"><mat-label>{{ repo.type === 'Tfvc' ? 'Server path root ($/Project/Branch)' : 'Clone URL' }}</mat-label><input matInput [(ngModel)]="repo.url" /></mat-form-field>
          <mat-form-field><mat-label>Product</mat-label><mat-select [(ngModel)]="repo.productId"><mat-option [value]="undefined">–</mat-option>
            @for (p of products(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }</mat-select></mat-form-field>
          <button mat-stroked-button [disabled]="!repo.name" (click)="saveRepo()">Add</button>
        </div>
      </div>

      <div class="panel">
        <h3>CI/CD integration keys</h3>
        <p class="muted small">CI posts builds to <span class="mono">POST /api/v1/ci/builds</span> and checks release gates with
          <span class="mono">GET /api/v1/ci/gates?product=CODE&amp;release=LABEL&amp;enforce=true</span> (409 when blocked), sending the key in <span class="mono">X-Wings-Integration-Key</span>.</p>
        @if (newSecret()) { <div class="secret">Copy this key now; it is not shown again: <span class="mono">{{ newSecret() }}</span></div> }
        <table class="data"><thead><tr><th>Name</th><th>Prefix</th><th>Enabled</th><th>Last used</th><th>Created</th><th></th></tr></thead><tbody>
          @for (k of keys(); track k.id) { <tr><td>{{ k.name }}</td><td class="mono">{{ k.keyPrefix }}…</td>
            <td><mat-checkbox [checked]="k.isEnabled" (change)="toggleKey(k)" /></td><td>{{ k.lastUsedAt | date: 'short' }}</td><td>{{ k.createdBy }}, {{ k.createdAt | date: 'shortDate' }}</td>
            <td><button mat-icon-button (click)="deleteKey(k)"><mat-icon>delete</mat-icon></button></td></tr> }
          @empty { <tr><td colspan="6" class="empty">No keys.</td></tr> }</tbody></table>
        <div class="wrap-row"><mat-form-field><mat-label>Key name</mat-label><input matInput [(ngModel)]="keyName" placeholder="Azure Pipelines" /></mat-form-field>
          <button mat-stroked-button [disabled]="!keyName" (click)="createKey()">Create key</button></div>
      </div>

      <div class="panel">
        <h3>Issue trackers</h3>
        <table class="data"><thead><tr><th>Name</th><th>Kind</th><th>Target</th><th>Base URL</th><th>Token</th><th></th></tr></thead><tbody>
          @for (t of trackers(); track t.id) { <tr><td>{{ t.name }}</td><td>{{ t.kind }}</td><td class="mono">{{ t.target }}</td><td>{{ t.baseUrl }}</td><td>{{ t.hasToken ? 'stored' : '–' }}</td>
            <td><button mat-icon-button (click)="deleteTracker(t)"><mat-icon>delete</mat-icon></button></td></tr> }
          @empty { <tr><td colspan="6" class="empty">No issue trackers. Defects stay in the built-in repository.</td></tr> }</tbody></table>
        <div class="wrap-row">
          <mat-form-field><mat-label>Name</mat-label><input matInput [(ngModel)]="tracker.name" /></mat-form-field>
          <mat-form-field><mat-label>Kind</mat-label><mat-select [(ngModel)]="tracker.kind">@for (k of ['GitHub', 'Jira', 'AzureDevOps', 'Webhook']; track k) { <mat-option [value]="k">{{ k }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field><mat-label>{{ tracker.kind === 'GitHub' ? 'owner/repo' : tracker.kind === 'Jira' ? 'Project key' : tracker.kind === 'AzureDevOps' ? 'Project' : 'Target (optional)' }}</mat-label><input matInput [(ngModel)]="tracker.target" /></mat-form-field>
          <mat-form-field class="wide"><mat-label>Base URL{{ tracker.kind === 'GitHub' ? ' (Enterprise only)' : '' }}</mat-label><input matInput [(ngModel)]="tracker.baseUrl"
            [placeholder]="tracker.kind === 'Jira' ? 'https://your-site.atlassian.net' : tracker.kind === 'AzureDevOps' ? 'https://dev.azure.com/your-org' : 'https://…'" /></mat-form-field>
          @if (tracker.kind === 'Jira') { <mat-form-field><mat-label>User (email)</mat-label><input matInput [(ngModel)]="tracker.username" /></mat-form-field> }
          <mat-form-field><mat-label>Token</mat-label><input matInput type="password" [(ngModel)]="tracker.token" autocomplete="new-password" /></mat-form-field>
          <button mat-stroked-button [disabled]="!tracker.name" (click)="saveTracker()">Add</button>
        </div>
        <p class="muted small">Tokens are encrypted at rest and never returned. Use a least-privilege token that can only create issues.</p>
      </div>
    </div>`,
  styles: `.wrap-row mat-form-field { width: 180px; } .wrap-row .wide { width: 300px; } .small { font-size: 12px; }
    .secret { background: var(--warn-bg); padding: 8px; border-radius: 6px; margin-bottom: 8px; word-break: break-all; }`,
})
export class Integrations {
  private q = inject(QualityApi);
  private api = inject(ApiService);
  private ui = inject(UiService);
  products = signal<Product[]>([]);
  repos = signal<Repository[]>([]);
  keys = signal<IntegrationKey[]>([]);
  trackers = signal<Tracker[]>([]);
  newSecret = signal('');
  repo: { name: string; type: VcsType; url?: string; productId?: string } = { name: '', type: 'Git' };
  keyName = '';
  tracker = { name: '', kind: 'GitHub', target: '', baseUrl: '', username: '', token: '' };

  constructor() {
    this.api.products().subscribe(p => this.products.set(p));
    this.load();
  }

  load() {
    const err = (e: unknown) => this.ui.error(e);
    this.q.repositories().subscribe({ next: r => this.repos.set(r), error: err });
    this.q.keys().subscribe({ next: k => this.keys.set(k), error: err });
    this.q.trackers().subscribe({ next: t => this.trackers.set(t), error: err });
  }

  productName(id?: string) { return this.products().find(p => p.id === id)?.name ?? ''; }
  private async act(work: Promise<unknown>, ok: string) { try { await work; this.ui.ok(ok); this.load(); return true; } catch (e) { this.ui.error(e); return false; } }

  async saveRepo() { if (await this.act(firstValueFrom(this.q.saveRepository(null, this.repo)), 'Repository added')) this.repo = { name: '', type: 'Git' }; }
  async deleteRepo(r: Repository) { if (await this.ui.confirm({ title: 'Delete repository', message: r.name, danger: true, confirmText: 'Delete' })) this.act(firstValueFrom(this.q.deleteRepository(r.id)), 'Deleted'); }
  async createKey() {
    try { const r = await firstValueFrom(this.q.createKey(this.keyName)); this.newSecret.set(r.secret); this.keyName = ''; this.load(); } catch (e) { this.ui.error(e); }
  }
  toggleKey(k: IntegrationKey) { this.act(firstValueFrom(this.q.setKeyEnabled(k.id, !k.isEnabled)), k.isEnabled ? 'Key disabled' : 'Key enabled'); }
  async deleteKey(k: IntegrationKey) { if (await this.ui.confirm({ title: 'Delete key', message: `CI using ${k.name} stops working immediately.`, danger: true, confirmText: 'Delete' })) this.act(firstValueFrom(this.q.deleteKey(k.id)), 'Deleted'); }
  async saveTracker() {
    const t = this.tracker;
    if (await this.act(firstValueFrom(this.q.saveTracker(null, { name: t.name, kind: t.kind, target: t.target, baseUrl: t.baseUrl || undefined, username: t.username || undefined, token: t.token || null, isEnabled: true })), 'Tracker added'))
      this.tracker = { name: '', kind: 'GitHub', target: '', baseUrl: '', username: '', token: '' };
  }
  async deleteTracker(t: Tracker) { if (await this.ui.confirm({ title: 'Delete tracker', message: t.name, danger: true, confirmText: 'Delete' })) this.act(firstValueFrom(this.q.deleteTracker(t.id)), 'Deleted'); }
}
