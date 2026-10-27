import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService, saveBlob } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import {
  AuditEntry, DependencyType, ExecutionHistory, FeatureAutomation, FeatureDetail, FeatureFields, FeatureSearchHit, ProductRelease, Requirement,
  SourceMappingKind, TestCaseSummary,
} from '../core/models';
import { UiService } from '../core/ui.service';
import { FeaturePickerDialog, TestCasePickerDialog } from '../shared/dialogs';
import { AuditChangesPipe, BytesPipe, HumanizePipe, StatusPill } from '../shared/display';

type TextField = keyof Pick<FeatureFields, 'description' | 'businessObjective' | 'functionalRequirements' | 'currentVersion' | 'assemblyName' | 'namespace'
  | 'className' | 'formOrRoute' | 'relatedServices' | 'apiEndpoints' | 'databaseObjects' | 'sourceRepository' | 'buildPackage' | 'entryPoint'
  | 'requiredPermissions' | 'preconditions' | 'inputParameters' | 'expectedOutcomes' | 'businessRules' | 'validationRules' | 'errorConditions'
  | 'userRoles' | 'knownLimitations' | 'deprecationNote'>;

interface FieldDef { key: TextField; label: string; multiline?: boolean; }

const TECHNICAL: FieldDef[] = [
  { key: 'assemblyName', label: 'Assembly / component' }, { key: 'namespace', label: 'Namespace' }, { key: 'className', label: 'Class name' },
  { key: 'formOrRoute', label: 'Form name or browser route' }, { key: 'entryPoint', label: 'Application entry point' },
  { key: 'sourceRepository', label: 'Source repository' }, { key: 'buildPackage', label: 'Build / deployment package' },
  { key: 'relatedServices', label: 'Related services', multiline: true }, { key: 'apiEndpoints', label: 'API endpoints', multiline: true },
  { key: 'databaseObjects', label: 'Database tables, views and procedures', multiline: true }, { key: 'requiredPermissions', label: 'Required permissions', multiline: true },
];
const BEHAVIOUR: FieldDef[] = [
  { key: 'preconditions', label: 'Preconditions', multiline: true }, { key: 'inputParameters', label: 'Input parameters', multiline: true },
  { key: 'expectedOutcomes', label: 'Expected outcomes', multiline: true }, { key: 'businessRules', label: 'Business rules', multiline: true },
  { key: 'validationRules', label: 'Validation rules', multiline: true }, { key: 'errorConditions', label: 'Error conditions', multiline: true },
  { key: 'userRoles', label: 'User roles', multiline: true }, { key: 'knownLimitations', label: 'Known limitations', multiline: true },
];

@Component({
  selector: 'app-feature-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe, RouterLink, MatTabsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatCheckboxModule, MatMenuModule, MatTooltipModule, MatProgressBarModule, StatusPill, HumanizePipe, BytesPipe, AuditChangesPipe],
  templateUrl: './feature-detail.html',
  styleUrl: './feature-detail.scss',
})
export class FeatureDetailPanel {
  private api = inject(ApiService);
  private dialog = inject(MatDialog);
  private router = inject(Router);
  ui = inject(UiService);
  auth = inject(AuthService);

  featureId = input.required<string>();
  tabIndex = input(0);
  loaded = output<FeatureDetail>();
  changed = output<void>();
  navigate = output<string>();

  detail = signal<FeatureDetail | null>(null);
  loading = signal(false);
  editing = signal(false);
  draft = signal<FeatureFields | null>(null);
  saving = signal(false);
  selectedTab = signal(0);
  releases = signal<ProductRelease[]>([]);
  testCases = signal<TestCaseSummary[]>([]);
  includeSubfeatures = signal(false);
  automation = signal<FeatureAutomation[]>([]);
  history = signal<ExecutionHistory[]>([]);
  audit = signal<AuditEntry[]>([]);
  auditTotal = signal(0);
  canEdit = computed(() => this.auth.can('features.edit'));
  technical = TECHNICAL;
  behaviour = BEHAVIOUR;

