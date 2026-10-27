import { ScrollingModule, CdkVirtualScrollViewport } from '@angular/cdk/scrolling';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule, MatMenuTrigger } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subject, debounceTime, firstValueFrom } from 'rxjs';
import { ApiService, saveBlob } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { FeatureFilter, FeatureNode, FeatureSearchHit, Product } from '../core/models';
import { ChangesService, UiService } from '../core/ui.service';
import { FeaturePickerDialog } from '../shared/dialogs';
import { CreateFeatureDialog, CreateFeatureResult } from './feature-dialogs';

const ROOT = '__root__';
interface Row { node: FeatureNode; depth: number; }
type DropZone = 'before' | 'into';

/**
 * Feature explorer. Children load lazily per node; rows render through a virtual scroller so very large trees stay
 * responsive. Any filter switches to a flat, paged result list with breadcrumbs.
 */
@Component({
  selector: 'app-feature-tree',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ScrollingModule, FormsModule, MatButtonModule, MatIconModule, MatMenuModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatCheckboxModule, MatTooltipModule, MatDividerModule, MatProgressBarModule],
  templateUrl: './feature-tree.html',
  styleUrl: './feature-tree.scss',
})
export class FeatureTree {
  private api = inject(ApiService);
  private dialog = inject(MatDialog);
  ui = inject(UiService);
  auth = inject(AuthService);

  selectedId = input<string | undefined>();
  productId = input<string | undefined>();
  selected = output<string>();
  productChange = output<string>();

  products = signal<Product[]>([]);
  product = computed(() => this.products().find(p => p.id === this.productId()));
  private children = signal(new Map<string, FeatureNode[]>());
  private expanded = signal(new Set<string>());
  private loadingIds = signal(new Set<string>());
  focusIndex = signal(0);
  dropTarget = signal<{ id: string; zone: DropZone } | null>(null);
  private dragId?: string;

  // Filters
  filter = signal<FeatureFilter>({});
  text = signal('');
  hits = signal<FeatureSearchHit[]>([]);
  hitTotal = signal(0);
  searching = signal(false);
  filtering = computed(() => !!this.text().trim() || Object.values(this.filter()).some(v => v !== undefined && v !== false && v !== ''));
  private search$ = new Subject<void>();

  trackRow = (_: number, r: Row) => r.node.id;

  rows = computed<Row[]>(() => {
    const out: Row[] = [];
    const walk = (parent: string, depth: number) => {
      for (const n of this.children().get(parent) ?? []) {
        out.push({ node: n, depth });
        if (this.expanded().has(n.id)) walk(n.id, depth + 1);
      }
    };
    walk(ROOT, 0);
    return out;
  });

  private viewport = viewChild<CdkVirtualScrollViewport>('viewport');
  private contextTrigger = viewChild<MatMenuTrigger>('contextTrigger');
  contextPos = signal({ x: 0, y: 0 });
  contextNode = signal<FeatureNode | null>(null);

  constructor() {
    this.api.products().subscribe({ next: p => this.products.set(p), error: e => this.ui.error(e) });

    effect(() => {
      const pid = this.productId();
      if (!pid) return;
      untracked(() => {
        this.children.set(new Map());
        this.expanded.set(new Set());
        this.load(undefined);
        if (this.filtering()) this.search$.next();
      });
    });

    this.search$.pipe(debounceTime(250), takeUntilDestroyed()).subscribe(() => this.runSearch());

    // Live refresh when anyone changes features in this product.
    inject(ChangesService).events.pipe(takeUntilDestroyed()).subscribe(e => {
      if (e.topic === 'featuresChanged' && e.payload['productId'] === this.productId()) this.refreshLoaded();
      if (e.topic === 'productsChanged') this.api.products().subscribe(p => this.products.set(p));
    });
    inject(DestroyRef).onDestroy(() => this.search$.complete());
  }

  // ---- loading ----

