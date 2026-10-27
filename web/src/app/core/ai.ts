import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { toParams } from './api.service';
import { PackageVersionSummary, PagedResult } from './models';

export interface AiStatus { enabled: boolean; ready: boolean; provider: string; model: string; unavailable?: string; dailyLimit: number; usedToday: number; allowSourceCode: boolean; allowScreenshots: boolean; }
export interface AiPolicy { isEnabled: boolean; allowSourceCode: boolean; allowScreenshots: boolean; maskEmails: boolean; maskPhoneNumbers: boolean; additionalRedactionPatterns?: string; dailyRequestsPerUser: number; projectConventions?: string; version: number; }
export interface ContextSection { name: string; chars: number; }

export interface SuggestedStep { action: string; expected: string; }
export interface TestSuggestion { index: number; name: string; scenarioType: string; priority: string; objective: string; preconditions: string; steps: SuggestedStep[]; testData: string; requirementCodes: string[]; rationale: string; similarExisting?: string; }
export interface TestGenerationResult {
  interactionId: string; notes: string; warnings: string[]; suggestions: TestSuggestion[];
  facts: { featureId: string; productId: string; feature: string; sequenceNumber: number; requirements: number; existingTestCases: number; defects: number; sourcesUsed: string[] };
}
export interface ApplyResult { createdIds: string[]; labels: string[]; message: string; }

export type CodeTarget = 'FlaUiCSharp' | 'PlaywrightCSharp' | 'PlaywrightTypeScript' | 'ApiTests' | 'DatabaseValidation';
export const CODE_TARGETS: [CodeTarget, string][] = [['FlaUiCSharp', 'FlaUI / C# (WinForms)'], ['PlaywrightTypeScript', 'Playwright / TypeScript'], ['PlaywrightCSharp', 'Playwright / C#'], ['ApiTests', 'API tests (C#)'], ['DatabaseValidation', 'Database validation (C#)']];
export interface GeneratedFile { path: string; content: string; }
export interface CodeGenerationResult {
  interactionId: string; files: GeneratedFile[]; assumptions: string[]; todos: string[]; notes: string; warnings: string[];
  facts: { testCaseId: string; testCase: string; featureId?: string; feature?: string; featureSequence?: number; productId?: string; target: string; convention: string; referencePackage?: string; referenceFiles: string[] };
}

export interface FailureAnalysisResult {
  interactionId: string; warnings: string[];
  facts: {
    testExecutionId: string; runId: string; test: string; run: string; outcome: string; errorExcerpt?: string; executions30d: number; failures30d: number; lastPassedRun?: string; lastPassedBuild?: string;
    firstFailingBuild?: string; history: { run: string; outcome: string; build?: string; environment?: string; at?: string }[]; linkedDefect?: string; linkedDefectId?: string;
    recentChanges: { build: string; at: string; detail: string }[]; environmentHealth?: string; evidenceSent: string[]; evidenceNotSent: string[];
  };
  analysis: { summary: string; category: string; confidence: string; probableCauses: { cause: string; likelihood: string; evidence: string[] }[]; suspectedLocations: { location: string; reason: string }[]; recommendedActions: string[]; likelyFlaky: boolean };
}

export interface SelectionCandidate { testCaseId: string; numberDisplay: string; name: string; priority: string; isAutomated: boolean; estimatedMinutes: number; signals: string[]; riskScore: number; }
export interface SelectedTest { testCaseId: string; numberDisplay: string; name: string; order: number; reason: string; }
export interface SelectionResult { interactionId?: string; candidates: SelectionCandidate[]; totalCandidates: number; scoreDefinition: string; selected: SelectedTest[]; deferred: SelectedTest[]; summary: string; estimatedMinutes: number; warnings: string[]; }

export interface MaintenanceFinding { id: string; kind: string; title: string; evidence: string; testCaseIds: string[]; testCaseNumbers: string[]; testExecutionId?: string; }
export interface MaintenanceResult { interactionId?: string; findings: MaintenanceFinding[]; recommendations: { findingId: string; recommendation: string; proposedChange: string; confidence: string }[]; summary: string; warnings: string[]; }

export interface RepairResult {
  interactionId: string; originalContent: string; proposedContent: string; explanation: string; changeSummary: string; confidence: string; warnings: string[];
  facts: { testExecutionId: string; test: string; packageId: string; package: string; baseVersionId: string; baseVersionNumber: number; filePath: string; errorExcerpt?: string };
}

export interface AiInteraction {
  id: string; feature: string; userName: string; provider: string; model: string; subjectType?: string; subjectId?: string; subjectLabel?: string; status: 'Succeeded' | 'Failed' | 'Refused';
  error?: string; redactionCount: number; inputChars: number; imageCount: number; inputTokens?: number; outputTokens?: number; durationMs: number; createdAt: string;
  outcome?: string; outcomeBy?: string; outcomeAt?: string; outcomeNote?: string; context: ContextSection[]; result?: unknown; applied?: unknown;
}

const API = '/api/v1/ai';

@Injectable({ providedIn: 'root' })
export class AiApi {
  private http = inject(HttpClient);
  status() { return this.http.get<AiStatus>(`${API}/status`); }
  policy() { return this.http.get<AiPolicy>(`${API}/policy`); }
  savePolicy(p: AiPolicy) { return this.http.put<AiPolicy>(`${API}/policy`, p); }
  suggestTests(featureId: string, maxSuggestions: number, focus?: string) { return this.http.post<TestGenerationResult>(`${API}/test-suggestions`, { featureId, maxSuggestions, focus }); }
  applyTests(id: string, indexes: number[]) { return this.http.post<ApplyResult>(`${API}/test-suggestions/${id}/apply`, { indexes }); }
  generateCode(testCaseId: string, target: CodeTarget, instructions?: string) { return this.http.post<CodeGenerationResult>(`${API}/code`, { testCaseId, target, instructions }); }
  applyCode(id: string, packageName?: string) { return this.http.post<PackageVersionSummary>(`${API}/code/${id}/apply`, { packageName }); }
  analyze(testExecutionId: string) { return this.http.post<FailureAnalysisResult>(`${API}/failure-analysis/${testExecutionId}`, {}); }
  analysisToDefect(id: string, defectId: string) { return this.http.post<void>(`${API}/failure-analysis/${id}/defects/${defectId}`, {}); }
  select(body: { productId: string; releaseId?: string; buildId?: string; changedFiles?: string[]; budgetMinutes?: number; maxTests: number }) { return this.http.post<SelectionResult>(`${API}/test-selection`, body); }
  maintenance(productId: string) { return this.http.post<MaintenanceResult>(`${API}/maintenance/${productId}`, {}); }
  repair(testExecutionId: string) { return this.http.post<RepairResult>(`${API}/repairs/${testExecutionId}`, {}); }
  applyRepair(id: string) { return this.http.post<PackageVersionSummary>(`${API}/repairs/${id}/apply`, {}); }
  history(mine: boolean, page = 1) { return this.http.get<PagedResult<AiInteraction>>(`${API}/history`, { params: toParams({ mine, page, pageSize: 50 }) }); }
  interaction(id: string) { return this.http.get<AiInteraction>(`${API}/history/${id}`); }
  feedback(id: string, outcome: 'Useful' | 'NotUseful' | 'Rejected', note?: string) { return this.http.post<void>(`${API}/history/${id}/feedback`, { outcome, note }); }
}
