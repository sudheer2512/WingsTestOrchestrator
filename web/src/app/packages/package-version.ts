import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService, saveBlob } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { AuditEntry, DiscoveredTest, FeatureSearchHit, PackageFile, PackageVersionDetail, PackageVersionSummary, TestCaseSummary } from '../core/models';
import { ChangesService, UiService } from '../core/ui.service';
import { CodeEditor } from '../shared/code-editor';
import { RunLauncher } from '../execution/start-run';
import { FeaturePickerDialog, TestCasePickerDialog } from '../shared/dialogs';
import { AuditChangesPipe, BytesPipe, HumanizePipe, StatusPill } from '../shared/display';

/** One immutable package version: validation report, discovered tests and mapping, file viewer/editor, diff and review workflow. */
@Component({
  selector: 'app-package-version',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, DatePipe, MatTabsModule, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, MatProgressBarModule,
    CodeEditor, StatusPill, HumanizePipe, BytesPipe, AuditChangesPipe],
  templateUrl: './package-version.html',
  styleUrl: './package-version.scss',
})
export class PackageVersion {
  private api = inject(ApiService);
  private dialog = inject(MatDialog);
  private router = inject(Router);
  ui = inject(UiService);
  auth = inject(AuthService);

  versionId = input.required<string>();
  /** Optional query parameter: feature the user came from, offered as the default mapping target. */
  featureId = input<string | undefined>();

  v = signal<PackageVersionDetail | null>(null);
  siblings = signal<PackageVersionSummary[]>([]);
  audit = signal<AuditEntry[]>([]);
  busy = signal(false);
  pending = computed(() => ['Uploaded', 'Validating'].includes(this.v()?.summary.status ?? ''));
  findingsCount = computed(() => (this.v()?.report?.stages ?? []).reduce((n, s) => n + s.findings.filter(f => f.severity !== 'info').length, 0));
  unmapped = computed(() => (this.v()?.tests ?? []).filter(t => !t.featureId));

  // File viewer / editor
  selectedFile = signal<PackageFile | null>(null);
  fileContent = signal('');
  fileLanguage = signal('plaintext');
  editing = signal(false);
  edits = signal(new Map<string, string>());
  compareWithParent = signal(false);
  parentContent = signal<string | undefined>(undefined);
  fileFilter = signal('');
  files = computed(() => (this.v()?.files ?? []).filter(f => f.path.toLowerCase().includes(this.fileFilter().toLowerCase())));
  private poll?: ReturnType<typeof setInterval>;

  constructor() {
    effect(() => { const id = this.versionId(); untracked(() => this.load(id)); });
    inject(ChangesService).events.pipe(takeUntilDestroyed()).subscribe(e => {
      if (e.topic === 'packageVersionChanged' && e.payload['versionId'] === this.versionId()) this.load(this.versionId());
    });
    inject(DestroyRef).onDestroy(() => clearInterval(this.poll));
  }

  async load(id: string) {
    try {
      const v = await firstValueFrom(this.api.packageVersion(id));
      this.v.set(v);
      this.api.package(v.packageId).subscribe(p => this.siblings.set(p.versions));
      this.api.packageAudit(v.packageId).subscribe(a => this.audit.set(a.items));
      // SignalR pushes completion; polling is a fallback when the socket is unavailable.
      clearInterval(this.poll);
      if (this.pending()) this.poll = setInterval(() => this.load(id), 3000);
      const keep = this.selectedFile();
      if (keep) { const f = v.files.find(x => x.path === keep.path); if (f) this.openFile(f); }
      else { const first = v.files.find(f => f.isText && /\.(cs|ts|js|py|ya?ml|json)$/.test(f.path)); if (first) this.openFile(first); }
    } catch (e) { this.ui.error(e); }
  }

  // ---- review workflow ----

  private launcher = inject(RunLauncher);
  execute() {
    const v = this.v()!;
    this.launcher.launch({ title: `Execute ${v.packageName} v${v.summary.versionNumber}`, scope: { packageVersionId: v.summary.id },
      description: `All ${v.tests.length} discovered test(s) of this ${v.summary.status.toLowerCase()} version.` });
  }

  async review(action: 'approve' | 'reject') {
    const v = this.v()!;
    const comment = await this.ui.prompt({ title: `${action === 'approve' ? 'Approve' : 'Reject'} v${v.summary.versionNumber}`, label: action === 'approve' ? 'Review comment (optional)' : 'Reason', multiline: true, required: action === 'reject', confirmText: action === 'approve' ? 'Approve' : 'Reject' });
    if (comment === undefined) return;
    await this.run(() => this.api.reviewPackage(v.summary.id, action, comment || undefined));
  }

  async act(action: 'publish' | 'deprecate' | 'revalidate') {
    const v = this.v()!;
    if (action === 'publish' && !await this.ui.confirm({ title: `Publish v${v.summary.versionNumber}`, message: 'Published versions become available to compatible execution agents. Older published versions of this package are marked deprecated.', confirmText: 'Publish' })) return;
    if (action === 'deprecate' && !await this.ui.confirm({ title: `Deprecate v${v.summary.versionNumber}`, message: 'The artifact stays stored for historical runs but is no longer offered for execution.', confirmText: 'Deprecate', danger: true })) return;
    await this.run(() => this.api.packageAction(v.summary.id, action));
  }

