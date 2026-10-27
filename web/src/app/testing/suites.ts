import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList, moveItemInArray } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Product, SuiteDetail, SuiteSummary, TestCaseSummary } from '../core/models';
import { UiService } from '../core/ui.service';
import { TestCasePickerDialog } from '../shared/dialogs';
import { RunLauncher } from '../execution/start-run';
import { HumanizePipe, StatusPill } from '../shared/display';

/** Test suite builder: reusable, ordered collections of test cases. */
@Component({
  selector: 'app-suites',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, CdkDropList, CdkDrag, CdkDragHandle, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, StatusPill, HumanizePipe],
  template: `
    <div class="layout">
      <aside class="list">
        <div class="row head"><h2>Test suites</h2><span class="spacer"></span>
          @if (canEdit()) { <button mat-icon-button (click)="create()"><mat-icon>add</mat-icon></button> }</div>
        <input class="cell full" placeholder="Filter suites" [(ngModel)]="text" (ngModelChange)="loadList()" />
        @for (s of suites(); track s.id) {
          <a class="suite" [class.on]="s.id === id()" [routerLink]="['/suites', s.id]">
            <span class="num">{{ s.numberDisplay }}</span><span class="name">{{ s.name }}</span>
            <span class="muted small">{{ s.itemCount }} cases · {{ s.automatedCount }} automated</span>
          </a>
        } @empty { <p class="muted">No suites yet.</p> }
      </aside>
      <section class="page">
        @if (detail(); as d) {
          <div class="page-head">
            <span class="num">{{ d.suite.numberDisplay }}</span>
            <input class="title cell" [(ngModel)]="draft.name" [readonly]="!canEdit()" />
            <span class="spacer"></span>
            @if (canEdit()) {
            @if (canRun() && d.items.length) { <button mat-flat-button (click)="execute()"><mat-icon>play_arrow</mat-icon>Execute suite</button> }
              <button mat-flat-button (click)="saveMeta()"><mat-icon>save</mat-icon>Save</button>
              <button mat-stroked-button (click)="addCases()"><mat-icon>playlist_add</mat-icon>Add test cases</button>
              <button mat-icon-button (click)="remove()"><mat-icon>delete</mat-icon></button>
            }
          </div>
          <div class="panel grid-3">
            <mat-form-field><mat-label>Product</mat-label><mat-select [(ngModel)]="draft.productId" [disabled]="!canEdit()">
              <mat-option [value]="undefined">Cross-product</mat-option>@for (p of products(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field><mat-label>Tags</mat-label><input matInput [(ngModel)]="draft.tags" [readonly]="!canEdit()" /></mat-form-field>
            <mat-form-field><mat-label>Owner</mat-label><input matInput [(ngModel)]="draft.ownerUserName" [readonly]="!canEdit()" /></mat-form-field>
            <mat-form-field class="full span"><mat-label>Description</mat-label><textarea matInput rows="2" [(ngModel)]="draft.description" [readonly]="!canEdit()"></textarea></mat-form-field>
          </div>
          <div class="panel">
            <div class="row"><h3>{{ d.items.length }} test cases</h3><span class="muted">Drag to set the run order. Declared test-case dependencies still take precedence at execution.</span></div>
            <div cdkDropList (cdkDropListDropped)="drop($event)">
              @for (i of d.items; track i.itemId; let n = $index) {
                <div class="item" cdkDrag [cdkDragDisabled]="!canEdit()">
                  <mat-icon cdkDragHandle class="handle">drag_indicator</mat-icon><span class="muted">{{ n + 1 }}</span>
                  <span class="num">{{ i.testCase.numberDisplay }}</span><a [routerLink]="['/test-cases', i.testCase.id]" class="name">{{ i.testCase.name }}</a>
                  <app-status [value]="i.testCase.automationStatus" [label]="i.testCase.automationStatus | humanize" />
                  <app-status [value]="i.testCase.reviewStatus" [label]="i.testCase.reviewStatus | humanize" />
                  @if (canEdit()) { <button mat-icon-button (click)="removeItem(i.itemId)"><mat-icon>close</mat-icon></button> }
                </div>
              } @empty { <p class="empty">Empty suite. Add test cases to build it.</p> }
            </div>
          </div>
        } @else { <div class="empty">Select or create a suite.</div> }
      </section>
    </div>`,
  styles: `
    .layout { display: grid; grid-template-columns: 300px 1fr; height: 100%; }
    .list { background: var(--surface); border-right: 1px solid var(--border); padding: 8px; overflow: auto; }
    .head { margin-bottom: 6px; }
    .suite { display: grid; grid-template-columns: auto 1fr; gap: 0 8px; padding: 6px 8px; border-radius: 4px; text-decoration: none; color: var(--text); }
    .suite .small { grid-column: 2; font-size: 11px; }
    .suite:hover { background: var(--hover); } .suite.on { background: var(--selected); }
    .num { font-family: var(--mono); color: var(--muted); }
    .cell { font: inherit; padding: 5px 7px; border: 1px solid var(--border); border-radius: 4px; margin-bottom: 8px; }
    .title { font-size: 16px; font-weight: 600; flex: 1; min-width: 240px; margin: 0; }
    .span { grid-column: 1 / -1; }
    .item { display: flex; align-items: center; gap: 8px; padding: 4px; border-bottom: 1px solid var(--border); background: var(--surface); }
    .item .name { flex: 1; }
    .handle { cursor: grab; color: var(--muted); }
  `,
})
export class Suites {
  private api = inject(ApiService);
  private dialog = inject(MatDialog);
  private router = inject(Router);
  private ui = inject(UiService);
  private auth = inject(AuthService);
  id = input<string | undefined>();
  suites = signal<SuiteSummary[]>([]);
  products = signal<Product[]>([]);
  detail = signal<SuiteDetail | null>(null);
  draft: Partial<SuiteSummary> = {};
  text = '';
  canEdit = () => this.auth.can('testcases.edit');
  canRun = () => this.auth.can('tests.execute');
  private launcher = inject(RunLauncher);

