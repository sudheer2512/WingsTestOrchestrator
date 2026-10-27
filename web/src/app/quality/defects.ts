import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { debounceTime, firstValueFrom } from 'rxjs';
import { ApiService, saveBlob } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Artifact, Product, ProductRelease, TestEnvironment } from '../core/models';
import {
  DEFECT_STATUSES, DefectDetail, DefectFilter, DefectSeverity, DefectStatus, DefectSummary, DefectUpsert, DirectoryUser, FailureCluster, FailureSuggestion,
  QualityApi, RecurringFailure, SEVERITIES, Tracker, defectTone,
} from '../core/quality';
import { ChangesService, UiService } from '../core/ui.service';
import { HumanizePipe, StatusPill } from '../shared/display';

type Tab = 'defects' | 'suggestions' | 'clusters' | 'recurring';

/** Defect repository with failure suggestions (no duplicates), clusters and recurring/flaky failures. */
@Component({
  selector: 'app-defects',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, DatePipe, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, MatTooltipModule, StatusPill, HumanizePipe],
  template: `
    <div class="page">
      <div class="page-head"><h1>Defects</h1><span class="spacer"></span>
        @if (auth.can('defects.manage')) { <a mat-flat-button routerLink="/defects/new"><mat-icon>bug_report</mat-icon>New defect</a> }</div>
      <nav class="tabs">
        @for (t of tabs; track t[0]) { <a [class.on]="tab() === t[0]" (click)="setTab(t[0])">{{ t[1] }}</a> }
      </nav>
      <div class="wrap-row filters">
        <mat-form-field><mat-label>Product</mat-label><mat-select [(ngModel)]="productId" (ngModelChange)="load()"><mat-option [value]="undefined">All products</mat-option>
          @for (p of products(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }</mat-select></mat-form-field>
        @if (tab() === 'defects') {
          <mat-form-field><mat-label>Status</mat-label><mat-select [(ngModel)]="filter.status" (ngModelChange)="load()"><mat-option [value]="undefined">Any</mat-option>
            @for (s of statuses; track s) { <mat-option [value]="s">{{ s | humanize }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field><mat-label>Severity</mat-label><mat-select [(ngModel)]="filter.severity" (ngModelChange)="load()"><mat-option [value]="undefined">Any</mat-option>
            @for (s of severities; track s) { <mat-option [value]="s">{{ s }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field><mat-label>Assigned to</mat-label><mat-select [(ngModel)]="filter.assignedTo" (ngModelChange)="load()"><mat-option [value]="undefined">Anyone</mat-option>
            @for (u of users(); track u.id) { <mat-option [value]="u.userName">{{ u.displayName }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field class="grow"><mat-label>Search title, DEF number or external key</mat-label><input matInput [(ngModel)]="filter.text" (keyup.enter)="load()" /></mat-form-field>
          <mat-checkbox [(ngModel)]="filter.openOnly" (ngModelChange)="load()">Unresolved only</mat-checkbox>
        } @else {
          <mat-form-field><mat-label>Period</mat-label><mat-select [(ngModel)]="days" (ngModelChange)="load()">
            @for (d of [1, 7, 30, 90]; track d) { <mat-option [value]="d">Last {{ d }} day(s)</mat-option> }</mat-select></mat-form-field>
        }
      </div>

      @switch (tab()) {
        @case ('defects') {
          <table class="data">
            <thead><tr><th>Defect</th><th>Title</th><th>Status</th><th>Severity</th><th>Pri</th><th>Feature</th><th>Assigned</th><th>Seen</th><th>External</th></tr></thead>
            <tbody>
              @for (d of defects(); track d.id) {
                <tr class="clickable" [routerLink]="['/defects', d.id]">
                  <td class="num">{{ d.numberDisplay }}</td><td>{{ d.title }}</td><td><app-status [value]="tone(d.status)" [label]="d.status | humanize" /></td>
                  <td><span class="sev" [attr.data-sev]="d.severity">{{ d.severity }}</span></td><td>{{ d.priority }}</td>
                  <td>@if (d.featureName) { <span class="mono muted">{{ d.featureSequence }}</span> {{ d.featureName }} }</td><td>{{ d.assignedTo }}</td>
                  <td [matTooltip]="'Last seen ' + (d.lastSeenAt | date: 'medium')">{{ d.occurrenceCount }}×</td><td>{{ d.externalKey }}</td>
                </tr>
              } @empty { <tr><td colspan="9" class="empty">No defects match.</td></tr> }
            </tbody>
          </table>
          <p class="muted">{{ total() }} defect(s)</p>
        }
        @case ('suggestions') {
          <p class="muted">Failed tests grouped by failure signature. A failure whose signature already has a defect is linked to it instead of creating a duplicate.</p>
          <table class="data">
            <thead><tr><th>Failed test</th><th>Run</th><th>Error</th><th>Defect</th><th></th></tr></thead>
            <tbody>
              @for (s of suggestions(); track s.testExecutionId) {
                <tr><td>{{ s.displayName }}<div class="muted small">{{ s.testCaseNumber }} {{ s.featureName }}</div></td>
                  <td><a [routerLink]="['/runs', s.runId]" class="mono">{{ s.runNumber }}</a><div class="muted small">{{ s.completedAt | date: 'short' }}</div></td>
                  <td class="err">{{ s.errorMessage }}</td>
                  <td>@if (s.existingDefectId) { <a [routerLink]="['/defects', s.existingDefectId]">{{ s.existingDefectNumber }}</a> <app-status [value]="tone(s.existingDefectStatus!)" [label]="s.existingDefectStatus | humanize" /> }
                    @else if (s.possibleDuplicateId) { <span class="muted">possibly <a [routerLink]="['/defects', s.possibleDuplicateId]">{{ s.possibleDuplicateNumber }}</a></span> }</td>
                  <td>@if (auth.can('defects.manage')) { <button mat-stroked-button (click)="raise(s)">{{ s.existingDefectId ? 'Link occurrence' : 'Raise defect' }}</button> }</td></tr>
              } @empty { <tr><td colspan="5" class="empty">No failed tests in the period.</td></tr> }
            </tbody>
          </table>
        }
        @case ('clusters') {
          <p class="muted">Failures with the same normalised error message (numbers, ids, timestamps and paths masked): often one root cause behind several tests.</p>
          <table class="data">
            <thead><tr><th>Error</th><th class="n">Failures</th><th class="n">Tests</th><th>First / last seen</th><th>Defects</th></tr></thead>
            <tbody>
              @for (c of clusters(); track c.clusterKey) {
                <tr><td class="err">{{ c.sampleMessage }}<div class="muted small">{{ c.tests.join(' · ') }}</div></td><td class="n">{{ c.failures }}</td><td class="n">{{ c.distinctTests }}</td>
                  <td class="small">{{ c.firstSeen | date: 'short' }}<br>{{ c.lastSeen | date: 'short' }}</td><td>{{ c.defectNumbers.join(', ') || '–' }}</td></tr>
              } @empty { <tr><td colspan="5" class="empty">No failures in the period.</td></tr> }
            </tbody>
          </table>
        }
        @case ('recurring') {
          <p class="muted">Tests failing repeatedly. Flips count changes between passed and failed: many flips with few failures suggests a flaky test rather than a product defect.</p>
          <table class="data">
            <thead><tr><th>Test</th><th class="n">Executions</th><th class="n">Failures</th><th class="n">Failing streak</th><th class="n">Flips</th><th>Last failed</th><th>Open defect</th></tr></thead>
            <tbody>
              @for (r of recurring(); track r.identifier) {
                <tr><td>{{ r.displayName }}<div class="muted small">{{ r.testCaseNumber }}</div><div class="err small">{{ r.lastError }}</div></td><td class="n">{{ r.executions }}</td><td class="n">{{ r.failures }}</td>
                  <td class="n">{{ r.consecutiveFailures }}</td><td class="n">{{ r.flips }}</td><td>{{ r.lastFailedAt | date: 'short' }}</td>
                  <td>@if (r.defectId) { <a [routerLink]="['/defects', r.defectId]">{{ r.defectNumber }}</a> } @else { <span class="muted">none</span> }</td></tr>
              } @empty { <tr><td colspan="7" class="empty">No recurring failures.</td></tr> }
            </tbody>
          </table>
        }
      }
    </div>`,
  styles: `
    .tabs { display: flex; gap: 4px; margin-bottom: 8px; } .tabs a { padding: 6px 12px; border-radius: 4px; cursor: pointer; color: var(--muted); }
    .tabs a.on { background: var(--selected); color: var(--text); font-weight: 600; }
    .filters mat-form-field { width: 170px; } .filters .grow { flex: 1; min-width: 220px; }
    .err { font-family: var(--mono); font-size: 12px; max-width: 520px; white-space: pre-wrap; word-break: break-word; color: var(--bad); }
    .small { font-size: 11px; } td.n, th.n { text-align: right; }
    .sev { font-weight: 600; font-size: 12px; } .sev[data-sev='Critical'] { color: var(--bad); } .sev[data-sev='Major'] { color: var(--warn); }
  `,
})
export class Defects {
  private q = inject(QualityApi);
  private api = inject(ApiService);
  private ui = inject(UiService);
  private router = inject(Router);
  auth = inject(AuthService);
  tabs: [Tab, string][] = [['defects', 'Defects'], ['suggestions', 'From failed tests'], ['clusters', 'Failure clusters'], ['recurring', 'Recurring & flaky']];
  statuses = DEFECT_STATUSES;
  severities = SEVERITIES;
  tone = defectTone;
  tab = signal<Tab>('defects');
  products = signal<Product[]>([]);
  users = signal<DirectoryUser[]>([]);
  defects = signal<DefectSummary[]>([]);
  total = signal(0);
  suggestions = signal<FailureSuggestion[]>([]);
  clusters = signal<FailureCluster[]>([]);
  recurring = signal<RecurringFailure[]>([]);
  productId?: string;
  days = 7;
  filter: DefectFilter = { pageSize: 200 };