  private async load(parentId: string | undefined) {
    const pid = this.productId();
    if (!pid) return;
    const key = parentId ?? ROOT;
    this.loadingIds.update(s => new Set(s).add(key));
    try {
      const nodes = await firstValueFrom(this.api.tree(pid, parentId));
      this.children.update(m => new Map(m).set(key, nodes));
    } catch (e) { this.ui.error(e); }
    finally { this.loadingIds.update(s => { const n = new Set(s); n.delete(key); return n; }); }
  }

  isLoading(id: string) { return this.loadingIds().has(id); }
  isExpanded(id: string) { return this.expanded().has(id); }

  async refreshLoaded() {
    const keys = [...this.children().keys()];
    await Promise.all(keys.map(k => this.load(k === ROOT ? undefined : k)));
    if (this.filtering()) this.runSearch();
  }

  async toggle(node: FeatureNode, force?: boolean) {
    const open = force ?? !this.isExpanded(node.id);
    this.expanded.update(s => { const n = new Set(s); if (open) n.add(node.id); else n.delete(node.id); return n; });
    if (open && !this.children().has(node.id)) await this.load(node.id);
  }

  /** Expands every loaded branch down to the given depth (0 = top level only), loading lazily as it goes. */
  async expandToLevel(level: number) {
    this.expanded.set(new Set());
    let frontier = this.children().get(ROOT) ?? [];
    for (let depth = 0; depth < level; depth++) {
      const parents = frontier.filter(n => n.hasChildren);
      await Promise.all(parents.filter(n => !this.children().has(n.id)).map(n => this.load(n.id)));
      this.expanded.update(s => { const n = new Set(s); parents.forEach(p => n.add(p.id)); return n; });
      frontier = parents.flatMap(p => this.children().get(p.id) ?? []);
    }
  }

  collapseAll() { this.expanded.set(new Set()); }

  /** Loads and expands the ancestors of a feature so it is visible in the tree, then scrolls to it. */
  async reveal(featureId: string) {
    const chain = await firstValueFrom(this.api.ancestors(featureId));
    for (const a of chain.slice(0, -1)) {
      if (!this.children().has(a.id)) await this.load(a.id);
      this.expanded.update(s => new Set(s).add(a.id));
    }
    const idx = this.rows().findIndex(r => r.node.id === featureId);
    if (idx >= 0) { this.focusIndex.set(idx); this.viewport()?.scrollToIndex(Math.max(0, idx - 5)); }
  }

  // ---- selection & keyboard ----

  select(node: FeatureNode, index?: number) {
    if (index !== undefined) this.focusIndex.set(index);
    this.selected.emit(node.id);
  }

  onKey(e: KeyboardEvent) {
    const rows = this.rows();
    if (!rows.length) return;
    const i = Math.min(this.focusIndex(), rows.length - 1);
    const row = rows[i];
    const move = (to: number) => { e.preventDefault(); const t = Math.max(0, Math.min(rows.length - 1, to)); this.focusIndex.set(t); this.viewport()?.scrollToIndex(Math.max(0, t - 5)); };
    switch (e.key) {
      case 'ArrowDown': move(i + 1); break;
      case 'ArrowUp': move(i - 1); break;
      case 'Home': move(0); break;
      case 'End': move(rows.length - 1); break;
      case 'ArrowRight':
        e.preventDefault();
        if (row.node.hasChildren && !this.isExpanded(row.node.id)) this.toggle(row.node, true); else if (row.node.hasChildren) move(i + 1);
        break;
      case 'ArrowLeft':
        e.preventDefault();
        if (this.isExpanded(row.node.id)) this.toggle(row.node, false);
        else { const p = rows.findIndex(r => r.node.id === row.node.parentId); if (p >= 0) move(p); }
        break;
      case 'Enter': case ' ': e.preventDefault(); this.select(row.node); break;
    }
  }

  // ---- filters ----

  setFilter(patch: Partial<FeatureFilter>) { this.filter.update(f => ({ ...f, ...patch })); this.search$.next(); }
  onText(v: string) { this.text.set(v); this.search$.next(); }
  clearFilters() { this.filter.set({}); this.text.set(''); this.hits.set([]); }

