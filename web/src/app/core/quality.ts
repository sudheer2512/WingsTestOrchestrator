import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { toParams } from './api.service';
import { Artifact, PagedResult, PlanSummary, RunSummary } from './models';

// ---- models (Phase 5: quality) ----

export type DefectStatus = 'Open' | 'Assigned' | 'InProgress' | 'Fixed' | 'ReadyForRetest' | 'Verified' | 'Closed' | 'Reopened' | 'Duplicate' | 'Rejected' | 'Deferred';
export type DefectSeverity = 'Critical' | 'Major' | 'Minor' | 'Trivial';
export const DEFECT_STATUSES: DefectStatus[] = ['Open', 'Assigned', 'InProgress', 'Fixed', 'ReadyForRetest', 'Verified', 'Closed', 'Reopened', 'Duplicate', 'Rejected', 'Deferred'];
export const SEVERITIES: DefectSeverity[] = ['Critical', 'Major', 'Minor', 'Trivial'];

export interface DefectSummary {
  id: string; number: number; numberDisplay: string; title: string; status: DefectStatus; severity: DefectSeverity; priority: string;
  productId?: string; productName?: string; featureId?: string; featureName?: string; featureSequence?: number; testCaseId?: string; testCaseNumber?: string;
  assignedTo?: string; occurrenceCount: number; lastSeenAt?: string; externalKey?: string; externalUrl?: string; createdAt: string; createdBy: string; version: number;
}
export interface DefectEvent { id: number; timestamp: string; userName: string; kind: string; fromStatus?: DefectStatus; toStatus?: DefectStatus; text?: string; }
export interface DefectOccurrence { testExecutionId: string; runId: string; runNumber: string; outcome: string; errorMessage?: string; completedAt?: string; buildVersion?: string; environmentName?: string; }
export interface DefectDetail {
  summary: DefectSummary; description?: string; failingRunId?: string; failingRunNumber?: string; failingTestExecutionId?: string; buildVersion?: string;
  environmentId?: string; environmentName?: string; reproductionSteps?: string; expectedResult?: string; actualResult?: string; rootCause?: string;
  fixVersionId?: string; fixVersionLabel?: string; verificationResult?: string; duplicateOfId?: string; duplicateOfNumber?: string; fingerprint?: string;
  history: DefectEvent[]; occurrences: DefectOccurrence[]; evidence: Artifact[]; allowedTransitions: DefectStatus[]; retests: RunSummary[];
}
export interface DefectUpsert {
  title: string; description?: string; productId?: string; featureId?: string; testCaseId?: string; buildVersion?: string; environmentId?: string;
  severity: DefectSeverity; priority: string; reproductionSteps?: string; expectedResult?: string; actualResult?: string; assignedTo?: string;
  rootCause?: string; fixVersionId?: string; version?: number;
}
export interface DefectTransition {
  to: DefectStatus; comment?: string; assignedTo?: string; duplicateOfId?: string; rootCause?: string; fixVersionId?: string; verificationResult?: string;
  retest?: boolean; retestEnvironmentId?: string; version?: number;
}
export interface DefectFilter { productId?: string; featureId?: string; status?: DefectStatus; severity?: DefectSeverity; assignedTo?: string; text?: string; openOnly?: boolean; page?: number; pageSize?: number; }
export interface FailureSuggestion {
  testExecutionId: string; runId: string; runNumber: string; displayName: string; testCaseId?: string; testCaseNumber?: string; featureId?: string; featureName?: string;
  errorMessage?: string; fingerprint: string; clusterKey: string; completedAt?: string; existingDefectId?: string; existingDefectNumber?: string; existingDefectStatus?: DefectStatus;
  possibleDuplicateId?: string; possibleDuplicateNumber?: string; suggestedTitle: string; suggestedSeverity: DefectSeverity; suggestedPriority: string;
}
export interface FailureCluster { clusterKey: string; sampleMessage: string; failures: number; distinctTests: number; firstSeen: string; lastSeen: string; tests: string[]; defectNumbers: string[]; }
export interface RecurringFailure {
  testCaseId?: string; testCaseNumber?: string; identifier: string; displayName: string; executions: number; failures: number; consecutiveFailures: number; flips: number;
  lastFailedAt: string; lastError?: string; defectId?: string; defectNumber?: string;
}
export interface Tracker { id: string; name: string; kind: string; target: string; baseUrl?: string; username?: string; hasToken: boolean; productId?: string; isEnabled: boolean; version: number; }

