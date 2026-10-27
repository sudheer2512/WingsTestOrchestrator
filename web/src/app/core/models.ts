// TypeScript mirrors of the API contracts (Wings.Application DTOs). Enums travel as strings.

export type ApplicationType = 'None' | 'WinForms' | 'Browser' | 'Api' | 'Service' | 'Database' | 'Mixed';
export type AutomationStatus = 'Manual' | 'AutomationPlanned' | 'Automated' | 'NotAutomatable';
export type ReviewStatus = 'Draft' | 'InReview' | 'Approved' | 'Rejected' | 'Obsolete';
export type PackageStatus = 'Draft' | 'Uploaded' | 'Validating' | 'ValidationFailed' | 'Validated' | 'Approved' | 'Published' | 'Deprecated' | 'Rejected';
export type DependencyType = 'Prerequisite' | 'SetupDependency' | 'SharedResource' | 'DataDependency' | 'Related';
export type SourceMappingKind = 'File' | 'Assembly' | 'Class' | 'Form' | 'ApiEndpoint' | 'DatabaseTable' | 'DatabaseView' | 'StoredProcedure' | 'Route';
export type Coverage = 'NONE' | 'MANUAL' | 'PARTIAL' | 'AUTOMATED';

export interface PagedResult<T> { items: T[]; total: number; page: number; pageSize: number; }
export interface LookupItem { code: string; name: string; sortOrder: number; }
export interface Framework { code: string; name: string; language: string; platform: ApplicationType; }
export interface SequenceScope { id: string; key: string; name: string; nextValue: number; displayWidth: number; prefix?: string; }

export interface Lookups {
  featureTypes: LookupItem[]; featureStatuses: LookupItem[]; testTypes: LookupItem[]; frameworks: Framework[];
  sequenceScopes: SequenceScope[]; applicationTypes: ApplicationType[]; priorities: string[]; severities: string[];
  dependencyTypes: DependencyType[]; sourceMappingKinds: SourceMappingKind[];
}

export interface UserSummary {
  id: string; userName: string; displayName: string; email?: string; isActive: boolean; roles: string[];
  productAccess: { productId: string; canEdit: boolean }[]; lastLoginAt?: string; isLockedOut: boolean;
}
export interface Session { token: string; expiresAt: string; user: UserSummary; permissions: string[]; }
export interface Role { id: string; name: string; permissions: string[]; userCount: number; }
export interface Permission { code: string; description: string; }

export interface Product {
  id: string; code: string; name: string; description?: string; displayOrder: number; isActive: boolean;
  sequenceScopeId: string; sequenceScopeKey: string; featureCount: number; version: number;
}
export interface ProductRelease { id: string; productId: string; versionLabel: string; releaseDate?: string; isReleased: boolean; notes?: string; version: number; }

export interface FeatureNode {
  id: string; productId: string; parentId?: string; name: string; sequenceNumber: number; sequenceDisplay: string;
  featureTypeCode: string; statusCode: string; applicationType: ApplicationType; displayOrder: number; hasChildren: boolean;
  isDeprecated: boolean; testCaseCount: number; automatedTestCount: number; coverageStatus: Coverage; createdAt: string;
}
export interface Breadcrumb { id: string; name: string; sequenceDisplay: string; }
export interface FeatureSearchHit { node: FeatureNode; productName: string; ancestors: Breadcrumb[]; }

export interface FeatureFilter {
  productId?: string; text?: string; statusCode?: string; featureTypeCode?: string; applicationType?: ApplicationType;
  sequenceFrom?: number; sequenceTo?: number; createdAfter?: string; introducedInVersionId?: string; sinceLastRelease?: boolean;
  untested?: boolean; missingAutomation?: boolean; recentDays?: number; includeDeprecated?: boolean; underFeatureId?: string;
  sortBy?: 'tree' | 'sequence' | 'name' | 'created'; descending?: boolean;
}