  execute() {
    const d = this.detail()!;
    this.launcher.launch({ title: `Execute ${d.suite.name}`, scope: { suiteId: d.suite.id }, description: `${d.suite.numberDisplay} ${d.suite.name}: ${d.items.length} test case(s), ${d.suite.automatedCount} automated.` });
  }

  constructor() {
    this.loadList();
    this.api.products().subscribe(p => this.products.set(p));
    effect(() => { const id = this.id(); untracked(() => id ? this.load(id) : this.detail.set(null)); });
  }

  loadList() { this.api.suites(undefined, this.text || undefined).subscribe({ next: s => this.suites.set(s), error: e => this.ui.error(e) }); }

  async load(id: string) {
    try { const d = await firstValueFrom(this.api.suite(id)); this.detail.set(d); this.draft = { ...d.suite }; } catch (e) { this.ui.error(e); }
  }

  async create() {
    const name = await this.ui.prompt({ title: 'New test suite', label: 'Suite name', hint: 'e.g. Wings Auto Daily Smoke Tests' });
    if (!name) return;
    try { const s = await firstValueFrom(this.api.createSuite({ name })); this.loadList(); this.router.navigate(['/suites', s.suite.id]); } catch (e) { this.ui.error(e); }
  }

  async saveMeta() {
    const d = this.detail()!;
    try { this.detail.set(await firstValueFrom(this.api.updateSuite(d.suite.id, { ...this.draft, version: d.suite.version }))); this.ui.ok('Suite saved'); this.loadList(); }
    catch (e) { this.ui.error(e); }
  }

  async addCases() {
    const d = this.detail()!;
    const chosen = await firstValueFrom(this.dialog.open(TestCasePickerDialog, { width: '600px', data: { title: 'Add to suite', excludeIds: d.items.map(i => i.testCase.id) } }).afterClosed()) as TestCaseSummary[] | undefined;
    if (!chosen?.length) return;
    try { this.detail.set(await firstValueFrom(this.api.addSuiteItems(d.suite.id, chosen.map(c => c.id)))); this.loadList(); } catch (e) { this.ui.error(e); }
  }

  async removeItem(itemId: string) {
    try { this.detail.set(await firstValueFrom(this.api.removeSuiteItem(this.detail()!.suite.id, itemId))); this.loadList(); } catch (e) { this.ui.error(e); }
  }

  async drop(e: CdkDragDrop<unknown>) {
    const d = this.detail()!;
    const items = [...d.items];
    moveItemInArray(items, e.previousIndex, e.currentIndex);
    this.detail.set({ ...d, items });
    try { this.detail.set(await firstValueFrom(this.api.reorderSuite(d.suite.id, items.map(i => i.itemId)))); } catch (err) { this.ui.error(err); this.load(d.suite.id); }
  }

  async remove() {
    const d = this.detail()!;
    if (!await this.ui.confirm({ title: 'Delete suite', message: `Delete ${d.suite.name}? Test cases are not affected.`, danger: true, confirmText: 'Delete' })) return;
    try { await firstValueFrom(this.api.deleteSuite(d.suite.id)); this.loadList(); this.router.navigate(['/suites']); } catch (e) { this.ui.error(e); }
  }
}
