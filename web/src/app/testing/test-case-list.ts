import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { Subject, debounceTime, firstValueFrom } from 'rxjs';
import { ApiService, saveBlob } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { AutomationStatus, Folder, TestCaseFilter, TestCaseSummary } from '../core/models';
import { UiService } from '../core/ui.service';
import { HumanizePipe, StatusPill } from '../shared/display';

const AUTOMATION: AutomationStatus[] = ['Manual', 'AutomationPlanned', 'Automated', 'NotAutomatable'];

@Component({
  selector: 'app-test-case-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, DatePipe, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule,
    MatMenuModule, MatPaginatorModule, MatProgressBarModule, StatusPill, HumanizePipe],
  template: `
    <div class="layout">
      <aside class="folders">
        <div class="row"><h3>Folders</h3><span class="spacer"></span>
          @if (auth.can('testcases.edit')) { <button mat-icon-button (click)="newFolder()"><mat-icon>create_new_folder</mat-icon></button> }</div>
        <a class="folder" [class.on]="!filter().folderId && !filter().isTemplate && !filter().isSharedSteps" (click)="setFilter({ folderId: undefined, isTemplate: undefined, isSharedSteps: undefined })">All test cases</a>
        <a class="folder" [class.on]="filter().isTemplate" (click)="setFilter({ folderId: undefined, isTemplate: true, isSharedSteps: undefined })"><mat-icon>content_paste</mat-icon>Templates</a>
        <a class="folder" [class.on]="filter().isSharedSteps" (click)="setFilter({ folderId: undefined, isTemplate: undefined, isSharedSteps: true })"><mat-icon>share</mat-icon>Shared steps</a>
        @for (f of folderRows(); track f.folder.id) {
          <a class="folder" [style.padding-left.px]="8 + f.depth * 14" [class.on]="filter().folderId === f.folder.id" (click)="setFilter({ folderId: f.folder.id, isTemplate: undefined, isSharedSteps: undefined })">
            <mat-icon>folder</mat-icon>{{ f.folder.name }} <span class="muted">{{ f.folder.testCaseCount }}</span>
            @if (auth.can('testcases.edit')) {
              <span class="spacer"></span><button mat-icon-button class="mini" [matMenuTriggerFor]="fm" (click)="$event.stopPropagation()"><mat-icon>more_horiz</mat-icon></button>
              <mat-menu #fm>
                <button mat-menu-item (click)="newFolder(f.folder.id)">New subfolder</button>
                <button mat-menu-item (click)="renameFolder(f.folder)">Rename</button>
                <button mat-menu-item (click)="deleteFolder(f.folder)">Delete</button>
              </mat-menu>
            }
          </a>
        }
      </aside>

      <section class="page">
        <div class="page-head">
          <h1>Test cases</h1><span class="muted">{{ total() }} total</span><span class="spacer"></span>
          @if (auth.can('testcases.edit')) { <button mat-flat-button routerLink="/test-cases/new" [queryParams]="{ folderId: filter().folderId }"><mat-icon>add</mat-icon>New test case</button> }
          <button mat-stroked-button [matMenuTriggerFor]="io"><mat-icon>import_export</mat-icon>Import / export</button>
          <mat-menu #io>
            <button mat-menu-item (click)="export('xlsx')">Export filtered (Excel)</button>
            <button mat-menu-item (click)="export('csv')">Export filtered (CSV)</button>
            @if (auth.can('testcases.edit')) { <button mat-menu-item (click)="importInput.click()">Import from CSV / Excel…</button> }
          </mat-menu>
          <input #importInput type="file" accept=".csv,.xlsx" hidden (change)="import(importInput)" />
        </div>

        <div class="filters wrap-row">
          <mat-form-field class="search"><mat-icon matPrefix>search</mat-icon><mat-label>Number, name or tag</mat-label>
            <input matInput [ngModel]="filter().text" (ngModelChange)="setFilter({ text: $event }, true)" /></mat-form-field>
          <mat-form-field><mat-label>Type</mat-label><mat-select [ngModel]="filter().testTypeCode" (ngModelChange)="setFilter({ testTypeCode: $event })">
            <mat-option [value]="undefined">Any</mat-option>@for (t of ui.lookups()?.testTypes; track t.code) { <mat-option [value]="t.code">{{ t.name }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field><mat-label>Automation</mat-label><mat-select [ngModel]="filter().automationStatus" (ngModelChange)="setFilter({ automationStatus: $event })">
            <mat-option [value]="undefined">Any</mat-option>@for (a of automation; track a) { <mat-option [value]="a">{{ a | humanize }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field><mat-label>Review</mat-label><mat-select [ngModel]="filter().reviewStatus" (ngModelChange)="setFilter({ reviewStatus: $event })">
            <mat-option [value]="undefined">Any</mat-option>@for (r of reviews; track r) { <mat-option [value]="r">{{ r | humanize }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field class="narrow"><mat-label>Priority</mat-label><mat-select [ngModel]="filter().priority" (ngModelChange)="setFilter({ priority: $event })">
            <mat-option [value]="undefined">Any</mat-option>@for (p of ui.lookups()?.priorities; track p) { <mat-option [value]="p">{{ p }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field class="narrow"><mat-label>Platform</mat-label><mat-select [ngModel]="filter().platform" (ngModelChange)="setFilter({ platform: $event })">
            <mat-option [value]="undefined">Any</mat-option>@for (p of ui.lookups()?.applicationTypes; track p) { <mat-option [value]="p">{{ p }}</mat-option> }</mat-select></mat-form-field>
        </div>

        @if (selection().size) {
          <div class="bulk panel row">
            <b>{{ selection().size }} selected</b>
            <mat-form-field class="narrow"><mat-label>Priority</mat-label><mat-select [(ngModel)]="bulk.priority"><mat-option [value]="undefined">—</mat-option>@for (p of ui.lookups()?.priorities; track p) { <mat-option [value]="p">{{ p }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field><mat-label>Automation</mat-label><mat-select [(ngModel)]="bulk.automationStatus"><mat-option [value]="undefined">—</mat-option>@for (a of automation; track a) { <mat-option [value]="a">{{ a | humanize }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field><mat-label>Move to folder</mat-label><mat-select [(ngModel)]="bulk.folderId"><mat-option [value]="undefined">—</mat-option>@for (f of folders(); track f.id) { <mat-option [value]="f.id">{{ f.name }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field><mat-label>Owner</mat-label><input matInput [(ngModel)]="bulk.ownerUserName" /></mat-form-field>
            <mat-form-field><mat-label>Add tags (comma separated)</mat-label><input matInput [(ngModel)]="bulk.addTags" /></mat-form-field>
            <button mat-flat-button (click)="applyBulk()" [disabled]="!auth.can('testcases.edit')">Apply</button>
            <button mat-button (click)="toggleAll(false)">Clear</button>
          </div>
        }

        @if (loading()) { <mat-progress-bar mode="indeterminate" /> }
        <table class="data">
          <thead><tr>
            <th class="check"><mat-checkbox [checked]="allSelected()" [indeterminate]="selection().size > 0 && !allSelected()" (change)="toggleAll($event.checked)" /></th>
            <th><a (click)="sort('number')">Number</a></th><th><a (click)="sort('name')">Name</a></th><th>Type</th><th><a (click)="sort('priority')">Priority</a></th>
            <th>Platform</th><th>Automation</th><th>Review</th><th>Features</th><th>Owner</th><th><a (click)="sort('modified')">Modified</a></th>
          </tr></thead>
          <tbody>
            @for (t of items(); track t.id) {
              <tr class="clickable" [class.selected]="selection().has(t.id)" (click)="router.navigate(['/test-cases', t.id])">
                <td class="check" (click)="$event.stopPropagation()"><mat-checkbox [checked]="selection().has(t.id)" (change)="toggle(t.id)" /></td>
                <td class="num">{{ t.numberDisplay }}</td>
                <td>{{ t.name }} @if (t.isTemplate) { <span class="tag">template</span> } @if (t.isSharedSteps) { <span class="tag">shared steps</span> }
                  @for (tag of t.tags; track tag) { <span class="tag">{{ tag }}</span> }</td>
                <td>{{ ui.label('testTypes', t.testTypeCode) }}</td><td>{{ t.priority }}</td><td>{{ t.platform === 'None' ? '' : t.platform }}</td>
                <td><app-status [value]="t.automationStatus" [label]="t.automationStatus | humanize" /></td>
                <td><app-status [value]="t.reviewStatus" [label]="t.reviewStatus | humanize" /></td>
                <td class="num">{{ t.featureCount }}</td><td>{{ t.ownerUserName }}</td><td class="muted">{{ (t.modifiedAt ?? t.createdAt) | date: 'short' }}</td>
              </tr>
            } @empty { @if (!loading()) { <tr><td colspan="11" class="empty">No test cases match these filters.</td></tr> } }
          </tbody>
        </table>
        <mat-paginator [length]="total()" [pageIndex]="page() - 1" [pageSize]="pageSize()" [pageSizeOptions]="[25, 50, 100, 250]" (page)="onPage($event)" />
      </section>
    </div>`,
  styles: `
    .layout { display: grid; grid-template-columns: 220px 1fr; height: 100%; }
    .folders { background: var(--surface); border-right: 1px solid var(--border); padding: 8px; overflow: auto; }
    .folder { display: flex; align-items: center; gap: 6px; padding: 4px 8px; border-radius: 4px; cursor: pointer; color: var(--text); text-decoration: none; min-height: 28px; }
    .folder:hover { background: var(--hover); } .folder.on { background: var(--selected); }
    .folder mat-icon { font-size: 16px; width: 16px; height: 16px; color: var(--muted); }
    .mini { width: 24px !important; height: 24px !important; padding: 0 !important; }
    .search { min-width: 260px; flex: 1; } .narrow { width: 120px; }
    .bulk { flex-wrap: wrap; margin-bottom: 8px; }
    th a { cursor: pointer; color: inherit; } .check { width: 32px; }
  `,
})
export class TestCaseList {
  private api = inject(ApiService);
  router = inject(Router);
  ui = inject(UiService);
  auth = inject(AuthService);
  automation = AUTOMATION;
  reviews = ['Draft', 'InReview', 'Approved', 'Rejected', 'Obsolete'];

