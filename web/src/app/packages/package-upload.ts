import { HttpEventType } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { PackageSummary, Product } from '../core/models';
import { UiService } from '../core/ui.service';
import { BytesPipe } from '../shared/display';

const ACCEPT = ['.zip', '.cs', '.csproj', '.sln', '.slnx', '.ts', '.js', '.mjs', '.cjs', '.py', '.json', '.yaml', '.yml', '.runsettings', '.config', '.xml', '.props', '.targets'];

@Component({
  selector: 'app-package-upload',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatProgressBarModule, BytesPipe],
  template: `
    <div class="page narrow">
      <div class="page-head"><a mat-icon-button routerLink="/test-code"><mat-icon>arrow_back</mat-icon></a><h1>Upload test code</h1></div>
      <div class="drop" [class.over]="over()" (dragover)="$event.preventDefault(); over.set(true)" (dragleave)="over.set(false)" (drop)="onDrop($event)" (click)="picker.click()">
        <mat-icon>cloud_upload</mat-icon>
        @if (file(); as f) { <div><b>{{ f.name }}</b> · {{ f.size | bytes }}</div><div class="muted">Click or drop to replace</div> }
        @else { <div><b>Drop a test project here</b> or click to choose</div>
          <div class="muted">A .zip of a complete project (C#/.NET, Playwright, pytest) or a single {{ accept.slice(1).join(' ') }} file.</div> }
        <input #picker type="file" hidden [accept]="accept.join(',')" (change)="choose(picker)" />
      </div>

      <div class="panel">
        <mat-form-field class="full"><mat-label>Package</mat-label>
          <mat-select [(ngModel)]="packageId">
            <mat-option [value]="undefined">Create a new package</mat-option>
            @for (p of packages(); track p.id) { <mat-option [value]="p.id">{{ p.numberDisplay }} {{ p.name }} (new version after v{{ p.latestVersionNumber }})</mat-option> }
          </mat-select>
        </mat-form-field>
        @if (!packageId) {
          <mat-form-field class="full"><mat-label>Package name</mat-label><input matInput [(ngModel)]="name" placeholder="Wings Auto – Invoicing UI tests" /></mat-form-field>
          <mat-form-field class="full"><mat-label>Description</mat-label><textarea matInput rows="2" [(ngModel)]="description"></textarea></mat-form-field>
          <mat-form-field class="full"><mat-label>Product</mat-label>
            <mat-select [(ngModel)]="productId"><mat-option [value]="undefined">None</mat-option>@for (p of products(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }</mat-select>
            <mat-hint>Tests tagged with a feature sequence (e.g. [Trait("WingsFeature","00146")] or &#64;feature-00146) are mapped to that product's features automatically.</mat-hint>
          </mat-form-field>
        }
        <div class="grid-2">
          <mat-form-field><mat-label>Source repository</mat-label><input matInput [(ngModel)]="sourceRepository" /></mat-form-field>
          <mat-form-field><mat-label>Source commit</mat-label><input matInput [(ngModel)]="sourceCommit" /></mat-form-field>
        </div>
        <mat-form-field class="full"><mat-label>Change note</mat-label><input matInput [(ngModel)]="changeNote" /></mat-form-field>
        @if (progress() !== null) { <mat-progress-bar mode="determinate" [value]="progress()" /> }
        <div class="row actions">
          <span class="muted small">Uploads are hashed, scanned and statically validated in the background. Nothing is compiled or executed on the server.</span>
          <span class="spacer"></span>
          <button mat-flat-button [disabled]="!file() || (!packageId && !name.trim()) || progress() !== null" (click)="upload()"><mat-icon>upload</mat-icon>Upload and validate</button>
        </div>
      </div>
    </div>`,
  styles: `
    .narrow { max-width: 820px; margin: 0 auto; }
    .drop { border: 2px dashed var(--border); border-radius: 10px; background: var(--surface); padding: 32px; text-align: center; cursor: pointer; margin-bottom: 12px; }
    .drop.over { border-color: var(--accent); background: var(--selected); }
    .drop mat-icon { font-size: 40px; width: 40px; height: 40px; color: var(--muted); }
    .actions { margin-top: 8px; } .small { font-size: 12px; }
  `,
})
export class PackageUpload {
  private api = inject(ApiService);
  private ui = inject(UiService);
  private router = inject(Router);
  /** Query parameters. */
  packageIdParam = input<string | undefined>(undefined, { alias: 'packageId' });
  productIdParam = input<string | undefined>(undefined, { alias: 'productId' });
  featureId = input<string | undefined>();

  accept = ACCEPT;
  file = signal<File | null>(null);
  over = signal(false);
  progress = signal<number | null>(null);
  products = signal<Product[]>([]);
  packages = signal<PackageSummary[]>([]);
  packageId?: string;
  productId?: string;
  name = '';
  description = '';
  sourceRepository = '';
  sourceCommit = '';
  changeNote = '';

  constructor() {
    this.api.products().subscribe(p => this.products.set(p));
    this.api.packages().subscribe(p => this.packages.set(p));
    effect(() => { const pk = this.packageIdParam(), pr = this.productIdParam(); untracked(() => { this.packageId = pk; this.productId = pr; }); });
  }

  choose(input: HTMLInputElement) { const f = input.files?.[0]; input.value = ''; if (f) this.setFile(f); }
  onDrop(e: DragEvent) { e.preventDefault(); this.over.set(false); const f = e.dataTransfer?.files?.[0]; if (f) this.setFile(f); }

  private setFile(f: File) {
    const ext = f.name.slice(f.name.lastIndexOf('.')).toLowerCase();
    if (!ACCEPT.includes(ext)) { this.ui.error(new Error(`'${ext}' files are not accepted. Upload a .zip of the project or a supported source file.`)); return; }
    this.file.set(f);
    if (!this.name) this.name = f.name.replace(/\.[^.]+$/, '');
  }

  async upload() {
    const f = this.file();
    if (!f) return;
    this.progress.set(0);
    this.api.uploadPackage(f, {
      packageId: this.packageId, name: this.packageId ? undefined : this.name.trim(), description: this.description || undefined,
      productId: this.packageId ? undefined : this.productId, sourceRepository: this.sourceRepository || undefined,
      sourceCommit: this.sourceCommit || undefined, changeNote: this.changeNote || undefined,
    }).subscribe({
      next: ev => {
        if (ev.type === HttpEventType.UploadProgress && ev.total) this.progress.set(Math.round(100 * ev.loaded / ev.total));
        if (ev.type === HttpEventType.Response && ev.body) {
          this.ui.ok('Uploaded — validation started');
          this.router.navigate(['/test-code/versions', ev.body.id], { queryParams: { featureId: this.featureId() } });
        }
      },
      error: e => { this.progress.set(null); this.ui.error(e); },
    });
  }
}