  constructor() {
    const qp = inject(ActivatedRoute).snapshot.queryParamMap;
    this.productId = qp.get('productId') ?? undefined;
    this.filter.status = (qp.get('status') as DefectStatus) ?? undefined;
    this.filter.openOnly = qp.get('openOnly') === 'true';
    if (qp.get('tab')) this.tab.set(qp.get('tab') as Tab);
    this.api.products().subscribe(p => this.products.set(p));
    this.q.users().subscribe(u => this.users.set(u));
    this.load();
    inject(ChangesService).events.pipe(debounceTime(1000), takeUntilDestroyed()).subscribe(e => { if (e.topic === 'defectsChanged') this.load(); });
  }

  setTab(t: Tab) { this.tab.set(t); this.load(); }

  load() {
    const productId = this.productId;
    const err = (e: unknown) => this.ui.error(e);
    switch (this.tab()) {
      case 'defects': this.q.defects({ ...this.filter, productId }).subscribe({ next: r => { this.defects.set(r.items); this.total.set(r.total); }, error: err }); break;
      case 'suggestions': this.q.suggestions({ productId, days: this.days }).subscribe({ next: s => this.suggestions.set(s), error: err }); break;
      case 'clusters': this.q.clusters({ productId, days: this.days }).subscribe({ next: c => this.clusters.set(c), error: err }); break;
      case 'recurring': this.q.recurring({ productId, days: this.days }).subscribe({ next: r => this.recurring.set(r), error: err }); break;
    }
  }