  filter = signal<TestCaseFilter>({});
  items = signal<TestCaseSummary[]>([]);
  total = signal(0);
  page = signal(1);
  pageSize = signal(50);
  loading = signal(false);
  selection = signal(new Set<string>());
  folders = signal<Folder[]>([]);
  bulk: { priority?: string; automationStatus?: AutomationStatus; folderId?: string; ownerUserName?: string; addTags?: string } = {};
  allSelected = computed(() => this.items().length > 0 && this.items().every(i => this.selection().has(i.id)));
  folderRows = computed(() => {
    const out: { folder: Folder; depth: number }[] = [];
    const walk = (parent: string | undefined, depth: number) => this.folders().filter(f => f.parentId === parent).forEach(f => { out.push({ folder: f, depth }); walk(f.id, depth + 1); });
    walk(undefined, 0);
    return out;
  });
  private reload$ = new Subject<void>();

  constructor() {
    this.reload$.pipe(debounceTime(250), takeUntilDestroyed()).subscribe(() => this.load());
    this.load();
    this.loadFolders();
  }

  setFilter(patch: Partial<TestCaseFilter>, debounce = false) {
    this.filter.update(f => ({ ...f, ...patch }));
    this.page.set(1);
    if (debounce) this.reload$.next(); else this.load();
  }