export interface FeatureFields {
  name: string; featureTypeCode: string; statusCode: string; applicationType: ApplicationType;
  description?: string; businessObjective?: string; functionalRequirements?: string; currentVersion?: string; introducedInVersionId?: string;
  assemblyName?: string; namespace?: string; className?: string; formOrRoute?: string; relatedServices?: string; apiEndpoints?: string;
  databaseObjects?: string; sourceRepository?: string; buildPackage?: string; entryPoint?: string; requiredPermissions?: string;
  preconditions?: string; inputParameters?: string; expectedOutcomes?: string; businessRules?: string; validationRules?: string;
  errorConditions?: string; userRoles?: string; knownLimitations?: string; isDeprecated: boolean; replacedByFeatureId?: string; deprecationNote?: string;
}

export interface FeatureTesting {
  testCaseCount: number; automatedCount: number; manualCount: number; approvedCount: number; draftCount: number;
  packagedTestCount: number; coverageStatus: Coverage; lastExecutedAt?: string; lastResult?: string; lastRunId?: string;
}
export interface Requirement { id: string; code: string; title: string; description?: string; priority: string; externalLink?: string; version: number; }
export interface SourceMapping { id: string; kind: SourceMappingKind; value: string; repository?: string; notes?: string; isApproved: boolean; createdBy: string; }
export interface Attachment { id: string; fileName: string; contentType: string; sizeBytes: number; externalUrl?: string; caption?: string; createdAt: string; createdBy: string; }
export interface Dependency { id: string; featureId: string; featureName: string; featureSequence: string; type: DependencyType; notes?: string; }

export interface FeatureDetail {
  id: string; productId: string; productName: string; parentId?: string; parentName?: string; ancestors: Breadcrumb[];
  sequenceNumber: number; sequenceDisplay: string; sequenceScopeKey: string; isLegacySequence: boolean; displayOrder: number; version: number;
  createdAt: string; createdBy: string; modifiedAt?: string; modifiedBy?: string; introducedInVersionLabel?: string; replacedByName?: string;
  fields: FeatureFields; testing: FeatureTesting; requirements: Requirement[]; sourceMappings: SourceMapping[]; attachments: Attachment[];
  dependencies: Dependency[]; dependents: Dependency[];
}

export interface AuditEntry { id: number; timestamp: string; userName: string; action: string; entityType: string; entityId: string; changesJson?: string; summary?: string; }

export interface TestStep { stepNumber: number; action: string; expectedResult?: string; testData?: string; sharedStepsTestCaseId?: string; sharedStepsName?: string; }
export interface TestCaseFields {
  name: string; description?: string; objective?: string; testTypeCode: string; platform: ApplicationType; frameworkCode?: string;
  priority: string; failureSeverity: string; preconditions?: string; testDataRequirements?: string; expectedResults?: string; cleanupSteps?: string;
  estimatedDurationMinutes?: number; automationStatus: AutomationStatus; ownerUserName?: string; folderId?: string; isTemplate: boolean;
  isSharedSteps: boolean; parametersJson?: string; isDestructive: boolean; resourceKeys?: string; dataRequirements?: string; steps: TestStep[]; featureIds: string[]; primaryFeatureId?: string; tags: string[]; requirementIds: string[];
}
export interface TestCaseSummary {
  id: string; number: number; numberDisplay: string; name: string; testTypeCode: string; platform: ApplicationType; frameworkCode?: string;
  priority: string; failureSeverity: string; automationStatus: AutomationStatus; reviewStatus: ReviewStatus; revision: number; ownerUserName?: string;
  featureCount: number; tags: string[]; folderId?: string; isTemplate: boolean; isSharedSteps: boolean; createdAt: string; modifiedAt?: string;
}
export interface LinkedFeature { featureId: string; name: string; sequenceDisplay: string; productName: string; isPrimary: boolean; }
export interface LinkedRequirement { requirementId: string; code: string; title: string; featureId: string; featureName: string; }
export interface TestCaseDependency { id: string; dependsOnTestCaseId: string; numberDisplay: string; name: string; type: DependencyType; notes?: string; }
export interface LinkedAutomation { packageId: string; packageName: string; versionNumber: number; status: PackageStatus; identifier: string; filePath: string; line: number; }
export interface TestCaseDetail {
  id: string; number: number; numberDisplay: string; revision: number; reviewStatus: ReviewStatus; reviewComment?: string; approvedBy?: string; approvedAt?: string;
  version: number; createdAt: string; createdBy: string; modifiedAt?: string; modifiedBy?: string; fields: TestCaseFields;
  features: LinkedFeature[]; requirements: LinkedRequirement[]; dependencies: TestCaseDependency[]; automation: LinkedAutomation[];
  suites: { id: string; numberDisplay: string; name: string }[];
}
export interface TestCaseVersion { revision: number; changeSummary?: string; createdAt: string; createdBy: string; snapshotJson: string; }
export interface TestCaseFilter {
  text?: string; featureId?: string; includeSubfeatures?: boolean; folderId?: string; testTypeCode?: string; automationStatus?: AutomationStatus;
  reviewStatus?: ReviewStatus; priority?: string; tag?: string; platform?: ApplicationType; isTemplate?: boolean; isSharedSteps?: boolean;
  ownerUserName?: string; suiteId?: string; sortBy?: string; descending?: boolean;
}
export interface Folder { id: string; name: string; parentId?: string; displayOrder: number; testCaseCount: number; }

