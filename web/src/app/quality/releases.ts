import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { debounceTime, firstValueFrom } from 'rxjs';
import { ApiService, saveBlob } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { FeatureSearchHit, Product, SuiteSummary, TestEnvironment } from '../core/models';
import { ChangeKind, Gate, GateType, GateUpsert, QualityApi, ReleaseStatus, ReleaseSummary, ReleaseWorkspace, RiskLevel, gateTone, readinessTone, defectTone } from '../core/quality';
import { ChangesService, UiService } from '../core/ui.service';
import { HumanizePipe, StatusPill } from '../shared/display';
import { RunBar } from '../execution/run-bar';

const GATE_TYPES: { type: GateType; label: string; param: string }[] = [
  { type: 'SuitePassed', label: 'Suite passed', param: 'Suite' },
  { type: 'NoOpenDefects', label: 'No unresolved defects', param: 'Severity or worse' },
  { type: 'ChangedFeaturesTested', label: 'Changed features tested', param: 'Minimum change risk' },
  { type: 'MinAutomationCoverage', label: 'Minimum automation coverage', param: 'Percent' },
  { type: 'TaggedTestsPassed', label: 'Tagged tests passed', param: 'Tag' },
  { type: 'MinPassRate', label: 'Minimum pass rate', param: 'Percent' },
];

@Component({
  selector: 'app-gate-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule],
  template: `
    <h2 mat-dialog-title>{{ data.gate ? 'Edit' : 'New' }} quality gate</h2>
    <mat-dialog-content class="form">
      <mat-form-field><mat-label>Name</mat-label><input matInput [(ngModel)]="g.name" placeholder="Mandatory smoke tests passed" /></mat-form-field>
      <mat-form-field><mat-label>Type</mat-label><mat-select [(ngModel)]="g.type" (ngModelChange)="g.parameter = undefined">
        @for (t of types; track t.type) { <mat-option [value]="t.type">{{ t.label }}</mat-option> }</mat-select></mat-form-field>
      @switch (g.type) {
        @case ('SuitePassed') { <mat-form-field><mat-label>Suite</mat-label><mat-select [(ngModel)]="g.parameter">@for (s of data.suites; track s.id) { <mat-option [value]="s.id">{{ s.numberDisplay }} {{ s.name }}</mat-option> }</mat-select></mat-form-field> }
        @case ('NoOpenDefects') { <mat-form-field><mat-label>Severity or worse</mat-label><mat-select [(ngModel)]="g.parameter">@for (s of ['Critical', 'Major', 'Minor']; track s) { <mat-option [value]="s">{{ s }}</mat-option> }</mat-select></mat-form-field> }
        @case ('ChangedFeaturesTested') { <mat-form-field><mat-label>Minimum change risk</mat-label><mat-select [(ngModel)]="g.parameter">@for (s of ['Low', 'Medium', 'High', 'Critical']; track s) { <mat-option [value]="s">{{ s }}</mat-option> }</mat-select></mat-form-field> }
        @case ('TaggedTestsPassed') { <mat-form-field><mat-label>Tag</mat-label><input matInput [(ngModel)]="g.parameter" placeholder="accounting" /></mat-form-field> }
        @default { <mat-form-field><mat-label>Percent</mat-label><input matInput type="number" [(ngModel)]="g.parameter" /></mat-form-field> }
      }
      <mat-form-field><mat-label>Applies to</mat-label><mat-select [(ngModel)]="g.releaseId"><mat-option [value]="undefined">Every release of the product</mat-option>
        @if (data.releaseId) { <mat-option [value]="data.releaseId">This release only</mat-option> }</mat-select></mat-form-field>
      <mat-checkbox [(ngModel)]="g.isBlocking">Blocking (sign-off is refused while it fails, unless an exception is approved)</mat-checkbox>
      <mat-checkbox [(ngModel)]="g.isEnabled">Enabled</mat-checkbox>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Cancel</button><button mat-flat-button [disabled]="!g.name" (click)="ref.close(g)">Save</button></mat-dialog-actions>`,
  styles: `.form { display: flex; flex-direction: column; min-width: 440px; }`,
})
export class GateDialog {
  data = inject<{ gate?: Gate; productId: string; releaseId?: string; suites: SuiteSummary[] }>(MAT_DIALOG_DATA);
  ref = inject(MatDialogRef);
  types = GATE_TYPES;
  g: GateUpsert = this.data.gate
    ? { productId: this.data.productId, releaseId: this.data.gate.releaseId, name: this.data.gate.name, type: this.data.gate.type, parameter: this.data.gate.parameter,
        isBlocking: this.data.gate.isBlocking, order: this.data.gate.order, isEnabled: this.data.gate.isEnabled, version: this.data.gate.version }
    : { productId: this.data.productId, name: '', type: 'SuitePassed', isBlocking: true, order: 0, isEnabled: true };
}