  sort(by: string) {
    const f = this.filter();
    this.setFilter({ sortBy: by, descending: f.sortBy === by ? !f.descending : false });
  }

  onPage(e: PageEvent) { this.page.set(e.pageIndex + 1); this.pageSize.set(e.pageSize); this.load(); }

  async load() {
    this.loading.set(true);
    try {
      const r = await firstValueFrom(this.api.testCases(this.filter(), this.page(), this.pageSize()));
      this.items.set(r.items);
      this.total.set(r.total);
    } catch (e) { this.ui.error(e); }
    finally { this.loading.set(false); }
  }

  loadFolders() { this.api.folders().subscribe({ next: f => this.folders.set(f), error: e => this.ui.error(e) }); }

  toggle(id: string) { this.selection.update(s => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n; }); }
  toggleAll(on: boolean) { this.selection.set(on ? new Set(this.items().map(i => i.id)) : new Set()); }

  async applyBulk() {
    try {
      const r = await firstValueFrom(this.api.bulkUpdate({
        ids: [...this.selection()], priority: this.bulk.priority, automationStatus: this.bulk.automationStatus, folderId: this.bulk.folderId,
        ownerUserName: this.bulk.ownerUserName || undefined, addTags: this.bulk.addTags?.split(',').map(t => t.trim()).filter(Boolean),
      }));
      this.ui.ok(`Updated ${r.updated} test case(s)`);
      this.bulk = {};
      this.selection.set(new Set());
      this.load();
      this.loadFolders();
    } catch (e) { this.ui.error(e); }
  }

  async export(format: 'csv' | 'xlsx') {
    try { saveBlob(await firstValueFrom(this.api.exportTestCases(this.filter(), format)), `test-cases.${format}`); } catch (e) { this.ui.error(e); }
  }

  async import(input: HTMLInputElement) {
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    try {
      const r = await firstValueFrom(this.api.importTestCases(file, this.filter().folderId));
      if (r.errors.length) await this.ui.confirm({ title: 'Import rejected — nothing was imported', message: r.errors.join('\n'), confirmText: 'OK' });
      else { this.ui.ok(`Imported ${r.created} test case(s)`); this.load(); }
    } catch (e) { this.ui.error(e); }
  }

  async newFolder(parentId?: string) {
    const name = await this.ui.prompt({ title: 'New folder', label: 'Folder name' });
    if (!name) return;
    try { await firstValueFrom(this.api.createFolder(name, parentId)); this.loadFolders(); } catch (e) { this.ui.error(e); }
  }
  async renameFolder(f: Folder) {
    const name = await this.ui.prompt({ title: 'Rename folder', label: 'Folder name', value: f.name });
    if (!name) return;
    try { await firstValueFrom(this.api.updateFolder(f.id, name, f.parentId)); this.loadFolders(); } catch (e) { this.ui.error(e); }
  }
  async deleteFolder(f: Folder) {
    if (!await this.ui.confirm({ title: 'Delete folder', message: `Delete "${f.name}"? Only empty folders can be deleted.`, danger: true, confirmText: 'Delete' })) return;
    try { await firstValueFrom(this.api.deleteFolder(f.id)); this.loadFolders(); if (this.filter().folderId === f.id) this.setFilter({ folderId: undefined }); }
    catch (e) { this.ui.error(e); }
  }
}