export interface SuiteSummary { id: string; number: number; numberDisplay: string; name: string; description?: string; productId?: string; tags?: string; ownerUserName?: string; itemCount: number; automatedCount: number; version: number; }
export interface SuiteDetail { suite: SuiteSummary; items: { itemId: string; order: number; testCase: TestCaseSummary }[]; }

export interface PackageSummary {
  id: string; number: number; numberDisplay: string; name: string; description?: string; productId?: string; latestVersionNumber: number;
  latestStatus?: PackageStatus; framework?: string; testCount: number; createdAt: string; createdBy: string;
}
export interface PackageVersionSummary {
  id: string; versionNumber: number; status: PackageStatus; originalFileName: string; sha256: string; sizeBytes: number; fileCount: number; testCount: number;
  detectedFramework?: string; detectedLanguage?: string; platform: ApplicationType; sourceCommit?: string; parentVersionId?: string; changeNote?: string;
  createdAt: string; createdBy: string;
}
export type StageStatus = 'Passed' | 'Warning' | 'Failed' | 'Skipped' | 'Pending';
export interface ValidationFinding { severity: 'error' | 'warning' | 'info'; message: string; file?: string; line?: number; }
export interface ValidationStage { name: string; status: StageStatus; summary: string; findings: ValidationFinding[]; }
export interface ValidationReport { stages: ValidationStage[]; framework?: string; language?: string; platform: ApplicationType; passed: boolean; }
export interface PackageFile { id: string; path: string; sizeBytes: number; sha256: string; isText: boolean; language?: string; }
export interface DiscoveredTest {
  id: string; identifier: string; displayName: string; filePath: string; line: number; framework: string; traits?: string;
  testCaseId?: string; testCaseNumber?: string; testCaseName?: string; featureId?: string; featureName?: string; featureSequence?: string;
}
export interface PackageVersionDetail {
  summary: PackageVersionSummary; packageId: string; packageName: string; sourceRepository?: string; report?: ValidationReport;
  files: PackageFile[]; tests: DiscoveredTest[]; reviewedBy?: string; reviewedAt?: string; reviewComment?: string; publishedBy?: string; publishedAt?: string;
}
export interface PackageFileContent { path: string; language: string; content: string; sha256: string; }
export interface FeatureAutomation { packageId: string; packageName: string; versionId: string; versionNumber: number; status: PackageStatus; test: DiscoveredTest; }

// ---- Execution (phase 3) ----
export type RunStatus = 'Queued' | 'Running' | 'Completed' | 'Cancelled';
export type JobStatus = 'Queued' | 'Leased' | 'Running' | 'Completed' | 'Cancelled' | 'TimedOut' | 'InfrastructureError';
export type Outcome = 'Pending' | 'Passed' | 'Failed' | 'Skipped' | 'Blocked' | 'NotRun' | 'InfrastructureError';

