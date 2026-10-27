import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { debounceTime, firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Campaign, PlanSummary, RunSummary, TestEnvironment } from '../core/models';
import { ChangesService, UiService } from '../core/ui.service';
import { StatusPill } from '../shared/display';
import { RunBar } from '../execution/run-bar';

interface CampaignDraft { id: string | null; name: string; description: string; buildVersion: string; planIds: string[]; environmentIds: string[]; version?: number; }

/** Campaigns: one build, several plans across several environments; a live plan × environment matrix. */
@Component({
  selector: 'app-campaigns',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, StatusPill, RunBar],
  template: `
    <div class="layout">
      <aside class="list">
        <div class="row"><h2>Campaigns</h2><span class="spacer"></span>@if (canEdit()) { <button mat-icon-button (click)="newDraft()"><mat-icon>add</mat-icon></button> }</div>
        @for (c of campaigns(); track c.id) {
          <a class="item" [class.on]="draft()?.id === c.id" (click)="open(c)">
            <span class="num">{{ c.numberDisplay }}</span><span>{{ c.name }}</span><app-status [value]="c.status === 'Running' ? 'InReview' : c.status" [label]="c.status" />
            <span class="sub">{{ c.plans.length }} plan(s) × {{ c.environments.length }} environment(s){{ c.buildVersion ? ' · build ' + c.buildVersion : '' }}</span>
          </a>
        } @empty { <p class="muted">No campaigns yet.</p> }
      </aside>
      <section class="page">
        @if (draft(); as d) {
          <div class="page-head"><h1>{{ d.id ? d.name : 'New campaign' }}</h1><span class="spacer"></span>
            @if (d.id && auth.can('tests.execute')) { <button mat-flat-button (click)="execute()"><mat-icon>play_arrow</mat-icon>Execute all</button> }
            @if (canEdit()) { @if (d.id) { <button mat-icon-button (click)="remove()"><mat-icon>delete</mat-icon></button> }
              <button mat-stroked-button (click)="save()"><mat-icon>save</mat-icon>Save</button> }
          </div>
          <div class="panel grid-2">
            <mat-form-field><mat-label>Name</mat-label><input matInput [(ngModel)]="d.name" placeholder="Release 2026.2 regression" /></mat-form-field>
            <mat-form-field><mat-label>Build version</mat-label><input matInput [(ngModel)]="d.buildVersion" /></mat-form-field>
            <mat-form-field><mat-label>Plans</mat-label><mat-select multiple [(ngModel)]="d.planIds">
              @for (p of plans(); track p.id) { <mat-option [value]="p.id">{{ p.numberDisplay }} {{ p.name }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field><mat-label>Environments</mat-label><mat-select multiple [(ngModel)]="d.environmentIds">
              @for (e of envs(); track e.id) { <mat-option [value]="e.id">{{ e.name }}{{ e.isProduction ? ' (PROD)' : '' }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field class="full span"><mat-label>Description</mat-label><textarea matInput rows="2" [(ngModel)]="d.description"></textarea></mat-form-field>
          </div>
          @if (current(); as c) {
            <div class="panel">
              <div class="row"><h3>Progress</h3><span class="muted">{{ c.passed }} passed · {{ c.failed }} failed · {{ c.pending }} pending of {{ c.total }}</span></div>
              <table class="data matrix">
                <thead><tr><th>Plan</th>@for (e of c.environments; track e.id) { <th>{{ e.name }}</th> }</tr></thead>
                <tbody>
                  @for (p of c.plans; track p.id) {
                    <tr><td><a [routerLink]="['/plans', p.id]">{{ p.numberDisplay }}</a> {{ p.name }}</td>
                      @for (e of c.environments; track e.id) {
                        <td>@if (cell(c, p.id, e.id); as r) { <a [routerLink]="['/runs', r.id]" class="run"><span class="mono small">{{ r.numberDisplay }}</span> <app-status [value]="r.status" /></a><app-run-bar [run]="r" /> }
                          @else { <span class="muted">not run</span> }</td>
                      }</tr>
                  }
                </tbody>
              </table>
            </div>
          }
        } @else { <div class="empty">Select a campaign or create one.</div> }
      </section>
    </div>`,
  styles: `
    .layout { display: grid; grid-template-columns: 320px 1fr; height: 100%; }
    .list { background: var(--surface); border-right: 1px solid var(--border); padding: 8px; overflow: auto; }
    .item { display: grid; grid-template-columns: auto 1fr auto; gap: 2px 8px; padding: 6px 8px; border-radius: 4px; cursor: pointer; }
    .item .sub { grid-column: 1 / -1; font-size: 12px; color: var(--muted); } .item:hover { background: var(--hover); } .item.on { background: var(--selected); }
    .num { font-family: var(--mono); color: var(--muted); } .span { grid-column: 1 / -1; } .small { font-size: 11px; }
    .matrix td { vertical-align: top; min-width: 180px; } .run { display: flex; gap: 6px; align-items: center; margin-bottom: 4px; text-decoration: none; }
  `,
})
export class Campaigns {
  private api = inject(ApiService);
  private ui = inject(UiService);
  auth = inject(AuthService);
  campaigns = signal<Campaign[]>([]);
  plans = signal<PlanSummary[]>([]);
  envs = signal<TestEnvironment[]>([]);
  draft = signal<CampaignDraft | null>(null);
  current = signal<Campaign | null>(null);
  canEdit = () => this.auth.can('plans.manage');

