import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList, moveItemInArray } from '@angular/cdk/drag-drop';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
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
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { AutomationStatus, FeatureDetail, FeatureSearchHit, Folder, LinkedFeature, TestCaseDetail, TestCaseFields, TestCaseSummary, TestCaseVersion } from '../core/models';
import { UiService } from '../core/ui.service';
import { FeaturePickerDialog, TestCasePickerDialog } from '../shared/dialogs';
import { RunLauncher } from '../execution/start-run';
import { HumanizePipe, StatusPill } from '../shared/display';

const EMPTY: TestCaseFields = {
  name: '', testTypeCode: 'FUNCTIONAL', platform: 'None', priority: 'P2', failureSeverity: 'MAJOR', automationStatus: 'Manual',
  isTemplate: false, isSharedSteps: false, isDestructive: false, steps: [], featureIds: [], tags: [], requirementIds: [],
};

interface DiffRow { field: string; a: string; b: string; }

@Component({
  selector: 'app-test-case-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, DatePipe, CdkDropList, CdkDrag, CdkDragHandle, MatTabsModule, MatButtonModule, MatIconModule, MatFormFieldModule,
    MatInputModule, MatSelectModule, MatCheckboxModule, MatChipsModule, MatMenuModule, MatTooltipModule, MatProgressBarModule, StatusPill, HumanizePipe],
  templateUrl: './test-case-editor.html',
  styleUrl: './test-case-editor.scss',
})
export class TestCaseEditor {
  private api = inject(ApiService);
  private dialog = inject(MatDialog);
  private router = inject(Router);
  ui = inject(UiService);
  auth = inject(AuthService);

  /** Route parameter; absent for /test-cases/new. */
  id = input<string | undefined>();
  /** Query parameters for new test cases. */
  featureId = input<string | undefined>();
  folderId = input<string | undefined>();
  templateId = input<string | undefined>();

  detail = signal<TestCaseDetail | null>(null);
  fields = signal<TestCaseFields>(structuredClone(EMPTY));
  features = signal<LinkedFeature[]>([]);
  requirementOptions = signal<{ id: string; label: string }[]>([]);
  folders = signal<Folder[]>([]);
  versions = signal<TestCaseVersion[]>([]);
  compareA = signal<number | null>(null);
  compareB = signal<number | null>(null);
  loading = signal(false);
  saving = signal(false);
  dirty = signal(false);
  tagInput = '';
  changeSummary = '';
  automationStatuses: AutomationStatus[] = ['Manual', 'AutomationPlanned', 'Automated', 'NotAutomatable'];
  isNew = computed(() => !this.id() || this.id() === 'new');
  canEdit = computed(() => this.auth.can('testcases.edit'));
  frameworks = computed(() => this.ui.lookups()?.frameworks ?? []);

  diff = computed<DiffRow[]>(() => {
    const a = this.versions().find(v => v.revision === this.compareA()), b = this.versions().find(v => v.revision === this.compareB());
    if (!a || !b) return [];
    const pa = JSON.parse(a.snapshotJson) as Record<string, unknown>, pb = JSON.parse(b.snapshotJson) as Record<string, unknown>;
    const fmt = (v: unknown) => v === undefined || v === null ? '' : typeof v === 'string' ? v : JSON.stringify(v, null, 1);
    return [...new Set([...Object.keys(pa), ...Object.keys(pb)])]
      .map(k => ({ field: k, a: k === 'steps' ? this.stepsText(pa[k]) : fmt(pa[k]), b: k === 'steps' ? this.stepsText(pb[k]) : fmt(pb[k]) }))
      .filter(r => r.a !== r.b);
  });

  constructor() {
    this.api.folders().subscribe(f => this.folders.set(f));
    effect(() => {
      const id = this.id(), featureId = this.featureId(), folderId = this.folderId(), templateId = this.templateId();
      untracked(() => {
        if (id && id !== 'new') this.load(id);
        else this.initNew(featureId, folderId, templateId);
      });
    });
  }

  private async initNew(featureId?: string, folderId?: string, templateId?: string) {
    this.detail.set(null);
    this.versions.set([]);
    let f: TestCaseFields = { ...structuredClone(EMPTY), folderId };
    if (templateId) {
      const t = await firstValueFrom(this.api.testCase(templateId));
      f = { ...structuredClone(t.fields), name: '', isTemplate: false, folderId: folderId ?? t.fields.folderId };
    }
    this.fields.set(f);
    this.features.set([]);
    if (featureId) {
      const feat = await firstValueFrom(this.api.feature(featureId));
      this.addFeature(feat);
      this.fields.update(x => ({ ...x, primaryFeatureId: feat.id, platform: x.platform === 'None' ? feat.fields.applicationType : x.platform }));
    }
    this.dirty.set(false);
    this.refreshRequirementOptions();
  }

