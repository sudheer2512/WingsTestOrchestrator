import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { FeatureSearchHit, Product, TestCaseSummary } from '../core/models';
import {
  AiApi, AiInteraction, AiStatus, CODE_TARGETS, CodeGenerationResult, CodeTarget, FailureAnalysisResult, MaintenanceResult, RepairResult, SelectionResult, TestGenerationResult,
} from '../core/ai';
import { BuildSummary, FailureSuggestion, QualityApi, ReleaseSummary } from '../core/quality';
import { UiService } from '../core/ui.service';
import { CodeEditor } from '../shared/code-editor';
import { HumanizePipe, StatusPill } from '../shared/display';
import { RunLauncher } from '../execution/start-run';

type Tab = 'tests' | 'code' | 'failure' | 'selection' | 'maintenance' | 'history';

/**
 * AI testing assistant. Every result shows platform-verified facts and AI suggestions in separate, labelled panels; nothing the
 * AI proposes changes data until a person applies it, and applied items enter the normal review and approval workflows.
 */
@Component({
  selector: 'app-ai-assistant',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, DatePipe, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule,
    MatProgressBarModule, MatTooltipModule, StatusPill, HumanizePipe, CodeEditor],
  template: `
    <div class="page">
      <div class="page-head">
        <h1><mat-icon class="inline">auto_awesome</mat-icon> AI testing assistant</h1>
        @if (status(); as s) {
          <span class="muted">{{ s.provider }} · {{ s.model }} · {{ s.usedToday }}/{{ s.dailyLimit }} requests today</span>
        }
        <span class="spacer"></span>
        @if (auth.can('ai.manage')) { <a mat-stroked-button routerLink="/ai/settings"><mat-icon>tune</mat-icon>AI policy</a> }
      </div>
      @if (status(); as s) {
        @if (!s.ready) { <div class="banner warn"><mat-icon>info</mat-icon> {{ s.unavailable }}</div> }
      }
      <p class="legend">
        <span class="fact-key"><mat-icon class="inline">verified</mat-icon> Verified facts</span> come from the platform's own records.
        <span class="ai-key"><mat-icon class="inline">auto_awesome</mat-icon> AI suggestions</span> are generated and unverified: review them before use.
        Secrets and personal data are masked before anything is sent; applying a suggestion goes through the usual review and approval.
      </p>
      <nav class="tabs">@for (t of tabs; track t[0]) { <a [class.on]="tab() === t[0]" (click)="setTab(t[0])">{{ t[1] }}</a> }</nav>
      @if (busy()) { <mat-progress-bar mode="indeterminate" /><p class="muted">Asking the model… this can take up to a minute.</p> }

      @switch (tab()) {
        <!-- ============ Test generation ============ -->
        @case ('tests') {
          <div class="panel wrap-row">
            <mat-form-field class="wide"><mat-label>Feature (search name or sequence, Enter)</mat-label><input matInput [(ngModel)]="featureText" (keyup.enter)="searchFeatures()" /></mat-form-field>
            @if (featureHits().length) { <mat-form-field class="wide"><mat-label>Feature</mat-label><mat-select [(ngModel)]="featureId">
              @for (h of featureHits(); track h.node.id) { <mat-option [value]="h.node.id">{{ h.productName }} › {{ h.node.sequenceDisplay }} {{ h.node.name }}</mat-option> }</mat-select></mat-form-field> }
            <mat-form-field class="narrow"><mat-label>Suggestions</mat-label><input matInput type="number" min="1" max="20" [(ngModel)]="maxSuggestions" /></mat-form-field>
            <mat-form-field class="wide"><mat-label>Focus (optional)</mat-label><input matInput [(ngModel)]="focus" placeholder="e.g. discounts and approval limits" /></mat-form-field>
            <button mat-flat-button [disabled]="!featureId || busy() || !ready()" (click)="suggestTests()"><mat-icon>auto_awesome</mat-icon>Suggest test cases</button>
          </div>
          @if (gen(); as g) {
            <div class="panel facts"><h3><mat-icon class="inline">verified</mat-icon> Verified facts</h3>
              Feature <a [routerLink]="['/features', g.facts.featureId]">[{{ g.facts.sequenceNumber }}] {{ g.facts.feature }}</a>: {{ g.facts.requirements }} requirement(s), {{ g.facts.existingTestCases }} existing test case(s),
              {{ g.facts.defects }} historical defect(s). Sent: {{ g.facts.sourcesUsed.join('; ') }}.
              @for (w of g.warnings; track w) { <div class="warn-line"><mat-icon class="inline">warning</mat-icon> {{ w }}</div> }
            </div>
            <div class="panel ai"><div class="row"><h3><mat-icon class="inline">auto_awesome</mat-icon> AI suggestions (unverified)</h3><span class="spacer"></span>
              @if (auth.can('testcases.edit')) { <button mat-flat-button [disabled]="!picked().size" (click)="applyTests()">Create {{ picked().size }} draft test case(s)</button> }
              <button mat-button (click)="feedback(g.interactionId, 'NotUseful')">Not useful</button></div>
              @if (g.notes) { <p class="muted pre">{{ g.notes }}</p> }
              @for (s of g.suggestions; track s.index) {
                <div class="sugg" [class.on]="picked().has(s.index)">
                  <mat-checkbox [checked]="picked().has(s.index)" (change)="toggle(s.index)" />
                  <div class="grow">
                    <div class="row"><b>{{ s.name }}</b><span class="pill">{{ s.scenarioType }}</span><span class="pill">{{ s.priority }}</span>
                      @for (c of s.requirementCodes; track c) { <span class="tag">{{ c }}</span> }
                      @if (s.similarExisting) { <span class="pill" data-tone="warn">overlaps {{ s.similarExisting }}</span> }</div>
                    <div class="muted">{{ s.objective }}</div>
                    @if (s.preconditions) { <div class="small">Preconditions: {{ s.preconditions }}</div> }
                    <ol>@for (st of s.steps; track $index) { <li>{{ st.action }} <span class="muted">→ {{ st.expected }}</span></li> }</ol>
                    @if (s.testData) { <div class="small">Test data: {{ s.testData }}</div> }
                    <div class="small muted">Why: {{ s.rationale }}</div>
                  </div>
                </div>
              }
            </div>
          }
        }

        <!-- ============ Code generation ============ -->
        @case ('code') {
          <div class="panel wrap-row">
            <mat-form-field class="wide"><mat-label>Test case (search name or TC number, Enter)</mat-label><input matInput [(ngModel)]="caseText" (keyup.enter)="searchCases()" /></mat-form-field>
            @if (caseHits().length) { <mat-form-field class="wide"><mat-label>Test case</mat-label><mat-select [(ngModel)]="testCaseId">
              @for (c of caseHits(); track c.id) { <mat-option [value]="c.id">{{ c.numberDisplay }} {{ c.name }}</mat-option> }</mat-select></mat-form-field> }
            <mat-form-field><mat-label>Target</mat-label><mat-select [(ngModel)]="target">@for (t of targets; track t[0]) { <mat-option [value]="t[0]">{{ t[1] }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field class="wide"><mat-label>Extra instructions (optional)</mat-label><input matInput [(ngModel)]="instructions" /></mat-form-field>
            <button mat-flat-button [disabled]="!testCaseId || busy() || !ready()" (click)="generateCode()"><mat-icon>auto_awesome</mat-icon>Draft test code</button>
          </div>
          @if (code(); as c) {
            <div class="panel facts"><h3><mat-icon class="inline">verified</mat-icon> Verified facts</h3>
              <div>{{ c.facts.testCase }} → {{ c.facts.target | humanize }}{{ c.facts.feature ? ' · feature [' + c.facts.featureSequence + '] ' + c.facts.feature : '' }}</div>
              <div class="small">Conventions applied: {{ c.facts.convention }}</div>
              <div class="small">Style reference: {{ c.facts.referencePackage ? c.facts.referencePackage + ' (' + c.facts.referenceFiles.join(', ') + ')' : 'none' }}</div>
              @for (w of c.warnings; track w) { <div class="warn-line"><mat-icon class="inline">warning</mat-icon> {{ w }}</div> }
            </div>
            <div class="panel ai"><div class="row"><h3><mat-icon class="inline">auto_awesome</mat-icon> AI draft (unverified)</h3><span class="spacer"></span>
              @if (auth.can('testcode.upload')) { <button mat-flat-button [disabled]="!c.files.length" (click)="applyCode()">Upload as a new package for review</button> }</div>
              <p class="small muted">Uploading creates a package version that is validated, then must be approved by someone other than you before it is published and can run.</p>
              @if (c.todos.length) { <h4>To do before approval</h4><ul>@for (t of c.todos; track t) { <li>{{ t }}</li> }</ul> }
              @if (c.assumptions.length) { <h4>Assumptions</h4><ul>@for (t of c.assumptions; track t) { <li>{{ t }}</li> }</ul> }
              @for (f of c.files; track f.path) { <h4 class="mono">{{ f.path }}</h4><div class="editor"><app-code-editor [value]="f.content" [language]="lang(f.path)" /></div> }
            </div>
          }
        }

        <!-- ============ Failure analysis ============ -->
        @case ('failure') {
          <div class="panel wrap-row">
            <mat-form-field class="wide"><mat-label>Failed test (last 7 days)</mat-label><mat-select [(ngModel)]="executionId">
              @for (f of failures(); track f.testExecutionId) { <mat-option [value]="f.testExecutionId">{{ f.runNumber }} · {{ f.displayName }}</mat-option> }</mat-select></mat-form-field>
            <button mat-flat-button [disabled]="!executionId || busy() || !ready()" (click)="analyze()"><mat-icon>auto_awesome</mat-icon>Analyse failure</button>
            <button mat-stroked-button [disabled]="!executionId || busy() || !ready() || !status()?.allowSourceCode" (click)="proposeRepair()"
              [matTooltip]="status()?.allowSourceCode ? 'Propose a fix to the test code' : 'Needs source code sharing in the AI policy'"><mat-icon>build</mat-icon>Propose test repair</button>
          </div>
          @if (analysis(); as a) {
            <div class="panel facts"><h3><mat-icon class="inline">verified</mat-icon> Verified facts</h3>
              <div><b>{{ a.facts.test }}</b> · <a [routerLink]="['/runs', a.facts.runId]">{{ a.facts.run }}</a> · <app-status [value]="a.facts.outcome" /></div>
              <div class="mono small err">{{ a.facts.errorExcerpt }}</div>
              <div>{{ a.facts.failures30d }} failure(s) in {{ a.facts.executions30d }} run(s) over 30 days.
                @if (a.facts.lastPassedRun) { Last passed in {{ a.facts.lastPassedRun }} (build {{ a.facts.lastPassedBuild }}); failing since build {{ a.facts.firstFailingBuild }}. } @else { No passing run on record. }</div>
              @if (a.facts.linkedDefect) { <div>Same failure signature as <a [routerLink]="['/defects', a.facts.linkedDefectId]">{{ a.facts.linkedDefect }}</a>.</div> }
              @if (a.facts.recentChanges.length) { <div>Recent changes: @for (c of a.facts.recentChanges; track c.build + c.detail) { <div class="small">{{ c.build }} ({{ c.at | date: 'shortDate' }}): {{ c.detail }}</div> }</div> }
              <div class="small muted">Evidence sent: {{ a.facts.evidenceSent.join(', ') || 'error and history only' }}@if (a.facts.evidenceNotSent.length) { · not sent: {{ a.facts.evidenceNotSent.join(', ') }} }</div>
              <table class="data compact"><tbody>@for (h of a.facts.history; track $index) { <tr><td>{{ h.run }}</td><td><app-status [value]="h.outcome" /></td><td>{{ h.build }}</td><td>{{ h.environment }}</td><td>{{ h.at | date: 'short' }}</td></tr> }</tbody></table>
            </div>
            <div class="panel ai"><div class="row"><h3><mat-icon class="inline">auto_awesome</mat-icon> AI analysis (unverified)</h3><span class="pill">{{ a.analysis.category | humanize }}</span>
              <span class="pill">confidence {{ a.analysis.confidence }}</span>@if (a.analysis.likelyFlaky) { <span class="pill" data-tone="warn">likely flaky</span> }<span class="spacer"></span>
              @if (a.facts.linkedDefectId && auth.can('defects.manage')) { <button mat-stroked-button (click)="toDefect(a)">Add to {{ a.facts.linkedDefect }}</button> }
              <button mat-button (click)="feedback(a.interactionId, 'Useful')">Useful</button><button mat-button (click)="feedback(a.interactionId, 'NotUseful')">Not useful</button></div>
              <p>{{ a.analysis.summary }}</p>
              <h4>Probable causes</h4>
              @for (c of a.analysis.probableCauses; track c.cause) { <div class="cause"><span class="pill">{{ c.likelihood }}</span> {{ c.cause }}<ul>@for (e of c.evidence; track e) { <li class="small">{{ e }}</li> }</ul></div> }
              @if (a.analysis.suspectedLocations.length) { <h4>Where to look</h4><ul>@for (l of a.analysis.suspectedLocations; track l.location) { <li><span class="mono">{{ l.location }}</span>: {{ l.reason }}</li> }</ul> }
              @if (a.analysis.recommendedActions.length) { <h4>Recommended actions</h4><ul>@for (r of a.analysis.recommendedActions; track r) { <li>{{ r }}</li> }</ul> }
            </div>
          }
          @if (repair(); as r) {
            <div class="panel facts"><h3><mat-icon class="inline">verified</mat-icon> Verified facts</h3>
              {{ r.facts.test }} · {{ r.facts.package }} v{{ r.facts.baseVersionNumber }} · <span class="mono">{{ r.facts.filePath }}</span>
              @for (w of r.warnings; track w) { <div class="warn-line"><mat-icon class="inline">warning</mat-icon> {{ w }}</div> }
            </div>
            <div class="panel ai"><div class="row"><h3><mat-icon class="inline">auto_awesome</mat-icon> AI repair proposal (unverified)</h3><span class="pill">confidence {{ r.confidence }}</span><span class="spacer"></span>
              @if (auth.can('testcode.edit')) { <button mat-flat-button (click)="applyRepair()">Create new package version for review</button> }</div>
              <p>{{ r.explanation }}</p><p class="small muted">Change: {{ r.changeSummary }}. The new version must pass validation and approval before it replaces the published one.</p>
              <div class="editor tall"><app-code-editor [value]="r.proposedContent" [original]="r.originalContent" [language]="lang(r.facts.filePath)" /></div>
            </div>
          }
        }

        <!-- ============ Test selection ============ -->
        @case ('selection') {
          <div class="panel wrap-row">
            <mat-form-field><mat-label>Product</mat-label><mat-select [(ngModel)]="productId" (ngModelChange)="productChanged()">
              @for (p of products(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field><mat-label>Release scope</mat-label><mat-select [(ngModel)]="releaseId"><mat-option [value]="undefined">–</mat-option>
              @for (r of releases(); track r.id) { <mat-option [value]="r.id">{{ r.versionLabel }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field><mat-label>Build</mat-label><mat-select [(ngModel)]="buildId"><mat-option [value]="undefined">–</mat-option>
              @for (b of builds(); track b.id) { <mat-option [value]="b.id">{{ b.buildNumber }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field class="narrow"><mat-label>Budget (min)</mat-label><input matInput type="number" [(ngModel)]="budget" /></mat-form-field>
            <mat-form-field class="narrow"><mat-label>Max tests</mat-label><input matInput type="number" [(ngModel)]="maxTests" /></mat-form-field>
            <mat-form-field class="full"><mat-label>Changed files (optional, one per line)</mat-label><textarea matInput rows="2" [(ngModel)]="changedFiles"></textarea></mat-form-field>
            <button mat-flat-button [disabled]="!productId || busy()" (click)="select()"><mat-icon>auto_awesome</mat-icon>Recommend tests</button>
          </div>
          @if (selection(); as s) {
            <div class="two">
              <div class="panel facts"><h3><mat-icon class="inline">verified</mat-icon> Risk-scored candidates ({{ s.totalCandidates }})</h3>
                <p class="small muted">{{ s.scoreDefinition }}</p>
                <table class="data compact"><thead><tr><th>Test case</th><th class="n">Score</th><th>Signals</th></tr></thead><tbody>
                  @for (c of s.candidates.slice(0, 40); track c.testCaseId) { <tr><td><a [routerLink]="['/test-cases', c.testCaseId]">{{ c.numberDisplay }}</a> {{ c.name }}</td><td class="n">{{ c.riskScore }}</td><td class="small">{{ c.signals.join('; ') }}</td></tr> }
                </tbody></table></div>
              <div class="panel ai"><div class="row"><h3><mat-icon class="inline">auto_awesome</mat-icon> AI recommendation (unverified)</h3><span class="spacer"></span>
                @if (auth.can('tests.execute') && s.selected.length) { <button mat-flat-button (click)="runSelected(s)"><mat-icon>play_arrow</mat-icon>Run {{ s.selected.length }} test(s)</button> }</div>
                <p>{{ s.summary }} <span class="muted">(~{{ s.estimatedMinutes }} min)</span></p>
                @for (w of s.warnings; track w) { <div class="warn-line"><mat-icon class="inline">warning</mat-icon> {{ w }}</div> }
                <ol>@for (t of s.selected; track t.testCaseId) { <li><b>{{ t.numberDisplay }}</b> {{ t.name }} <div class="small muted">{{ t.reason }}</div></li> }</ol>
                @if (s.deferred.length) { <h4>Deferred</h4><ul>@for (t of s.deferred; track t.testCaseId) { <li>{{ t.numberDisplay }} {{ t.name }} <span class="small muted">{{ t.reason }}</span></li> }</ul> }
              </div>
            </div>
          }
        }

        <!-- ============ Maintenance ============ -->
        @case ('maintenance') {
          <div class="panel wrap-row">
            <mat-form-field><mat-label>Product</mat-label><mat-select [(ngModel)]="productId">@for (p of products(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }</mat-select></mat-form-field>
            <button mat-flat-button [disabled]="!productId || busy()" (click)="maintain()"><mat-icon>auto_awesome</mat-icon>Review test health</button>
          </div>
          @if (maint(); as m) {
            <div class="panel facts"><h3><mat-icon class="inline">verified</mat-icon> Findings detected by the platform ({{ m.findings.length }})</h3>
              @if (!m.findings.length || !m.recommendations.length) { <p>{{ m.summary }}</p> }
              @if (!m.recommendations.length) { @for (w of m.warnings; track w) { <div class="warn-line"><mat-icon class="inline">warning</mat-icon> {{ w }}</div> } }
              <table class="data compact"><tbody>@for (f of m.findings; track f.id) {
                <tr><td class="mono">{{ f.id }}</td><td><span class="pill">{{ f.kind | humanize }}</span></td><td><b>{{ f.title }}</b><div class="small muted">{{ f.evidence }}</div></td>
                  <td class="links">@for (id of f.testCaseIds; track id; let i = $index) { <a [routerLink]="['/test-cases', id]">{{ f.testCaseNumbers[i] }}</a> }</td>
                  <td>@if (f.testExecutionId && status()?.allowSourceCode) { <button mat-button (click)="repairFrom(f.testExecutionId)">Propose repair</button> }</td></tr>
              }</tbody></table></div>
            @if (m.recommendations.length) {
              <div class="panel ai"><h3><mat-icon class="inline">auto_awesome</mat-icon> AI recommendations (unverified)</h3><p>{{ m.summary }}</p>
                @for (w of m.warnings; track w) { <div class="warn-line"><mat-icon class="inline">warning</mat-icon> {{ w }}</div> }
                @for (r of m.recommendations; track r.findingId) { <div class="cause"><span class="mono">{{ r.findingId }}</span> <span class="pill">{{ r.confidence }}</span> {{ r.recommendation }}
                  @if (r.proposedChange) { <div class="small muted">Proposed: {{ r.proposedChange }}</div> }</div> }
              </div>
            }
          }
        }

        <!-- ============ History ============ -->
        @case ('history') {
          <div class="row"><mat-checkbox [(ngModel)]="mineOnly" (ngModelChange)="loadHistory()">Only mine</mat-checkbox><span class="muted small">Every request is recorded: what was sent (sizes, masking), model, tokens, and what was done with it.</span></div>
          <table class="data">
            <thead><tr><th>When</th><th>Who</th><th>Capability</th><th>Subject</th><th>Status</th><th class="n">Masked</th><th class="n">Tokens in/out</th><th>Outcome</th></tr></thead>
            <tbody>@for (h of history(); track h.id) {
              <tr><td>{{ h.createdAt | date: 'short' }}</td><td>{{ h.userName }}</td><td>{{ h.feature | humanize }}</td><td>{{ h.subjectLabel }}</td>
                <td><app-status [value]="h.status === 'Succeeded' ? 'Passed' : 'Failed'" [label]="h.status" />@if (h.error) { <div class="small muted">{{ h.error }}</div> }</td>
                <td class="n">{{ h.redactionCount }}</td><td class="n">{{ h.inputTokens ?? '–' }} / {{ h.outputTokens ?? '–' }}</td>
                <td>{{ h.outcome ?? '' }}{{ h.outcomeBy ? ' by ' + h.outcomeBy : '' }}</td></tr>
            } @empty { <tr><td colspan="8" class="empty">No AI requests yet.</td></tr> }</tbody>
          </table>
        }
      }
    </div>`,
  styles: `
    .tabs { display: flex; gap: 4px; margin: 8px 0 12px; flex-wrap: wrap; } .tabs a { padding: 6px 12px; border-radius: 4px; cursor: pointer; color: var(--muted); }
    .tabs a.on { background: var(--selected); color: var(--text); font-weight: 600; }
    .banner { display: flex; gap: 8px; align-items: center; padding: 8px 12px; border-radius: 6px; margin-bottom: 8px; } .banner.warn { background: var(--warn-bg); color: var(--warn); }
    .legend { font-size: 12px; color: var(--muted); } .fact-key { color: var(--good); font-weight: 600; } .ai-key { color: #6f42c1; font-weight: 600; }
    .facts { border-left: 4px solid var(--good); } .facts h3 { color: var(--good); }
    .ai { border-left: 4px solid #6f42c1; background: linear-gradient(0deg, var(--surface), var(--surface)), #f8f5ff; } .ai h3 { color: #6f42c1; }
    .wrap-row mat-form-field { width: 200px; } .wrap-row .wide { width: 320px; } .wrap-row .narrow { width: 110px; } .wrap-row .full { width: 100%; }
    .sugg { display: flex; gap: 8px; padding: 8px; border: 1px solid var(--border); border-radius: 6px; margin-top: 8px; } .sugg.on { border-color: #6f42c1; } .grow { flex: 1; }
    .sugg ol { margin: 4px 0; padding-left: 20px; } .small { font-size: 12px; } .warn-line { color: var(--warn); font-size: 12px; } .err { color: var(--bad); white-space: pre-wrap; }
    .editor { height: 320px; border: 1px solid var(--border); border-radius: 4px; overflow: hidden; } .editor.tall { height: 460px; } h4 { margin: 10px 0 4px; }
    .two { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(460px, 100%), 1fr)); gap: 12px; } .two .panel + .panel { margin-top: 0; }
    .links a { display: block; white-space: nowrap; } .cause { margin: 6px 0; } .cause ul { margin: 2px 0; } td.n, th.n { text-align: right; } table.compact td { padding: 3px 6px; }
  `,
})
export class AiAssistant {
  private ai = inject(AiApi);
  private q = inject(QualityApi);
  private api = inject(ApiService);
  private ui = inject(UiService);
  private router = inject(Router);
  private launcher = inject(RunLauncher);
  auth = inject(AuthService);
  tabs: [Tab, string][] = [['tests', 'Suggest test cases'], ['code', 'Draft test code'], ['failure', 'Analyse a failure'], ['selection', 'Select tests'], ['maintenance', 'Test maintenance'], ['history', 'History']];
  targets = CODE_TARGETS;
  tab = signal<Tab>('tests');
  status = signal<AiStatus | null>(null);
  ready = computed(() => this.status()?.ready ?? false);
  busy = signal(false);
  products = signal<Product[]>([]);
  featureHits = signal<FeatureSearchHit[]>([]);
  caseHits = signal<TestCaseSummary[]>([]);
  failures = signal<FailureSuggestion[]>([]);
  releases = signal<ReleaseSummary[]>([]);
  builds = signal<BuildSummary[]>([]);
  gen = signal<TestGenerationResult | null>(null);
  picked = signal(new Set<number>());
  code = signal<CodeGenerationResult | null>(null);
  analysis = signal<FailureAnalysisResult | null>(null);
  repair = signal<RepairResult | null>(null);
  selection = signal<SelectionResult | null>(null);
  maint = signal<MaintenanceResult | null>(null);
  history = signal<AiInteraction[]>([]);
  featureText = ''; featureId?: string; maxSuggestions = 8; focus = '';
  caseText = ''; testCaseId?: string; target: CodeTarget = 'PlaywrightTypeScript'; instructions = '';
  executionId?: string;
  productId?: string; releaseId?: string; buildId?: string; budget?: number; maxTests = 40; changedFiles = '';
  mineOnly = true;