/** Release list and the release-testing workspace: scope, required tests, quality gates, exceptions, runs and sign-off. */
@Component({
  selector: 'app-releases',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, DatePipe, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, MatTooltipModule, StatusPill, HumanizePipe, RunBar],
  template: `
    <div class="layout">
      <aside class="list">
        <div class="row"><h2>Releases</h2></div>
        <mat-form-field class="full"><mat-label>Product</mat-label><mat-select [(ngModel)]="productId" (ngModelChange)="loadList()"><mat-option [value]="undefined">All products</mat-option>
          @for (p of products(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }</mat-select></mat-form-field>
        @for (r of list(); track r.id) {
          <a class="item" [class.on]="id() === r.id" [routerLink]="['/releases', r.id]">
            <b>{{ r.versionLabel }}</b><span class="muted">{{ r.productName }}</span><app-status [value]="tone(r.readiness)" [label]="r.isReleased ? 'Released' : r.readiness" />
            <span class="sub">{{ r.status | humanize }} · {{ r.gatesPassed }}/{{ r.gatesTotal }} gates{{ r.releaseDate ? ' · ' + r.releaseDate : '' }}</span>
          </a>
        } @empty { <p class="muted">No releases. Add them on the product (Administration → Products).</p> }
      </aside>
      <section class="page">
        @if (w(); as w) {
          <div class="page-head">
            <h1>{{ w.release.productName }} {{ w.release.versionLabel }}</h1>
            <app-status [value]="tone(w.release.readiness)" [label]="w.release.readiness" /><span class="pill">{{ w.release.status | humanize }}</span>
            <span class="spacer"></span>
            <button mat-stroked-button (click)="report()"><mat-icon>picture_as_pdf</mat-icon>Readiness report</button>
            @if (auth.can('tests.execute')) { <button mat-stroked-button (click)="execute()"><mat-icon>play_arrow</mat-icon>Execute release scope</button> }
            @if (auth.can('releases.manage')) { <button mat-stroked-button (click)="requestSignOff()"><mat-icon>how_to_reg</mat-icon>Request sign-off</button> }
            @if (auth.can('releases.approve')) { <button mat-flat-button (click)="signOff()"><mat-icon>verified</mat-icon>Sign off</button> }
          </div>
          <p class="muted">Testing window {{ w.windowStart | date: 'mediumDate' }} – {{ w.windowEnd | date: 'mediumDate' }}. Results are the latest execution of each test case inside the window.</p>

          <div class="tiles">
            <div class="tile"><div class="v">{{ w.metrics.featuresAdded }} / {{ w.metrics.featuresModified }} / {{ w.metrics.featuresDeprecated }}</div><div class="l">Features added / modified / deprecated</div></div>
            <div class="tile"><div class="v">{{ w.metrics.testsCompleted }}/{{ w.metrics.testsRequired }}</div><div class="l">Required tests completed</div></div>
            <div class="tile"><div class="v">{{ w.metrics.testsPassed }}</div><div class="l">Passed</div><div class="s">{{ w.metrics.passRate == null ? 'no results yet' : w.metrics.passRate + '% pass rate' }}</div></div>
            <div class="tile" [class.bad]="w.metrics.failedCritical"><div class="v">{{ w.metrics.failedCritical }}</div><div class="l">Failed critical (P1) tests</div></div>
            <div class="tile" [class.bad]="w.metrics.openCriticalDefects"><div class="v">{{ w.metrics.openCriticalDefects }}</div><div class="l">Unresolved critical defects</div></div>
            <div class="tile"><div class="v">{{ w.metrics.regressionCompleted }}/{{ w.metrics.regressionTotal }}</div><div class="l">Regression completion</div></div>
          </div>

          <div class="panel">
            <div class="row"><h3>Quality gates</h3><span class="spacer"></span>@if (auth.can('releases.manage')) { <button mat-stroked-button (click)="editGate()"><mat-icon>add</mat-icon>Gate</button> }</div>
            <table class="data"><thead><tr><th>Gate</th><th>Status</th><th>Result</th><th>Exception</th><th></th></tr></thead><tbody>
              @for (g of w.gates; track g.gateId) {
                <tr><td [matTooltip]="g.definition"><b>{{ g.name }}</b>@if (!g.isBlocking) { <span class="muted"> (warning only)</span> }@if (g.isReleaseSpecific) { <span class="muted"> · this release</span> }</td>
                  <td><app-status [value]="gateTone(g.status)" [label]="g.status" /></td><td>{{ g.detail }}</td>
                  <td>@if (g.waiver; as x) { <span class="small">{{ x.approvedBy }}, {{ x.approvedAt | date: 'short' }}: “{{ x.justification }}”</span>
                    @if (auth.can('releases.approve')) { <button mat-button (click)="revoke(x.id)">Revoke</button> } }
                    @else if (g.status === 'Failed' || g.status === 'Warning') { @if (auth.can('releases.approve')) { <button mat-stroked-button (click)="waive(g.gateId, g.name)">Approve exception</button> } }</td>
                  <td>@if (auth.can('releases.manage')) { <button mat-icon-button (click)="editGateById(g.gateId)"><mat-icon>edit</mat-icon></button> }</td></tr>
              } @empty { <tr><td colspan="5" class="empty">No gates configured for this product.</td></tr> }
            </tbody></table>
          </div>

          @if (w.risks.length || w.knownRisks) {
            <div class="panel risks"><h3>Known risks</h3>@if (w.knownRisks) { <p class="pre">{{ w.knownRisks }}</p> }<ul>@for (r of w.risks; track r) { <li>{{ r }}</li> }</ul></div>
          }

          <div class="panel">
            <div class="row"><h3>Release scope</h3><span class="spacer"></span>
              @if (auth.can('releases.manage')) {
                <mat-form-field class="pick"><mat-label>Add feature</mat-label><input matInput [(ngModel)]="featureText" (keyup.enter)="searchFeatures()" placeholder="Search name or sequence, Enter" /></mat-form-field>
                @if (hits().length) { <mat-form-field class="pick"><mat-label>Feature</mat-label><mat-select [(ngModel)]="change.featureId">@for (h of hits(); track h.node.id) { <mat-option [value]="h.node.id">{{ h.node.sequenceDisplay }} {{ h.node.name }}</mat-option> }</mat-select></mat-form-field> }
                <mat-form-field class="small-field"><mat-label>Change</mat-label><mat-select [(ngModel)]="change.kind">@for (k of kinds; track k) { <mat-option [value]="k">{{ k }}</mat-option> }</mat-select></mat-form-field>
                <mat-form-field class="small-field"><mat-label>Risk</mat-label><mat-select [(ngModel)]="change.risk">@for (k of risks; track k) { <mat-option [value]="k">{{ k }}</mat-option> }</mat-select></mat-form-field>
                <button mat-stroked-button [disabled]="!change.featureId" (click)="addChange()">Add</button>
              }
            </div>
            <table class="data"><thead><tr><th>Seq</th><th>Feature</th><th>Change</th><th>Source</th><th>Risk</th><th class="n">Tests</th><th class="n">Executed</th><th class="n">Passed</th><th>Status</th><th></th></tr></thead><tbody>
              @for (f of w.features; track f.featureId + f.kind) {
                <tr><td class="num">{{ f.sequenceNumber }}</td><td><a [routerLink]="['/features', f.featureId]">{{ f.name }}</a>@if (f.notes) { <div class="muted small">{{ f.notes }}</div> }</td>
                  <td>{{ f.kind }}</td><td class="muted">{{ f.source }}</td><td>{{ f.risk }}</td><td class="n">{{ f.testCases }}</td><td class="n">{{ f.executed }}</td><td class="n">{{ f.passed }}</td>
                  <td><app-status [value]="f.status === 'NoTests' ? 'NONE' : f.status === 'NotRun' ? 'Pending' : f.status" [label]="f.status | humanize" /></td>
                  <td>@if (f.changeId && auth.can('releases.manage')) { <button mat-icon-button (click)="removeChange(f.changeId)"><mat-icon>close</mat-icon></button> }</td></tr>
              } @empty { <tr><td colspan="10" class="empty">No features in scope: features introduced in this release, deprecated in its window, declared here or changed by its builds appear automatically.</td></tr> }
            </tbody></table>
          </div>

          <div class="panel">
            <h3>Required tests ({{ w.requiredTests.length }})</h3>
            <table class="data"><thead><tr><th>Test case</th><th>Pri</th><th>Automated</th><th>Why required</th><th>Latest result</th><th>Run</th><th>Environment</th></tr></thead><tbody>
              @for (t of w.requiredTests; track t.testCaseId) {
                <tr><td><a [routerLink]="['/test-cases', t.testCaseId]">{{ t.numberDisplay }}</a> {{ t.name }}</td><td>{{ t.priority }}</td><td>{{ t.isAutomated ? 'Yes' : 'Manual' }}</td>
                  <td class="small">{{ t.reasons.join('; ') }}</td><td>@if (t.outcome) { <app-status [value]="t.outcome" /> } @else { <span class="muted">not executed</span> }</td>
                  <td>@if (t.runId) { <a [routerLink]="['/runs', t.runId]" class="mono">{{ t.runNumber }}</a> }</td><td>{{ t.environmentName }}</td></tr>
              } @empty { <tr><td colspan="7" class="empty">No required tests.</td></tr> }
            </tbody></table>
          </div>

          <div class="two">
            <div class="panel"><h3>Environment coverage</h3>
              <table class="data"><thead><tr><th>Environment</th><th class="n">Executed</th><th class="n">Passed</th><th class="n">Failed</th><th>Last run</th></tr></thead><tbody>
                @for (e of w.environments; track e.name) { <tr><td>{{ e.name }}</td><td class="n">{{ e.executed }}</td><td class="n">{{ e.passed }}</td><td class="n">{{ e.failed }}</td><td>{{ e.lastRunAt | date: 'short' }}</td></tr> }
                @empty { <tr><td colspan="5" class="empty">No required test executed yet.</td></tr> }</tbody></table></div>
            <div class="panel"><h3>Unresolved critical defects</h3>
              @for (d of w.openCriticalDefects; track d.id) { <div class="row"><a [routerLink]="['/defects', d.id]" class="mono">{{ d.numberDisplay }}</a> {{ d.title }}<span class="spacer"></span><app-status [value]="defTone(d.status)" [label]="d.status | humanize" /></div> }
              @empty { <p class="muted">None.</p> }</div>
          </div>

          <div class="two">
            <div class="panel"><h3>Runs</h3>
              @for (r of w.runs; track r.id) { <a [routerLink]="['/runs', r.id]" class="runrow"><span class="mono">{{ r.numberDisplay }}</span> {{ r.name }}</a><app-run-bar [run]="r" /> }
              @empty { <p class="muted">No runs for this release yet. Use “Execute release scope”, or target plans at this release.</p> }
              @if (w.plans.length) { <h3>Plans targeting this release</h3>@for (p of w.plans; track p.id) { <div><a [routerLink]="['/plans', p.id]">{{ p.numberDisplay }}</a> {{ p.name }}</div> } }</div>
            <div class="panel"><h3>Sign-off</h3>
              @for (a of w.approvals; track a.id) {
                <div class="appr"><app-status [value]="a.decision === 'Approved' ? 'Passed' : 'Failed'" [label]="a.decision" /> <b>{{ a.approvedBy }}</b> {{ a.approvedAt | date: 'medium' }}
                  <div class="pre">{{ a.comment }}</div><div class="row"><button mat-button (click)="approvalReport(a.id)"><mat-icon>picture_as_pdf</mat-icon>Signed report</button>
                  <span class="mono small muted" matTooltip="SHA-256 of the stored report">{{ a.reportSha256.slice(0, 16) }}…</span></div></div>
              } @empty { <p class="muted">Not signed off.</p> }
              @if (auth.can('releases.manage')) {
                <h3>Release details</h3>
                <div class="grid-2">
                  <mat-form-field><mat-label>Status</mat-label><mat-select [(ngModel)]="edit.status">@for (s of statuses; track s) { <mat-option [value]="s" [disabled]="s === 'SignedOff'">{{ s | humanize }}</mat-option> }</mat-select></mat-form-field>
                  <mat-form-field><mat-label>Release candidate build</mat-label><input matInput [(ngModel)]="edit.buildVersion" /></mat-form-field>
                  <mat-form-field><mat-label>Testing starts</mat-label><input matInput type="date" [(ngModel)]="edit.testingStartDate" /></mat-form-field>
                  <mat-form-field><mat-label>Release date</mat-label><input matInput type="date" [(ngModel)]="edit.releaseDate" /></mat-form-field>
                </div>
                <mat-form-field class="full"><mat-label>Known risks</mat-label><textarea matInput rows="3" [(ngModel)]="edit.knownRisks"></textarea></mat-form-field>
                <button mat-stroked-button (click)="saveRelease()">Save details</button>
              }
            </div>
          </div>
        } @else { <div class="empty">Select a release to see its readiness.</div> }
      </section>
    </div>`,
  styles: `
    .layout { display: grid; grid-template-columns: 300px 1fr; height: 100%; }
    @media (max-width: 900px) { .layout { grid-template-columns: 1fr; height: auto; } .list { max-height: 220px; border-right: 0; border-bottom: 1px solid var(--border); } }
    .list { background: var(--surface); border-right: 1px solid var(--border); padding: 8px; overflow: auto; }
    .item { display: grid; grid-template-columns: auto 1fr auto; gap: 2px 8px; padding: 6px 8px; border-radius: 4px; cursor: pointer; text-decoration: none; color: inherit; }
    .item .sub { grid-column: 1 / -1; font-size: 12px; color: var(--muted); } .item:hover { background: var(--hover); } .item.on { background: var(--selected); }
    .tiles { display: grid; grid-template-columns: repeat(auto-fill, minmax(160px, 1fr)); gap: 8px; margin-bottom: 12px; }
    .tile { background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 10px 12px; } .tile.bad { border-color: var(--bad); }
    .tile .v { font-size: 20px; font-weight: 600; } .tile .l, .tile .s { font-size: 12px; color: var(--muted); }
    .two { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(420px, 100%), 1fr)); gap: 12px; margin-top: 12px; } .two .panel + .panel { margin-top: 0; }
    .risks { border-color: var(--warn); } .risks ul { margin: 4px 0; padding-left: 18px; }
    .small { font-size: 12px; } td.n, th.n { text-align: right; } .pick { width: 220px; } .small-field { width: 120px; }
    .runrow { display: block; margin-top: 8px; text-decoration: none; } .appr { padding: 6px 0; border-bottom: 1px solid var(--border); }
  `,
})
export class Releases {
  private q = inject(QualityApi);
  private api = inject(ApiService);
  private ui = inject(UiService);
  private dialog = inject(MatDialog);
  auth = inject(AuthService);
  id = input<string>();
  products = signal<Product[]>([]);
  list = signal<ReleaseSummary[]>([]);
  w = signal<ReleaseWorkspace | null>(null);
  gates = signal<Gate[]>([]);
  hits = signal<FeatureSearchHit[]>([]);
  productId?: string;
  featureText = '';
  change: { featureId?: string; kind: ChangeKind; risk: RiskLevel } = { kind: 'Modified', risk: 'Medium' };
  kinds: ChangeKind[] = ['Added', 'Modified', 'Deprecated'];
  risks: RiskLevel[] = ['Low', 'Medium', 'High', 'Critical'];
  statuses: ReleaseStatus[] = ['Planning', 'Testing', 'SignedOff', 'Released'];
  edit: { status: ReleaseStatus; buildVersion?: string; testingStartDate?: string; releaseDate?: string; knownRisks?: string } = { status: 'Planning' };
  tone = readinessTone;
  gateTone = gateTone;
  defTone = defectTone;