export type ReleaseStatus = 'Planning' | 'Testing' | 'SignedOff' | 'Released';
export type GateType = 'SuitePassed' | 'NoOpenDefects' | 'ChangedFeaturesTested' | 'MinAutomationCoverage' | 'TaggedTestsPassed' | 'MinPassRate';
export type RiskLevel = 'Low' | 'Medium' | 'High' | 'Critical';
export type ChangeKind = 'Added' | 'Modified' | 'Deprecated';
export interface ReleaseSummary {
  id: string; productId: string; productName: string; versionLabel: string; status: ReleaseStatus; testingStartDate?: string; releaseDate?: string; isReleased: boolean;
  buildVersion?: string; readiness: 'Ready' | 'Warning' | 'Blocked'; gatesPassed: number; gatesTotal: number; version: number;
}
export interface ReleaseFeature { featureId: string; sequenceNumber: number; name: string; kind: ChangeKind; source: string; risk: RiskLevel; changeId?: string; notes?: string; testCases: number; automated: number; executed: number; passed: number; status: string; }
export interface RequiredTest { testCaseId: string; numberDisplay: string; name: string; priority: string; isAutomated: boolean; reasons: string[]; outcome?: string; at?: string; runId?: string; runNumber?: string; environmentName?: string; }
export interface Waiver { id: string; justification: string; approvedBy: string; approvedAt: string; }
export interface GateResult {
  gateId: string; name: string; type: GateType; parameter?: string; isBlocking: boolean; isReleaseSpecific: boolean; status: 'Passed' | 'Failed' | 'Waived' | 'Warning';
  detail: string; measured?: number; numerator?: number; denominator?: number; definition: string; waiver?: Waiver;
}
export interface EnvironmentCoverage { environmentId?: string; name: string; executed: number; passed: number; failed: number; lastRunAt?: string; }
export interface ReleaseMetrics {
  featuresAdded: number; featuresModified: number; featuresDeprecated: number; testsRequired: number; testsCompleted: number; testsPassed: number; testsFailed: number;
  failedCritical: number; openCriticalDefects: number; regressionTotal: number; regressionCompleted: number; passRate?: number;
}
export interface Approval { id: string; decision: 'Approved' | 'Rejected'; comment: string; approvedBy: string; approvedAt: string; reportSha256: string; }
export interface ReleaseWorkspace {
  release: ReleaseSummary; windowStart: string; windowEnd: string; knownRisks?: string; metrics: ReleaseMetrics; features: ReleaseFeature[]; requiredTests: RequiredTest[];
  gates: GateResult[]; openCriticalDefects: DefectSummary[]; environments: EnvironmentCoverage[]; risks: string[]; plans: PlanSummary[]; runs: RunSummary[]; approvals: Approval[];
  failedCriticalTestCaseIds: string[];
}
export interface Gate { id: string; productId: string; releaseId?: string; name: string; type: GateType; parameter?: string; isBlocking: boolean; order: number; isEnabled: boolean; definition: string; version: number; }
export interface GateUpsert { productId: string; releaseId?: string; name: string; type: GateType; parameter?: string; isBlocking: boolean; order: number; isEnabled: boolean; version?: number; }

export type ChannelType = 'Email' | 'Teams' | 'Webhook';
export interface InboxItem { eventId: string; type: string; severity: string; title: string; body?: string; link?: string; createdAt: string; readAt?: string; acknowledgedAt?: string; acknowledgedBy?: string; escalatedAt?: string; }
export interface Inbox { unread: number; items: InboxItem[]; }
export interface Channel { id: string; name: string; type: ChannelType; recipients?: string; hasUrl: boolean; urlHint?: string; hasSigningSecret: boolean; isEnabled: boolean; version: number; }
export interface Rule { id: string; name: string; eventTypes: string[]; productId?: string; productName?: string; minSeverity: string; userId?: string; userName?: string; emailUser: boolean; channelId?: string; channelName?: string; isEnabled: boolean; version: number; }
export interface Escalation { id: string; name: string; eventTypes: string[]; afterMinutes: number; channelId: string; channelName?: string; isEnabled: boolean; version: number; }
export interface Template { eventType: string; description: string; subject: string; body: string; isCustomized: boolean; }
export interface Delivery { id: string; target: string; status: 'Pending' | 'Sent' | 'Failed'; attempts: number; error?: string; createdAt: string; sentAt?: string; isEscalation: boolean; }
export interface EventLog { id: string; type: string; severity: string; title: string; link?: string; createdAt: string; recipients: number; acknowledgedAt?: string; acknowledgedBy?: string; escalatedAt?: string; deliveries: Delivery[]; }
export interface EventType { type: string; description: string; }