  constructor() {
    this.refreshStatus();
    this.api.products().subscribe(p => this.products.set(p));
    const qp = inject(ActivatedRoute).snapshot.queryParamMap;
    if (qp.get('feature')) { this.featureId = qp.get('feature')!; this.api.feature(this.featureId).subscribe(f => { this.featureText = f.fields.name; this.searchFeatures(); }); }
    if (qp.get('testCase')) { this.testCaseId = qp.get('testCase')!; this.api.testCases({ text: qp.get('testCaseNumber') ?? '' }).subscribe(r => this.caseHits.set(r.items)); }
    if (qp.get('execution')) this.executionId = qp.get('execution')!;
    this.setTab((qp.get('tab') as Tab) ?? 'tests');
  }

  refreshStatus() { this.ai.status().subscribe({ next: s => this.status.set(s), error: e => this.ui.error(e) }); }

  setTab(t: Tab) {
    this.tab.set(t);
    if (t === 'failure') this.q.suggestions({ days: 7 }).subscribe(f => this.failures.set(f));
    if (t === 'history') this.loadHistory();
  }

  /** Runs one AI request with a busy indicator and refreshes the usage counter. */
  private async ask<T>(work: Promise<T>, set: (v: T) => void) {
    this.busy.set(true);
    try { set(await work); } catch (e) { this.ui.error(e); } finally { this.busy.set(false); this.refreshStatus(); }
  }

