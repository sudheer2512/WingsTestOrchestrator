import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Subject, debounceTime, switchMap } from 'rxjs';
import { ApiService } from '../core/api.service';
import { FeatureSearchHit, Product, TestCaseSummary } from '../core/models';

export interface ConfirmData { title: string; message: string; confirmText?: string; danger?: boolean; }

@Component({
  selector: 'app-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content><p class="pre">{{ data.message }}</p></mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [class.danger]="data.danger" [mat-dialog-close]="true">{{ data.confirmText ?? 'Confirm' }}</button>
    </mat-dialog-actions>`,
})
export class ConfirmDialog {
  data = inject<ConfirmData>(MAT_DIALOG_DATA);
}

export interface PromptData { title: string; label: string; value?: string; required?: boolean; multiline?: boolean; hint?: string; confirmText?: string; }

@Component({
  selector: 'app-prompt-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, FormsModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      <mat-form-field class="full">
        <mat-label>{{ data.label }}</mat-label>
        @if (data.multiline) {
          <textarea matInput rows="4" [(ngModel)]="value" cdkFocusInitial></textarea>
        } @else {
          <input matInput [(ngModel)]="value" (keydown.enter)="value.trim() && ref.close(value.trim())" cdkFocusInitial />
        }
        @if (data.hint) { <mat-hint>{{ data.hint }}</mat-hint> }
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [disabled]="data.required !== false && !value.trim()" (click)="ref.close(value.trim())">{{ data.confirmText ?? 'OK' }}</button>
    </mat-dialog-actions>`,
})
export class PromptDialog {
  data = inject<PromptData>(MAT_DIALOG_DATA);
  ref = inject(MatDialogRef<PromptDialog>);
  value = this.data.value ?? '';
}

export interface FeaturePickerData { title?: string; productId?: string; excludeId?: string; }

/** Searches features across visible products (by name or sequence) and returns the chosen hit. */
@Component({
  selector: 'app-feature-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatIconModule, FormsModule],
  template: `
    <h2 mat-dialog-title>{{ data.title ?? 'Select feature' }}</h2>
    <mat-dialog-content class="picker">
      <div class="row">
        <mat-form-field class="grow">
          <mat-label>Search by name or sequence</mat-label>
          <input matInput [(ngModel)]="text" (ngModelChange)="search$.next()" cdkFocusInitial />
        </mat-form-field>
        <mat-form-field>
          <mat-label>Product</mat-label>
          <mat-select [(ngModel)]="productId" (ngModelChange)="search$.next()">
            <mat-option [value]="undefined">All products</mat-option>
            @for (p of products(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }
          </mat-select>
        </mat-form-field>
      </div>
      <div class="results">
        @for (h of hits(); track h.node.id) {
          <button type="button" class="hit" [class.selected]="selected()?.node?.id === h.node.id" [disabled]="h.node.id === data.excludeId"
                  (click)="selected.set(h)" (dblclick)="ref.close(h)">
            <span class="seq">{{ h.node.sequenceDisplay }}</span>
            <span class="name">{{ h.node.name }}</span>
            <span class="crumbs">{{ h.productName }} › {{ crumbs(h) }}</span>
          </button>
        } @empty { <p class="muted">No matching features.</p> }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [disabled]="!selected()" (click)="ref.close(selected())">Select</button>
    </mat-dialog-actions>`,
  styles: `
    .picker { min-width: 560px; }
    .row { display: flex; gap: 8px; } .grow { flex: 1; }
    .results { max-height: 360px; overflow: auto; border: 1px solid var(--border); border-radius: 6px; }
    .hit { display: grid; grid-template-columns: 64px 1fr; gap: 0 8px; width: 100%; text-align: left; padding: 6px 10px; border: 0; border-bottom: 1px solid var(--border); background: none; cursor: pointer; font: inherit; color: inherit; }
    .hit:hover { background: var(--hover); } .hit.selected { background: var(--selected); }
    .hit:disabled { opacity: .45; cursor: default; }
    .seq { font-family: var(--mono); color: var(--muted); grid-row: span 2; }
    .crumbs { font-size: 12px; color: var(--muted); }
  `,
})
export class FeaturePickerDialog {
  data = inject<FeaturePickerData>(MAT_DIALOG_DATA);
  ref = inject(MatDialogRef<FeaturePickerDialog>);
  private api = inject(ApiService);
  text = '';
  productId = this.data.productId;
  products = signal<Product[]>([]);
  hits = signal<FeatureSearchHit[]>([]);
  selected = signal<FeatureSearchHit | null>(null);
  search$ = new Subject<void>();