  private async run(call: () => ReturnType<ApiService['packageAction']>) {
    this.busy.set(true);
    try { this.v.set(await firstValueFrom(call())); this.ui.ok('Done'); await this.load(this.versionId()); }
    catch (e) { this.ui.error(e); }
    finally { this.busy.set(false); }
  }

  async download() {
    try {
      const r = await firstValueFrom(this.api.downloadPackage(this.v()!.summary.id));
      const name = /filename="?([^";]+)"?/.exec(r.headers.get('content-disposition') ?? '')?.[1] ?? `package-v${this.v()!.summary.versionNumber}.zip`;
      saveBlob(r.body!, name);
    } catch (e) { this.ui.error(e); }
  }

  // ---- test mapping ----

  async mapFeature(t: DiscoveredTest) {
    const hit = await firstValueFrom(this.dialog.open(FeaturePickerDialog, { width: '640px', data: { title: `Map "${t.displayName}" to feature` } }).afterClosed()) as FeatureSearchHit | undefined;
    if (hit) await this.map(t, { featureId: hit.node.id, testCaseId: t.testCaseId });
  }

  async mapTestCase(t: DiscoveredTest) {
    const chosen = await firstValueFrom(this.dialog.open(TestCasePickerDialog, { width: '600px', data: { title: `Link "${t.displayName}" to test case`, multiple: false } }).afterClosed()) as TestCaseSummary[] | undefined;
    if (chosen?.length) await this.map(t, { featureId: t.featureId, testCaseId: chosen[0].id });
  }

  async createTestCase(t: DiscoveredTest) { await this.map(t, { featureId: t.featureId, createTestCase: true }); }
  async unmap(t: DiscoveredTest) { await this.map(t, {}); }

  async mapAllTo(featureId: string) {
    for (const t of this.unmapped()) await this.map(t, { featureId, testCaseId: t.testCaseId }, false);
    await this.load(this.versionId());
    this.ui.ok('Mapped all unmapped tests');
  }

  private async map(t: DiscoveredTest, body: { featureId?: string; testCaseId?: string; createTestCase?: boolean }, reload = true) {
    try {
      const updated = await firstValueFrom(this.api.mapTest(this.v()!.summary.id, t.id, body));
      if (reload) this.v.update(v => v && ({ ...v, tests: v.tests.map(x => x.id === updated.id ? updated : x) }));
    } catch (e) { this.ui.error(e); }
  }

  traits(t: DiscoveredTest) {
    if (!t.traits) return '';
    try { return Object.entries(JSON.parse(t.traits) as Record<string, string[]>).map(([k, v]) => `${k}=${v.join(',')}`).join(' · '); } catch { return t.traits; }
  }

  // ---- files ----

  async openFile(f: PackageFile) {
    this.selectedFile.set(f);
    this.parentContent.set(undefined);
    if (!f.isText) { this.fileContent.set(''); return; }
    try {
      const c = await firstValueFrom(this.api.packageFile(this.v()!.summary.id, f.path));
      this.fileContent.set(this.edits().get(f.path) ?? c.content);
      this.fileLanguage.set(c.language);
      if (this.compareWithParent()) await this.loadParentContent(f.path);
    } catch (e) { this.fileContent.set(''); this.ui.error(e); }
  }

  openPath(path: string) {
    const f = this.v()?.files.find(x => x.path === path);
    if (f) { this.openFile(f); this.ui.ok(`Opened ${path} in the Files tab`); }
  }

  async toggleCompare() {
    this.compareWithParent.update(v => !v);
    const f = this.selectedFile();
    if (this.compareWithParent() && f) await this.loadParentContent(f.path); else this.parentContent.set(undefined);
  }

  private async loadParentContent(path: string) {
    const parentId = this.v()?.summary.parentVersionId ?? this.siblings().find(s => s.versionNumber === (this.v()!.summary.versionNumber - 1))?.id;
    if (!parentId) { this.parentContent.set(''); return; }
    try { this.parentContent.set((await firstValueFrom(this.api.packageFile(parentId, path))).content); }
    catch { this.parentContent.set(''); } // file is new in this version
  }

  onEdit(content: string) {
    const f = this.selectedFile();
    if (f) this.edits.update(m => new Map(m).set(f.path, content));
  }

  async saveEdits() {
    const v = this.v()!;
    const note = await this.ui.prompt({ title: 'Save as a new version', label: 'Change note', hint: `Creates v${Math.max(...this.siblings().map(s => s.versionNumber)) + 1} from v${v.summary.versionNumber}. This version stays unchanged.`, confirmText: 'Create version' });
    if (!note) return;
    try {
      const created = await firstValueFrom(this.api.editPackage(v.packageId, v.summary.id, [...this.edits()].map(([path, content]) => ({ path, content })), note));
      this.edits.set(new Map());
      this.editing.set(false);
      this.ui.ok(`Created v${created.versionNumber}; validation started`);
      this.router.navigate(['/test-code/versions', created.id]);
    } catch (e) { this.ui.error(e); }
  }

  discardEdits() { this.edits.set(new Map()); this.editing.set(false); const f = this.selectedFile(); if (f) this.openFile(f); }
}
