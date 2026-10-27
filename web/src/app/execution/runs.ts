import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { Router } from '@angular/router';
import { debounceTime, firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Product, ResourceLock, RunStatus, RunSummary } from '../core/models';
import { ChangesService, UiService } from '../core/ui.service';
import { StatusPill } from '../shared/display';
import { RunBar } from './run-bar';
import { RunLauncher } from './start-run';

/** Execution console: all runs, live; also starts runs by creation sequence. */
@Component({
  selector: 'app-runs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatPaginatorModule, StatusPill, RunBar],
  template: `
    <div class="page">
      <div class="page-head">
        <h1>Test runs</h1><span class="spacer"></span>
        @if (auth.can('tests.execute')) {
          <mat-form-field class="narrow"><mat-label>Product</mat-label><mat-select [(ngModel)]="seqProduct">@for (p of products(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field class="narrow"><mat-label>Features from sequence</mat-label><input matInput type="number" min="1" [(ngModel)]="seqFrom" /></mat-form-field>
          <button mat-stroked-button [disabled]="!seqProduct || !seqFrom" (click)="runFromSequence()"><mat-icon>play_arrow</mat-icon>Execute new features</button>
        }
      </div>
      <div class="wrap-row">
        <mat-form-field><mat-label>Search number or name</mat-label><input matInput [(ngModel)]="text" (ngModelChange)="page = 1; load()" /></mat-form-field>
        <mat-form-field><mat-label>Status</mat-label><mat-select [(ngModel)]="status" (ngModelChange)="page = 1; load()">
          <mat-option [value]="undefined">Any</mat-option>@for (s of statuses; track s) { <mat-option [value]="s">{{ s }}</mat-option> }</mat-select></mat-form-field>
      </div>
      <table class="data">
        <thead><tr><th>Run</th><th>Name / scope</th><th>Environment</th><th>Status</th><th class="bar-col">Progress</th><th>Tests</th><th>Started by</th><th>Created</th><th>Duration</th></tr></thead>
        <tbody>
          @for (r of runs(); track r.id) {
            <tr class="clickable" (click)="router.navigate(['/runs', r.id])">
              <td class="num">{{ r.numberDisplay }}</td>
              <td>{{ r.name }}@if (r.name !== r.scopeDescription) { <div class="muted small">{{ r.scopeDescription }}</div> }</td>
              <td>{{ r.environmentName }}@if (r.triggeredBy?.startsWith('schedule:') || r.triggeredBy?.startsWith('webhook:')) { <div class="muted small">{{ r.triggeredBy }}</div> }</td>
              <td><app-status [value]="r.status" />@if (r.schedulingPaused) { <span class="pill" data-tone="warn">paused</span> }</td>
              <td><app-run-bar [run]="r" /></td>
              <td class="small">{{ r.passed }} passed · {{ r.failed }} failed · {{ r.pending }} pending</td>
              <td>{{ r.createdBy }}</td><td class="muted">{{ r.createdAt | date: 'short' }}</td><td class="muted">{{ duration(r) }}</td>
            </tr>
          } @empty { <tr><td colspan="9" class="empty">No runs yet. Use "Execute tests" on a feature, suite, test case or published package.</td></tr> }
        </tbody>
      </table>
      <mat-paginator [length]="total()" [pageIndex]="page - 1" [pageSize]="50" (page)="onPage($event)" />
      @if (locks().length) {
        <div class="panel">
          <h3>Resource locks held</h3>
          <table class="data"><thead><tr><th>Resource</th><th>Run</th><th>Agent</th><th>Since</th></tr></thead>
            <tbody>@for (l of locks(); track l.key) { <tr class="clickable" (click)="router.navigate(['/runs', l.runId])"><td class="mono">{{ l.key }}</td><td>{{ l.runNumber }} {{ l.runName }}</td><td>{{ l.agentName }}</td><td class="muted">{{ l.acquiredAt | date: 'mediumTime' }}</td></tr> }</tbody></table>
          <p class="muted small">Jobs needing a held resource wait; locks are released when the job ends or its agent is lost.</p>
        </div>
      }
    </div>`,
  styles: `.narrow { width: 190px; } .small { font-size: 12px; } .bar-col { width: 200px; }`,
})
export class Runs {
  private api = inject(ApiService);
  private ui = inject(UiService);
  private launcher = inject(RunLauncher);
  router = inject(Router);
  auth = inject(AuthService);
  statuses: RunStatus[] = ['Queued', 'Running', 'Completed', 'Cancelled'];
  runs = signal<RunSummary[]>([]);
  locks = signal<ResourceLock[]>([]);
  total = signal(0);
  products = signal<Product[]>([]);
  text = '';
  status?: RunStatus;
  page = 1;
  seqProduct?: string;
  seqFrom?: number;

  constructor() {
    this.load();
    this.api.products().subscribe(p => { this.products.set(p); this.seqProduct = p[0]?.id; });
    inject(ChangesService).events.pipe(debounceTime(400), takeUntilDestroyed()).subscribe(e => { if (e.topic === 'runChanged') this.load(); });
  }

  onPage(e: PageEvent) { this.page = e.pageIndex + 1; this.load(); }

  async load() {
    this.api.runLocks().subscribe({ next: l => this.locks.set(l), error: () => this.locks.set([]) });
    try { const r = await firstValueFrom(this.api.runs({ text: this.text || undefined, status: this.status, page: this.page })); this.runs.set(r.items); this.total.set(r.total); }
    catch (e) { this.ui.error(e); }
  }

  runFromSequence() {
    const p = this.products().find(x => x.id === this.seqProduct)!;
    this.launcher.launch({ title: 'Execute tests of new features', description: `${p.name}: every feature created at or after sequence ${this.seqFrom}.`,
      scope: { productId: p.id, sequenceFrom: this.seqFrom } });
  }

  duration(r: RunSummary) {
    if (!r.startedAt) return '';
    const ms = new Date(r.completedAt ?? Date.now()).getTime() - new Date(r.startedAt).getTime();
    return ms < 60_000 ? `${Math.round(ms / 1000)}s` : `${Math.floor(ms / 60_000)}m ${Math.round((ms % 60_000) / 1000)}s`;
  }
}