  constructor() {
    this.api.products().subscribe(p => this.products.set(p));
    this.loadList();
    effect(() => { const id = this.id(); if (id) this.load(id); else this.w.set(null); });
    inject(ChangesService).events.pipe(debounceTime(1500), takeUntilDestroyed()).subscribe(e => {
      if (['runChanged', 'defectsChanged', 'releasesChanged', 'buildsChanged'].includes(e.topic) && this.id()) this.load(this.id()!, false);
    });
  }

  loadList() { this.q.releases(this.productId).subscribe({ next: l => this.list.set(l), error: e => this.ui.error(e) }); }

  load(id: string, resetEdit = true) {
    this.q.release(id).subscribe({ next: w => this.show(w, resetEdit), error: e => this.ui.error(e) });
  }

  private show(w: ReleaseWorkspace, resetEdit = true) {
    this.w.set(w);
    this.list.update(l => l.map(r => r.id === w.release.id ? w.release : r));
    if (resetEdit) this.edit = { status: w.release.status, buildVersion: w.release.buildVersion, testingStartDate: w.release.testingStartDate, releaseDate: w.release.releaseDate, knownRisks: w.knownRisks };
    this.q.gates(w.release.productId).subscribe(g => this.gates.set(g));
  }

  private async run<T>(work: Promise<T>, done?: string) {
    try { const r = await work; if (done) this.ui.ok(done); return r; } catch (e) { this.ui.error(e); return undefined; }
  }