  private async runSearch() {
    if (!this.filtering() || !this.productId()) { this.hits.set([]); return; }
    this.searching.set(true);
    try {
      const r = await firstValueFrom(this.api.searchFeatures({ ...this.filter(), text: this.text().trim() || undefined, productId: this.productId() }, 1, 500));
      this.hits.set(r.items);
      this.hitTotal.set(r.total);
    } catch (e) { this.ui.error(e); }
    finally { this.searching.set(false); }
  }

  crumbs(h: FeatureSearchHit) { return h.ancestors.slice(0, -1).map(a => a.name).join(' › '); }

  async showInTree(h: FeatureSearchHit) {
    this.clearFilters();
    this.select(h.node);
    await this.reveal(h.node.id);
  }

  // ---- context menu & commands ----

  openContext(e: MouseEvent, node: FeatureNode | null) {
    e.preventDefault();
    this.contextNode.set(node);
    this.contextPos.set({ x: e.clientX, y: e.clientY });
    this.contextTrigger()?.openMenu();
  }

  async create(parent: FeatureNode | null) {
    const product = this.product();
    if (!product) return;
    const parentType = parent?.featureTypeCode;
    const suggested = !parent ? 'MODULE' : parentType === 'MODULE' ? 'SUBMODULE' : parentType === 'SUBMODULE' ? 'FEATURE' : parentType === 'FEATURE' ? 'SUBFEATURE' : 'OPERATION';
    const result = await firstValueFrom(this.dialog.open(CreateFeatureDialog, { width: '620px', data: { parentName: parent?.name, productName: product.name, suggestedType: suggested } }).afterClosed()) as CreateFeatureResult | undefined;
    if (!result) return;
    try {
      const f = await firstValueFrom(this.api.createFeature(product.id, parent?.id, result.fields, result.legacySequenceNumber));
      this.ui.ok(`Created [${f.sequenceDisplay}] ${f.fields.name}`);
      await this.load(parent?.id);
      if (parent) await this.toggle(parent, true);
      this.select(this.rows().find(r => r.node.id === f.id)?.node ?? ({ id: f.id } as FeatureNode));
      await this.reveal(f.id);
    } catch (e) { this.ui.error(e); }
  }

  async duplicate(node: FeatureNode) {
    const name = await this.ui.prompt({ title: 'Duplicate feature', label: 'Name of the copy', value: `${node.name} (copy)`, hint: 'The copy gets a new sequence number; requirements are copied.' });
    if (!name) return;
    try {
      const f = await firstValueFrom(this.api.duplicateFeature(node.id, name));
      this.ui.ok(`Created [${f.sequenceDisplay}] ${f.fields.name}`);
      await this.load(node.parentId);
      this.select({ id: f.id } as FeatureNode);
    } catch (e) { this.ui.error(e); }
  }

  async move(node: FeatureNode) {
    const target = await firstValueFrom(this.dialog.open(FeaturePickerDialog, { width: '640px', data: { title: `Move "${node.name}" under…`, productId: node.productId, excludeId: node.id } }).afterClosed()) as FeatureSearchHit | undefined;
    if (!target) return;
    await this.doMove(node, target.node.id, undefined);
  }

  async moveToRoot(node: FeatureNode) { await this.doMove(node, undefined, undefined); }

  private async doMove(node: FeatureNode, newParentId: string | undefined, position: number | undefined) {
    try {
      const detail = await firstValueFrom(this.api.feature(node.id));
      await firstValueFrom(this.api.moveFeature(node.id, { newParentId, position, version: detail.version }));
      this.ui.ok(`Moved [${node.sequenceDisplay}] — sequence number unchanged`);
      await this.refreshLoaded();
      if (newParentId) { const p = this.findNode(newParentId); if (p) await this.toggle(p, true); }
    } catch (e) { this.ui.error(e); }
  }

  async deprecate(node: FeatureNode) {
    const note = await this.ui.prompt({ title: `Deprecate [${node.sequenceDisplay}] ${node.name}`, label: 'Reason / replacement note', multiline: true, required: false, confirmText: 'Deprecate' });
    if (note === undefined) return;
    try { await firstValueFrom(this.api.deprecateFeature(node.id, undefined, note)); this.ui.ok('Feature deprecated'); await this.refreshLoaded(); }
    catch (e) { this.ui.error(e); }
  }

