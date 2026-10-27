import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
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
import { Router, RouterLink } from '@angular/router';
import { debounceTime, firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Campaign, FeatureSearchHit, PackageSummary, PlanSummary, Schedule, ScheduleTarget, SuiteSummary, TestEnvironment, TriggerType } from '../core/models';
import { ChangesService, UiService } from '../core/ui.service';
import { FeaturePickerDialog } from '../shared/dialogs';

interface Draft {
  id: string | null; name: string; isEnabled: boolean; trigger: TriggerType; cronExpression: string; timeZoneId: string; runOnceAt: string; minute: number; time: string;
  daysOfWeek: number[]; dayOfMonth: number; targetType: ScheduleTarget; targetId?: string; targetLabel?: string; environmentId?: string; variables: string; priority: number; version?: number;
}

const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/** Unattended execution: calendar triggers (time zone aware) and webhooks for builds and deployments. */
@Component({
  selector: 'app-schedules',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe, RouterLink, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, MatTooltipModule],
  template: `
    <div class="page">
      <div class="page-head"><h1>Schedules & triggers</h1><span class="spacer"></span>
        @if (canEdit()) { <button mat-flat-button (click)="newDraft()"><mat-icon>add</mat-icon>New schedule</button> }</div>

      @if (token(); as t) {
        <div class="panel key">
          <h3>Webhook token for {{ t.name }} — shown only once</h3>
          <pre class="mono">curl -X POST "{{ t.url }}" \\
  -H "X-Wings-Trigger-Token: {{ t.token }}" \\
  -H "Content-Type: application/json" \\
  -d '&#123;"buildVersion": "2026.2.19"&#125;'</pre>
          <button mat-button (click)="token.set(null)">I have stored it in the pipeline's secret store</button>
        </div>
      }

      @if (draft(); as d) {
        <div class="panel">
          <div class="grid-3">
            <mat-form-field><mat-label>Name</mat-label><input matInput [(ngModel)]="d.name" /></mat-form-field>
            <mat-form-field><mat-label>Trigger</mat-label><mat-select [(ngModel)]="d.trigger">@for (t of triggers; track t) { <mat-option [value]="t">{{ t }}</mat-option> }</mat-select></mat-form-field>
            <div class="row"><mat-checkbox [(ngModel)]="d.isEnabled">Enabled</mat-checkbox></div>
            @switch (d.trigger) {
              @case ('Once') { <mat-form-field><mat-label>Run at (local time)</mat-label><input matInput type="datetime-local" [(ngModel)]="d.runOnceAt" /></mat-form-field> }
              @case ('Hourly') { <mat-form-field><mat-label>Minute past the hour</mat-label><input matInput type="number" min="0" max="59" [(ngModel)]="d.minute" /></mat-form-field> }
              @case ('Cron') { <mat-form-field class="span2"><mat-label>Cron (minute hour day month weekday)</mat-label><input matInput class="mono" [(ngModel)]="d.cronExpression" placeholder="0 2 * * 1-5" /></mat-form-field> }
              @case ('Webhook') { <p class="muted small span2">Started by CI/CD (after a build or deployment) with a token. Save, then create the token.</p> }
              @default { <mat-form-field><mat-label>Time (HH:mm)</mat-label><input matInput type="time" [(ngModel)]="d.time" /></mat-form-field> }
            }
            @if (d.trigger === 'Weekly') {
              <div class="days span2">@for (day of days; track $index) { <mat-checkbox [checked]="d.daysOfWeek.includes($index)" (change)="toggleDay($index)">{{ day }}</mat-checkbox> }</div>
            }
            @if (d.trigger === 'Monthly') { <mat-form-field><mat-label>Day of month (1–28)</mat-label><input matInput type="number" min="1" max="28" [(ngModel)]="d.dayOfMonth" /></mat-form-field> }
            @if (d.trigger !== 'Webhook' && d.trigger !== 'Once') {
              <mat-form-field><mat-label>Time zone</mat-label><input matInput [(ngModel)]="d.timeZoneId" placeholder="Asia/Kolkata" /></mat-form-field>
            }
            <mat-form-field><mat-label>Target</mat-label><mat-select [(ngModel)]="d.targetType" (ngModelChange)="d.targetId = undefined">
              @for (t of targets; track t) { <mat-option [value]="t">{{ t }}</mat-option> }</mat-select></mat-form-field>
            @switch (d.targetType) {
              @case ('Suite') { <mat-form-field><mat-label>Suite</mat-label><mat-select [(ngModel)]="d.targetId">@for (s of suites(); track s.id) { <mat-option [value]="s.id">{{ s.name }}</mat-option> }</mat-select></mat-form-field> }
              @case ('Plan') { <mat-form-field><mat-label>Plan</mat-label><mat-select [(ngModel)]="d.targetId">@for (p of plans(); track p.id) { <mat-option [value]="p.id">{{ p.numberDisplay }} {{ p.name }}</mat-option> }</mat-select></mat-form-field> }
              @case ('Campaign') { <mat-form-field><mat-label>Campaign</mat-label><mat-select [(ngModel)]="d.targetId">@for (c of campaigns(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select></mat-form-field> }
              @case ('Package') { <mat-form-field><mat-label>Package (latest published version)</mat-label><mat-select [(ngModel)]="d.targetId">@for (p of packages(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }</mat-select></mat-form-field> }
              @case ('Feature') { <div class="row"><button mat-stroked-button (click)="pickFeature()">{{ d.targetLabel ?? 'Choose feature…' }}</button></div> }
            }
            @if (d.targetType !== 'Campaign') {
              <mat-form-field><mat-label>Environment</mat-label><mat-select [(ngModel)]="d.environmentId"><mat-option [value]="undefined">{{ d.targetType === 'Plan' ? "Plan's default" : 'None' }}</mat-option>
                @for (e of envs(); track e.id) { <mat-option [value]="e.id">{{ e.name }}</mat-option> }</mat-select></mat-form-field>
            }
            <mat-form-field><mat-label>Priority</mat-label><input matInput type="number" min="0" max="100" [(ngModel)]="d.priority" /></mat-form-field>
            <mat-form-field class="span3"><mat-label>Variables (NAME=value per line)</mat-label><textarea matInput rows="2" class="mono" [(ngModel)]="d.variables"></textarea></mat-form-field>
          </div>
          <div class="row"><span class="spacer"></span><button mat-button (click)="draft.set(null)">Cancel</button>
            <button mat-flat-button (click)="save()" [disabled]="!d.name || !d.targetId">Save</button></div>
        </div>
      }

      <table class="data">
        <thead><tr><th>Schedule</th><th>When</th><th>Target</th><th>Environment</th><th>Next runs</th><th>Last</th><th></th></tr></thead>
        <tbody>
          @for (s of schedules(); track s.id) {
            <tr>
              <td><b>{{ s.name }}</b> @if (!s.isEnabled) { <span class="pill">disabled</span> }<div class="muted small">by {{ s.createdBy }}</div></td>
              <td>{{ describe(s) }}<div class="mono muted small">{{ s.cronExpression }} {{ s.cronExpression ? s.timeZoneId : '' }}</div></td>
              <td>{{ s.targetType }}: {{ s.targetName ?? '—' }}</td><td>{{ s.environmentName ?? '' }}</td>
              <td class="small">@for (u of s.upcoming.slice(0, 3); track u) { <div>{{ u | date: 'EEE d MMM, HH:mm' }}</div> }
                @if (s.trigger === 'Once' && s.nextRunAt) { {{ s.nextRunAt | date: 'medium' }} }</td>
              <td class="small">@if (s.lastRunId) { <a [routerLink]="['/runs', s.lastRunId]">{{ s.lastTriggeredAt | date: 'short' }}</a> }
                @if (s.lastError) { <div class="err" [matTooltip]="s.lastError">{{ s.lastError }}</div> }</td>
              <td class="actions">
                @if (auth.can('tests.execute')) { <button mat-icon-button matTooltip="Run now" (click)="runNow(s)"><mat-icon>play_arrow</mat-icon></button> }
                @if (canEdit()) {
                  @if (s.trigger === 'Webhook') { <button mat-icon-button [matTooltip]="s.webhookTokenPrefix ? 'Rotate token (' + s.webhookTokenPrefix + '…)' : 'Create token'" (click)="rotate(s)"><mat-icon>key</mat-icon></button> }
                  <button mat-icon-button (click)="edit(s)"><mat-icon>edit</mat-icon></button><button mat-icon-button (click)="remove(s)"><mat-icon>delete</mat-icon></button>
                }
              </td>
            </tr>
          } @empty { <tr><td colspan="7" class="empty">No schedules. Create one to run suites, plans or campaigns nightly, or after each build.</td></tr> }
        </tbody>
      </table>
    </div>`,
  styles: `
    .small { font-size: 12px; } .span2 { grid-column: span 2; } .span3 { grid-column: 1 / -1; } .days { display: flex; gap: 4px; flex-wrap: wrap; align-items: center; }
    .err { color: var(--bad); max-width: 260px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; } td.actions { white-space: nowrap; width: 1%; }
    .key { background: var(--warn-bg); } pre { background: var(--surface); padding: 8px; border-radius: 4px; white-space: pre-wrap; font-size: 12px; }
  `,
})
export class Schedules {
  private api = inject(ApiService);
  private ui = inject(UiService);
  private dialog = inject(MatDialog);
  private router = inject(Router);
  auth = inject(AuthService);
  triggers: TriggerType[] = ['Daily', 'Weekly', 'Hourly', 'Monthly', 'Once', 'Cron', 'Webhook'];
  targets: ScheduleTarget[] = ['Suite', 'Plan', 'Campaign', 'Feature', 'Package'];
  days = DAYS;
  schedules = signal<Schedule[]>([]);
  suites = signal<SuiteSummary[]>([]);
  plans = signal<PlanSummary[]>([]);
  campaigns = signal<Campaign[]>([]);
  packages = signal<PackageSummary[]>([]);
  envs = signal<TestEnvironment[]>([]);
  draft = signal<Draft | null>(null);
  token = signal<{ name: string; token: string; url: string } | null>(null);
  canEdit = () => this.auth.can('schedules.manage');
  private localZone = computed(() => Intl.DateTimeFormat().resolvedOptions().timeZone);