  searchFeatures() {
    if (!this.featureText.trim()) return;
    this.api.searchFeatures({ text: this.featureText.trim() }, 1, 30).subscribe(r => { this.featureHits.set(r.items); this.featureId ??= r.items[0]?.node.id; });
  }
  searchCases() { this.api.testCases({ text: this.caseText.trim() }, 1, 30).subscribe(r => { this.caseHits.set(r.items); this.testCaseId = r.items[0]?.id; }); }
  productChanged() {
    this.releaseId = undefined; this.buildId = undefined;
    if (!this.productId) return;
    this.q.releases(this.productId).subscribe(r => this.releases.set(r.filter(x => !x.isReleased)));
    this.q.builds(this.productId).subscribe(b => this.builds.set(b.slice(0, 30)));
  }

  suggestTests() { this.picked.set(new Set()); this.ask(firstValueFrom(this.ai.suggestTests(this.featureId!, this.maxSuggestions, this.focus || undefined)), r => this.gen.set(r)); }
  toggle(i: number) { this.picked.update(s => { const n = new Set(s); if (n.has(i)) n.delete(i); else n.add(i); return n; }); }
  async applyTests() {
    try {
      const r = await firstValueFrom(this.ai.applyTests(this.gen()!.interactionId, [...this.picked()]));
      this.ui.ok(r.message);
      this.picked.set(new Set());
    } catch (e) { this.ui.error(e); }
  }