  async saveRelease() {
    const w = this.w()!;
    const r = await this.run(firstValueFrom(this.q.updateRelease(w.release.id, { ...this.edit, testingStartDate: this.edit.testingStartDate || null, releaseDate: this.edit.releaseDate || null, version: w.release.version })), 'Saved');
    if (r) { this.show(r); this.loadList(); }
  }

  searchFeatures() {
    const w = this.w(); if (!w || !this.featureText.trim()) return;
    this.api.searchFeatures({ productId: w.release.productId, text: this.featureText.trim() }, 1, 30).subscribe(r => { this.hits.set(r.items); this.change.featureId = r.items[0]?.node.id; });
  }

  async addChange() {
    const w = this.w()!;
    const r = await this.run(firstValueFrom(this.q.addChange(w.release.id, { featureId: this.change.featureId!, kind: this.change.kind, risk: this.change.risk })));
    if (r) { this.show(r, false); this.hits.set([]); this.featureText = ''; }
  }

  async removeChange(changeId: string) {
    const r = await this.run(firstValueFrom(this.q.removeChange(this.w()!.release.id, changeId)));
    if (r) this.show(r, false);
  }

  editGateById(gateId: string) { this.editGate(this.gates().find(g => g.id === gateId)); }

  async editGate(gate?: Gate) {
    const w = this.w()!;
    const suites = await firstValueFrom(this.api.suites());
    const body = await firstValueFrom(this.dialog.open(GateDialog, { data: { gate, productId: w.release.productId, releaseId: w.release.id, suites } }).afterClosed());
    if (!body) return;
    if (await this.run(firstValueFrom(this.q.saveGate(gate?.id ?? null, body)), 'Gate saved')) this.load(w.release.id, false);
  }