  async raise(s: FailureSuggestion) {
    try {
      const r = await firstValueFrom(this.q.fromFailure({ testExecutionId: s.testExecutionId }));
      this.ui.ok(r.linkedToExisting ? `Linked to ${r.defect.summary.numberDisplay}` : `${r.defect.summary.numberDisplay} created`);
      this.router.navigate(['/defects', r.defect.summary.id]);
    } catch (e) { this.ui.error(e); }
  }
}

interface TransitionData { defect: DefectDetail; to: DefectStatus; users: DirectoryUser[]; releases: ProductRelease[]; environments: TestEnvironment[]; }

@Component({
  selector: 'app-transition-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, HumanizePipe],
  template: `
    <h2 mat-dialog-title>{{ data.defect.summary.numberDisplay }} → {{ data.to | humanize }}</h2>
    <mat-dialog-content class="form">
      @if (data.to === 'Assigned') {
        <mat-form-field class="full"><mat-label>Assign to</mat-label><mat-select [(ngModel)]="assignedTo">
          @for (u of data.users; track u.id) { <mat-option [value]="u.userName">{{ u.displayName }} ({{ u.userName }})</mat-option> }</mat-select></mat-form-field>
      }
      @if (data.to === 'Duplicate') {
        <mat-form-field class="full"><mat-label>Duplicate of (DEF number)</mat-label><input matInput [(ngModel)]="duplicateNumber" placeholder="DEF-00012" /></mat-form-field>
      }
      @if (data.to === 'Fixed') {
        <mat-form-field class="full"><mat-label>Root cause</mat-label><textarea matInput rows="3" [(ngModel)]="rootCause"></textarea></mat-form-field>
        <mat-form-field class="full"><mat-label>Fix version</mat-label><mat-select [(ngModel)]="fixVersionId"><mat-option [value]="undefined">–</mat-option>
          @for (r of data.releases; track r.id) { <mat-option [value]="r.id">{{ r.versionLabel }}</mat-option> }</mat-select></mat-form-field>
      }
      @if (data.to === 'ReadyForRetest') {
        <mat-checkbox [(ngModel)]="retest" [disabled]="!data.defect.summary.testCaseId">Start a retest run of {{ data.defect.summary.testCaseNumber ?? 'the linked test case' }} now</mat-checkbox>
        @if (retest) { <mat-form-field class="full"><mat-label>Environment</mat-label><mat-select [(ngModel)]="environmentId"><mat-option [value]="undefined">Defect's environment</mat-option>
          @for (e of data.environments; track e.id) { <mat-option [value]="e.id">{{ e.name }}</mat-option> }</mat-select></mat-form-field> }
        <p class="muted">When the linked test passes in a later run the defect moves to Verified automatically; if it fails it is reopened.</p>
      }
      @if (data.to === 'Verified') {
        <mat-form-field class="full"><mat-label>Verification result</mat-label><textarea matInput rows="2" [(ngModel)]="verification" required></textarea></mat-form-field>
      }
      <mat-form-field class="full"><mat-label>Comment{{ needsComment ? ' (required)' : '' }}</mat-label><textarea matInput rows="3" [(ngModel)]="comment"></textarea></mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Cancel</button><button mat-flat-button (click)="ok()">{{ data.to | humanize }}</button></mat-dialog-actions>`,
  styles: `.form { display: flex; flex-direction: column; min-width: 420px; }`,
})
export class TransitionDialog {
  data = inject<TransitionData>(MAT_DIALOG_DATA);
  private ref = inject(MatDialogRef);
  private q = inject(QualityApi);
  assignedTo = this.data.defect.summary.assignedTo;
  duplicateNumber = '';
  rootCause = this.data.defect.rootCause ?? '';
  fixVersionId = this.data.defect.fixVersionId;
  retest = !!this.data.defect.summary.testCaseId;
  environmentId?: string;
  verification = '';
  comment = '';
  needsComment = ['Rejected', 'Deferred', 'Reopened'].includes(this.data.to);