  generateCode() { this.ask(firstValueFrom(this.ai.generateCode(this.testCaseId!, this.target, this.instructions || undefined)), r => this.code.set(r)); }
  async applyCode() {
    try {
      const v = await firstValueFrom(this.ai.applyCode(this.code()!.interactionId));
      this.ui.ok(`Uploaded as version ${v.versionNumber}; validation is running.`);
      this.router.navigate(['/test-code/versions', v.id]);
    } catch (e) { this.ui.error(e); }
  }

  analyze() { this.repair.set(null); this.ask(firstValueFrom(this.ai.analyze(this.executionId!)), r => this.analysis.set(r)); }
  proposeRepair() { this.analysis.set(null); this.ask(firstValueFrom(this.ai.repair(this.executionId!)), r => this.repair.set(r)); }
  repairFrom(executionId: string) { this.executionId = executionId; this.setTab('failure'); this.proposeRepair(); }
  async applyRepair() {
    try {
      const v = await firstValueFrom(this.ai.applyRepair(this.repair()!.interactionId));
      this.ui.ok(`Version ${v.versionNumber} created; it must be validated and approved before it runs.`);
      this.router.navigate(['/test-code/versions', v.id]);
    } catch (e) { this.ui.error(e); }
  }
  async toDefect(a: FailureAnalysisResult) {
    try { await firstValueFrom(this.ai.analysisToDefect(a.interactionId, a.facts.linkedDefectId!)); this.ui.ok(`Added to ${a.facts.linkedDefect} as an AI-generated comment`); }
    catch (e) { this.ui.error(e); }
  }