  async waive(gateId: string, name: string) {
    const justification = await this.ui.prompt({ title: `Approve exception: ${name}`, label: 'Justification (recorded and audited)', multiline: true, required: true,
      hint: 'At least 20 characters: why the release may proceed and who accepted the risk.', confirmText: 'Approve exception' });
    if (!justification) return;
    const r = await this.run(firstValueFrom(this.q.waive(this.w()!.release.id, gateId, justification)), 'Exception approved');
    if (r) this.show(r, false);
  }

  async revoke(waiverId: string) {
    if (!await this.ui.confirm({ title: 'Revoke exception', message: 'The gate counts as failing again.', danger: true, confirmText: 'Revoke' })) return;
    const r = await this.run(firstValueFrom(this.q.revokeWaiver(this.w()!.release.id, waiverId)), 'Exception revoked');
    if (r) this.show(r, false);
  }

  async execute() {
    const w = this.w()!;
    const envs = await firstValueFrom(this.api.environments());
    const r = await firstValueFrom(this.dialog.open(ExecuteReleaseDialog, { data: { release: w.release, envs, count: w.requiredTests.length } }).afterClosed());
    if (!r) return;
    const run = await this.run(firstValueFrom(this.q.executeRelease(w.release.id, r)), 'Release validation started');
    if (run) this.load(w.release.id, false);
  }

