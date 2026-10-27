import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { DataRecord, DataSet, Reservation, TestEnvironment } from '../core/models';
import { UiService } from '../core/ui.service';

interface SetDraft { id: string | null; code: string; name: string; description: string; category: string; environmentId?: string; setupNotes: string; cleanupNotes: string; version?: number; }

/** Versioned business data sets; jobs reserve records exclusively, so parallel runs never share a customer, product or ledger. */
@Component({
  selector: 'app-test-data',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe, RouterLink, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule],
  template: `
    <div class="layout">
      <aside class="list">
        <div class="row"><h2>Test data</h2><span class="spacer"></span>@if (canEdit()) { <button mat-icon-button (click)="newSet()"><mat-icon>add</mat-icon></button> }</div>
        @for (s of sets(); track s.id) {
          <a class="item" [class.on]="draft()?.id === s.id" (click)="open(s)">
            <span class="mono">{{ s.code }}</span><span class="muted small">v{{ s.dataVersion }}</span>
            <span class="sub">{{ s.name }} · {{ s.activeRecords }} active · {{ s.reserved }} reserved{{ s.environmentName ? ' · ' + s.environmentName : '' }}</span>
          </a>
        } @empty { <p class="muted">No data sets yet.</p> }
        <p class="muted small help">Test cases declare what they need, e.g. <span class="mono">CUSTOMERS:2, VEHICLES</span>. When a job is dispatched the records are
          reserved for it alone and written to the file named by <span class="mono">WINGS_CONTEXT_FILE</span>; they are released when the job ends.</p>
      </aside>
      <section class="page">
        @if (draft(); as d) {
          <div class="page-head"><h1>{{ d.id ? d.code : 'New data set' }}</h1><span class="spacer"></span>
            @if (canEdit()) {
              @if (d.id) { <button mat-stroked-button (click)="importInput.click()"><mat-icon>upload</mat-icon>Import CSV / Excel</button>
                <mat-checkbox [(ngModel)]="replace">replace (deactivate rows missing from the file)</mat-checkbox>
                <button mat-icon-button (click)="remove()"><mat-icon>delete</mat-icon></button> }
              <button mat-flat-button (click)="save()"><mat-icon>save</mat-icon>Save</button>
            }
            <input #importInput type="file" accept=".csv,.xlsx" hidden (change)="import(importInput)" />
          </div>
          <div class="panel grid-3">
            <mat-form-field><mat-label>Code</mat-label><input matInput [(ngModel)]="d.code" [readonly]="!!d.id" placeholder="CUSTOMERS" /></mat-form-field>
            <mat-form-field><mat-label>Name</mat-label><input matInput [(ngModel)]="d.name" /></mat-form-field>
            <mat-form-field><mat-label>Category</mat-label><input matInput [(ngModel)]="d.category" placeholder="Customers, Products, Ledgers…" /></mat-form-field>
            <mat-form-field><mat-label>Environment</mat-label><mat-select [(ngModel)]="d.environmentId">
              <mat-option [value]="undefined">Any environment</mat-option>@for (e of envs(); track e.id) { <mat-option [value]="e.id">{{ e.name }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field class="span2"><mat-label>Description</mat-label><input matInput [(ngModel)]="d.description" /></mat-form-field>
            <mat-form-field class="span3"><mat-label>Setup procedure</mat-label><textarea matInput rows="2" [(ngModel)]="d.setupNotes"></textarea></mat-form-field>
            <mat-form-field class="span3"><mat-label>Cleanup procedure</mat-label><textarea matInput rows="2" [(ngModel)]="d.cleanupNotes"></textarea></mat-form-field>
          </div>
          @if (d.id) {
            <div class="panel">
              <h3>Records</h3>
              <table class="data">
                <thead><tr><th>Key</th><th>Values (JSON)</th><th>Active</th><th>Reserved by</th><th></th></tr></thead>
                <tbody>
                  @for (r of records(); track r.id) {
                    <tr><td class="mono">{{ r.key }}</td><td class="mono small">{{ json(r.values) }}</td><td>{{ r.isActive ? 'Yes' : 'No' }}</td>
                      <td>{{ r.reservedByRun ?? '' }}</td>
                      <td class="actions">@if (canEdit()) { <button mat-icon-button (click)="edit(r)"><mat-icon>edit</mat-icon></button>
                        <button mat-icon-button (click)="deleteRecord(r)" [disabled]="r.isReserved"><mat-icon>delete</mat-icon></button> }</td></tr>
                  } @empty { <tr><td colspan="5" class="empty">No records. Add them below or import a file with a Key column.</td></tr> }
                </tbody>
              </table>
              @if (canEdit()) {
                <div class="add">
                  <input class="cell" placeholder="Key (e.g. C001)" [(ngModel)]="recKey" />
                  <textarea class="cell mono" rows="2" placeholder='{"name": "Asha Rao", "mobile": "9876500001"}' [(ngModel)]="recValues"></textarea>
                  <mat-checkbox [(ngModel)]="recActive">Active</mat-checkbox>
                  <button mat-stroked-button (click)="saveRecord()" [disabled]="!recKey">Save record</button>
                </div>
              }
            </div>
          }
        } @else { <div class="empty">Select a data set or create one.</div> }

        <div class="panel">
          <div class="row"><h3>Active reservations</h3><span class="spacer"></span><button mat-button (click)="loadReservations()"><mat-icon>refresh</mat-icon></button></div>
          <table class="data">
            <thead><tr><th>Data set</th><th>Record</th><th>Run</th><th>Reserved</th><th></th></tr></thead>
            <tbody>
              @for (r of reservations(); track r.id) {
                <tr><td class="mono">{{ r.dataSetCode }}</td><td class="mono">{{ r.recordKey }}</td><td><a [routerLink]="['/runs', r.runId]">{{ r.runNumber }}</a></td>
                  <td class="muted">{{ r.reservedAt | date: 'medium' }}</td>
                  <td>@if (canEdit()) { <button mat-button (click)="release(r)">Release</button> }</td></tr>
              } @empty { <tr><td colspan="5" class="empty">No records are reserved.</td></tr> }
            </tbody>
          </table>
        </div>
      </section>
    </div>`,
  styles: `
    .layout { display: grid; grid-template-columns: 300px 1fr; height: 100%; }
    .list { background: var(--surface); border-right: 1px solid var(--border); padding: 8px; overflow: auto; }
    .item { display: grid; grid-template-columns: 1fr auto; padding: 6px 8px; border-radius: 4px; cursor: pointer; } .item .sub { grid-column: 1 / -1; font-size: 12px; color: var(--muted); }
    .item:hover { background: var(--hover); } .item.on { background: var(--selected); }
    .small { font-size: 12px; } .help { margin-top: 16px; } .span2 { grid-column: span 2; } .span3 { grid-column: 1 / -1; }
    .cell { font: inherit; padding: 5px 7px; border: 1px solid var(--border); border-radius: 4px; }
    .add { display: grid; grid-template-columns: 160px 1fr auto auto; gap: 8px; align-items: center; margin-top: 8px; }
    td.actions { width: 1%; white-space: nowrap; }
  `,
})
export class TestData {
  private api = inject(ApiService);
  private ui = inject(UiService);
  private auth = inject(AuthService);
  sets = signal<DataSet[]>([]);
  envs = signal<TestEnvironment[]>([]);
  records = signal<DataRecord[]>([]);
  reservations = signal<Reservation[]>([]);
  draft = signal<SetDraft | null>(null);
  replace = false;
  recKey = '';
  recValues = '{}';
  recActive = true;
  canEdit = () => this.auth.can('testdata.manage');