  select() {
    const files = this.changedFiles.split('\n').map(x => x.trim()).filter(Boolean);
    this.ask(firstValueFrom(this.ai.select({ productId: this.productId!, releaseId: this.releaseId, buildId: this.buildId, changedFiles: files.length ? files : undefined,
      budgetMinutes: this.budget || undefined, maxTests: this.maxTests })), r => this.selection.set(r));
  }
  runSelected(s: SelectionResult) {
    this.launcher.launch({ title: 'Run AI-recommended tests', description: `${s.selected.length} test case(s) chosen from ${s.totalCandidates} risk-scored candidates. ${s.summary}`,
      scope: { name: 'AI-recommended selection', testCaseIds: s.selected.map(t => t.testCaseId), includeManual: false } });
    if (s.interactionId) this.ai.feedback(s.interactionId, 'Useful', 'Run started from the recommendation').subscribe();
  }

  maintain() { this.ask(firstValueFrom(this.ai.maintenance(this.productId!)), r => this.maint.set(r)); }

  loadHistory() { this.ai.history(this.mineOnly).subscribe({ next: h => this.history.set(h.items), error: e => this.ui.error(e) }); }
  feedback(id: string, outcome: 'Useful' | 'NotUseful') { this.ai.feedback(id, outcome).subscribe({ next: () => this.ui.ok('Thanks, feedback recorded'), error: e => this.ui.error(e) }); }

  lang(path: string) {
    const ext = path.split('.').pop()?.toLowerCase();
    return ext === 'cs' ? 'csharp' : ext === 'ts' || ext === 'js' ? 'typescript' : ext === 'json' ? 'json' : ext === 'csproj' || ext === 'xml' ? 'xml' : ext === 'py' ? 'python' : 'plaintext';
  }
}
