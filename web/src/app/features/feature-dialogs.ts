import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ApplicationType, FeatureFields } from '../core/models';
import { UiService } from '../core/ui.service';

export interface CreateFeatureData { parentName?: string; productName: string; suggestedType: string; }
export interface CreateFeatureResult { fields: FeatureFields; legacySequenceNumber?: number; }

@Component({
  selector: 'app-create-feature-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, FormsModule],
  template: `
    <h2 mat-dialog-title>New feature</h2>
    <mat-dialog-content>
      <p class="muted">Under {{ data.productName }}{{ data.parentName ? ' › ' + data.parentName : ' (top level)' }}.
        A unique, permanent sequence number is assigned when you create it.</p>
      <mat-form-field class="full"><mat-label>Name</mat-label><input matInput [(ngModel)]="name" required cdkFocusInitial (keydown.enter)="create()" /></mat-form-field>
      <div class="grid-3">
        <mat-form-field><mat-label>Type</mat-label>
          <mat-select [(ngModel)]="type">@for (t of ui.lookups()?.featureTypes; track t.code) { <mat-option [value]="t.code">{{ t.name }}</mat-option> }</mat-select>
        </mat-form-field>
        <mat-form-field><mat-label>Status</mat-label>
          <mat-select [(ngModel)]="status">@for (t of ui.lookups()?.featureStatuses; track t.code) { <mat-option [value]="t.code">{{ t.name }}</mat-option> }</mat-select>
        </mat-form-field>
        <mat-form-field><mat-label>Application type</mat-label>
          <mat-select [(ngModel)]="app">@for (t of ui.lookups()?.applicationTypes; track t) { <mat-option [value]="t">{{ t }}</mat-option> }</mat-select>
        </mat-form-field>
      </div>
      <mat-form-field class="full"><mat-label>Description</mat-label><textarea matInput rows="3" [(ngModel)]="description"></textarea></mat-form-field>
      <mat-form-field class="full">
        <mat-label>Legacy sequence number (optional)</mat-label>
        <input matInput type="number" min="1" [(ngModel)]="legacy" />
        <mat-hint>Only when registering an existing feature that already has a number. Leave empty to allocate the next one.</mat-hint>
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [disabled]="!name.trim()" (click)="create()">Create</button>
    </mat-dialog-actions>`,
})
export class CreateFeatureDialog {
  data = inject<CreateFeatureData>(MAT_DIALOG_DATA);
  ref = inject(MatDialogRef<CreateFeatureDialog>);
  ui = inject(UiService);
  name = '';
  type = this.data.suggestedType;
  status = 'PLANNED';
  app: ApplicationType = 'None';
  description = '';
  legacy?: number;

  create() {
    if (!this.name.trim()) return;
    const result: CreateFeatureResult = {
      fields: { name: this.name.trim(), featureTypeCode: this.type, statusCode: this.status, applicationType: this.app, description: this.description || undefined, isDeprecated: false },
      legacySequenceNumber: this.legacy || undefined,
    };
    this.ref.close(result);
  }
}
