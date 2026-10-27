import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { debounceTime, firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { EnvComparisonRow, EnvironmentKind, TestCaseSummary, TestEnvironment } from '../core/models';
import { ChangesService, UiService } from '../core/ui.service';
import { TestCasePickerDialog } from '../shared/dialogs';
import { StatusPill } from '../shared/display';

interface VarRow { name: string; value: string; isSecret: boolean; hasValue: boolean; description: string; changed: boolean; }
interface Draft {
  id: string | null; code: string; name: string; kind: EnvironmentKind; description: string; isProduction: boolean; baseUrl: string; applicationBuildVersion: string;
  databaseReference: string; tenantCompany: string; authenticationNotes: string; dependencies: string; agentPools: string; healthCheckUrl: string; inMaintenance: boolean;
  resetProcedure: string; allowed: { id: string; label: string }[]; vars: VarRow[]; version?: number;
}

const KINDS: EnvironmentKind[] = ['Development', 'QA', 'Staging', 'Preproduction', 'Automation', 'Production'];

/** Environment inventory: endpoints, build, database, agent pools, variables and encrypted secrets, health, compare and clone. */
@Component({
  selector: 'app-environments',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, MatTooltipModule, StatusPill],
  template: `
    <div class="layout">
      <aside class="list">
        <div class="row head"><h2>Environments</h2><span class="spacer"></span>
          @if (canEdit()) { <button mat-icon-button (click)="newDraft()" matTooltip="New environment"><mat-icon>add</mat-icon></button> }</div>
        @for (e of envs(); track e.id) {
          <a class="item" [class.on]="draft()?.id === e.id" (click)="open(e)">
            <span class="dot" [attr.data-h]="e.inMaintenance ? 'maint' : e.health" [matTooltip]="e.inMaintenance ? 'Maintenance' : e.health + (e.healthMessage ? ': ' + e.healthMessage : '')"></span>
            <span class="name">{{ e.name }} <span class="muted mono">{{ e.code }}</span></span>
            @if (e.isProduction) { <span class="pill" data-tone="bad">PROD</span> } @else { <span class="pill">{{ e.kind }}</span> }
            <span class="muted small sub">{{ e.applicationBuildVersion ?? 'build ?' }} · {{ e.activeRuns }} active run(s)</span>
          </a>
        } @empty { <p class="muted">No environments yet.</p> }
        <div class="compare panel">
          <h3>Compare</h3>
          <select class="cell" [(ngModel)]="left">@for (e of envs(); track e.id) { <option [value]="e.id">{{ e.name }}</option> }</select>
          <select class="cell" [(ngModel)]="right">@for (e of envs(); track e.id) { <option [value]="e.id">{{ e.name }}</option> }</select>
          <button mat-stroked-button [disabled]="!left || !right || left === right" (click)="compare()">Compare</button>
        </div>
      </aside>

      <section class="page">
        @if (comparison(); as rows) {
          <div class="panel">
            <div class="row"><h2>Comparison</h2><span class="spacer"></span><button mat-button (click)="comparison.set(null)">Close</button></div>
            <table class="data">
              <thead><tr><th>Setting</th><th>{{ name(left) }}</th><th>{{ name(right) }}</th></tr></thead>
              <tbody>@for (r of rows; track r.item) { <tr [class.diff]="r.differs"><td class="mono">{{ r.item }}</td><td>{{ r.left ?? '—' }}</td><td>{{ r.right ?? '—' }}</td></tr> }</tbody>
            </table>
            <p class="muted small">Secrets are compared without being revealed.</p>
          </div>
        }
        @if (draft(); as d) {
          <div class="page-head">
            <h1>{{ d.id ? d.name : 'New environment' }}</h1>
            @if (current(); as e) { <app-status [value]="e.inMaintenance ? 'Warning' : e.health" [label]="e.inMaintenance ? 'Maintenance' : e.health" />
              <span class="muted small">{{ e.healthMessage }} {{ e.lastHealthCheckAt ? '· checked ' + (e.lastHealthCheckAt | date: 'short') : '' }}</span> }
            <span class="spacer"></span>
            @if (d.id) {
              <button mat-stroked-button (click)="check()" [disabled]="!d.healthCheckUrl"><mat-icon>monitor_heart</mat-icon>Check health</button>
              @if (canEdit()) { <button mat-stroked-button (click)="clone()"><mat-icon>content_copy</mat-icon>Clone</button>
                <button mat-icon-button (click)="remove()"><mat-icon>delete</mat-icon></button> }
            }
            @if (canEdit()) { <button mat-flat-button (click)="save()"><mat-icon>save</mat-icon>Save</button> }
          </div>
          <div class="panel grid-3">
            <mat-form-field><mat-label>Code</mat-label><input matInput [(ngModel)]="d.code" [readonly]="!!d.id" /></mat-form-field>
            <mat-form-field><mat-label>Name</mat-label><input matInput [(ngModel)]="d.name" /></mat-form-field>
            <mat-form-field><mat-label>Kind</mat-label><mat-select [(ngModel)]="d.kind" (ngModelChange)="d.isProduction = d.isProduction || $event === 'Production'">
              @for (k of kinds; track k) { <mat-option [value]="k">{{ k }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field class="span"><mat-label>Base URL</mat-label><input matInput [(ngModel)]="d.baseUrl" placeholder="https://qa.wings.local/portal/" /></mat-form-field>
            <mat-form-field><mat-label>Application build</mat-label><input matInput [(ngModel)]="d.applicationBuildVersion" /></mat-form-field>
            <mat-form-field><mat-label>Database reference</mat-label><input matInput [(ngModel)]="d.databaseReference" placeholder="SQLQA01 / WingsAuto_QA" /></mat-form-field>
            <mat-form-field><mat-label>Test tenant / company</mat-label><input matInput [(ngModel)]="d.tenantCompany" /></mat-form-field>
            <mat-form-field><mat-label>Agent pools (comma separated)</mat-label><input matInput [(ngModel)]="d.agentPools" placeholder="qa, windows-desktop" /></mat-form-field>
            <mat-form-field class="span"><mat-label>Health check URL</mat-label><input matInput [(ngModel)]="d.healthCheckUrl" placeholder="https://qa.wings.local/health" /></mat-form-field>
            <div class="row"><mat-checkbox [(ngModel)]="d.inMaintenance">In maintenance (no new runs or dispatch)</mat-checkbox></div>
            <mat-form-field class="full span3"><mat-label>Description</mat-label><textarea matInput rows="2" [(ngModel)]="d.description"></textarea></mat-form-field>
            <mat-form-field class="full span3"><mat-label>Authentication configuration (notes)</mat-label><textarea matInput rows="2" [(ngModel)]="d.authenticationNotes"></textarea></mat-form-field>
            <mat-form-field class="full span3"><mat-label>Required dependencies</mat-label><textarea matInput rows="2" [(ngModel)]="d.dependencies"></textarea></mat-form-field>
            <mat-form-field class="full span3"><mat-label>Reset procedure</mat-label><textarea matInput rows="2" [(ngModel)]="d.resetProcedure"></textarea></mat-form-field>
          </div>

          <div class="panel" [class.prod]="d.isProduction">
            <div class="row"><mat-checkbox [(ngModel)]="d.isProduction" [disabled]="d.kind === 'Production'">Production environment</mat-checkbox></div>
            @if (d.isProduction) {
              <p class="small">Runs here need the <b>environments.production</b> permission. Test cases marked <b>destructive</b> are skipped unless listed below,
                and automated tests not linked to a test case are always skipped.</p>
              <div class="wrap-row">
                @for (a of d.allowed; track a.id) { <span class="tag">{{ a.label }} @if (canEdit()) { <a (click)="d.allowed.splice($index, 1)">×</a> }</span> }
                @if (canEdit()) { <button mat-stroked-button (click)="authorise()"><mat-icon>verified_user</mat-icon>Authorise destructive test cases</button> }
              </div>
            }
          </div>

          <div class="panel">
            <div class="row"><h3>Variables</h3><span class="spacer"></span>@if (canEdit()) { <button mat-stroked-button (click)="addVar()"><mat-icon>add</mat-icon>Add variable</button> }</div>
            <p class="muted small">Given to tests as environment variables (with WINGS_BASE_URL, WINGS_ENVIRONMENT and WINGS_APP_BUILD). Secret values are encrypted at rest,
              never shown again, decrypted only when a job is dispatched, and masked by agents in logs and text evidence.</p>
            <table class="data">
              <thead><tr><th>Name</th><th>Value</th><th>Secret</th><th>Description</th><th></th></tr></thead>
              <tbody>
                @for (v of d.vars; track $index) {
                  <tr>
                    <td><input class="cell mono" [(ngModel)]="v.name" placeholder="UPPER_SNAKE_CASE" /></td>
                    <td>@if (v.isSecret) {
                          <input class="cell" type="password" autocomplete="new-password" [(ngModel)]="v.value" (ngModelChange)="v.changed = true"
                                 [placeholder]="v.hasValue ? '•••••• (stored — type to replace)' : 'enter secret value'" />
                        } @else { <input class="cell" [(ngModel)]="v.value" /> }</td>
                    <td><mat-checkbox [(ngModel)]="v.isSecret" (ngModelChange)="v.changed = v.changed || !$event" /></td>
                    <td><input class="cell" [(ngModel)]="v.description" /></td>
                    <td>@if (canEdit()) { <button mat-icon-button (click)="d.vars.splice($index, 1)"><mat-icon>close</mat-icon></button> }</td>
                  </tr>
                } @empty { <tr><td colspan="5" class="empty">No variables.</td></tr> }
              </tbody>
            </table>
          </div>
        } @else if (!comparison()) { <div class="empty">Select an environment or create one.</div> }
      </section>
    </div>`,
  styles: `
    .layout { display: grid; grid-template-columns: 320px 1fr; height: 100%; }
    .list { background: var(--surface); border-right: 1px solid var(--border); padding: 8px; overflow: auto; }
    .item { display: grid; grid-template-columns: 12px 1fr auto; gap: 2px 8px; align-items: center; padding: 6px 8px; border-radius: 4px; cursor: pointer; }
    .item:hover { background: var(--hover); } .item.on { background: var(--selected); } .item .sub { grid-column: 2 / 4; }
    .dot { width: 10px; height: 10px; border-radius: 50%; background: #c9ced6; }
    .dot[data-h='Available'] { background: #2da44e; } .dot[data-h='Degraded'] { background: #d4a72c; } .dot[data-h='Unavailable'] { background: #cf222e; } .dot[data-h='maint'] { background: #8250df; }
    .small { font-size: 12px; } .span { grid-column: span 2; } .span3 { grid-column: 1 / -1; }
    .cell { font: inherit; padding: 5px 7px; border: 1px solid var(--border); border-radius: 4px; width: 100%; }
    .compare { display: flex; flex-direction: column; gap: 6px; margin-top: 16px; }
    .prod { border-color: var(--bad); background: var(--bad-bg); }
    tr.diff td { background: var(--warn-bg); }
    .tag a { cursor: pointer; }
  `,
})
export class Environments {
  private api = inject(ApiService);
  private ui = inject(UiService);
  private dialog = inject(MatDialog);
  private auth = inject(AuthService);
  kinds = KINDS;
  envs = signal<TestEnvironment[]>([]);
  draft = signal<Draft | null>(null);
  current = signal<TestEnvironment | null>(null);
  comparison = signal<EnvComparisonRow[] | null>(null);
  left = '';
  right = '';
  canEdit = () => this.auth.can('environments.manage');