  async load(id: string) {
    this.loading.set(true);
    try {
      const d = await firstValueFrom(this.api.testCase(id));
      this.detail.set(d);
      this.fields.set(structuredClone(d.fields));
      this.features.set(d.features);
      this.dirty.set(false);
      this.refreshRequirementOptions();
      this.api.testCaseVersions(id).subscribe(v => {
        this.versions.set(v);
        this.compareB.set(v[0]?.revision ?? null);
        this.compareA.set(v[1]?.revision ?? null);
      });
    } catch (e) { this.ui.error(e); }
    finally { this.loading.set(false); }
  }

  /** Mutable view of the working copy for template two-way bindings; edits call touch(). */
  get f(): TestCaseFields { return this.fields(); }

  patch(p: Partial<TestCaseFields>) { this.fields.update(f => ({ ...f, ...p })); this.dirty.set(true); }
  touch() { this.dirty.set(true); }

  // ---- steps ----
  addStep() { this.patch({ steps: [...this.fields().steps, { stepNumber: this.fields().steps.length + 1, action: '' }] }); }
  removeStep(i: number) { const s = [...this.fields().steps]; s.splice(i, 1); this.patch({ steps: s }); }
  dropStep(e: CdkDragDrop<unknown>) { const s = [...this.fields().steps]; moveItemInArray(s, e.previousIndex, e.currentIndex); this.patch({ steps: s }); }
  async addSharedSteps() {
    const chosen = await firstValueFrom(this.dialog.open(TestCasePickerDialog, { width: '600px', data: { title: 'Insert shared steps', sharedStepsOnly: true, excludeIds: [this.detail()?.id ?? ''] } }).afterClosed()) as TestCaseSummary[] | undefined;
    if (!chosen?.length) return;
    this.patch({ steps: [...this.fields().steps, ...chosen.map(c => ({ stepNumber: 0, action: '', sharedStepsTestCaseId: c.id, sharedStepsName: `${c.numberDisplay} ${c.name}` }))] });
  }

  // ---- coverage ----
  async pickFeature() {
    const hit = await firstValueFrom(this.dialog.open(FeaturePickerDialog, { width: '640px', data: { title: 'Link feature' } }).afterClosed()) as FeatureSearchHit | undefined;
    if (!hit) return;
    this.addFeature(await firstValueFrom(this.api.feature(hit.node.id)));
  }

  private addFeature(f: FeatureDetail) {
    if (this.fields().featureIds.includes(f.id)) return;
    this.features.update(list => [...list, { featureId: f.id, name: f.fields.name, sequenceDisplay: f.sequenceDisplay, productName: f.productName, isPrimary: list.length === 0 }]);
    this.patch({ featureIds: [...this.fields().featureIds, f.id], primaryFeatureId: this.fields().primaryFeatureId ?? f.id });
    this.requirementCache.set(f.id, f.requirements.map(r => ({ id: r.id, label: `${r.code} – ${r.title} (${f.fields.name})` })));
    this.refreshRequirementOptions();
  }

  removeFeature(id: string) {
    this.features.update(l => l.filter(f => f.featureId !== id));
    const remaining = this.fields().featureIds.filter(f => f !== id);
    const reqs = this.requirementCache.get(id)?.map(r => r.id) ?? [];
    this.patch({ featureIds: remaining, primaryFeatureId: this.fields().primaryFeatureId === id ? remaining[0] : this.fields().primaryFeatureId,
      requirementIds: this.fields().requirementIds.filter(r => !reqs.includes(r)) });
    this.refreshRequirementOptions();
  }

  private requirementCache = new Map<string, { id: string; label: string }[]>();
  private async refreshRequirementOptions() {
    for (const fid of this.fields().featureIds)
      if (!this.requirementCache.has(fid)) {
        const f = await firstValueFrom(this.api.feature(fid));
        this.requirementCache.set(fid, f.requirements.map(r => ({ id: r.id, label: `${r.code} – ${r.title} (${f.fields.name})` })));
      }
    this.requirementOptions.set(this.fields().featureIds.flatMap(fid => this.requirementCache.get(fid) ?? []));
  }

  toggleRequirement(id: string, on: boolean) {
    const s = new Set(this.fields().requirementIds);
    if (on) s.add(id); else s.delete(id);
    this.patch({ requirementIds: [...s] });
  }

  addTag() {
    const t = this.tagInput.trim().toLowerCase();
    if (t && !this.fields().tags.includes(t)) this.patch({ tags: [...this.fields().tags, t] });
    this.tagInput = '';
  }
  removeTag(t: string) { this.patch({ tags: this.fields().tags.filter(x => x !== t) }); }