export interface StartRunRequest {
  name?: string; testCaseIds?: string[]; featureId?: string; includeSubfeatures?: boolean; suiteId?: string; packageVersionId?: string;
  productId?: string; sequenceFrom?: number; rerunOfRunId?: string; onlyFailed?: boolean; variables?: Record<string, string>;
  priority?: number; maxInfrastructureRetries?: number; timeoutMinutes?: number;
  suiteIds?: string[]; environmentId?: string; buildVersion?: string; includeManual?: boolean; ignoreEnvironmentHealth?: boolean;
}
export interface RunSummary {
  id: string; number: number; numberDisplay: string; name: string; status: RunStatus; scopeDescription: string; priority: number; schedulingPaused: boolean;
  cancelRequested: boolean; totalTests: number; passed: number; failed: number; skipped: number; blocked: number; notRun: number; infrastructureErrors: number;
  pending: number; jobCount: number; createdAt: string; createdBy: string; startedAt?: string; completedAt?: string; sourceRunId?: string;
  environmentId?: string; environmentName?: string; buildVersion?: string; triggeredBy?: string; planId?: string; campaignId?: string; scheduleId?: string; includeManual: boolean;
}
export interface Job {
  id: string; packageVersionId: string; packageName: string; packageVersionNumber: number; framework: string; requiresDesktop: boolean; status: JobStatus;
  attempt: number; agentId?: string; agentName?: string; leaseExpiresAt?: string; startedAt?: string; completedAt?: string; cancelRequested: boolean;
  error?: string; exitCode?: number; testCount: number; logLineCount: number; retryOfJobId?: string; wave: number; waitingOn: number; resourceKeys?: string; dataRequirements?: string;
}
export interface Artifact { id: string; jobId: string; testExecutionId?: string; kind: string; fileName: string; contentType: string; sizeBytes: number; sha256: string; createdAt: string; }
export interface TestExecution {
  id: string; runId: string; jobId?: string; attempt: number; identifier: string; displayName: string; outcome: Outcome; reason?: string; durationMs?: number;
  errorMessage?: string; stackTrace?: string; startedAt?: string; completedAt?: string; testCaseId?: string; testCaseNumber?: string; featureId?: string;
  featureName?: string; agentName?: string; isSuperseded: boolean; artifacts: Artifact[]; executedBy?: string; isManual: boolean; prerequisiteCount: number;
}
export interface RunDetail { run: RunSummary; variables: Record<string, string>; jobs: Job[]; tests: TestExecution[]; jobArtifacts: Artifact[]; maxInfrastructureRetries: number; timeoutMinutes: number; }
export interface LogLine { sequence: number; timestamp: string; level: string; message: string; }
export interface ExecutionHistory {
  runId: string; runNumber: string; runName: string; testExecutionId: string; identifier: string; displayName: string; outcome: Outcome;
  errorMessage?: string; durationMs?: number; completedAt?: string; agentName?: string; packageVersionNumber: number; packageName?: string;
}
export interface Agent {
  id: string; name: string; kind: string; pool?: string; description?: string; isEnabled: boolean; inMaintenance: boolean; isOnline: boolean; machineName?: string;
  operatingSystem?: string; agentVersion?: string; slots: number; frameworks: string[]; interactiveSession: boolean; screenResolution?: string; dpiScalePercent?: number;
  processorCount: number; totalMemoryMb: number; capabilitiesJson?: string; lastHeartbeatAt?: string; registeredAt?: string; keyPrefix: string; activeJobs: number; version: number;
}