  async remove(node: FeatureNode) {
    const ok = await this.ui.confirm({ title: 'Delete feature', danger: true, confirmText: 'Delete',
      message: `Delete [${node.sequenceDisplay}] ${node.name}${node.hasChildren ? ' and everything under it' : ''}?\n\nThe record is kept for audit and its sequence number is never reused. Prefer "Deprecate" for features that still exist in older releases.` });
    if (!ok) return;
    try { const r = await firstValueFrom(this.api.deleteFeature(node.id)); this.ui.ok(`Deleted ${r.deleted} feature(s)`); await this.load(node.parentId); }
    catch (e) { this.ui.error(e); }
  }

  copySequence(node: FeatureNode) { navigator.clipboard?.writeText(node.sequenceDisplay); this.ui.ok(`Copied ${node.sequenceDisplay}`); }

  // ---- drag and drop (display order only; sequence numbers never change) ----

  dragStart(e: DragEvent, node: FeatureNode) {
    if (!this.auth.can('features.edit')) { e.preventDefault(); return; }
    this.dragId = node.id;
    e.dataTransfer?.setData('text/plain', node.id);
    if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move';
  }

  dragOver(e: DragEvent, node: FeatureNode) {
    if (!this.dragId || this.dragId === node.id) return;
    e.preventDefault();
    const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
    this.dropTarget.set({ id: node.id, zone: e.clientY - rect.top < rect.height / 2 ? 'before' : 'into' });
  }

  dragEnd() { this.dragId = undefined; this.dropTarget.set(null); }

  async drop(e: DragEvent, target: FeatureNode) {
    e.preventDefault();
    const zone = this.dropTarget()?.zone;
    const dragged = this.dragId ? this.findNode(this.dragId) : undefined;
    this.dragEnd();
    if (!dragged || dragged.id === target.id || !zone) return;

    if (zone === 'before' && dragged.parentId === target.parentId) {
      const siblings = (this.children().get(target.parentId ?? ROOT) ?? []).map(n => n.id).filter(id => id !== dragged.id);
      siblings.splice(siblings.indexOf(target.id), 0, dragged.id);
      try { await firstValueFrom(this.api.reorder(dragged.productId, dragged.parentId, siblings)); await this.load(dragged.parentId); }
      catch (err) { this.ui.error(err); }
      return;
    }
    if (zone === 'before') {
      const siblings = this.children().get(target.parentId ?? ROOT) ?? [];
      await this.doMove(dragged, target.parentId, siblings.findIndex(s => s.id === target.id));
    } else {
      await this.doMove(dragged, target.id, undefined);
    }
  }

  private findNode(id: string) {
    for (const list of this.children().values()) { const n = list.find(x => x.id === id); if (n) return n; }
    return undefined;
  }

  // ---- import / export ----

  async export(format: 'csv' | 'xlsx') {
    const p = this.product();
    if (!p) return;
    try { saveBlob(await firstValueFrom(this.api.exportFeatures(p.id, format)), `${p.code}-features.${format}`); }
    catch (e) { this.ui.error(e); }
  }

  async import(input: HTMLInputElement) {
    const file = input.files?.[0];
    input.value = '';
    const p = this.product();
    if (!file || !p) return;
    try {
      const r = await firstValueFrom(this.api.importFeatures(p.id, file));
      if (r.errors.length) await this.ui.confirm({ title: 'Import rejected', message: r.errors.join('\n'), confirmText: 'OK' });
      else { this.ui.ok(`Imported ${r.created} feature(s), skipped ${r.skipped} existing`); await this.refreshLoaded(); }
    } catch (e) { this.ui.error(e); }
  }

  coverageTip(n: FeatureNode) {
    return n.testCaseCount === 0 ? 'No test cases' : `${n.testCaseCount} test case(s), ${n.automatedTestCount} automated`;
  }
}