  async ok() {
    let duplicateOfId: string | undefined;
    if (this.data.to === 'Duplicate') {
      const hit = (await firstValueFrom(this.q.defects({ text: this.duplicateNumber, pageSize: 5 }))).items.find(d => d.numberDisplay.toUpperCase() === this.duplicateNumber.trim().toUpperCase());
      duplicateOfId = hit?.id;
    }
    this.ref.close({ to: this.data.to, comment: this.comment || undefined, assignedTo: this.assignedTo, duplicateOfId, rootCause: this.rootCause || undefined,
      fixVersionId: this.fixVersionId, verificationResult: this.verification || undefined, retest: this.data.to === 'ReadyForRetest' && this.retest,
      retestEnvironmentId: this.environmentId, version: this.data.defect.summary.version });
  }
}

const EMPTY: DefectUpsert = { title: '', severity: 'Major', priority: 'P2' };

/** Defect detail: fields, workflow transitions, failure evidence, occurrences, retests, history and tracker push. */
@Component({
  selector: 'app-defect-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, DatePipe, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatMenuModule, MatTooltipModule, StatusPill, HumanizePipe],
  template: `
    <div class="page">
      <div class="page-head">
        <a routerLink="/defects" class="muted">Defects</a> ›
        <h1>{{ d() ? d()!.summary.numberDisplay + ' ' + d()!.summary.title : 'New defect' }}</h1>
        @if (d(); as d) { <app-status [value]="tone(d.summary.status)" [label]="d.summary.status | humanize" /> }
        <span class="spacer"></span>
        @if (d(); as d) {
          @if (canEdit()) {
            @for (t of d.allowedTransitions; track t) { <button mat-stroked-button (click)="move(t)">{{ t | humanize }}</button> }
            @if (!d.summary.externalKey && trackers().length) {
              <button mat-stroked-button [matMenuTriggerFor]="push"><mat-icon>upload</mat-icon>Push to tracker</button>
              <mat-menu #push>@for (t of trackers(); track t.id) { <button mat-menu-item (click)="pushTo(t)">{{ t.name }} ({{ t.kind }})</button> }</mat-menu>
            }
          }
          @if (d.summary.externalUrl) { <a mat-stroked-button [href]="d.summary.externalUrl" target="_blank" rel="noopener"><mat-icon>open_in_new</mat-icon>{{ d.summary.externalKey }}</a> }
        }
        @if (canEdit()) { <button mat-flat-button (click)="save()"><mat-icon>save</mat-icon>Save</button> }
      </div>
      <div class="cols">
        <div>
          <div class="panel grid-2">
            <mat-form-field class="span"><mat-label>Title</mat-label><input matInput [(ngModel)]="form.title" [readonly]="!canEdit()" /></mat-form-field>
            <mat-form-field><mat-label>Product</mat-label><mat-select [(ngModel)]="form.productId" (ngModelChange)="loadReleases()" [disabled]="!canEdit()">
              <mat-option [value]="undefined">–</mat-option>@for (p of products(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field><mat-label>Assigned to</mat-label><mat-select [(ngModel)]="form.assignedTo" [disabled]="!canEdit()"><mat-option [value]="undefined">Unassigned</mat-option>
              @for (u of users(); track u.id) { <mat-option [value]="u.userName">{{ u.displayName }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field><mat-label>Severity</mat-label><mat-select [(ngModel)]="form.severity" [disabled]="!canEdit()">@for (s of severities; track s) { <mat-option [value]="s">{{ s }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field><mat-label>Priority</mat-label><mat-select [(ngModel)]="form.priority" [disabled]="!canEdit()">@for (p of ['P1', 'P2', 'P3', 'P4']; track p) { <mat-option [value]="p">{{ p }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field><mat-label>Build version</mat-label><input matInput [(ngModel)]="form.buildVersion" [readonly]="!canEdit()" /></mat-form-field>
            <mat-form-field><mat-label>Fix version</mat-label><mat-select [(ngModel)]="form.fixVersionId" [disabled]="!canEdit()"><mat-option [value]="undefined">–</mat-option>
              @for (r of releases(); track r.id) { <mat-option [value]="r.id">{{ r.versionLabel }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field class="span"><mat-label>Description</mat-label><textarea matInput rows="3" [(ngModel)]="form.description" [readonly]="!canEdit()"></textarea></mat-form-field>
            <mat-form-field class="span"><mat-label>Reproduction steps</mat-label><textarea matInput rows="6" [(ngModel)]="form.reproductionSteps" [readonly]="!canEdit()"></textarea></mat-form-field>
            <mat-form-field><mat-label>Expected result</mat-label><textarea matInput rows="3" [(ngModel)]="form.expectedResult" [readonly]="!canEdit()"></textarea></mat-form-field>
            <mat-form-field><mat-label>Actual result</mat-label><textarea matInput rows="3" [(ngModel)]="form.actualResult" [readonly]="!canEdit()"></textarea></mat-form-field>
            <mat-form-field class="span"><mat-label>Root cause</mat-label><textarea matInput rows="2" [(ngModel)]="form.rootCause" [readonly]="!canEdit()"></textarea></mat-form-field>
          </div>
          @if (d(); as d) {
            @if (d.evidence.length) {
              <div class="panel"><h3>Evidence from the failing run</h3>
                <div class="evidence">@for (a of d.evidence; track a.id) {
                  @if (a.contentType.startsWith('image/')) { <figure (click)="download(a)"><img [src]="images()[a.id]" [alt]="a.fileName" /><figcaption>{{ a.fileName }}</figcaption></figure> }
                  @else { <a class="artifact" (click)="download(a)"><mat-icon class="inline">description</mat-icon>{{ a.fileName }} <span class="muted">{{ a.kind }}</span></a> }
                }</div></div>
            }
            <div class="panel"><h3>Occurrences ({{ d.summary.occurrenceCount }})</h3>
              <table class="data"><thead><tr><th>Run</th><th>When</th><th>Build</th><th>Environment</th><th>Error</th></tr></thead><tbody>
                @for (o of d.occurrences; track o.testExecutionId) { <tr><td><a [routerLink]="['/runs', o.runId]" class="mono">{{ o.runNumber }}</a></td><td>{{ o.completedAt | date: 'short' }}</td>
                  <td>{{ o.buildVersion }}</td><td>{{ o.environmentName }}</td><td class="err">{{ o.errorMessage }}</td></tr> }
                @empty { <tr><td colspan="5" class="empty">Not linked to a failed test.</td></tr> }</tbody></table></div>
          }
        </div>
        @if (d(); as d) {
          <aside>
            <div class="panel"><dl class="kv">
              <dt>Feature</dt><dd>@if (d.summary.featureId) { <a [routerLink]="['/features', d.summary.featureId]">[{{ d.summary.featureSequence }}] {{ d.summary.featureName }}</a> } @else { – }</dd>
              <dt>Test case</dt><dd>@if (d.summary.testCaseId) { <a [routerLink]="['/test-cases', d.summary.testCaseId]">{{ d.summary.testCaseNumber }}</a> } @else { – }</dd>
              <dt>Failing run</dt><dd>@if (d.failingRunId) { <a [routerLink]="['/runs', d.failingRunId]">{{ d.failingRunNumber }}</a> } @else { – }</dd>
              <dt>Environment</dt><dd>{{ d.environmentName ?? '–' }}</dd>
              <dt>Verification</dt><dd>{{ d.verificationResult ?? '–' }}</dd>
              @if (d.duplicateOfId) { <dt>Duplicate of</dt><dd><a [routerLink]="['/defects', d.duplicateOfId]">{{ d.duplicateOfNumber }}</a></dd> }
              <dt>Raised</dt><dd>{{ d.summary.createdBy }}, {{ d.summary.createdAt | date: 'medium' }}</dd>
              <dt>Signature</dt><dd class="mono small" matTooltip="Failures with this signature are linked to this defect instead of creating duplicates">{{ d.fingerprint ?? '–' }}</dd>
            </dl></div>
            @if (d.retests.length) {
              <div class="panel"><h3>Retests</h3>@for (r of d.retests; track r.id) { <div class="row"><a [routerLink]="['/runs', r.id]" class="mono">{{ r.numberDisplay }}</a>
                <app-status [value]="r.failed ? 'Failed' : r.passed ? 'Passed' : r.status" [label]="r.failed ? 'Failed' : r.passed ? 'Passed' : r.status" /></div> }</div>
            }
            <div class="panel"><h3>History</h3>
              @for (h of d.history; track h.id) {
                <div class="ev"><span class="muted small">{{ h.timestamp | date: 'short' }} · {{ h.userName }}</span>
                  @if (h.toStatus) { <div>{{ h.fromStatus ? (h.fromStatus | humanize) + ' → ' : '' }}<b>{{ h.toStatus | humanize }}</b></div> }
                  @if (h.text) { <div class="pre">{{ h.text }}</div> }</div>
              }
              @if (canEdit()) { <mat-form-field class="full"><mat-label>Add a comment</mat-label><textarea matInput rows="2" [(ngModel)]="comment"></textarea></mat-form-field>
                <button mat-stroked-button [disabled]="!comment.trim()" (click)="addComment()">Comment</button> }
            </div>
          </aside>
        }
      </div>
    </div>`,
  styles: `
    .cols { display: grid; grid-template-columns: minmax(0, 1fr) 340px; gap: 12px; align-items: start; } .span { grid-column: 1 / -1; }
    @media (max-width: 1100px) { .cols { grid-template-columns: minmax(0, 1fr); } }
    td.err { max-height: 120px; } .err { display: block; max-height: 120px; overflow: auto; }
    .err { font-family: var(--mono); font-size: 12px; white-space: pre-wrap; word-break: break-word; color: var(--bad); }
    .evidence { display: flex; flex-wrap: wrap; gap: 8px; } figure { margin: 0; cursor: pointer; } img { max-width: 260px; max-height: 180px; border: 1px solid var(--border); border-radius: 4px; }
    figcaption { font-size: 11px; color: var(--muted); } .artifact { display: block; cursor: pointer; }
    .ev { border-left: 2px solid var(--border); padding: 2px 8px; margin-bottom: 8px; } .small { font-size: 11px; } .kv { grid-template-columns: 100px 1fr; }
  `,
})
export class DefectDetailPage {
  private q = inject(QualityApi);
  private api = inject(ApiService);
  private ui = inject(UiService);
  private dialog = inject(MatDialog);
  private router = inject(Router);
  auth = inject(AuthService);
  id = input<string>();
  d = signal<DefectDetail | null>(null);
  products = signal<Product[]>([]);
  users = signal<DirectoryUser[]>([]);
  releases = signal<ProductRelease[]>([]);
  trackers = signal<Tracker[]>([]);
  images = signal<Record<string, string>>({});
  severities = SEVERITIES;
  tone = defectTone;
  form: DefectUpsert = { ...EMPTY };
  comment = '';
  canEdit = computed(() => this.auth.can('defects.manage'));

