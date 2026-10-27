import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuditEntry } from '../core/models';
import { UiService } from '../core/ui.service';
import { AuditChangesPipe } from '../shared/display';

@Component({
  selector: 'app-audit-trail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe, MatButtonModule, MatFormFieldModule, MatInputModule, MatPaginatorModule, AuditChangesPipe],
  template: `
    <div class="page">
      <div class="page-head"><h1>Audit trail</h1><span class="muted">Every change to features, test cases, packages, users and roles.</span></div>
      <div class="wrap-row">
        <mat-form-field><mat-label>Entity type</mat-label><input matInput [(ngModel)]="q.entityType" placeholder="Feature, TestCase, TestPackage, User…" /></mat-form-field>
        <mat-form-field><mat-label>User</mat-label><input matInput [(ngModel)]="q.userName" /></mat-form-field>
        <mat-form-field><mat-label>Action</mat-label><input matInput [(ngModel)]="q.action" placeholder="Created, Updated, Moved, Approved…" /></mat-form-field>
        <mat-form-field><mat-label>From</mat-label><input matInput type="date" [(ngModel)]="from" /></mat-form-field>
        <mat-form-field><mat-label>To</mat-label><input matInput type="date" [(ngModel)]="to" /></mat-form-field>
        <button mat-flat-button (click)="page = 1; load()">Search</button>
      </div>
      <table class="data">
        <thead><tr><th>When</th><th>User</th><th>Action</th><th>Entity</th><th>Details</th></tr></thead>
        <tbody>
          @for (a of items(); track a.id) {
            <tr>
              <td class="nowrap">{{ a.timestamp | date: 'medium' }}</td><td>{{ a.userName }}</td><td><b>{{ a.action }}</b></td>
              <td>{{ a.entityType }}<div class="mono muted small">{{ a.entityId }}</div></td>
              <td>@if (a.summary) { <div>{{ a.summary }}</div> }
                @for (c of a.changesJson | auditChanges; track c.field) { <div class="small"><b>{{ c.field }}</b>: @if (c.old !== undefined) { <s>{{ c.old }}</s> → } {{ c.new }}</div> }</td>
            </tr>
          } @empty { <tr><td colspan="5" class="empty">No entries.</td></tr> }
        </tbody>
      </table>
      <mat-paginator [length]="total()" [pageIndex]="page - 1" [pageSize]="50" (page)="onPage($event)" />
    </div>`,
  styles: `.small { font-size: 12px; } .nowrap { white-space: nowrap; } s { color: var(--bad); }`,
})
export class AuditTrail {
  private api = inject(ApiService);
  private ui = inject(UiService);
  q: { entityType?: string; userName?: string; action?: string } = {};
  from = '';
  to = '';
  page = 1;
  items = signal<AuditEntry[]>([]);
  total = signal(0);

  constructor() { this.load(); }

  onPage(e: PageEvent) { this.page = e.pageIndex + 1; this.load(); }

  async load() {
    try {
      const r = await firstValueFrom(this.api.audit({
        ...this.q, from: this.from ? `${this.from}T00:00:00Z` : undefined, to: this.to ? `${this.to}T23:59:59Z` : undefined, page: this.page, pageSize: 50,
      }));
      this.items.set(r.items);
      this.total.set(r.total);
    } catch (e) { this.ui.error(e); }
  }
}