  constructor() {
    this.load();
    this.loadReservations();
    this.api.environments().subscribe(e => this.envs.set(e));
  }

  load() { this.api.dataSets().subscribe({ next: s => this.sets.set(s), error: e => this.ui.error(e) }); }
  loadReservations() { this.api.reservations().subscribe({ next: r => this.reservations.set(r), error: e => this.ui.error(e) }); }

  newSet() { this.records.set([]); this.draft.set({ id: null, code: '', name: '', description: '', category: '', setupNotes: '', cleanupNotes: '' }); }

  open(s: DataSet) {
    this.draft.set({ id: s.id, code: s.code, name: s.name, description: s.description ?? '', category: s.category ?? '', environmentId: s.environmentId,
      setupNotes: s.setupNotes ?? '', cleanupNotes: s.cleanupNotes ?? '', version: s.version });
    this.api.dataRecords(s.id).subscribe({ next: r => this.records.set(r), error: e => this.ui.error(e) });
  }

  async save() {
    const d = this.draft()!;
    try {
      const s = await firstValueFrom(this.api.saveDataSet(d.id, { code: d.code, name: d.name, description: d.description || null, category: d.category || null,
        environmentId: d.environmentId ?? null, setupNotes: d.setupNotes || null, cleanupNotes: d.cleanupNotes || null, version: d.version }));
      this.ui.ok('Saved'); this.load(); this.open(s);
    } catch (e) { this.ui.error(e); }
  }

  async remove() {
    const d = this.draft()!;
    if (!await this.ui.confirm({ title: 'Delete data set', message: `Delete ${d.code}? Test cases that require it can no longer run.`, danger: true, confirmText: 'Delete' })) return;
    try { await firstValueFrom(this.api.deleteDataSet(d.id!)); this.draft.set(null); this.load(); } catch (e) { this.ui.error(e); }
  }

  edit(r: DataRecord) { this.recKey = r.key; this.recValues = JSON.stringify(r.values, null, 1); this.recActive = r.isActive; }

  async saveRecord() {
    let values: unknown;
    try { values = JSON.parse(this.recValues || '{}'); } catch { this.ui.error(new Error('Values must be a JSON object.')); return; }
    const d = this.draft()!;
    try {
      await firstValueFrom(this.api.saveDataRecord(d.id!, { key: this.recKey, values, isActive: this.recActive }));
      this.recKey = ''; this.recValues = '{}'; this.recActive = true;
      this.refreshSelected();
    } catch (e) { this.ui.error(e); }
  }

  async deleteRecord(r: DataRecord) {
    try { await firstValueFrom(this.api.deleteDataRecord(this.draft()!.id!, r.id)); this.refreshSelected(); } catch (e) { this.ui.error(e); }
  }

  async import(input: HTMLInputElement) {
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    try {
      const r = await firstValueFrom(this.api.importDataRecords(this.draft()!.id!, file, this.replace));
      if (r.errors.length) await this.ui.confirm({ title: 'Import rejected', message: r.errors.join('\n'), confirmText: 'OK' });
      else { this.ui.ok(`Imported: ${r.created} new, ${r.updated} updated`); this.refreshSelected(); }
    } catch (e) { this.ui.error(e); }
  }

  async release(r: Reservation) {
    if (!await this.ui.confirm({ title: 'Release reservation', message: `Release ${r.dataSetCode}/${r.recordKey} held by ${r.runNumber}? Only do this if that job is stuck.`, confirmText: 'Release', danger: true })) return;
    try { await firstValueFrom(this.api.releaseReservation(r.id)); this.loadReservations(); this.refreshSelected(); } catch (e) { this.ui.error(e); }
  }

  json(v: unknown) { return JSON.stringify(v); }

  private refreshSelected() {
    this.load();
    const id = this.draft()?.id;
    if (id) this.api.dataRecords(id).subscribe(r => this.records.set(r));
  }
}