// ---- Orchestration (phase 4) ----
export type EnvironmentKind = 'Development' | 'QA' | 'Staging' | 'Preproduction' | 'Automation' | 'Production';
export type EnvironmentHealth = 'Unknown' | 'Available' | 'Degraded' | 'Unavailable';
export interface EnvVariable { name: string; value?: string | null; isSecret: boolean; hasValue: boolean; description?: string; }
export interface TestEnvironment {
  id: string; code: string; name: string; kind: EnvironmentKind; description?: string; isProduction: boolean; allowedDestructiveTestCaseIds: string[];
  baseUrl?: string; applicationBuildVersion?: string; databaseReference?: string; tenantCompany?: string; authenticationNotes?: string; dependencies?: string;
  agentPools: string[]; healthCheckUrl?: string; health: EnvironmentHealth; healthMessage?: string; lastHealthCheckAt?: string; inMaintenance: boolean;
  resetProcedure?: string; variables: EnvVariable[]; activeRuns: number; version: number; createdAt: string; createdBy: string;
}
export interface EnvComparisonRow { item: string; left?: string; right?: string; differs: boolean; }
export interface DataSet {
  id: string; code: string; name: string; description?: string; category?: string; environmentId?: string; environmentName?: string; dataVersion: number;
  recordCount: number; activeRecords: number; reserved: number; setupNotes?: string; cleanupNotes?: string; version: number; modifiedAt?: string;
}
export interface DataRecord { id: string; key: string; values: Record<string, unknown>; isActive: boolean; isReserved: boolean; reservedByRun?: string; }
export interface Reservation { id: string; dataSetCode: string; recordKey: string; runId: string; runNumber: string; jobId: string; reservedAt: string; releasedAt?: string; }
export interface ResourceLock { key: string; runId: string; runNumber: string; runName: string; jobId: string; agentName?: string; acquiredAt: string; }
export type PlanStatus = 'Draft' | 'Active' | 'Completed' | 'Approved';
export type RiskLevel = 'Low' | 'Medium' | 'High' | 'Critical';
export interface PlanSummary {
  id: string; number: number; numberDisplay: string; name: string; status: PlanStatus; risk: RiskLevel; productId?: string; targetReleaseId?: string;
  targetReleaseLabel?: string; environmentId?: string; environmentName?: string; buildVersion?: string; startDate?: string; endDate?: string; isTemplate: boolean;
  suiteCount: number; testCaseCount: number; runCount: number; version: number;
}
export interface PlanDetail {
  summary: PlanSummary; objective?: string; scope?: string; owners?: string; testers?: string; entryCriteria?: string; exitCriteria?: string;
  defectAcceptanceRules?: string; approvedBy?: string; approvedAt?: string; approvalComment?: string;
  suites: { suiteId: string; numberDisplay: string; name: string; testCaseCount: number; order: number }[];
  runs: RunSummary[]; coverage: { testCaseId: string; numberDisplay: string; name: string; outcome?: string; runId?: string; runNumber?: string; at?: string; executedBy?: string }[];
  coverageTotals: Record<string, number>;
}
export interface Campaign {
  id: string; number: number; numberDisplay: string; name: string; description?: string; buildVersion?: string; status: 'Planned' | 'Running' | 'Completed' | 'Cancelled';
  startedAt?: string; plans: PlanSummary[]; environments: { id: string; code: string; name: string; health: EnvironmentHealth }[];
  cells: { planId: string; environmentId: string; latestRun?: RunSummary }[]; passed: number; failed: number; pending: number; total: number; version: number;
}
export type TriggerType = 'Once' | 'Hourly' | 'Daily' | 'Weekly' | 'Monthly' | 'Cron' | 'Webhook';
export type ScheduleTarget = 'Suite' | 'Plan' | 'Campaign' | 'Feature' | 'Package';
export interface Schedule {
  id: string; name: string; isEnabled: boolean; trigger: TriggerType; cronExpression?: string; timeZoneId: string; runOnceAt?: string; targetType: ScheduleTarget;
  targetId: string; targetName?: string; environmentId?: string; environmentName?: string; variables: Record<string, string>; priority: number; nextRunAt?: string;
  upcoming: string[]; lastTriggeredAt?: string; lastRunId?: string; lastError?: string; webhookTokenPrefix?: string; createdBy: string; version: number;
}