  constructor() {
    this.load();
    inject(ChangesService).events.pipe(debounceTime(500), takeUntilDestroyed()).subscribe(e => { if (e.topic === 'environmentsChanged') this.load(); });
  }

  load() { this.api.environments().subscribe({ next: e => { this.envs.set(e); this.current.set(e.find(x => x.id === this.draft()?.id) ?? null); }, error: e => this.ui.error(e) }); }

  newDraft() {
    this.current.set(null);
    this.draft.set({ id: null, code: '', name: '', kind: 'QA', description: '', isProduction: false, baseUrl: '', applicationBuildVersion: '', databaseReference: '',
      tenantCompany: '', authenticationNotes: '', dependencies: '', agentPools: '', healthCheckUrl: '', inMaintenance: false, resetProcedure: '', allowed: [], vars: [] });
  }

  open(e: TestEnvironment) {
    this.current.set(e);
    this.comparison.set(null);
    this.draft.set({
      id: e.id, code: e.code, name: e.name, kind: e.kind, description: e.description ?? '', isProduction: e.isProduction, baseUrl: e.baseUrl ?? '',
      applicationBuildVersion: e.applicationBuildVersion ?? '', databaseReference: e.databaseReference ?? '', tenantCompany: e.tenantCompany ?? '',
      authenticationNotes: e.authenticationNotes ?? '', dependencies: e.dependencies ?? '', agentPools: e.agentPools.join(', '), healthCheckUrl: e.healthCheckUrl ?? '',
      inMaintenance: e.inMaintenance, resetProcedure: e.resetProcedure ?? '', allowed: e.allowedDestructiveTestCaseIds.map(id => ({ id, label: id.slice(0, 8) + '…' })),
      vars: e.variables.map(v => ({ name: v.name, value: v.value ?? '', isSecret: v.isSecret, hasValue: v.hasValue, description: v.description ?? '', changed: false })),
      version: e.version,
    });
  }