export type VcsType = 'Git' | 'Tfvc';
export type ImpactReason = 'SourceMapping' | 'CommitMessage' | 'Dependency';
export interface Repository { id: string; name: string; type: VcsType; url?: string; productId?: string; defaultBranch?: string; version: number; }
export interface IntegrationKey { id: string; name: string; keyPrefix: string; isEnabled: boolean; lastUsedAt?: string; createdAt: string; createdBy: string; }
export interface Commit { id: string; author?: string; message: string; timestamp?: string; files?: string[]; }
export interface BuildReport { product: string; buildNumber: string; repository?: string; vcs?: string; branch?: string; release?: string; changedFiles: string[]; commits: Commit[]; trigger: 'none' | 'affected'; environmentCode?: string; includeDependents: boolean; }
export interface ImpactedFeature { featureId: string; sequenceNumber: number; name: string; reason: ImpactReason; evidence: string; }
export interface AffectedTest { testCaseId: string; numberDisplay: string; name: string; priority: string; isAutomated: boolean; viaFeatures: number[]; latestOutcome?: string; }
export interface SuggestedSuite { suiteId: string; numberDisplay: string; name: string; matching: number; total: number; }
export interface BuildSummary {
  id: string; productId: string; productName: string; buildNumber: string; branch?: string; commitId?: string; buildUrl?: string; releaseId?: string; releaseLabel?: string;
  repositoryName?: string; changedFileCount: number; commitCount: number; impactedFeatureCount: number; triggeredRunId?: string; triggeredRunNumber?: string; gateStatus?: string; createdAt: string;
}
export interface BuildImpact {
  build?: BuildSummary; changedFiles: string[]; commits: Commit[]; features: ImpactedFeature[]; tests: AffectedTest[]; suites: SuggestedSuite[];
  unmappedFiles: string[]; warnings: string[]; triggeredRun?: RunSummary;
}

export interface TrendPoint { date: string; passed: number; failed: number; blocked: number; other: number; }
export interface ProductRow { productId: string; code: string; name: string; features: number; featuresWithTests: number; testCases: number; automated: number; automationPercent?: number; latestPassed: number; latestFailed: number; openDefects: number; criticalDefects: number; nextRelease?: string; nextReleaseReadiness?: string; }
export interface GlobalDashboard {
  products: number; features: number; featuresWithTests: number; featuresWithoutTests: number; automatedTests: number; manualTests: number; latestPassed: number; latestFailed: number;
  latestBlocked: number; neverExecuted: number; untestedChangedFeatures: number; openDefects: number; openDefectsBySeverity: Record<string, number>; automationCoverage?: number;
  featureCoverage?: number; requirementCoverage?: number; releases: ReleaseSummary[]; trend: TrendPoint[]; productRows: ProductRow[]; definitions: Record<string, string>;
}
export interface ModuleRow { featureId: string; sequenceNumber: number; name: string; features: number; featuresWithTests: number; testCases: number; automated: number; passed: number; failed: number; notExecuted: number; openDefects: number; featureCoverage?: number; passRate?: number; }
export interface FeatureStatusRow { featureId: string; sequenceNumber: number; name: string; depth: number; moduleId?: string; testCases: number; automated: number; coverage: string; latestOutcome?: string; lastExecutedAt?: string; openDefects: number; }
export interface RiskArea { featureId: string; sequenceNumber: number; name: string; reason: string; score: number; }
export interface DefectBucket { key: string; label: string; critical: number; major: number; minor: number; trivial: number; total: number; }
export interface ProductDashboard {
  productId: string; productName: string; features: number; featuresWithTests: number; testCases: number; automated: number; automationPercent?: number; modules: ModuleRow[];
  featureStatus: FeatureStatusRow[]; latestRegression: RunSummary[]; failureTrend: TrendPoint[]; automationTrend: { week: string; testCases: number; automated: number }[];
  riskAreas: RiskArea[]; defectsByModule: DefectBucket[]; defectsByStatus: Record<string, number>;
  buildQuality: { buildId: string; buildNumber: string; at: string; impactedFeatures: number; runNumber?: string; runId?: string; passed: number; failed: number; gateStatus?: string }[];
  definitions: Record<string, string>;
}
export interface ExecutionDashboard {
  running: { jobId: string; runId: string; runNumber: string; runName: string; framework: string; agentName?: string; status: string; startedAt?: string; tests: number }[];
  queue: { framework: string; requiresDesktop: boolean; queued: number; oldestQueuedAt?: string }[]; agentsTotal: number; agentsOnline: number; agentsBusy: number; slotUtilizationNow?: number;
  agents: { agentId: string; name: string; kind: string; isOnline: boolean; inMaintenance: boolean; slots: number; activeJobs: number; busyHours24h: number; utilization24h?: number }[];
  failedRuns: RunSummary[]; durations: { framework: string; jobs: number; averageSeconds: number; maxSeconds: number }[]; throughput: { hour: string; tests: number; failed: number }[];
  retries7d: number; jobs7d: number; retryRate?: number; environmentFailures: { environmentId?: string; name: string; infrastructureErrors: number; runs: number; health: string }[];
}