  newReq: Partial<Requirement> = { priority: 'MEDIUM' };
  editingReq = signal<string | null>(null);
  reqDraft: Partial<Requirement> = {};
  newMapping: { kind: SourceMappingKind; value: string; repository?: string; notes?: string } = { kind: 'Class', value: '' };
  newDepType: DependencyType = 'Prerequisite';
  linkUrl = '';
  linkCaption = '';

  constructor() {
    effect(() => { const t = this.tabIndex(); if (t >= 0) untracked(() => this.loadTab(t)); });
    effect(() => { const id = this.featureId(); if (id) untracked(() => this.load(id)); });
  }

  async load(id = this.featureId()) {
    this.loading.set(true);
    this.editing.set(false);
    try {
      const d = await firstValueFrom(this.api.feature(id));
      this.detail.set(d);
      this.loaded.emit(d);
      this.api.releases(d.productId).subscribe(r => this.releases.set(r));
      this.loadTab(this.selectedTab());
    } catch (e) { this.ui.error(e); this.detail.set(null); }
    finally { this.loading.set(false); }
  }

  loadTab(index: number) {
    this.selectedTab.set(index);
    const d = this.detail();
    if (!d) return;
    if (index === 2) this.loadTestCases();
    if (index === 3) this.api.featureAutomation(d.id).subscribe({ next: a => this.automation.set(a), error: e => this.ui.error(e) });
    if (index === 8) this.api.history({ featureId: d.id }).subscribe({ next: h => this.history.set(h), error: e => this.ui.error(e) });
    if (index === 7) this.api.featureAudit(d.id).subscribe({ next: a => { this.audit.set(a.items); this.auditTotal.set(a.total); }, error: e => this.ui.error(e) });
  }

  loadTestCases() {
    const d = this.detail();
    if (d) this.api.featureTestCases(d.id, this.includeSubfeatures()).subscribe({ next: r => this.testCases.set(r.items), error: e => this.ui.error(e) });
  }

  // ---- overview editing ----

  startEdit() { this.draft.set(structuredClone(this.detail()!.fields)); this.editing.set(true); this.selectedTab.set(0); }
  cancelEdit() { this.editing.set(false); this.draft.set(null); }
  get d(): FeatureFields { return this.draft()!; }

  async save() {
    const d = this.detail(), draft = this.draft();
    if (!d || !draft) return;
    if (!draft.name?.trim()) { this.ui.error(new Error('Name is required.')); return; }
    this.saving.set(true);
    try {
      const updated = await firstValueFrom(this.api.updateFeature(d.id, draft, d.version));
      this.detail.set(updated);
      this.editing.set(false);
      this.ui.ok('Feature saved');
      this.changed.emit();
    } catch (e) { this.ui.error(e); }
    finally { this.saving.set(false); }
  }

  value(key: TextField) { return this.detail()?.fields[key]; }

  async pickReplacement() {
    const hit = await this.pickFeature('Replaced by…');
    if (hit) this.draft.update(f => ({ ...f!, replacedByFeatureId: hit.node.id }));
  }

  // ---- requirements ----

  async addRequirement() {
    const d = this.detail()!;
    try { await firstValueFrom(this.api.addRequirement(d.id, this.newReq)); this.newReq = { priority: 'MEDIUM' }; await this.load(); }
    catch (e) { this.ui.error(e); }
  }
  editRequirement(r: Requirement) { this.reqDraft = { ...r }; this.editingReq.set(r.id); }
  async saveRequirement() {
    try { await firstValueFrom(this.api.updateRequirement(this.detail()!.id, this.editingReq()!, this.reqDraft)); this.editingReq.set(null); await this.load(); }
    catch (e) { this.ui.error(e); }
  }
  async deleteRequirement(r: Requirement) {
    if (!await this.ui.confirm({ title: 'Delete requirement', message: `Delete ${r.code} – ${r.title}?`, danger: true, confirmText: 'Delete' })) return;
    try { await firstValueFrom(this.api.deleteRequirement(this.detail()!.id, r.id)); await this.load(); } catch (e) { this.ui.error(e); }
  }