  addVar() { this.draft()!.vars.push({ name: '', value: '', isSecret: false, hasValue: false, description: '', changed: true }); }

  async authorise() {
    const chosen = await firstValueFrom(this.dialog.open(TestCasePickerDialog, { width: '600px', data: { title: 'Destructive test cases allowed on this production environment' } }).afterClosed()) as TestCaseSummary[] | undefined;
    for (const c of chosen ?? []) if (!this.draft()!.allowed.some(a => a.id === c.id)) this.draft()!.allowed.push({ id: c.id, label: `${c.numberDisplay} ${c.name}` });
  }

  async save() {
    const d = this.draft()!;
    const body = {
      code: d.code, name: d.name, kind: d.kind, description: d.description || null, isProduction: d.isProduction, baseUrl: d.baseUrl || null,
      applicationBuildVersion: d.applicationBuildVersion || null, databaseReference: d.databaseReference || null, tenantCompany: d.tenantCompany || null,
      authenticationNotes: d.authenticationNotes || null, dependencies: d.dependencies || null, healthCheckUrl: d.healthCheckUrl || null, inMaintenance: d.inMaintenance,
      resetProcedure: d.resetProcedure || null, agentPools: d.agentPools.split(',').map(p => p.trim()).filter(Boolean), allowedDestructiveTestCaseIds: d.allowed.map(a => a.id),
      // An untouched stored secret is sent with a null value, which keeps it.
      variables: d.vars.map(v => ({ name: v.name.trim(), isSecret: v.isSecret, description: v.description || null, value: v.isSecret && !v.changed ? null : v.value })),
      version: d.version,
    };
    try { const e = await firstValueFrom(this.api.saveEnvironment(d.id, body)); this.ui.ok('Environment saved'); this.load(); this.open(e); }
    catch (err) { this.ui.error(err); }
  }

