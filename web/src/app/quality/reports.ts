import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { ApiService, saveBlob } from '../core/api.service';
import { FeatureSearchHit, Product, ProductRelease } from '../core/models';
import { Channel, QualityApi, ReportDefinition, ReportParameters, ReportResult, SavedReport } from '../core/quality';
import { UiService } from '../core/ui.service';

/** Reports: catalog, parameters, configurable columns, CSV/Excel/PDF export, saved filters and scheduled email delivery. */
@Component({
  selector: 'app-reports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, MatMenuModule, MatTooltipModule],
  template: `
    <div class="layout">
      <aside class="list">
        @for (g of groups(); track g[0]) {
          <h3>{{ g[0] }}</h3>
          @for (r of g[1]; track r.key) { <a class="item" [class.on]="def()?.key === r.key" (click)="select(r)">{{ r.title }}</a> }
        }
        <h3>Saved</h3>
        @for (s of saved(); track s.id) {
          <a class="item" [class.on]="savedId() === s.id" (click)="openSaved(s)"><mat-icon class="inline">{{ s.scheduleCron ? 'schedule_send' : 'bookmark' }}</mat-icon> {{ s.name }}
            <span class="muted small">{{ s.isMine ? '' : 'by ' + s.owner }}</span></a>
        } @empty { <p class="muted small">No saved reports.</p> }
      </aside>
      <section class="page">
        @if (def(); as d) {
          <div class="page-head"><h1>{{ d.title }}</h1><span class="spacer"></span>
            <button mat-stroked-button [matMenuTriggerFor]="cols"><mat-icon>view_column</mat-icon>Columns</button>
            <mat-menu #cols><div class="cols" (click)="$event.stopPropagation()">@for (c of d.columns; track c.key) {
              <mat-checkbox [checked]="columns().includes(c.key)" (change)="toggleColumn(c.key)">{{ c.label }}</mat-checkbox> }</div></mat-menu>
            <button mat-stroked-button [matMenuTriggerFor]="exp" [disabled]="!result()"><mat-icon>download</mat-icon>Export</button>
            <mat-menu #exp>@for (f of formats; track f[0]) { <button mat-menu-item (click)="export(f[0])">{{ f[1] }}</button> }</mat-menu>
            <button mat-stroked-button (click)="saveAs()"><mat-icon>bookmark_add</mat-icon>{{ savedId() ? 'Update saved' : 'Save' }}</button>
          </div>
          <p class="muted">{{ d.description }}</p>
          <div class="panel wrap-row params">
            @if (has('productId')) {
              <mat-form-field><mat-label>Product</mat-label><mat-select [(ngModel)]="p.productId" (ngModelChange)="productChanged()"><mat-option [value]="undefined">All products</mat-option>
                @for (x of products(); track x.id) { <mat-option [value]="x.id">{{ x.name }}</mat-option> }</mat-select></mat-form-field>
            }
            @if (has('featureId')) {
              <mat-form-field><mat-label>Feature (search, Enter)</mat-label><input matInput [(ngModel)]="featureText" (keyup.enter)="searchFeature()" /></mat-form-field>
              @if (hits().length) { <mat-form-field><mat-label>Feature</mat-label><mat-select [(ngModel)]="p.featureId">@for (h of hits(); track h.node.id) {
                <mat-option [value]="h.node.id">{{ h.node.sequenceDisplay }} {{ h.node.name }}</mat-option> }</mat-select></mat-form-field> }
            }
            @if (has('releaseId')) {
              <mat-form-field><mat-label>{{ d.key === 'features-between-releases' ? 'After release' : 'Release A' }}</mat-label><mat-select [(ngModel)]="p.releaseId">
                @for (r of releases(); track r.id) { <mat-option [value]="r.id">{{ r.versionLabel }}</mat-option> }</mat-select></mat-form-field>
            }
            @if (has('compareReleaseId')) {
              <mat-form-field><mat-label>{{ d.key === 'features-between-releases' ? 'Up to release' : 'Release B' }}</mat-label><mat-select [(ngModel)]="p.compareReleaseId">
                @for (r of releases(); track r.id) { <mat-option [value]="r.id">{{ r.versionLabel }}</mat-option> }</mat-select></mat-form-field>
            }
            @if (has('fromSequence')) { <mat-form-field><mat-label>From sequence</mat-label><input matInput type="number" [(ngModel)]="p.fromSequence" /></mat-form-field> }
            @if (has('days')) {
              <mat-form-field><mat-label>Period</mat-label><mat-select [(ngModel)]="p.days">@for (n of [1, 7, 30, 90, 180, 365]; track n) { <mat-option [value]="n">Last {{ n }} day(s)</mat-option> }</mat-select></mat-form-field>
            }
            <button mat-flat-button (click)="run()"><mat-icon>play_arrow</mat-icon>Run</button>
          </div>
          @if (result(); as r) {
            <div class="panel">
              <div class="wrap-row summary">@for (s of r.summary; track s.label) { <span><span class="muted">{{ s.label }}</span> <b>{{ s.value }}</b></span> }
                <span class="spacer"></span><span class="muted small">{{ r.parameterText }} · {{ r.generatedAt | date: 'medium' }}</span></div>
              <div class="scroll"><table class="data">
                <thead><tr>@for (c of r.columns; track c.key) { <th (click)="sortBy($index)" class="sortable">{{ c.label }}{{ sort()?.[0] === $index ? (sort()![1] ? ' ▲' : ' ▼') : '' }}</th> }</tr></thead>
                <tbody>@for (row of rows(); track $index) { <tr>@for (cell of row; track $index) { <td>{{ cell }}</td> }</tr> }
                  @empty { <tr><td [attr.colspan]="r.columns.length" class="empty">No rows.</td></tr> }</tbody>
              </table></div>
            </div>
            <div class="panel definition"><h3>Definition</h3><p>{{ r.definition }}</p></div>
          }
          @if (savedId(); as sid) {
            <div class="panel"><h3>Schedule</h3>
              <div class="wrap-row">
                <mat-form-field><mat-label>Cron (UTC)</mat-label><input matInput [(ngModel)]="schedule.cron" placeholder="0 7 * * 1" /></mat-form-field>
                <mat-form-field><mat-label>Email channel</mat-label><mat-select [(ngModel)]="schedule.channelId"><mat-option [value]="undefined">–</mat-option>
                  @for (c of emailChannels(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select></mat-form-field>
                <mat-form-field><mat-label>Format</mat-label><mat-select [(ngModel)]="schedule.format">@for (f of formats; track f[0]) { <mat-option [value]="f[0]">{{ f[1] }}</mat-option> }</mat-select></mat-form-field>
                <mat-checkbox [(ngModel)]="shared">Shared with everyone</mat-checkbox>
                <button mat-stroked-button (click)="saveSchedule()">Save schedule</button>
                @if (current()?.isMine) { <button mat-button (click)="deleteSaved(sid)">Delete saved report</button> }
              </div>
              @if (current(); as s) { <p class="muted small">{{ s.nextRunAt ? 'Next delivery ' + (s.nextRunAt | date: 'medium') : 'Not scheduled' }}{{ s.lastRunAt ? ' · last ' + (s.lastRunAt | date: 'medium') : '' }}</p> }
            </div>
          }
        } @else { <div class="empty">Choose a report.</div> }
      </section>
    </div>`,
  styles: `
    .layout { display: grid; grid-template-columns: 260px 1fr; height: 100%; }
    @media (max-width: 900px) { .layout { grid-template-columns: 1fr; height: auto; } .list { max-height: 220px; border-right: 0; border-bottom: 1px solid var(--border); } }
    .list { background: var(--surface); border-right: 1px solid var(--border); padding: 8px; overflow: auto; }
    .list h3 { margin: 12px 0 4px; } .item { display: block; padding: 4px 8px; border-radius: 4px; cursor: pointer; color: inherit; } .item:hover { background: var(--hover); } .item.on { background: var(--selected); }
    .params mat-form-field { width: 200px; } .summary { gap: 16px; margin-bottom: 8px; } .small { font-size: 11px; }
    .scroll { max-height: calc(100vh - 360px); overflow: auto; } th.sortable { cursor: pointer; user-select: none; }
    .cols { display: flex; flex-direction: column; padding: 8px 12px; } .definition p { margin: 0; color: var(--muted); }
  `,
})
export class Reports {
  private q = inject(QualityApi);
  private api = inject(ApiService);
  private ui = inject(UiService);
  catalog = signal<ReportDefinition[]>([]);
  saved = signal<SavedReport[]>([]);
  products = signal<Product[]>([]);
  releases = signal<ProductRelease[]>([]);
  channels = signal<Channel[]>([]);
  hits = signal<FeatureSearchHit[]>([]);
  def = signal<ReportDefinition | null>(null);
  result = signal<ReportResult | null>(null);
  columns = signal<string[]>([]);
  savedId = signal<string | null>(null);
  sort = signal<[number, boolean] | null>(null);
  p: ReportParameters = { days: 30 };
  featureText = '';
  shared = false;
  schedule: { cron?: string; channelId?: string; format: string } = { format: 'xlsx' };
  formats: [string, string][] = [['xlsx', 'Excel (.xlsx)'], ['csv', 'CSV'], ['pdf', 'PDF']];