  constructor() {
    this.load();
    this.api.suites().subscribe(s => this.suites.set(s));
    this.api.plans().subscribe(p => this.plans.set(p));
    this.api.campaigns().subscribe(c => this.campaigns.set(c));
    this.api.packages().subscribe(p => this.packages.set(p));
    this.api.environments().subscribe(e => this.envs.set(e));
    inject(ChangesService).events.pipe(debounceTime(500), takeUntilDestroyed()).subscribe(e => { if (e.topic === 'schedulesChanged') this.load(); });
  }

  load() { this.api.schedules().subscribe({ next: s => this.schedules.set(s), error: e => this.ui.error(e) }); }

  newDraft() {
    this.draft.set({ id: null, name: '', isEnabled: true, trigger: 'Daily', cronExpression: '', timeZoneId: this.localZone(), runOnceAt: '', minute: 0, time: '02:00',
      daysOfWeek: [1, 2, 3, 4, 5], dayOfMonth: 1, targetType: 'Suite', variables: '', priority: 50 });
  }

  edit(s: Schedule) {
    const parts = (s.cronExpression ?? '').split(' ');
    const hhmm = parts.length === 5 ? `${parts[1].padStart(2, '0')}:${parts[0].padStart(2, '0')}` : '02:00';
    this.draft.set({ id: s.id, name: s.name, isEnabled: s.isEnabled, trigger: s.trigger, cronExpression: s.cronExpression ?? '', timeZoneId: s.timeZoneId,
      runOnceAt: s.runOnceAt ? new Date(s.runOnceAt).toISOString().slice(0, 16) : '', minute: Number(parts[0]) || 0, time: hhmm,
      daysOfWeek: s.trigger === 'Weekly' ? parts[4].split(',').map(Number) : [1, 2, 3, 4, 5], dayOfMonth: s.trigger === 'Monthly' ? Number(parts[2]) : 1,
      targetType: s.targetType, targetId: s.targetId, targetLabel: s.targetName, environmentId: s.environmentId,
      variables: Object.entries(s.variables).map(([k, v]) => `${k}=${v}`).join('\n'), priority: s.priority, version: s.version });
  }