  async requestSignOff() { await this.run(firstValueFrom(this.q.requestSignOff(this.w()!.release.id)), 'Sign-off requested from approvers'); }

  async signOff() {
    const w = this.w()!;
    const r = await firstValueFrom(this.dialog.open(SignOffDialog, { data: w }).afterClosed());
    if (!r) return;
    const res = await this.run(firstValueFrom(this.q.signOff(w.release.id, r.decision, r.comment)), r.decision === 'Approved' ? 'Release signed off' : 'Sign-off rejected');
    if (res) { this.show(res); this.loadList(); }
  }

  report() { const w = this.w()!; this.q.releaseReport(w.release.id).subscribe({ next: b => saveBlob(b, `${w.release.productName}-${w.release.versionLabel}-readiness.pdf`), error: e => this.ui.error(e) }); }
  approvalReport(id: string) { const w = this.w()!; this.q.approvalReport(w.release.id, id).subscribe(b => saveBlob(b, `${w.release.productName}-${w.release.versionLabel}-signoff.pdf`)); }
}

@Component({
  selector: 'app-execute-release-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule],
  template: `
    <h2 mat-dialog-title>Execute release scope</h2>
    <mat-dialog-content class="form">
      <p>Runs the {{ data.count }} required test(s) of {{ data.release.productName }} {{ data.release.versionLabel }}: tests of added and modified features, gate suites and tags, and plans targeting the release.</p>
      <mat-form-field><mat-label>Environment</mat-label><mat-select [(ngModel)]="r.environmentId"><mat-option [value]="undefined">None</mat-option>
        @for (e of data.envs; track e.id) { <mat-option [value]="e.id">{{ e.name }}{{ e.isProduction ? ' (PROD)' : '' }}</mat-option> }</mat-select></mat-form-field>
      <mat-form-field><mat-label>Build</mat-label><input matInput [(ngModel)]="r.buildVersion" [placeholder]="data.release.buildVersion ?? ''" /></mat-form-field>
      <mat-checkbox [(ngModel)]="r.includeManual">Include manual tests (pending for testers)</mat-checkbox>
      <mat-checkbox [(ngModel)]="r.onlyNotPassed">Only tests that have not passed yet</mat-checkbox>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Cancel</button><button mat-flat-button (click)="ref.close(r)">Execute</button></mat-dialog-actions>`,
  styles: `.form { display: flex; flex-direction: column; min-width: 440px; }`,
})
export class ExecuteReleaseDialog {
  data = inject<{ release: ReleaseSummary; envs: TestEnvironment[]; count: number }>(MAT_DIALOG_DATA);
  ref = inject(MatDialogRef);
  r: { environmentId?: string; buildVersion?: string; includeManual: boolean; onlyNotPassed: boolean } = { includeManual: true, onlyNotPassed: false };
}