  groups = computed(() => {
    const m = new Map<string, ReportDefinition[]>();
    for (const r of this.catalog()) m.set(r.category, [...(m.get(r.category) ?? []), r]);
    return [...m.entries()];
  });
  current = computed(() => this.saved().find(s => s.id === this.savedId()) ?? null);
  emailChannels = computed(() => this.channels().filter(c => c.type === 'Email'));
  rows = computed(() => {
    const r = this.result(); if (!r) return [];
    const s = this.sort(); if (!s) return r.rows;
    const [i, asc] = s;
    const num = (v: string | null) => v !== null && v !== '' && !isNaN(Number(v)) ? Number(v) : null;
    return [...r.rows].sort((a, b) => {
      const x = a[i], y = b[i];
      const nx = num(x), ny = num(y);
      const c = nx !== null && ny !== null ? nx - ny : (x ?? '').localeCompare(y ?? '');
      return asc ? c : -c;
    });
  });

  constructor() {
    this.q.reportCatalog().subscribe({ next: c => this.catalog.set(c), error: e => this.ui.error(e) });
    this.loadSaved();
    this.api.products().subscribe(p => this.products.set(p));
    this.q.channels().subscribe({ next: c => this.channels.set(c), error: () => { } });
  }

  loadSaved() { this.q.savedReports().subscribe(s => this.saved.set(s)); }
  has(param: string) { return this.def()?.parameters.includes(param) ?? false; }