  toggleDay(i: number) { const d = this.draft()!; d.daysOfWeek = d.daysOfWeek.includes(i) ? d.daysOfWeek.filter(x => x !== i) : [...d.daysOfWeek, i]; }

  async pickFeature() {
    const hit = await firstValueFrom(this.dialog.open(FeaturePickerDialog, { width: '640px', data: { title: 'Schedule tests of feature' } }).afterClosed()) as FeatureSearchHit | undefined;
    if (hit) { const d = this.draft()!; d.targetId = hit.node.id; d.targetLabel = `${hit.node.sequenceDisplay} ${hit.node.name}`; }
  }

  async save() {
    const d = this.draft()!;
    const variables: Record<string, string> = {};
    for (const line of d.variables.split('\n').map(l => l.trim()).filter(Boolean)) { const i = line.indexOf('='); if (i > 0) variables[line.slice(0, i).trim()] = line.slice(i + 1).trim(); }
    const body = {
      name: d.name, isEnabled: d.isEnabled, trigger: d.trigger, cronExpression: d.cronExpression || null, timeZoneId: d.timeZoneId || 'UTC',
      runOnceAt: d.runOnceAt ? new Date(d.runOnceAt).toISOString() : null, minute: d.minute, time: d.time, daysOfWeek: d.daysOfWeek, dayOfMonth: d.dayOfMonth,
      targetType: d.targetType, targetId: d.targetId, environmentId: d.environmentId ?? null, variables, priority: d.priority, version: d.version,
    };
    try { await firstValueFrom(this.api.saveSchedule(d.id, body)); this.ui.ok('Schedule saved'); this.draft.set(null); this.load(); } catch (e) { this.ui.error(e); }
  }