@Component({
  selector: 'app-signoff-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <h2 mat-dialog-title>Release testing sign-off</h2>
    <mat-dialog-content class="form">
      <p>{{ data.release.productName }} {{ data.release.versionLabel }} is <b>{{ data.release.readiness }}</b>: {{ data.release.gatesPassed }}/{{ data.release.gatesTotal }} gates passed,
        {{ waived }} with an approved exception. A PDF report of the current state is generated and stored with its SHA-256.</p>
      @if (data.release.readiness === 'Blocked') { <p class="error">Blocking gates are failing; only a rejection can be recorded until they pass or exceptions are approved.</p> }
      <mat-form-field><mat-label>Decision</mat-label><mat-select [(ngModel)]="decision">
        <mat-option value="Approved" [disabled]="data.release.readiness === 'Blocked'">Approve</mat-option><mat-option value="Rejected">Reject</mat-option></mat-select></mat-form-field>
      <mat-form-field><mat-label>Comment</mat-label><textarea matInput rows="4" [(ngModel)]="comment" required></textarea></mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Cancel</button><button mat-flat-button [disabled]="!comment.trim()" (click)="ref.close({ decision, comment })">Record decision</button></mat-dialog-actions>`,
  styles: `.form { display: flex; flex-direction: column; min-width: 460px; }`,
})
export class SignOffDialog {
  data = inject<ReleaseWorkspace>(MAT_DIALOG_DATA);
  ref = inject(MatDialogRef);
  decision: 'Approved' | 'Rejected' = this.data.release.readiness === 'Blocked' ? 'Rejected' : 'Approved';
  comment = '';
  waived = this.data.gates.filter(g => g.status === 'Waived').length;
}