  select(r: ReportDefinition) {
    this.def.set(r);
    this.savedId.set(null);
    this.result.set(null);
    this.sort.set(null);
    this.columns.set(r.columns.map(c => c.key));
  }

  openSaved(s: SavedReport) {
    const d = this.catalog().find(c => c.key === s.reportKey);
    if (!d) return;
    this.select(d);
    this.savedId.set(s.id);
    this.p = { ...s.parameters };
    this.shared = s.isShared;
    this.schedule = { cron: s.scheduleCron, channelId: s.deliveryChannelId, format: s.format };
    if (s.columns?.length) this.columns.set(s.columns);
    this.productChanged(false);
    this.run();
  }

  productChanged(reset = true) {
    if (reset) { this.p.releaseId = undefined; this.p.compareReleaseId = undefined; }
    if (this.p.productId) this.api.releases(this.p.productId).subscribe(r => this.releases.set(r)); else this.releases.set([]);
  }

  searchFeature() {
    this.api.searchFeatures({ productId: this.p.productId, text: this.featureText }, 1, 30).subscribe(r => { this.hits.set(r.items); this.p.featureId = r.items[0]?.node.id; });
  }

  toggleColumn(key: string) {
    const all = this.def()!.columns.map(c => c.key);
    this.columns.update(c => c.includes(key) ? c.filter(x => x !== key) : all.filter(x => x === key || c.includes(x)));
  }

  async run() {
    this.sort.set(null);
    try { this.result.set(await firstValueFrom(this.q.runReport(this.def()!.key, this.p, this.columns()))); } catch (e) { this.ui.error(e); }
  }

  sortBy(i: number) { const s = this.sort(); this.sort.set(s?.[0] === i ? [i, !s[1]] : [i, true]); }

  async export(format: string) {
    const d = this.def()!;
    try { saveBlob(await firstValueFrom(this.q.exportReport(d.key, this.p, this.columns(), format)), `${d.key}.${format}`); } catch (e) { this.ui.error(e); }
  }

  async saveAs() {
    const current = this.current();
    const name = current?.name ?? await this.ui.prompt({ title: 'Save report', label: 'Name', required: true, value: this.def()!.title });
    if (!name) return;
    await this.persist(name, current?.scheduleCron, current?.deliveryChannelId, current?.format ?? 'xlsx');
  }

  async saveSchedule() {
    await this.persist(this.current()!.name, this.schedule.cron || undefined, this.schedule.cron ? this.schedule.channelId : undefined, this.schedule.format);
  }

  private async persist(name: string, scheduleCron: string | undefined, deliveryChannelId: string | undefined, format: string) {
    try {
      const s = await firstValueFrom(this.q.saveReport(this.savedId(), { name, reportKey: this.def()!.key, parameters: this.p, columns: this.columns(), isShared: this.shared,
        scheduleCron, deliveryChannelId, format, version: this.current()?.version }));
      this.savedId.set(s.id);
      this.ui.ok('Saved');
      this.loadSaved();
    } catch (e) { this.ui.error(e); }
  }

  async deleteSaved(id: string) {
    if (!await this.ui.confirm({ title: 'Delete saved report', message: 'Its schedule stops too.', danger: true, confirmText: 'Delete' })) return;
    try { await firstValueFrom(this.q.deleteSavedReport(id)); this.savedId.set(null); this.loadSaved(); } catch (e) { this.ui.error(e); }
  }
}
