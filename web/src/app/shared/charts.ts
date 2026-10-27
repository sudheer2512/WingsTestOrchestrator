import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

export interface ChartSeries { key: string; label: string; color: string; }
export interface ChartPoint { label: string; values: Record<string, number>; }

/**
 * Outcome colours for stacked bars. Passed is blue rather than green: red/green adjacent in a stack fails colour-blind
 * separation (validated: blue/red/amber passes CVD and normal-vision checks). "Other" is neutral and always labelled.
 */
export const STATUS = { passed: '#2a78d6', failed: '#d03b3b', blocked: '#eda100', other: '#a3a8b0' };
export const SERIES = ['#2a78d6', '#eb6834'];

const W = 640, H = 180, PAD = { l: 34, r: 8, t: 8, b: 22 };

function niceMax(v: number) {
  if (v <= 0) return 4;
  const p = 10 ** Math.floor(Math.log10(v));
  return [1, 2, 2.5, 5, 10].map(m => m * p).find(m => m >= v)!;
}

/**
 * Stacked bar chart. Thin bars with 2px surface gaps between segments, rounded top ends, a recessive grid, legend,
 * per-bar hover tooltip and a table view for accessibility.
 */
@Component({
  selector: 'app-bar-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="chart">
      <div class="legend">
        @for (s of series(); track s.key) { <span class="key"><i [style.background]="s.color"></i>{{ s.label }}</span> }
        <span class="spacer"></span><button class="link" (click)="table.set(!table())">{{ table() ? 'Chart' : 'Table' }}</button>
      </div>
      @if (!table()) {
        <div class="plot">
          <svg [attr.viewBox]="'0 0 ' + w + ' ' + h" preserveAspectRatio="none" role="img" [attr.aria-label]="ariaLabel()">
            @for (t of ticks(); track t) {
              <line [attr.x1]="pad.l" [attr.x2]="w - pad.r" [attr.y1]="y(t)" [attr.y2]="y(t)" class="grid" />
              <text [attr.x]="pad.l - 4" [attr.y]="y(t) + 3" class="axis" text-anchor="end">{{ t }}</text>
            }
            @for (b of bars(); track $index) {
              @for (seg of b.segments; track seg.key) {
                <path [attr.d]="seg.d" [attr.fill]="seg.color" />
              }
              @if (b.showLabel) { <text [attr.x]="b.cx" [attr.y]="h - 6" class="axis" text-anchor="middle">{{ b.label }}</text> }
              <rect [attr.x]="b.x - b.gap / 2" [attr.y]="pad.t" [attr.width]="b.bw + b.gap" [attr.height]="h - pad.t - pad.b" fill="transparent"
                (mouseenter)="hover.set($index)" (mouseleave)="hover.set(null)" />
            }
          </svg>
          @if (hover() !== null && points()[hover()!]; as p) {
            <div class="tip" [style.left.%]="(bars()[hover()!].cx / w) * 100">
              <b>{{ p.label }}</b>
              @for (s of series(); track s.key) { <div><i [style.background]="s.color"></i>{{ s.label }} <span>{{ p.values[s.key] ?? 0 }}</span></div> }
            </div>
          }
        </div>
      } @else {
        <table class="data compact"><thead><tr><th></th>@for (s of series(); track s.key) { <th>{{ s.label }}</th> }</tr></thead>
          <tbody>@for (p of points(); track $index) { <tr><td>{{ p.label }}</td>@for (s of series(); track s.key) { <td>{{ p.values[s.key] ?? 0 }}</td> }</tr> }</tbody></table>
      }
    </div>`,
  styles: `
    .chart { position: relative; } .plot { position: relative; }
    svg { width: 100%; height: 180px; display: block; overflow: visible; }
    .grid { stroke: var(--border); stroke-width: 1; } .axis { font-size: 10px; fill: var(--muted); }
    .legend { display: flex; gap: 12px; align-items: center; font-size: 12px; color: var(--muted); margin-bottom: 4px; }
    .key { display: inline-flex; align-items: center; gap: 4px; } i { display: inline-block; width: 10px; height: 10px; border-radius: 2px; }
    .link { border: 0; background: none; color: var(--accent); cursor: pointer; font-size: 12px; }
    .tip { position: absolute; top: 0; transform: translateX(-50%); background: var(--surface); border: 1px solid var(--border); border-radius: 6px; padding: 6px 8px;
      font-size: 12px; box-shadow: 0 4px 12px rgba(0,0,0,.12); pointer-events: none; white-space: nowrap; z-index: 2; }
    .tip div { display: flex; align-items: center; gap: 6px; } .tip span { margin-left: auto; padding-left: 12px; font-variant-numeric: tabular-nums; }
    table.compact td, table.compact th { padding: 2px 6px; font-size: 12px; }
  `,
})
export class BarChart {
  series = input.required<ChartSeries[]>();
  points = input.required<ChartPoint[]>();
  ariaLabel = input('Bar chart');
  hover = signal<number | null>(null);
  table = signal(false);
  w = W; h = H; pad = PAD;

  private max = computed(() => niceMax(Math.max(0, ...this.points().map(p => this.series().reduce((s, x) => s + (p.values[x.key] ?? 0), 0)))));
  ticks = computed(() => { const m = this.max(); return [0, m / 2, m].map(v => Math.round(v * 10) / 10); });
  y = (v: number) => H - PAD.b - (v / this.max()) * (H - PAD.t - PAD.b);

  bars = computed(() => {
    const n = Math.max(1, this.points().length);
    const slot = (W - PAD.l - PAD.r) / n;
    const bw = Math.max(3, Math.min(24, slot * 0.6));
    const every = Math.ceil(n / 8);
    return this.points().map((p, i) => {
      const x = PAD.l + i * slot + (slot - bw) / 2;
      let base = 0;
      const nonZero = this.series().filter(s => (p.values[s.key] ?? 0) > 0);
      const segments = nonZero.map((s, j) => {
        const v = p.values[s.key];
        const y0 = this.y(base), y1 = this.y(base + v);
        base += v;
        const top = j === nonZero.length - 1;
        // 2px surface gap between stacked segments; only the outermost end is rounded.
        const yTop = y1 + (top ? 0 : 1), yBottom = y0 - (j === 0 ? 0 : 1);
        const r = top ? Math.min(4, bw / 2, Math.max(0, yBottom - yTop)) : 0;
        const d = `M${x},${yBottom} V${yTop + r} Q${x},${yTop} ${x + r},${yTop} H${x + bw - r} Q${x + bw},${yTop} ${x + bw},${yTop + r} V${yBottom} Z`;
        return { key: s.key, color: s.color, d };
      });
      return { x, bw, gap: slot - bw, cx: x + bw / 2, segments, label: p.label, showLabel: (n - 1 - i) % every === 0 }; // anchored on the latest point so the last labels never collide
    });
  });
}

/** Two-series line chart (2px lines, end markers, crosshair tooltip). One y-axis: both series share a unit. */
@Component({
  selector: 'app-line-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="legend">
      @for (s of series(); track s.key) { <span class="key"><i [style.background]="s.color"></i>{{ s.label }}</span> }
      <span class="spacer"></span><button class="link" (click)="table.set(!table())">{{ table() ? 'Chart' : 'Table' }}</button>
    </div>
    @if (!table()) {
      <div class="plot">
        <svg [attr.viewBox]="'0 0 ' + w + ' ' + h" preserveAspectRatio="none" role="img" [attr.aria-label]="ariaLabel()" (mousemove)="move($event)" (mouseleave)="hover.set(null)">
          @for (t of ticks(); track t) {
            <line [attr.x1]="pad.l" [attr.x2]="w - pad.r" [attr.y1]="y(t)" [attr.y2]="y(t)" class="grid" />
            <text [attr.x]="pad.l - 4" [attr.y]="y(t) + 3" class="axis" text-anchor="end">{{ t }}</text>
          }
          @for (p of points(); track $index) { @if ((points().length - 1 - $index) % every() === 0) {
            <text [attr.x]="x($index)" [attr.y]="h - 6" class="axis" text-anchor="middle">{{ p.label }}</text> } }
          @if (hover() !== null) { <line [attr.x1]="x(hover()!)" [attr.x2]="x(hover()!)" [attr.y1]="pad.t" [attr.y2]="h - pad.b" class="cross" /> }
          @for (s of series(); track s.key) {
            <path [attr.d]="path(s.key)" fill="none" [attr.stroke]="s.color" stroke-width="2" vector-effect="non-scaling-stroke" stroke-linejoin="round" />
            @if (points().length) { <circle [attr.cx]="x(points().length - 1)" [attr.cy]="y(points()[points().length - 1].values[s.key] ?? 0)" r="4" [attr.fill]="s.color" stroke="var(--surface)" stroke-width="2" /> }
          }
        </svg>
        @if (hover() !== null && points()[hover()!]; as p) {
          <div class="tip" [style.left.%]="(x(hover()!) / w) * 100"><b>{{ p.label }}</b>
            @for (s of series(); track s.key) { <div><i [style.background]="s.color"></i>{{ s.label }} <span>{{ p.values[s.key] ?? 0 }}</span></div> }</div>
        }
      </div>
    } @else {
      <table class="data compact"><thead><tr><th></th>@for (s of series(); track s.key) { <th>{{ s.label }}</th> }</tr></thead>
        <tbody>@for (p of points(); track $index) { <tr><td>{{ p.label }}</td>@for (s of series(); track s.key) { <td>{{ p.values[s.key] ?? 0 }}</td> }</tr> }</tbody></table>
    }`,
  styles: `
    :host { display: block; } .plot { position: relative; }
    svg { width: 100%; height: 180px; display: block; overflow: visible; }
    .grid { stroke: var(--border); } .cross { stroke: var(--muted); stroke-dasharray: 3 3; } .axis { font-size: 10px; fill: var(--muted); }
    .legend { display: flex; gap: 12px; align-items: center; font-size: 12px; color: var(--muted); margin-bottom: 4px; }
    .key { display: inline-flex; align-items: center; gap: 4px; } i { display: inline-block; width: 10px; height: 10px; border-radius: 2px; }
    .link { border: 0; background: none; color: var(--accent); cursor: pointer; font-size: 12px; }
    .tip { position: absolute; top: 0; transform: translateX(-50%); background: var(--surface); border: 1px solid var(--border); border-radius: 6px; padding: 6px 8px;
      font-size: 12px; box-shadow: 0 4px 12px rgba(0,0,0,.12); pointer-events: none; white-space: nowrap; }
    .tip div { display: flex; align-items: center; gap: 6px; } .tip span { margin-left: auto; padding-left: 12px; }
    table.compact td, table.compact th { padding: 2px 6px; font-size: 12px; }
  `,
})
export class LineChart {
  series = input.required<ChartSeries[]>();
  points = input.required<ChartPoint[]>();
  ariaLabel = input('Line chart');
  hover = signal<number | null>(null);
  table = signal(false);
  w = W; h = H; pad = PAD;

  private max = computed(() => niceMax(Math.max(0, ...this.points().flatMap(p => this.series().map(s => p.values[s.key] ?? 0)))));
  ticks = computed(() => { const m = this.max(); return [0, m / 2, m].map(v => Math.round(v * 10) / 10); });
  every = computed(() => Math.ceil(this.points().length / 8));
  x = (i: number) => PAD.l + (this.points().length <= 1 ? 0 : (i / (this.points().length - 1)) * (W - PAD.l - PAD.r));
  y = (v: number) => H - PAD.b - (v / this.max()) * (H - PAD.t - PAD.b);
  path(key: string) { return this.points().map((p, i) => `${i ? 'L' : 'M'}${this.x(i)},${this.y(p.values[key] ?? 0)}`).join(' '); }

  move(e: MouseEvent) {
    const svg = e.currentTarget as SVGElement;
    const rect = svg.getBoundingClientRect();
    const vx = ((e.clientX - rect.left) / rect.width) * W;
    const n = this.points().length;
    if (n === 0) return;
    this.hover.set(Math.max(0, Math.min(n - 1, Math.round(((vx - PAD.l) / (W - PAD.l - PAD.r)) * (n - 1)))));
  }
}
