import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { FeatureDetail } from '../core/models';
import { UiService } from '../core/ui.service';
import { StatusPill } from '../shared/display';
import { FeatureDetailPanel } from './feature-detail';
import { FeatureTree } from './feature-tree';
import { RunLauncher } from '../execution/start-run';

const PREFS = 'wto.workspace';

/** Three-region workspace: feature explorer | feature detail | testing summary. Panels are resizable; the right one collapses. */
@Component({
  selector: 'app-feature-workspace',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FeatureTree, FeatureDetailPanel, MatButtonModule, MatIconModule, MatTooltipModule, RouterLink, DatePipe, StatusPill],
  template: `
    <div class="layout" [style.grid-template-columns]="columns()">
      <app-feature-tree #tree [productId]="productId()" [selectedId]="id()" (selected)="open($event)" (productChange)="setProduct($event)" />
      <div class="splitter" (pointerdown)="startResize($event, 'left')"></div>
      <section class="center">
        @if (id(); as fid) {
          <app-feature-detail [featureId]="fid" [tabIndex]="tab()" (loaded)="onLoaded($event)" (changed)="tree.refreshLoaded()" (navigate)="openAndReveal($event)" />
        } @else {
          <div class="placeholder">
            <h2>Select a feature</h2>
            <p class="muted">Browse the tree on the left, search by name or sequence number, or use the quick filters to find
              recently added, untested or not-yet-automated features.</p>
          </div>
        }
      </section>
      @if (!collapsedView()) { <div class="splitter" (pointerdown)="startResize($event, 'right')"></div> }
      <aside class="summary" [class.collapsed]="collapsedView()">
        <button mat-icon-button class="collapse" (click)="toggleRight()" [matTooltip]="rightCollapsed() ? 'Show testing summary' : 'Hide testing summary'">
          <mat-icon>{{ collapsedView() ? 'chevron_left' : 'chevron_right' }}</mat-icon>
        </button>
        @if (!collapsedView()) {
          @if (current(); as f) {
            <h3>Testing summary</h3>
            <div class="coverage"><app-status [value]="f.testing.coverageStatus" [label]="coverageLabel(f)" /></div>
            <div class="stats">
              <div><b>{{ f.testing.testCaseCount }}</b><span>Test cases</span></div>
              <div><b>{{ f.testing.automatedCount }}</b><span>Automated</span></div>
              <div><b>{{ f.testing.manualCount }}</b><span>Manual</span></div>
              <div><b>{{ f.testing.approvedCount }}</b><span>Approved</span></div>
              <div><b>{{ f.testing.draftCount }}</b><span>Draft / in review</span></div>
              <div><b>{{ f.testing.packagedTestCount }}</b><span>Mapped code tests</span></div>
            </div>
            <div class="bar" [attr.title]="automationPct(f) + '% of test cases automated'">
              <div class="fill" [style.width.%]="automationPct(f)"></div>
            </div>
            <p class="muted small">{{ automationPct(f) }}% of this feature's test cases are automated.</p>
            <dl class="kv small">
              <dt>Last run</dt><dd>@if (f.testing.lastRunId) { <a [routerLink]="['/runs', f.testing.lastRunId]">{{ f.testing.lastExecutedAt | date: 'short' }}</a> } @else { Not executed }</dd>
              @if (f.testing.lastResult) { <dt>Last result</dt><dd>{{ f.testing.lastResult }}</dd> }
              <dt>Requirements</dt><dd>{{ f.requirements.length }}</dd>
              <dt>Dependencies</dt><dd>{{ f.dependencies.length }}</dd>
            </dl>
            <div class="actions">
              @if (auth.can('tests.execute')) {
                <button mat-flat-button (click)="execute(f)"><mat-icon>play_arrow</mat-icon>Execute tests</button>
              }
              @if (auth.can('testcases.edit')) {
                <button mat-flat-button [routerLink]="['/test-cases/new']" [queryParams]="{ featureId: f.id }"><mat-icon>add</mat-icon>Add test case</button>
              }
              @if (auth.can('testcode.upload')) {
                <button mat-stroked-button [routerLink]="['/test-code/upload']" [queryParams]="{ productId: f.productId, featureId: f.id }"><mat-icon>upload</mat-icon>Upload test code</button>
              }
              <button mat-stroked-button (click)="tab.set(-1); tab.set(2)"><mat-icon>checklist</mat-icon>View test cases</button>
              <button mat-stroked-button (click)="tab.set(-1); tab.set(8)"><mat-icon>history</mat-icon>View results</button>
            </div>
            <p class="muted small note">Defects and release readiness arrive with the quality phase (phase 5).</p>
          } @else { <p class="muted">No feature selected.</p> }
        }
      </aside>
    </div>`,
  styles: `
    :host { display: block; height: 100%; }
    .layout { display: grid; height: 100%; min-height: 0; }
    .layout > * { min-width: 0; min-height: 0; }
    .splitter { cursor: col-resize; background: var(--border); width: 4px; }
    .splitter:hover { background: var(--accent); }
    .center { overflow: hidden; background: var(--canvas); }
    .placeholder { padding: 48px; max-width: 560px; }
    .summary { background: var(--surface); padding: 8px 12px; overflow: auto; position: relative; }
    .summary.collapsed { padding: 4px 0; }
    .collapse { position: absolute; right: 2px; top: 2px; }
    .summary h3 { margin: 10px 0 8px; }
    .stats { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; margin: 10px 0; }
    .stats div { border: 1px solid var(--border); border-radius: 6px; padding: 6px 8px; display: flex; flex-direction: column; }
    .stats b { font-size: 18px; } .stats span { color: var(--muted); font-size: 11px; }
    .bar { height: 6px; background: var(--off-bg); border-radius: 3px; overflow: hidden; }
    .fill { height: 100%; background: var(--good); }
    .small { font-size: 12px; }
    .kv { grid-template-columns: 100px 1fr; margin: 8px 0; }
    .actions { display: flex; flex-direction: column; gap: 6px; margin-top: 12px; }
    .note { margin-top: 16px; border-top: 1px solid var(--border); padding-top: 8px; }
  `,
})
export class FeatureWorkspace {
  private api = inject(ApiService);
  private router = inject(Router);
  private ui = inject(UiService);
  auth = inject(AuthService);