  constructor() {
    this.api.products().subscribe(p => this.products.set(p));
    this.q.users().subscribe(u => this.users.set(u));
    this.q.trackers().subscribe(t => this.trackers.set(t.filter(x => x.isEnabled)));
    effect(() => { const id = this.id(); if (id && id !== 'new') this.load(id); });
  }

  load(id: string) {
    this.q.defect(id).subscribe({ next: d => this.show(d), error: e => this.ui.error(e) });
  }

  private show(d: DefectDetail) {
    this.d.set(d);
    const s = d.summary;
    this.form = { title: s.title, description: d.description, productId: s.productId, featureId: s.featureId, testCaseId: s.testCaseId, buildVersion: d.buildVersion,
      environmentId: d.environmentId, severity: s.severity, priority: s.priority, reproductionSteps: d.reproductionSteps, expectedResult: d.expectedResult,
      actualResult: d.actualResult, assignedTo: s.assignedTo, rootCause: d.rootCause, fixVersionId: d.fixVersionId, version: s.version };
    this.loadReleases();
    for (const a of d.evidence.filter(a => a.contentType.startsWith('image/')))
      if (d.failingRunId) this.api.artifact(d.failingRunId, a.id).subscribe(b => this.images.update(m => ({ ...m, [a.id]: URL.createObjectURL(b) })));
  }