export interface ReportParameters { productId?: string; featureId?: string; releaseId?: string; compareReleaseId?: string; fromSequence?: number; days: number; }
export interface ReportColumn { key: string; label: string; }
export interface ReportDefinition { key: string; title: string; category: string; description: string; definition: string; parameters: string[]; columns: ReportColumn[]; requiredPermission?: string; }
export interface ReportResult { key: string; title: string; description: string; definition: string; columns: ReportColumn[]; rows: (string | null)[][]; summary: { label: string; value: string }[]; generatedAt: string; parameterText: string; }
export interface SavedReport { id: string; name: string; reportKey: string; parameters: ReportParameters; columns?: string[]; owner: string; isShared: boolean; scheduleCron?: string; deliveryChannelId?: string; format: string; nextRunAt?: string; lastRunAt?: string; isMine: boolean; version: number; }
export interface DirectoryUser { id: string; userName: string; displayName: string; }

const API = '/api/v1';

/** HTTP client for defects, releases, notifications, integrations, dashboards and reports. */
@Injectable({ providedIn: 'root' })
export class QualityApi {
  private http = inject(HttpClient);

  users() { return this.http.get<DirectoryUser[]>(`${API}/users/directory`); }

  // defects
  defects(f: DefectFilter) { return this.http.get<PagedResult<DefectSummary>>(`${API}/defects`, { params: toParams(f) }); }
  defect(id: string) { return this.http.get<DefectDetail>(`${API}/defects/${id}`); }
  createDefect(body: DefectUpsert) { return this.http.post<DefectDetail>(`${API}/defects`, body); }
  updateDefect(id: string, body: DefectUpsert) { return this.http.put<DefectDetail>(`${API}/defects/${id}`, body); }
  transition(id: string, body: DefectTransition) { return this.http.post<DefectDetail>(`${API}/defects/${id}/transition`, body); }
  comment(id: string, text: string) { return this.http.post<DefectDetail>(`${API}/defects/${id}/comments`, { text }); }
  deleteDefect(id: string) { return this.http.delete<void>(`${API}/defects/${id}`); }
  suggestions(q: { runId?: string; productId?: string; days?: number }) { return this.http.get<FailureSuggestion[]>(`${API}/defects/suggestions`, { params: toParams(q) }); }
  fromFailure(body: { testExecutionId: string; title?: string; severity?: DefectSeverity; priority?: string; assignedTo?: string }) {
    return this.http.post<{ defect: DefectDetail; linkedToExisting: boolean }>(`${API}/defects/from-failure`, body);
  }
  clusters(q: { productId?: string; days?: number }) { return this.http.get<FailureCluster[]>(`${API}/defects/clusters`, { params: toParams(q) }); }
  recurring(q: { productId?: string; days?: number; minFailures?: number }) { return this.http.get<RecurringFailure[]>(`${API}/defects/recurring`, { params: toParams(q) }); }
  push(id: string, connectionId: string) { return this.http.post<DefectDetail>(`${API}/defects/${id}/push`, { connectionId }); }