  // ---- persistence & workflow ----

  async save() {
    const f = this.fields();
    if (!f.name.trim()) { this.ui.error(new Error('Name is required.')); return; }
    if (f.parametersJson?.trim()) { try { JSON.parse(f.parametersJson); } catch { this.ui.error(new Error('Parameters must be valid JSON.')); return; } }
    this.saving.set(true);
    try {
      const steps = f.steps.map((s, i) => ({ ...s, stepNumber: i + 1 }));
      const d = this.detail();
      const saved = d
        ? await firstValueFrom(this.api.updateTestCase(d.id, { ...f, steps }, d.version, this.changeSummary || undefined))
        : await firstValueFrom(this.api.createTestCase({ ...f, steps }, this.changeSummary || undefined));
      this.changeSummary = '';
      this.ui.ok(d ? `Saved revision ${saved.revision}` : `Created ${saved.numberDisplay}`);
      if (!d) this.router.navigate(['/test-cases', saved.id], { replaceUrl: true });
      else await this.load(saved.id);
    } catch (e) { this.ui.error(e); }
    finally { this.saving.set(false); }
  }

  async workflow(action: 'submit' | 'approve' | 'reject') {
    const d = this.detail();
    if (!d) return;
    try {
      if (action === 'submit') await firstValueFrom(this.api.submitTestCase(d.id));
      if (action === 'approve') {
        const c = await this.ui.prompt({ title: `Approve ${d.numberDisplay} r${d.revision}`, label: 'Comment (optional)', required: false, confirmText: 'Approve' });
        if (c === undefined) return;
        await firstValueFrom(this.api.approveTestCase(d.id, c || undefined));
      }
      if (action === 'reject') {
        const c = await this.ui.prompt({ title: `Reject ${d.numberDisplay}`, label: 'What needs to change?', multiline: true, confirmText: 'Reject' });
        if (!c) return;
        await firstValueFrom(this.api.rejectTestCase(d.id, c));
      }
      await this.load(d.id);
    } catch (e) { this.ui.error(e); }
  }

  async clone(asTemplate: boolean) {
    const d = this.detail()!;
    const name = await this.ui.prompt({ title: asTemplate ? 'Save as template' : 'Clone test case', label: 'Name', value: asTemplate ? `${d.fields.name} (template)` : `${d.fields.name} (copy)` });
    if (!name) return;
    try { const c = await firstValueFrom(this.api.cloneTestCase(d.id, name, asTemplate)); this.router.navigate(['/test-cases', c.id]); } catch (e) { this.ui.error(e); }
  }

  newFromTemplate() { this.router.navigate(['/test-cases/new'], { queryParams: { templateId: this.detail()!.id } }); }

  private launcher = inject(RunLauncher);
  execute() {
    const d = this.detail()!;
    this.launcher.launch({ title: `Execute ${d.numberDisplay}`, scope: { testCaseIds: [d.id] }, description: `${d.numberDisplay} ${d.fields.name} — its published automated test(s).` });
  }

  async remove() {
    const d = this.detail()!;
    if (!await this.ui.confirm({ title: 'Delete test case', message: `Delete ${d.numberDisplay} ${d.fields.name}? Its history is kept for audit.`, danger: true, confirmText: 'Delete' })) return;
    try { await firstValueFrom(this.api.deleteTestCase(d.id)); this.router.navigate(['/test-cases']); } catch (e) { this.ui.error(e); }
  }

  async addDependency() {
    const d = this.detail()!;
    const chosen = await firstValueFrom(this.dialog.open(TestCasePickerDialog, { width: '600px', data: { title: 'Must run after…', multiple: false, excludeIds: [d.id] } }).afterClosed()) as TestCaseSummary[] | undefined;
    if (!chosen?.length) return;
    try { await firstValueFrom(this.api.addTestCaseDependency(d.id, chosen[0].id)); await this.load(d.id); } catch (e) { this.ui.error(e); }
  }
  async removeDependency(depId: string) {
    const d = this.detail()!;
    try { await firstValueFrom(this.api.removeTestCaseDependency(d.id, depId)); await this.load(d.id); } catch (e) { this.ui.error(e); }
  }

  private stepsText(v: unknown) {
    if (!Array.isArray(v)) return '';
    return (v as { action?: string; expectedResult?: string; sharedStepsTestCaseId?: string }[])
      .map((s, i) => `${i + 1}. ${s.sharedStepsTestCaseId ? '[shared steps]' : s.action}${s.expectedResult ? ' ⇒ ' + s.expectedResult : ''}`).join('\n');
  }
}