  loadReleases() { if (this.form.productId) this.api.releases(this.form.productId).subscribe(r => this.releases.set(r)); else this.releases.set([]); }

  async save() {
    try {
      const d = this.d();
      const saved = await firstValueFrom(d ? this.q.updateDefect(d.summary.id, this.form) : this.q.createDefect(this.form));
      this.ui.ok('Saved');
      if (!d) this.router.navigate(['/defects', saved.summary.id]); else this.show(saved);
    } catch (e) { this.ui.error(e); }
  }

  async move(to: DefectStatus) {
    const d = this.d()!;
    const environments = to === 'ReadyForRetest' ? await firstValueFrom(this.api.environments()) : [];
    const body = await firstValueFrom(this.dialog.open(TransitionDialog, { data: { defect: d, to, users: this.users(), releases: this.releases(), environments } satisfies TransitionData }).afterClosed());
    if (!body) return;
    try { this.show(await firstValueFrom(this.q.transition(d.summary.id, body))); this.ui.ok(`Moved to ${to}`); } catch (e) { this.ui.error(e); }
  }

  async addComment() {
    try { this.show(await firstValueFrom(this.q.comment(this.d()!.summary.id, this.comment))); this.comment = ''; } catch (e) { this.ui.error(e); }
  }

  async pushTo(t: Tracker) {
    try { this.show(await firstValueFrom(this.q.push(this.d()!.summary.id, t.id))); this.ui.ok(`Pushed to ${t.name}`); } catch (e) { this.ui.error(e); }
  }

  download(a: Artifact) {
    const runId = this.d()?.failingRunId;
    if (runId) this.api.artifact(runId, a.id).subscribe(b => saveBlob(b, a.fileName));
  }
}
