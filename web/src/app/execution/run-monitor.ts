import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { Subject, debounceTime, firstValueFrom } from 'rxjs';
import { ApiService, saveBlob } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Artifact, Job, LogLine, Outcome, RunDetail, TestExecution } from '../core/models';
import { ChangesService, UiService } from '../core/ui.service';
import { BytesPipe, HumanizePipe, StatusPill } from '../shared/display';
import { ArtifactImage, RunBar } from './run-bar';
import { RunLauncher } from './start-run';
import { QualityApi } from '../core/quality';

const OUTCOMES: Outcome[] = ['Failed', 'InfrastructureError', 'Blocked', 'Passed', 'Skipped', 'NotRun', 'Pending'];

/** Live execution monitor: progress, controls, jobs/agents, streaming logs and per-test evidence. */
@Component({
  selector: 'app-run-monitor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DatePipe, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule, StatusPill, HumanizePipe, BytesPipe, RunBar, ArtifactImage],
  templateUrl: './run-monitor.html',
  styleUrl: './run-monitor.scss',
})
export class RunMonitor {
  private api = inject(ApiService);
  private ui = inject(UiService);
  private launcher = inject(RunLauncher);
  private quality = inject(QualityApi);
  router = inject(Router);
  auth = inject(AuthService);

  id = input.required<string>();
  detail = signal<RunDetail | null>(null);
  outcomeFilter = signal<Outcome | 'All'>('All');
  showSuperseded = signal(false);
  expanded = signal<string | null>(null);
  selectedJob = signal<string | null>(null);
  logs = signal<LogLine[]>([]);
  follow = signal(true);
  outcomes = OUTCOMES;
  private refresh$ = new Subject<void>();
  private logBox = viewChild<ElementRef<HTMLDivElement>>('logBox');

  active = computed(() => ['Queued', 'Running'].includes(this.detail()?.run.status ?? ''));
  counts = computed(() => {
    const m = new Map<string, number>();
    for (const t of this.detail()?.tests ?? []) if (!t.isSuperseded) m.set(t.outcome, (m.get(t.outcome) ?? 0) + 1);
    return m;
  });
  tests = computed(() => (this.detail()?.tests ?? [])
    .filter(t => this.showSuperseded() || !t.isSuperseded)
    .filter(t => this.outcomeFilter() === 'All' || t.outcome === this.outcomeFilter())
    .sort((a, b) => OUTCOMES.indexOf(a.outcome) - OUTCOMES.indexOf(b.outcome) || a.identifier.localeCompare(b.identifier)));
  job = computed(() => this.detail()?.jobs.find(j => j.id === this.selectedJob()) ?? null);
  variables = computed(() => Object.entries(this.detail()?.variables ?? {}));

  constructor() {
    effect(() => { const id = this.id(); untracked(() => this.load(id)); });
    this.refresh$.pipe(debounceTime(300), takeUntilDestroyed()).subscribe(() => this.load(this.id()));
    inject(ChangesService).events.pipe(takeUntilDestroyed()).subscribe(e => {
      if (e.payload['runId'] !== this.id()) return;
      if (e.topic === 'runChanged') this.refresh$.next();
      if (e.topic === 'jobLog' && e.payload['jobId'] === this.selectedJob()) this.appendLogs(e.payload['lines'] as LogLine[]);
    });
  }

  async load(id: string) {
    try {
      const d = await firstValueFrom(this.api.run(id));
      this.detail.set(d);
      if (!this.selectedJob() && d.jobs.length) this.selectJob(d.jobs.find(j => ['Running', 'Leased'].includes(j.status)) ?? d.jobs[d.jobs.length - 1]);
    } catch (e) { this.ui.error(e); }
  }

  // ---- logs ----
  async selectJob(j: Job) {
    this.selectedJob.set(j.id);
    this.logs.set([]);
    try { this.appendLogs(await firstValueFrom(this.api.jobLogs(j.id))); } catch (e) { this.ui.error(e); }
  }

  private appendLogs(lines: LogLine[]) {
    const last = this.logs().at(-1)?.sequence ?? 0;
    const fresh = lines.filter(l => l.sequence > last);
    if (!fresh.length) return;
    this.logs.update(l => [...l, ...fresh].slice(-5000));
    if (this.follow()) queueMicrotask(() => { const el = this.logBox()?.nativeElement; if (el) el.scrollTop = el.scrollHeight; });
  }

  // ---- controls ----
  async control(action: 'cancel' | 'pause' | 'resume') {
    if (action === 'cancel' && !await this.ui.confirm({ title: 'Cancel run', message: 'Queued jobs are cancelled now; running jobs are stopped by their agents within one heartbeat.', confirmText: 'Cancel run', danger: true })) return;
    try { await firstValueFrom(this.api.runAction(this.id(), action)); this.load(this.id()); } catch (e) { this.ui.error(e); }
  }

  async priority() {
    const v = await this.ui.prompt({ title: 'Change priority', label: 'Priority (0–100, higher runs first)', value: String(this.detail()!.run.priority) });
    if (v === undefined || isNaN(+v)) return;
    try { await firstValueFrom(this.api.setRunPriority(this.id(), +v)); this.load(this.id()); } catch (e) { this.ui.error(e); }
  }

  rerun(onlyFailed: boolean) {
    const r = this.detail()!.run;
    this.launcher.launch({ title: onlyFailed ? 'Re-run failed tests' : 'Re-run all tests', scope: { rerunOfRunId: r.id, onlyFailed },
      description: `${onlyFailed ? 'Failed, blocked and infrastructure-error tests' : 'All automated tests'} of ${r.numberDisplay}, on the same package versions.` });
  }

  // ---- evidence ----
  images(t: TestExecution) { return t.artifacts.filter(a => a.contentType.startsWith('image/')); }
  others(t: TestExecution) { return t.artifacts.filter(a => !a.contentType.startsWith('image/')); }

  async download(a: Artifact) {
    try { saveBlob(await firstValueFrom(this.api.artifact(this.id(), a.id)), a.fileName); } catch (e) { this.ui.error(e); }
  }

  isTrace = (a: Artifact) => a.kind === 'Trace';
  manualOutcomes = ['Passed', 'Failed', 'Blocked', 'Skipped'];
  async recordManual(t: TestExecution, outcome: string) {
    const comment = await this.ui.prompt({ title: `${outcome}: ${t.displayName}`, label: outcome === 'Passed' ? 'Notes (optional)' : 'What happened?',
      multiline: true, required: outcome !== 'Passed', confirmText: 'Record result' });
    if (comment === undefined) return;
    try { await firstValueFrom(this.api.recordManual(this.id(), t.id, outcome, comment || undefined)); this.load(this.id()); } catch (e) { this.ui.error(e); }
  }

  toggle(t: TestExecution) { this.expanded.update(x => x === t.id ? null : t.id); }
  duration(ms?: number) { return ms == null ? '' : ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`; }
  outcomeLabel(o: string) { return o === 'InfrastructureError' ? 'Infrastructure error' : o === 'NotRun' ? 'Not run' : o; }

  /** Creates a defect prefilled from the failure, or links it to the defect that already has the same failure signature. */
  async raiseDefect(t: TestExecution) {
    try {
      const r = await firstValueFrom(this.quality.fromFailure({ testExecutionId: t.id }));
      this.ui.ok(r.linkedToExisting ? `Same failure as ${r.defect.summary.numberDisplay}: occurrence linked` : `${r.defect.summary.numberDisplay} created`);
      this.router.navigate(['/defects', r.defect.summary.id]);
    } catch (e) { this.ui.error(e); }
  }
}
