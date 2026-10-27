import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { PlanDetail, PlanStatus, PlanSummary, Product, ProductRelease, RiskLevel, SuiteSummary, TestEnvironment } from '../core/models';
import { UiService } from '../core/ui.service';
import { HumanizePipe, StatusPill } from '../shared/display';
import { RunBar } from '../execution/run-bar';

interface PlanDraft {
  name: string; objective: string; scope: string; productId?: string; targetReleaseId?: string; buildVersion: string; environmentId?: string; owners: string; testers: string;
  startDate: string; endDate: string; entryCriteria: string; exitCriteria: string; risk: RiskLevel; defectAcceptanceRules: string; isTemplate: boolean; suiteIds: string[]; version?: number;
}

/** Test plans: objective, scope (suites), release, environment, criteria, testers; execution, coverage and completion approval. */
@Component({
  selector: 'app-plans',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe, RouterLink, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, MatMenuModule,
    StatusPill, HumanizePipe, RunBar],
  templateUrl: './plans.html',
  styles: `
    .layout { display: grid; grid-template-columns: 320px 1fr; height: 100%; }
    .list { background: var(--surface); border-right: 1px solid var(--border); padding: 8px; overflow: auto; }
    .item { display: grid; grid-template-columns: auto 1fr auto; gap: 2px 8px; padding: 6px 8px; border-radius: 4px; text-decoration: none; color: var(--text); }
    .item .sub { grid-column: 1 / -1; font-size: 12px; color: var(--muted); } .item:hover { background: var(--hover); } .item.on { background: var(--selected); }
    .num { font-family: var(--mono); color: var(--muted); } .span2 { grid-column: span 2; } .span3 { grid-column: 1 / -1; }
    .totals { display: flex; gap: 16px; flex-wrap: wrap; margin-bottom: 8px; } .totals b { font-size: 16px; }
    .approved { background: var(--good-bg); }
  `,
})
export class Plans {
  private api = inject(ApiService);
  private ui = inject(UiService);
  private router = inject(Router);
  auth = inject(AuthService);
  id = input<string | undefined>();
  templates = signal(false);
  plans = signal<PlanSummary[]>([]);
  detail = signal<PlanDetail | null>(null);
  draft = signal<PlanDraft | null>(null);
  suites = signal<SuiteSummary[]>([]);
  envs = signal<TestEnvironment[]>([]);
  products = signal<Product[]>([]);
  releases = signal<ProductRelease[]>([]);
  risks: RiskLevel[] = ['Low', 'Medium', 'High', 'Critical'];
  runEnv?: string;
  runBuild = '';
  canEdit = () => this.auth.can('plans.manage');

  constructor() {
    this.loadList();
    this.api.suites().subscribe(s => this.suites.set(s));
    this.api.environments().subscribe(e => this.envs.set(e));
    this.api.products().subscribe(p => this.products.set(p));
    effect(() => { const id = this.id(); untracked(() => id && id !== 'new' ? this.load(id) : id === 'new' ? this.newPlan() : this.detail.set(null)); });
  }

  loadList() { this.api.plans(this.templates()).subscribe({ next: p => this.plans.set(p), error: e => this.ui.error(e) }); }
  toggleTemplates() { this.templates.update(t => !t); this.loadList(); }

  newPlan() {
    this.detail.set(null);
    this.draft.set({ name: '', objective: '', scope: '', buildVersion: '', owners: this.auth.user()?.userName ?? '', testers: '', startDate: '', endDate: '', entryCriteria: '',
      exitCriteria: 'All mandatory test cases passed; no open critical defects.', risk: 'Medium', defectAcceptanceRules: '', isTemplate: this.templates(), suiteIds: [] });
  }

  async load(id: string) {
    try {
      const d = await firstValueFrom(this.api.plan(id));
      this.detail.set(d);
      const s = d.summary;
      this.runEnv = s.environmentId;
      this.runBuild = s.buildVersion ?? '';
      this.draft.set({ name: s.name, objective: d.objective ?? '', scope: d.scope ?? '', productId: s.productId, targetReleaseId: s.targetReleaseId, buildVersion: s.buildVersion ?? '',
        environmentId: s.environmentId, owners: d.owners ?? '', testers: d.testers ?? '', startDate: s.startDate ?? '', endDate: s.endDate ?? '', entryCriteria: d.entryCriteria ?? '',
        exitCriteria: d.exitCriteria ?? '', risk: s.risk, defectAcceptanceRules: d.defectAcceptanceRules ?? '', isTemplate: s.isTemplate, suiteIds: d.suites.map(x => x.suiteId),
        version: s.version });
      this.loadReleases(s.productId);
    } catch (e) { this.ui.error(e); }
  }

  loadReleases(productId?: string) { if (productId) this.api.releases(productId).subscribe(r => this.releases.set(r)); else this.releases.set([]); }

  async save() {
    const d = this.draft()!;
    const body = { ...d, startDate: d.startDate || null, endDate: d.endDate || null, buildVersion: d.buildVersion || null };
    try {
      const saved = await firstValueFrom(this.api.savePlan(this.detail()?.summary.id ?? null, body));
      this.ui.ok('Plan saved');
      this.loadList();
      if (!this.detail()) this.router.navigate(['/plans', saved.summary.id]); else this.load(saved.summary.id);
    } catch (e) { this.ui.error(e); }
  }

  async status(status: PlanStatus) {
    const comment = status === 'Approved' ? await this.ui.prompt({ title: 'Approve plan completion', label: 'Approval comment', required: false, multiline: true, confirmText: 'Approve' }) : '';
    if (comment === undefined) return;
    try { this.detail.set(await firstValueFrom(this.api.planStatus(this.detail()!.summary.id, status, comment || undefined))); this.loadList(); } catch (e) { this.ui.error(e); }
  }

  async execute() {
    try {
      const run = await firstValueFrom(this.api.executePlan(this.detail()!.summary.id, { environmentId: this.runEnv, buildVersion: this.runBuild || undefined }));
      this.ui.ok(`${run.numberDisplay} started`);
      this.router.navigate(['/runs', run.id]);
    } catch (e) { this.ui.error(e); }
  }

  async clone(asTemplate: boolean) {
    const s = this.detail()!.summary;
    const name = await this.ui.prompt({ title: asTemplate ? 'Save as template' : 'Clone plan', label: 'Name', value: `${s.name} ${asTemplate ? '(template)' : '(copy)'}` });
    if (!name) return;
    try { const p = await firstValueFrom(this.api.clonePlan(s.id, name, asTemplate)); this.templates.set(asTemplate); this.loadList(); this.router.navigate(['/plans', p.summary.id]); }
    catch (e) { this.ui.error(e); }
  }

  async remove() {
    const s = this.detail()!.summary;
    if (!await this.ui.confirm({ title: 'Delete plan', message: `Delete ${s.numberDisplay} ${s.name}?`, danger: true, confirmText: 'Delete' })) return;
    try { await firstValueFrom(this.api.deletePlan(s.id)); this.loadList(); this.router.navigate(['/plans']); } catch (e) { this.ui.error(e); }
  }

  totals() { return Object.entries(this.detail()?.coverageTotals ?? {}); }
}