  // releases and gates
  releases(productId?: string) { return this.http.get<ReleaseSummary[]>(`${API}/releases`, { params: toParams({ productId }) }); }
  release(id: string) { return this.http.get<ReleaseWorkspace>(`${API}/releases/${id}`); }
  updateRelease(id: string, body: { status: ReleaseStatus; testingStartDate?: string | null; releaseDate?: string | null; buildVersion?: string | null; knownRisks?: string | null; version?: number }) {
    return this.http.put<ReleaseWorkspace>(`${API}/releases/${id}`, body);
  }
  addChange(id: string, body: { featureId: string; kind: ChangeKind; risk: RiskLevel; notes?: string }) { return this.http.post<ReleaseWorkspace>(`${API}/releases/${id}/changes`, body); }
  removeChange(id: string, changeId: string) { return this.http.delete<ReleaseWorkspace>(`${API}/releases/${id}/changes/${changeId}`); }
  waive(id: string, gateId: string, justification: string) { return this.http.post<ReleaseWorkspace>(`${API}/releases/${id}/waivers`, { gateId, justification }); }
  revokeWaiver(id: string, waiverId: string) { return this.http.post<ReleaseWorkspace>(`${API}/releases/${id}/waivers/${waiverId}/revoke`, {}); }
  executeRelease(id: string, body: { environmentId?: string; buildVersion?: string; includeManual: boolean; onlyNotPassed: boolean; ignoreEnvironmentHealth?: boolean }) {
    return this.http.post<RunSummary>(`${API}/releases/${id}/execute`, body);
  }
  requestSignOff(id: string) { return this.http.post<void>(`${API}/releases/${id}/request-signoff`, {}); }
  signOff(id: string, decision: 'Approved' | 'Rejected', comment: string) { return this.http.post<ReleaseWorkspace>(`${API}/releases/${id}/signoff`, { decision, comment }); }
  releaseReport(id: string) { return this.http.get(`${API}/releases/${id}/report`, { responseType: 'blob' }); }
  approvalReport(id: string, approvalId: string) { return this.http.get(`${API}/releases/${id}/approvals/${approvalId}/report`, { responseType: 'blob' }); }
  gates(productId: string) { return this.http.get<Gate[]>(`${API}/quality-gates`, { params: toParams({ productId }) }); }
  saveGate(id: string | null, body: GateUpsert) { return id ? this.http.put<Gate>(`${API}/quality-gates/${id}`, body) : this.http.post<Gate>(`${API}/quality-gates`, body); }
  deleteGate(id: string) { return this.http.delete<void>(`${API}/quality-gates/${id}`); }

  // notifications
  inbox(unreadOnly = false, take = 50) { return this.http.get<Inbox>(`${API}/notifications/inbox`, { params: toParams({ unreadOnly, take }) }); }
  markRead(eventIds?: string[]) { return this.http.post<void>(`${API}/notifications/inbox/read`, { eventIds }); }
  acknowledge(eventId: string) { return this.http.post<void>(`${API}/notifications/${eventId}/acknowledge`, {}); }
  eventTypes() { return this.http.get<EventType[]>(`${API}/notifications/event-types`); }
  rules(all = false) { return this.http.get<Rule[]>(`${API}/notifications/rules`, { params: toParams({ all }) }); }
  saveRule(id: string | null, body: Partial<Rule>) { return id ? this.http.put<Rule>(`${API}/notifications/rules/${id}`, body) : this.http.post<Rule>(`${API}/notifications/rules`, body); }
  deleteRule(id: string) { return this.http.delete<void>(`${API}/notifications/rules/${id}`); }
  channels() { return this.http.get<Channel[]>(`${API}/notifications/channels`); }
  saveChannel(id: string | null, body: { name: string; type: ChannelType; recipients?: string; url?: string | null; signingSecret?: string | null; isEnabled: boolean; version?: number }) {
    return id ? this.http.put<Channel>(`${API}/notifications/channels/${id}`, body) : this.http.post<Channel>(`${API}/notifications/channels`, body);
  }
  deleteChannel(id: string) { return this.http.delete<void>(`${API}/notifications/channels/${id}`); }
  testChannel(id: string) { return this.http.post<{ eventId: string }>(`${API}/notifications/channels/${id}/test`, {}); }
  escalations() { return this.http.get<Escalation[]>(`${API}/notifications/escalations`); }
  saveEscalation(id: string | null, body: Partial<Escalation>) { return id ? this.http.put<Escalation>(`${API}/notifications/escalations/${id}`, body) : this.http.post<Escalation>(`${API}/notifications/escalations`, body); }
  deleteEscalation(id: string) { return this.http.delete<void>(`${API}/notifications/escalations/${id}`); }
  templates() { return this.http.get<Template[]>(`${API}/notifications/templates`); }
  saveTemplate(eventType: string, subject: string, body: string) { return this.http.put<Template>(`${API}/notifications/templates/${eventType}`, { subject, body }); }
  resetTemplate(eventType: string) { return this.http.delete<void>(`${API}/notifications/templates/${eventType}`); }
  eventLog(take = 100) { return this.http.get<EventLog[]>(`${API}/notifications/log`, { params: toParams({ take }) }); }