  async rotate(s: Schedule) {
    if (s.webhookTokenPrefix && !await this.ui.confirm({ title: 'Rotate token', message: 'The current token stops working immediately. Update the pipeline afterwards.', confirmText: 'Rotate', danger: true })) return;
    try { const r = await firstValueFrom(this.api.scheduleToken(s.id)); this.token.set({ name: s.name, token: r.token, url: r.url }); this.load(); } catch (e) { this.ui.error(e); }
  }

  async runNow(s: Schedule) {
    try { const runs = await firstValueFrom(this.api.runScheduleNow(s.id)); this.ui.ok(`${runs.length} run(s) started`); if (runs.length === 1) this.router.navigate(['/runs', runs[0].id]); this.load(); }
    catch (e) { this.ui.error(e); }
  }

  async remove(s: Schedule) {
    if (!await this.ui.confirm({ title: 'Delete schedule', message: `Delete ${s.name}?`, danger: true, confirmText: 'Delete' })) return;
    try { await firstValueFrom(this.api.deleteSchedule(s.id)); this.load(); } catch (e) { this.ui.error(e); }
  }

  describe(s: Schedule) {
    const parts = (s.cronExpression ?? '').split(' ');
    const at = parts.length === 5 ? `${parts[1].padStart(2, '0')}:${parts[0].padStart(2, '0')}` : '';
    switch (s.trigger) {
      case 'Daily': return `Daily at ${at}`;
      case 'Weekly': return `${parts[4].split(',').map(d => DAYS[+d]).join(', ')} at ${at}`;
      case 'Hourly': return `Hourly at :${parts[0].padStart(2, '0')}`;
      case 'Monthly': return `Monthly on day ${parts[2]} at ${at}`;
      case 'Once': return 'Once';
      case 'Webhook': return 'On webhook (build / deployment)';
      default: return 'Custom cron';
    }
  }
}