  // ---- test cases ----

  newTestCase() { this.router.navigate(['/test-cases/new'], { queryParams: { featureId: this.detail()!.id } }); }

  async linkTestCases() {
    const d = this.detail()!;
    const chosen = await firstValueFrom(this.dialog.open(TestCasePickerDialog, { width: '600px', data: { title: 'Link existing test cases', excludeIds: this.testCases().map(t => t.id) } }).afterClosed()) as TestCaseSummary[] | undefined;
    if (!chosen?.length) return;
    try {
      const r = await firstValueFrom(this.api.bulkUpdate({ ids: chosen.map(c => c.id), addFeatureIds: [d.id] }));
      this.ui.ok(`Linked ${r.updated} test case(s)`);
      await this.load();
      this.loadTestCases();
      this.changed.emit();
    } catch (e) { this.ui.error(e); }
  }

  // ---- dependencies ----

  private pickFeature(title: string) {
    const d = this.detail()!;
    return firstValueFrom(this.dialog.open(FeaturePickerDialog, { width: '640px', data: { title, productId: d.productId, excludeId: d.id } }).afterClosed()) as Promise<FeatureSearchHit | undefined>;
  }

  async addDependency() {
    const hit = await this.pickFeature('This feature depends on…');
    if (!hit) return;
    try { await firstValueFrom(this.api.addDependency(this.detail()!.id, hit.node.id, this.newDepType)); await this.load(); }
    catch (e) { this.ui.error(e); }
  }
  async removeDependency(id: string) {
    try { await firstValueFrom(this.api.deleteDependency(this.detail()!.id, id)); await this.load(); } catch (e) { this.ui.error(e); }
  }

  // ---- source mappings ----

  async addMapping() {
    try {
      await firstValueFrom(this.api.addSourceMapping(this.detail()!.id, this.newMapping));
      this.newMapping = { kind: this.newMapping.kind, value: '' };
      await this.load();
    } catch (e) { this.ui.error(e); }
  }
  async approveMapping(id: string) { try { await firstValueFrom(this.api.approveSourceMapping(this.detail()!.id, id)); await this.load(); } catch (e) { this.ui.error(e); } }
  async deleteMapping(id: string) { try { await firstValueFrom(this.api.deleteSourceMapping(this.detail()!.id, id)); await this.load(); } catch (e) { this.ui.error(e); } }

  // ---- attachments ----

  async upload(inputEl: HTMLInputElement) {
    const files = Array.from(inputEl.files ?? []);
    inputEl.value = '';
    for (const f of files) {
      try { await firstValueFrom(this.api.uploadAttachment(this.detail()!.id, f)); } catch (e) { this.ui.error(e); }
    }
    if (files.length) { this.ui.ok(`Uploaded ${files.length} file(s)`); await this.load(); }
  }
  async addLink() {
    try { await firstValueFrom(this.api.addLink(this.detail()!.id, this.linkUrl, this.linkCaption || undefined)); this.linkUrl = ''; this.linkCaption = ''; await this.load(); }
    catch (e) { this.ui.error(e); }
  }
  async download(id: string, name: string) {
    try { saveBlob(await firstValueFrom(this.api.downloadAttachment(this.detail()!.id, id)), name); } catch (e) { this.ui.error(e); }
  }
  async deleteAttachment(id: string, name: string) {
    if (!await this.ui.confirm({ title: 'Remove attachment', message: `Remove ${name}?`, danger: true, confirmText: 'Remove' })) return;
    try { await firstValueFrom(this.api.deleteAttachment(this.detail()!.id, id)); await this.load(); } catch (e) { this.ui.error(e); }
  }
}