  // integrations
  builds(productId?: string) { return this.http.get<BuildSummary[]>(`${API}/integrations/builds`, { params: toParams({ productId }) }); }
  build(id: string) { return this.http.get<BuildImpact>(`${API}/integrations/builds/${id}`); }
  analyze(report: BuildReport) { return this.http.post<BuildImpact>(`${API}/integrations/impact`, report); }
  recordBuild(report: BuildReport) { return this.http.post<BuildImpact>(`${API}/integrations/builds`, report); }
  repositories() { return this.http.get<Repository[]>(`${API}/integrations/repositories`); }
  saveRepository(id: string | null, body: Partial<Repository>) { return id ? this.http.put<Repository>(`${API}/integrations/repositories/${id}`, body) : this.http.post<Repository>(`${API}/integrations/repositories`, body); }
  deleteRepository(id: string) { return this.http.delete<void>(`${API}/integrations/repositories/${id}`); }
  keys() { return this.http.get<IntegrationKey[]>(`${API}/integrations/keys`); }
  createKey(name: string) { return this.http.post<{ key: IntegrationKey; secret: string }>(`${API}/integrations/keys`, { name }); }
  setKeyEnabled(id: string, enabled: boolean) { return this.http.put<void>(`${API}/integrations/keys/${id}/enabled`, { enabled }); }
  deleteKey(id: string) { return this.http.delete<void>(`${API}/integrations/keys/${id}`); }
  trackers() { return this.http.get<Tracker[]>(`${API}/integrations/trackers`); }
  saveTracker(id: string | null, body: { name: string; kind: string; target: string; baseUrl?: string; username?: string; token?: string | null; productId?: string; isEnabled: boolean; version?: number }) {
    return id ? this.http.put<Tracker>(`${API}/integrations/trackers/${id}`, body) : this.http.post<Tracker>(`${API}/integrations/trackers`, body);
  }
  deleteTracker(id: string) { return this.http.delete<void>(`${API}/integrations/trackers/${id}`); }

  // dashboards and reports
  globalDashboard() { return this.http.get<GlobalDashboard>(`${API}/dashboards/global`); }
  productDashboard(id: string) { return this.http.get<ProductDashboard>(`${API}/dashboards/products/${id}`); }
  executionDashboard() { return this.http.get<ExecutionDashboard>(`${API}/dashboards/execution`); }
  reportCatalog() { return this.http.get<ReportDefinition[]>(`${API}/reports`); }
  runReport(key: string, parameters: ReportParameters, columns?: string[]) { return this.http.post<ReportResult>(`${API}/reports/${key}/run`, { parameters, columns }); }
  exportReport(key: string, parameters: ReportParameters, columns: string[] | undefined, format: string) {
    return this.http.post(`${API}/reports/${key}/export`, { parameters, columns }, { params: toParams({ format }), responseType: 'blob' });
  }
  savedReports() { return this.http.get<SavedReport[]>(`${API}/reports/saved`); }
  saveReport(id: string | null, body: Omit<SavedReport, 'id' | 'owner' | 'isMine' | 'nextRunAt' | 'lastRunAt' | 'version'> & { version?: number }) {
    return id ? this.http.put<SavedReport>(`${API}/reports/saved/${id}`, body) : this.http.post<SavedReport>(`${API}/reports/saved`, body);
  }
  deleteSavedReport(id: string) { return this.http.delete<void>(`${API}/reports/saved/${id}`); }
}

export function readinessTone(r: string | undefined) { return r === 'Ready' ? 'Passed' : r === 'Blocked' ? 'Failed' : r === 'Warning' ? 'Warning' : r; }
export function gateTone(s: string) { return s === 'Passed' ? 'Passed' : s === 'Failed' ? 'Failed' : 'Warning'; }
export function defectTone(s: DefectStatus) {
  return ['Verified', 'Closed'].includes(s) ? 'Passed' : ['Open', 'Reopened'].includes(s) ? 'Failed' : ['Duplicate', 'Rejected', 'Deferred'].includes(s) ? 'Deprecated' : 'Pending';
}
