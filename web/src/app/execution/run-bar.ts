import { ChangeDetectionStrategy, Component, OnDestroy, computed, effect, inject, input, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { RunSummary } from '../core/models';

/** Segmented progress bar: passed, failed, blocked/infrastructure, skipped/not run, pending. */
@Component({
  selector: 'app-run-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="bar" [title]="title()">
      @for (s of segments(); track s.cls) { <div [class]="s.cls" [style.width.%]="s.pct"></div> }
    </div>`,
  styles: `
    .bar { display: flex; height: 8px; border-radius: 4px; overflow: hidden; background: var(--off-bg); min-width: 120px; }
    .passed { background: #2da44e; } .failed { background: #cf222e; } .infra { background: #bc4c00; } .skipped { background: #8c959f; } .pending { background: #c9d7ea; }
  `,
})
export class RunBar {
  run = input.required<RunSummary>();
  segments = computed(() => {
    const r = this.run(), t = Math.max(1, r.totalTests);
    return [
      { cls: 'passed', pct: 100 * r.passed / t }, { cls: 'failed', pct: 100 * r.failed / t },
      { cls: 'infra', pct: 100 * (r.infrastructureErrors + r.blocked) / t }, { cls: 'skipped', pct: 100 * (r.skipped + r.notRun) / t },
      { cls: 'pending', pct: 100 * r.pending / t },
    ].filter(s => s.pct > 0);
  });
  title = computed(() => {
    const r = this.run();
    return `${r.passed} passed, ${r.failed} failed, ${r.blocked} blocked, ${r.infrastructureErrors} infrastructure errors, ${r.skipped} skipped, ${r.notRun} not run, ${r.pending} pending`;
  });
}

/** Shows an image artifact; fetched with the user's token because <img src> cannot send it. */
@Component({
  selector: 'app-artifact-image',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (url()) { <img [src]="url()" [alt]="name()" (click)="open()" /> } @else { <span class="muted">loading…</span> }`,
  styles: `img { max-width: 100%; max-height: 260px; border: 1px solid var(--border); border-radius: 4px; cursor: zoom-in; display: block; }`,
})
export class ArtifactImage implements OnDestroy {
  private api = inject(ApiService);
  runId = input.required<string>();
  artifactId = input.required<string>();
  name = input('');
  url = signal<string | null>(null);

  constructor() {
    effect(() => {
      const runId = this.runId(), id = this.artifactId();
      firstValueFrom(this.api.artifact(runId, id)).then(b => { this.revoke(); this.url.set(URL.createObjectURL(b)); }).catch(() => this.url.set(null));
    });
  }

  open() { const u = this.url(); if (u) window.open(u, '_blank', 'noopener'); }
  ngOnDestroy() { this.revoke(); }
  private revoke() { const u = this.url(); if (u) URL.revokeObjectURL(u); }
}