  constructor() {
    this.api.products().subscribe(p => this.products.set(p));
    this.search$.pipe(debounceTime(200), switchMap(() => this.api.searchFeatures({ productId: this.productId, text: this.text, sortBy: 'tree' }, 1, 100)))
      .subscribe(r => this.hits.set(r.items));
    this.search$.next();
  }

  crumbs(h: FeatureSearchHit) { return h.ancestors.slice(0, -1).map(a => a.name).join(' › ') || '(top level)'; }
}

export interface TestCasePickerData { title?: string; multiple?: boolean; excludeIds?: string[]; sharedStepsOnly?: boolean; }

@Component({
  selector: 'app-test-case-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatCheckboxModule, FormsModule],
  template: `
    <h2 mat-dialog-title>{{ data.title ?? 'Select test cases' }}</h2>
    <mat-dialog-content class="picker">
      <mat-form-field class="full">
        <mat-label>Search by number, name or tag</mat-label>
        <input matInput [(ngModel)]="text" (ngModelChange)="search$.next()" cdkFocusInitial />
      </mat-form-field>
      <div class="results">
        @for (t of results(); track t.id) {
          <label class="hit" [class.disabled]="excluded(t.id)">
            <mat-checkbox [checked]="chosen().has(t.id)" [disabled]="excluded(t.id)" (change)="toggle(t)"></mat-checkbox>
            <span class="num">{{ t.numberDisplay }}</span><span>{{ t.name }}</span>
          </label>
        } @empty { <p class="muted">No matching test cases.</p> }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <span class="muted">{{ chosen().size }} selected</span>
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [disabled]="!chosen().size" (click)="ref.close([...chosen().values()])">Select</button>
    </mat-dialog-actions>`,
  styles: `
    .picker { min-width: 520px; }
    .results { max-height: 360px; overflow: auto; border: 1px solid var(--border); border-radius: 6px; }
    .hit { display: flex; align-items: center; gap: 8px; padding: 2px 8px; border-bottom: 1px solid var(--border); cursor: pointer; }
    .hit.disabled { opacity: .45; }
    .num { font-family: var(--mono); color: var(--muted); min-width: 76px; }
  `,
})
export class TestCasePickerDialog {
  data = inject<TestCasePickerData>(MAT_DIALOG_DATA);
  ref = inject(MatDialogRef<TestCasePickerDialog>);
  private api = inject(ApiService);
  text = '';
  results = signal<TestCaseSummary[]>([]);
  chosen = signal(new Map<string, TestCaseSummary>());
  search$ = new Subject<void>();

  constructor() {
    this.search$.pipe(debounceTime(200), switchMap(() => this.api.testCases({ text: this.text, isSharedSteps: this.data.sharedStepsOnly || undefined }, 1, 100)))
      .subscribe(r => this.results.set(r.items));
    this.search$.next();
  }

  excluded(id: string) { return this.data.excludeIds?.includes(id) ?? false; }

  toggle(t: TestCaseSummary) {
    const next = new Map(this.data.multiple === false ? [] : this.chosen());
    if (this.chosen().has(t.id)) next.delete(t.id); else next.set(t.id, t);
    this.chosen.set(next);
  }
}