  /** Route parameter (feature id). */
  id = input<string | undefined>();
  productId = signal<string | undefined>(undefined);
  current = signal<FeatureDetail | null>(null);
  tab = signal(0);
  leftWidth = signal(this.pref('left', 340));
  rightWidth = signal(this.pref('right', 260));
  rightCollapsed = signal(this.pref('collapsed', 0) === 1);
  /** Narrow windows hide the summary and cap the explorer so the detail workspace keeps usable width. */
  private viewportWidth = signal(window.innerWidth);
  private narrow = computed(() => this.viewportWidth() < 1100);
  columns = computed(() => {
    const left = Math.min(this.leftWidth(), Math.max(220, this.viewportWidth() * 0.4));
    return this.rightCollapsed() || this.narrow()
      ? `${left}px 4px minmax(0, 1fr) 0px 36px`
      : `${left}px 4px minmax(0, 1fr) 4px ${this.rightWidth()}px`;
  });
  collapsedView = computed(() => this.rightCollapsed() || this.narrow());
  private tree = viewChild(FeatureTree);

  constructor() {
    const onResize = () => this.viewportWidth.set(window.innerWidth);
    window.addEventListener('resize', onResize);
    inject(DestroyRef).onDestroy(() => window.removeEventListener('resize', onResize));
    // Pick a product: the selected feature's, the last used one, or the first available.
    effect(() => {
      if (this.productId()) return;
      const last = this.prefString('product');
      firstValueFrom(this.api.products()).then(products => {
        if (!this.productId()) this.productId.set(products.find(p => p.id === last)?.id ?? products[0]?.id);
      }).catch(e => this.ui.error(e));
    });
  }

  private launcher = inject(RunLauncher);

  execute(f: FeatureDetail) {
    this.launcher.launch({ title: 'Execute feature tests', scope: { featureId: f.id, includeSubfeatures: true },
      description: `[${f.sequenceDisplay}] ${f.fields.name} and its sub-features: every published automated test mapped to them or to their test cases.` });
  }

  open(id: string) { this.router.navigate(['/features', id]); }

  async openAndReveal(id: string) {
    this.open(id);
    await this.tree()?.reveal(id);
  }

  onLoaded(f: FeatureDetail) {
    this.current.set(f);
    if (f.productId !== this.productId()) { this.productId.set(f.productId); setTimeout(() => this.tree()?.reveal(f.id), 300); }
  }

  setProduct(id: string) {
    this.productId.set(id);
    this.savePref('product', id);
    if (this.current()?.productId !== id) { this.current.set(null); this.router.navigate(['/features']); }
  }

  coverageLabel(f: FeatureDetail) {
    return { NONE: 'Not covered', MANUAL: 'Manual only', PARTIAL: 'Partly automated', AUTOMATED: 'Fully automated' }[f.testing.coverageStatus];
  }

  automationPct(f: FeatureDetail) { return f.testing.testCaseCount ? Math.round(100 * f.testing.automatedCount / f.testing.testCaseCount) : 0; }

  toggleRight() { this.rightCollapsed.update(v => !v); this.savePref('collapsed', this.rightCollapsed() ? 1 : 0); }

  startResize(e: PointerEvent, side: 'left' | 'right') {
    e.preventDefault();
    const startX = e.clientX, startLeft = this.leftWidth(), startRight = this.rightWidth();
    const move = (ev: PointerEvent) => {
      if (side === 'left') this.leftWidth.set(Math.max(220, Math.min(700, startLeft + ev.clientX - startX)));
      else this.rightWidth.set(Math.max(200, Math.min(500, startRight - (ev.clientX - startX))));
    };
    const up = () => {
      window.removeEventListener('pointermove', move); window.removeEventListener('pointerup', up);
      this.savePref('left', this.leftWidth()); this.savePref('right', this.rightWidth());
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  }

  private prefs(): Record<string, string | number> {
    try { return JSON.parse(localStorage.getItem(PREFS) ?? '{}'); } catch { return {}; }
  }
  private pref(key: string, fallback: number) { const v = this.prefs()[key]; return typeof v === 'number' ? v : fallback; }
  private prefString(key: string) { const v = this.prefs()[key]; return typeof v === 'string' ? v : undefined; }
  private savePref(key: string, value: string | number) {
    try { localStorage.setItem(PREFS, JSON.stringify({ ...this.prefs(), [key]: value })); } catch { /* storage unavailable */ }
  }
}