  constructor() {
    this.load();
    this.api.plans().subscribe(p => this.plans.set(p));
    this.api.environments().subscribe(e => this.envs.set(e));
    inject(ChangesService).events.pipe(debounceTime(800), takeUntilDestroyed()).subscribe(e => { if (e.topic === 'runChanged' && this.current()) this.refresh(); });
  }

  load() { this.api.campaigns().subscribe({ next: c => this.campaigns.set(c), error: e => this.ui.error(e) }); }
  newDraft() { this.current.set(null); this.draft.set({ id: null, name: '', description: '', buildVersion: '', planIds: [], environmentIds: [] }); }

  open(c: Campaign) {
    this.current.set(c);
    this.draft.set({ id: c.id, name: c.name, description: c.description ?? '', buildVersion: c.buildVersion ?? '', planIds: c.plans.map(p => p.id),
      environmentIds: c.environments.map(e => e.id), version: c.version });
  }

  refresh() { const id = this.current()?.id; if (id) this.api.campaign(id).subscribe(c => { this.current.set(c); this.campaigns.update(l => l.map(x => x.id === id ? c : x)); }); }

  cell(c: Campaign, planId: string, envId: string): RunSummary | undefined { return c.cells.find(x => x.planId === planId && x.environmentId === envId)?.latestRun; }

  async save() {
    const d = this.draft()!;
    try { const c = await firstValueFrom(this.api.saveCampaign(d.id, { ...d, buildVersion: d.buildVersion || null })); this.ui.ok('Saved'); this.load(); this.open(c); }
    catch (e) { this.ui.error(e); }
  }

  async execute() {
    const d = this.draft()!;
    if (!await this.ui.confirm({ title: 'Execute campaign', message: `Start ${d.planIds.length * d.environmentIds.length} run(s): every plan on every environment.`, confirmText: 'Execute' })) return;
    try { const runs = await firstValueFrom(this.api.executeCampaign(d.id!)); this.ui.ok(`${runs.length} run(s) started`); this.refresh(); }
    catch (e) { this.ui.error(e); }
  }

  async remove() {
    if (!await this.ui.confirm({ title: 'Delete campaign', message: 'Runs already started are kept.', danger: true, confirmText: 'Delete' })) return;
    try { await firstValueFrom(this.api.deleteCampaign(this.draft()!.id!)); this.draft.set(null); this.current.set(null); this.load(); } catch (e) { this.ui.error(e); }
  }
}