  async check() {
    try { const e = await firstValueFrom(this.api.checkEnvironment(this.draft()!.id!)); this.current.set(e); this.ui.ok(`${e.health}: ${e.healthMessage ?? ''}`); this.load(); }
    catch (err) { this.ui.error(err); }
  }

  async clone() {
    const code = await this.ui.prompt({ title: 'Clone environment', label: 'New code', value: this.draft()!.code + '2' });
    if (!code) return;
    const name = await this.ui.prompt({ title: 'Clone environment', label: 'New name', value: this.draft()!.name + ' (copy)' });
    if (!name) return;
    try { const e = await firstValueFrom(this.api.cloneEnvironment(this.draft()!.id!, code, name)); this.load(); this.open(e); this.ui.ok('Cloned, including variables and secrets'); }
    catch (err) { this.ui.error(err); }
  }

  async remove() {
    const d = this.draft()!;
    if (!await this.ui.confirm({ title: 'Delete environment', message: `Delete ${d.name}? Past runs keep their reference.`, danger: true, confirmText: 'Delete' })) return;
    try { await firstValueFrom(this.api.deleteEnvironment(d.id!)); this.draft.set(null); this.load(); } catch (err) { this.ui.error(err); }
  }

  async compare() {
    try { this.comparison.set(await firstValueFrom(this.api.compareEnvironments(this.left, this.right))); } catch (err) { this.ui.error(err); }
  }

  name(id: string) { return this.envs().find(e => e.id === id)?.name ?? ''; }
}
