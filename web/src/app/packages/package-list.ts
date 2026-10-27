import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { PackageSummary, PackageVersionSummary, Product } from '../core/models';
import { ChangesService, UiService } from '../core/ui.service';
import { BytesPipe, HumanizePipe, StatusPill } from '../shared/display';

/** Test package registry: every uploaded automation package with its versions. */
@Component({
  selector: 'app-package-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, DatePipe, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, StatusPill, HumanizePipe, BytesPipe],
  template: `
    <div class="page">
      <div class="page-head">
        <h1>Test code</h1><span class="muted">Versioned automation packages. Code is validated statically and never executed on this server.</span>
        <span class="spacer"></span>
        @if (auth.can('testcode.upload')) { <button mat-flat-button routerLink="/test-code/upload"><mat-icon>upload</mat-icon>Upload test code</button> }
      </div>
      <div class="wrap-row">
        <mat-form-field><mat-label>Search packages</mat-label><input matInput [(ngModel)]="text" (ngModelChange)="load()" /></mat-form-field>
        <mat-form-field><mat-label>Product</mat-label><mat-select [(ngModel)]="productId" (ngModelChange)="load()">
          <mat-option [value]="undefined">All</mat-option>@for (p of products(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }</mat-select></mat-form-field>
      </div>
      <div class="split">
        <table class="data">
          <thead><tr><th>Number</th><th>Package</th><th>Latest</th><th>Framework</th><th>Tests</th><th>Created</th></tr></thead>
          <tbody>
            @for (p of packages(); track p.id) {
              <tr class="clickable" [class.selected]="p.id === openId()" (click)="open(p)">
                <td class="num">{{ p.numberDisplay }}</td><td>{{ p.name }}<div class="muted small">{{ p.description }}</div></td>
                <td>v{{ p.latestVersionNumber }} <app-status [value]="p.latestStatus" [label]="p.latestStatus | humanize" /></td>
                <td>{{ p.framework }}</td><td class="num">{{ p.testCount }}</td><td class="muted">{{ p.createdAt | date: 'short' }} · {{ p.createdBy }}</td>
              </tr>
            } @empty { <tr><td colspan="6" class="empty">No test packages uploaded yet.</td></tr> }
          </tbody>
        </table>
        @if (openId()) {
          <div class="panel versions">
            <div class="row"><h2>Versions</h2><span class="spacer"></span>
              @if (auth.can('testcode.upload')) { <button mat-stroked-button routerLink="/test-code/upload" [queryParams]="{ packageId: openId() }"><mat-icon>upload</mat-icon>Upload new version</button> }</div>
            @for (v of versions(); track v.id) {
              <a class="version" [routerLink]="['/test-code/versions', v.id]">
                <b>v{{ v.versionNumber }}</b> <app-status [value]="v.status" [label]="v.status | humanize" />
                <span class="muted">{{ v.testCount }} tests · {{ v.fileCount }} files · {{ v.sizeBytes | bytes }}</span>
                <div class="muted small">{{ v.createdAt | date: 'medium' }} · {{ v.createdBy }}{{ v.changeNote ? ' — ' + v.changeNote : '' }}</div>
              </a>
            }
          </div>
        }
      </div>
    </div>`,
  styles: `
    .split { display: grid; grid-template-columns: 1fr 380px; gap: 12px; align-items: start; }
    .versions { position: sticky; top: 0; }
    .version { display: block; padding: 6px 4px; border-bottom: 1px solid var(--border); text-decoration: none; color: var(--text); }
    .version:hover { background: var(--hover); }
    .small { font-size: 11px; }
  `,
})
export class PackageList {
  private api = inject(ApiService);
  private ui = inject(UiService);
  private router = inject(Router);
  auth = inject(AuthService);
  packages = signal<PackageSummary[]>([]);
  products = signal<Product[]>([]);
  versions = signal<PackageVersionSummary[]>([]);
  openId = signal<string | null>(null);
  text = '';
  productId?: string;

  constructor() {
    this.load();
    this.api.products().subscribe(p => this.products.set(p));
    inject(ChangesService).events.pipe(takeUntilDestroyed()).subscribe(e => {
      if (e.topic !== 'packageVersionChanged') return;
      this.load();
      if (e.payload['packageId'] === this.openId()) this.open({ id: this.openId()! } as PackageSummary);
    });
  }

  load() { this.api.packages(this.productId, this.text || undefined).subscribe({ next: p => this.packages.set(p), error: e => this.ui.error(e) }); }

  async open(p: PackageSummary) {
    this.openId.set(p.id);
    try { this.versions.set((await firstValueFrom(this.api.package(p.id))).versions); } catch (e) { this.ui.error(e); }
  }
}
