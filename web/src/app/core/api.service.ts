import { HttpClient, HttpEvent, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AuditEntry, Breadcrumb, Dependency, DependencyType, DiscoveredTest, FeatureAutomation, FeatureDetail, FeatureFields, FeatureFilter,
  FeatureNode, FeatureSearchHit, Folder, LookupItem, Lookups, PackageFileContent, PackageSummary, PackageVersionDetail,
  PackageVersionSummary, PagedResult, Permission, Product, ProductRelease, Requirement, Role, SourceMapping, SourceMappingKind,
  SuiteDetail, SuiteSummary, TestCaseDependency, TestCaseDetail, TestCaseFields, TestCaseFilter, TestCaseSummary, TestCaseVersion,
  Attachment, UserSummary, AutomationStatus, Agent, ExecutionHistory, LogLine, RunDetail, RunStatus, RunSummary, StartRunRequest,
  TestEnvironment, EnvComparisonRow, DataSet, DataRecord, Reservation, ResourceLock, PlanSummary, PlanDetail, PlanStatus, Campaign, Schedule, TestExecution,
} from './models';

const API = '/api/v1';

/** Drops empty values so filters serialise to clean query strings. */
export function toParams(obj: object): HttpParams {
  let p = new HttpParams();
  for (const [k, v] of Object.entries(obj)) {
    if (v === undefined || v === null || v === '' || v === false) continue;
    p = p.set(k, String(v));
  }
  return p;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private http = inject(HttpClient);

  // ---- lookups & products ----
  lookups() { return this.http.get<Lookups>(`${API}/lookups`); }
  products() { return this.http.get<Product[]>(`${API}/products`); }
  product(id: string) { return this.http.get<Product>(`${API}/products/${id}`); }
  createProduct(body: Partial<Product> & { sequenceScopeKey?: string }) { return this.http.post<Product>(`${API}/products`, body); }
  updateProduct(id: string, body: Partial<Product>) { return this.http.put<Product>(`${API}/products/${id}`, body); }
  deleteProduct(id: string) { return this.http.delete<void>(`${API}/products/${id}`); }
  releases(productId: string) { return this.http.get<ProductRelease[]>(`${API}/products/${productId}/releases`); }
  createRelease(productId: string, body: Partial<ProductRelease>) { return this.http.post<ProductRelease>(`${API}/products/${productId}/releases`, body); }
  updateRelease(productId: string, id: string, body: Partial<ProductRelease>) { return this.http.put<ProductRelease>(`${API}/products/${productId}/releases/${id}`, body); }
  upsertMaster(kind: 'feature-types' | 'feature-statuses' | 'test-types', body: { code: string; name: string; sortOrder: number }) {
    return this.http.put<LookupItem>(`${API}/master/${kind}`, body);
  }

  // ---- features ----
  tree(productId: string, parentId?: string) {
    return this.http.get<FeatureNode[]>(`${API}/products/${productId}/tree`, { params: toParams({ parentId }) });
  }
  searchFeatures(filter: FeatureFilter, page = 1, pageSize = 100) {
    return this.http.get<PagedResult<FeatureSearchHit>>(`${API}/features/search`, { params: toParams({ ...filter, page, pageSize }) });
  }
  feature(id: string) { return this.http.get<FeatureDetail>(`${API}/features/${id}`); }
  ancestors(id: string) { return this.http.get<Breadcrumb[]>(`${API}/features/${id}/ancestors`); }
  createFeature(productId: string, parentId: string | undefined, fields: FeatureFields, legacySequenceNumber?: number) {
    return this.http.post<FeatureDetail>(`${API}/features`, { productId, parentId, fields, legacySequenceNumber });
  }
  updateFeature(id: string, fields: FeatureFields, version: number) { return this.http.put<FeatureDetail>(`${API}/features/${id}`, { fields, version }); }
  moveFeature(id: string, body: { newParentId?: string; newProductId?: string; position?: number; version: number }) {
    return this.http.post<FeatureDetail>(`${API}/features/${id}/move`, body);
  }
  reorder(productId: string, parentId: string | undefined, orderedIds: string[]) {
    return this.http.post<void>(`${API}/features/reorder`, { productId, parentId, orderedIds });
  }
  duplicateFeature(id: string, newName?: string) { return this.http.post<FeatureDetail>(`${API}/features/${id}/duplicate`, { newName, includeRequirements: true }); }
  deprecateFeature(id: string, replacedById?: string, note?: string) { return this.http.post<FeatureDetail>(`${API}/features/${id}/deprecate`, { replacedById, note }); }
  deleteFeature(id: string) { return this.http.delete<{ deleted: number }>(`${API}/features/${id}`); }
  featureAudit(id: string, page = 1) { return this.http.get<PagedResult<AuditEntry>>(`${API}/features/${id}/audit`, { params: toParams({ page }) }); }
  featureTestCases(id: string, includeSubfeatures = false) {
    return this.http.get<PagedResult<TestCaseSummary>>(`${API}/features/${id}/test-cases`, { params: toParams({ includeSubfeatures, pageSize: 500 }) });
  }
  featureAutomation(id: string) { return this.http.get<FeatureAutomation[]>(`${API}/features/${id}/automation`); }
  addRequirement(id: string, body: Partial<Requirement>) { return this.http.post<Requirement>(`${API}/features/${id}/requirements`, body); }
  updateRequirement(id: string, rid: string, body: Partial<Requirement>) { return this.http.put<Requirement>(`${API}/features/${id}/requirements/${rid}`, body); }
  deleteRequirement(id: string, rid: string) { return this.http.delete<void>(`${API}/features/${id}/requirements/${rid}`); }
  addSourceMapping(id: string, body: { kind: SourceMappingKind; value: string; repository?: string; notes?: string }) {
    return this.http.post<SourceMapping>(`${API}/features/${id}/source-mappings`, body);
  }
  approveSourceMapping(id: string, mid: string) { return this.http.post<void>(`${API}/features/${id}/source-mappings/${mid}/approve`, {}); }
  deleteSourceMapping(id: string, mid: string) { return this.http.delete<void>(`${API}/features/${id}/source-mappings/${mid}`); }
  addDependency(id: string, dependsOnFeatureId: string, type: DependencyType, notes?: string) {
    return this.http.post<Dependency>(`${API}/features/${id}/dependencies`, { dependsOnFeatureId, type, notes });
  }
  deleteDependency(id: string, did: string) { return this.http.delete<void>(`${API}/features/${id}/dependencies/${did}`); }
  uploadAttachment(id: string, file: File, caption?: string) {
    const form = new FormData();
    form.append('file', file);
    if (caption) form.append('caption', caption);
    return this.http.post<Attachment>(`${API}/features/${id}/attachments`, form);
  }
  addLink(id: string, url: string, caption?: string) { return this.http.post<Attachment>(`${API}/features/${id}/links`, { url, caption }); }
  downloadAttachment(id: string, aid: string) { return this.http.get(`${API}/features/${id}/attachments/${aid}`, { responseType: 'blob' }); }
  deleteAttachment(id: string, aid: string) { return this.http.delete<void>(`${API}/features/${id}/attachments/${aid}`); }
  exportFeatures(productId: string, format: 'csv' | 'xlsx') {
    return this.http.get(`${API}/products/${productId}/features/export`, { params: toParams({ format }), responseType: 'blob' });
  }
  importFeatures(productId: string, file: File) {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<{ created: number; skipped: number; errors: string[] }>(`${API}/products/${productId}/features/import`, form);
  }

  // ---- test cases ----
  testCases(filter: TestCaseFilter, page = 1, pageSize = 50) {
    return this.http.get<PagedResult<TestCaseSummary>>(`${API}/test-cases`, { params: toParams({ ...filter, page, pageSize }) });
  }
  testCase(id: string) { return this.http.get<TestCaseDetail>(`${API}/test-cases/${id}`); }
  createTestCase(fields: TestCaseFields, changeSummary?: string) { return this.http.post<TestCaseDetail>(`${API}/test-cases`, { fields, changeSummary }); }
  updateTestCase(id: string, fields: TestCaseFields, version: number, changeSummary?: string) {
    return this.http.put<TestCaseDetail>(`${API}/test-cases/${id}`, { fields, version, changeSummary });
  }
  cloneTestCase(id: string, newName?: string, asTemplate = false) { return this.http.post<TestCaseDetail>(`${API}/test-cases/${id}/clone`, { newName, asTemplate, keepFeatureLinks: true }); }
  deleteTestCase(id: string) { return this.http.delete<void>(`${API}/test-cases/${id}`); }
  bulkUpdate(body: {
    ids: string[]; priority?: string; failureSeverity?: string; ownerUserName?: string; automationStatus?: AutomationStatus; testTypeCode?: string;
    folderId?: string; clearFolder?: boolean; addTags?: string[]; removeTags?: string[]; addFeatureIds?: string[];
  }) { return this.http.post<{ updated: number; errors: string[] }>(`${API}/test-cases/bulk`, body); }
  testCaseVersions(id: string) { return this.http.get<TestCaseVersion[]>(`${API}/test-cases/${id}/versions`); }
  submitTestCase(id: string) { return this.http.post<TestCaseDetail>(`${API}/test-cases/${id}/submit`, {}); }
  approveTestCase(id: string, comment?: string) { return this.http.post<TestCaseDetail>(`${API}/test-cases/${id}/approve`, { comment }); }
  rejectTestCase(id: string, comment: string) { return this.http.post<TestCaseDetail>(`${API}/test-cases/${id}/reject`, { comment }); }
  addTestCaseDependency(id: string, dependsOnTestCaseId: string) { return this.http.post<TestCaseDependency>(`${API}/test-cases/${id}/dependencies`, { dependsOnTestCaseId }); }
  removeTestCaseDependency(id: string, did: string) { return this.http.delete<void>(`${API}/test-cases/${id}/dependencies/${did}`); }
  testCaseAudit(id: string) { return this.http.get<PagedResult<AuditEntry>>(`${API}/test-cases/${id}/audit`); }
  exportTestCases(filter: TestCaseFilter, format: 'csv' | 'xlsx') {
    return this.http.get(`${API}/test-cases/export`, { params: toParams({ ...filter, format }), responseType: 'blob' });
  }
  importTestCases(file: File, folderId?: string) {
    const form = new FormData();
    form.append('file', file);
    if (folderId) form.append('folderId', folderId);
    return this.http.post<{ created: number; errors: string[] }>(`${API}/test-cases/import`, form);
  }
  folders() { return this.http.get<Folder[]>(`${API}/test-folders`); }
  createFolder(name: string, parentId?: string) { return this.http.post<Folder>(`${API}/test-folders`, { name, parentId }); }
  updateFolder(id: string, name: string, parentId?: string) { return this.http.put<Folder>(`${API}/test-folders/${id}`, { name, parentId }); }
  deleteFolder(id: string) { return this.http.delete<void>(`${API}/test-folders/${id}`); }

  // ---- suites ----
  suites(productId?: string, text?: string) { return this.http.get<SuiteSummary[]>(`${API}/test-suites`, { params: toParams({ productId, text }) }); }
  suite(id: string) { return this.http.get<SuiteDetail>(`${API}/test-suites/${id}`); }
  createSuite(body: Partial<SuiteSummary>) { return this.http.post<SuiteDetail>(`${API}/test-suites`, body); }
  updateSuite(id: string, body: Partial<SuiteSummary>) { return this.http.put<SuiteDetail>(`${API}/test-suites/${id}`, body); }
  deleteSuite(id: string) { return this.http.delete<void>(`${API}/test-suites/${id}`); }
  addSuiteItems(id: string, testCaseIds: string[]) { return this.http.post<SuiteDetail>(`${API}/test-suites/${id}/items`, { testCaseIds }); }
  removeSuiteItem(id: string, itemId: string) { return this.http.delete<SuiteDetail>(`${API}/test-suites/${id}/items/${itemId}`); }
  reorderSuite(id: string, itemIds: string[]) { return this.http.put<SuiteDetail>(`${API}/test-suites/${id}/items/order`, itemIds); }

  // ---- packages ----
  packages(productId?: string, text?: string) { return this.http.get<PackageSummary[]>(`${API}/test-packages`, { params: toParams({ productId, text }) }); }
  package(id: string) { return this.http.get<{ package: PackageSummary; versions: PackageVersionSummary[] }>(`${API}/test-packages/${id}`); }
  uploadPackage(file: File, meta: { packageId?: string; name?: string; description?: string; productId?: string; sourceRepository?: string; sourceCommit?: string; changeNote?: string }): Observable<HttpEvent<PackageVersionSummary>> {
    const form = new FormData();
    form.append('file', file);
    for (const [k, v] of Object.entries(meta)) if (v) form.append(k, v);
    return this.http.post<PackageVersionSummary>(`${API}/test-packages/upload`, form, { reportProgress: true, observe: 'events' });
  }
  packageVersion(id: string) { return this.http.get<PackageVersionDetail>(`${API}/test-packages/versions/${id}`); }
  packageFile(versionId: string, path: string) { return this.http.get<PackageFileContent>(`${API}/test-packages/versions/${versionId}/file`, { params: toParams({ path }) }); }
  downloadPackage(versionId: string) { return this.http.get(`${API}/test-packages/versions/${versionId}/download`, { responseType: 'blob', observe: 'response' }); }
  editPackage(packageId: string, baseVersionId: string, edits: { path: string; content?: string; delete?: boolean }[], changeNote: string) {
    return this.http.post<PackageVersionSummary>(`${API}/test-packages/${packageId}/edits`, { baseVersionId, edits, changeNote });
  }
  mapTest(versionId: string, testId: string, body: { testCaseId?: string; featureId?: string; createTestCase?: boolean }) {
    return this.http.put<DiscoveredTest>(`${API}/test-packages/versions/${versionId}/tests/${testId}/mapping`, body);
  }
  reviewPackage(versionId: string, action: 'approve' | 'reject', comment?: string) {
    return this.http.post<PackageVersionDetail>(`${API}/test-packages/versions/${versionId}/${action}`, { comment });
  }
  packageAction(versionId: string, action: 'publish' | 'deprecate' | 'revalidate') {
    return this.http.post<PackageVersionDetail>(`${API}/test-packages/versions/${versionId}/${action}`, {});
  }
  packageAudit(id: string) { return this.http.get<PagedResult<AuditEntry>>(`${API}/test-packages/${id}/audit`); }

  // ---- execution ----
  runs(q: { status?: RunStatus; text?: string; page?: number; pageSize?: number }) { return this.http.get<PagedResult<RunSummary>>(`${API}/runs`, { params: toParams(q) }); }
  run(id: string) { return this.http.get<RunDetail>(`${API}/runs/${id}`); }
  startRun(body: StartRunRequest) { return this.http.post<RunSummary>(`${API}/runs`, body); }
  runAction(id: string, action: 'cancel' | 'pause' | 'resume') { return this.http.post<RunSummary>(`${API}/runs/${id}/${action}`, {}); }
  setRunPriority(id: string, priority: number) { return this.http.put<RunSummary>(`${API}/runs/${id}/priority`, { priority }); }
  jobLogs(jobId: string, after = 0) { return this.http.get<LogLine[]>(`${API}/runs/jobs/${jobId}/logs`, { params: toParams({ after }) }); }
  artifact(runId: string, artifactId: string) { return this.http.get(`${API}/runs/${runId}/artifacts/${artifactId}`, { responseType: 'blob' }); }
  history(q: { featureId?: string; testCaseId?: string; take?: number }) { return this.http.get<ExecutionHistory[]>(`${API}/runs/history`, { params: toParams(q) }); }
  agents() { return this.http.get<Agent[]>(`${API}/agents`); }
  createAgent(body: { name: string; kind: string; pool?: string; description?: string }) { return this.http.post<{ agent: Agent; key: string }>(`${API}/agents`, body); }
  updateAgent(id: string, body: { name: string; kind: string; pool?: string; description?: string; isEnabled: boolean; inMaintenance: boolean; version: number }) { return this.http.put<Agent>(`${API}/agents/${id}`, body); }
  rotateAgentKey(id: string) { return this.http.post<{ agent: Agent; key: string }>(`${API}/agents/${id}/rotate-key`, {}); }
  deleteAgent(id: string) { return this.http.delete<void>(`${API}/agents/${id}`); }

  // ---- orchestration ----
  runLocks() { return this.http.get<ResourceLock[]>(`${API}/runs/locks`); }
  recordManual(runId: string, testId: string, outcome: string, comment?: string) { return this.http.post<TestExecution>(`${API}/runs/${runId}/tests/${testId}/manual`, { outcome, comment }); }
  environments() { return this.http.get<TestEnvironment[]>(`${API}/environments`); }
  environment(id: string) { return this.http.get<TestEnvironment>(`${API}/environments/${id}`); }
  saveEnvironment(id: string | null, body: object) { return id ? this.http.put<TestEnvironment>(`${API}/environments/${id}`, body) : this.http.post<TestEnvironment>(`${API}/environments`, body); }
  cloneEnvironment(id: string, code: string, name: string) { return this.http.post<TestEnvironment>(`${API}/environments/${id}/clone`, { code, name }); }
  deleteEnvironment(id: string) { return this.http.delete<void>(`${API}/environments/${id}`); }
  compareEnvironments(left: string, right: string) { return this.http.get<EnvComparisonRow[]>(`${API}/environments/compare`, { params: toParams({ left, right }) }); }
  checkEnvironment(id: string) { return this.http.post<TestEnvironment>(`${API}/environments/${id}/health-check`, {}); }
  dataSets() { return this.http.get<DataSet[]>(`${API}/test-data`); }
  saveDataSet(id: string | null, body: object) { return id ? this.http.put<DataSet>(`${API}/test-data/${id}`, body) : this.http.post<DataSet>(`${API}/test-data`, body); }
  deleteDataSet(id: string) { return this.http.delete<void>(`${API}/test-data/${id}`); }
  dataRecords(id: string) { return this.http.get<DataRecord[]>(`${API}/test-data/${id}/records`); }
  saveDataRecord(id: string, body: { key: string; values: unknown; isActive: boolean }) { return this.http.put<DataRecord>(`${API}/test-data/${id}/records`, body); }
  deleteDataRecord(id: string, recordId: string) { return this.http.delete<void>(`${API}/test-data/${id}/records/${recordId}`); }
  importDataRecords(id: string, file: File, replace: boolean) {
    const form = new FormData(); form.append('file', file); form.append('replace', String(replace));
    return this.http.post<{ created: number; updated: number; errors: string[] }>(`${API}/test-data/${id}/import`, form);
  }
  reservations(activeOnly = true) { return this.http.get<Reservation[]>(`${API}/test-data/reservations`, { params: toParams({ activeOnly }) }); }
  releaseReservation(id: string) { return this.http.post<void>(`${API}/test-data/reservations/${id}/release`, {}); }
  plans(templates = false) { return this.http.get<PlanSummary[]>(`${API}/plans`, { params: toParams({ templates }) }); }
  plan(id: string) { return this.http.get<PlanDetail>(`${API}/plans/${id}`); }
  savePlan(id: string | null, body: object) { return id ? this.http.put<PlanDetail>(`${API}/plans/${id}`, body) : this.http.post<PlanDetail>(`${API}/plans`, body); }
  clonePlan(id: string, name: string, asTemplate: boolean) { return this.http.post<PlanDetail>(`${API}/plans/${id}/clone`, { name, asTemplate }); }
  deletePlan(id: string) { return this.http.delete<void>(`${API}/plans/${id}`); }
  planStatus(id: string, status: PlanStatus, comment?: string) { return this.http.post<PlanDetail>(`${API}/plans/${id}/status`, { status, comment }); }
  executePlan(id: string, body: { environmentId?: string; buildVersion?: string; variables?: Record<string, string>; priority?: number; ignoreEnvironmentHealth?: boolean }) {
    return this.http.post<RunSummary>(`${API}/plans/${id}/execute`, body);
  }
  campaigns() { return this.http.get<Campaign[]>(`${API}/campaigns`); }
  campaign(id: string) { return this.http.get<Campaign>(`${API}/campaigns/${id}`); }
  saveCampaign(id: string | null, body: object) { return id ? this.http.put<Campaign>(`${API}/campaigns/${id}`, body) : this.http.post<Campaign>(`${API}/campaigns`, body); }
  deleteCampaign(id: string) { return this.http.delete<void>(`${API}/campaigns/${id}`); }
  executeCampaign(id: string) { return this.http.post<RunSummary[]>(`${API}/campaigns/${id}/execute`, {}); }
  schedules() { return this.http.get<Schedule[]>(`${API}/schedules`); }
  saveSchedule(id: string | null, body: object) { return id ? this.http.put<Schedule>(`${API}/schedules/${id}`, body) : this.http.post<Schedule>(`${API}/schedules`, body); }
  deleteSchedule(id: string) { return this.http.delete<void>(`${API}/schedules/${id}`); }
  scheduleToken(id: string) { return this.http.post<{ schedule: Schedule; token: string; url: string }>(`${API}/schedules/${id}/webhook-token`, {}); }
  runScheduleNow(id: string) { return this.http.post<RunSummary[]>(`${API}/schedules/${id}/run-now`, {}); }

  // ---- audit & admin ----
  audit(q: { entityType?: string; userName?: string; action?: string; from?: string; to?: string; page?: number; pageSize?: number }) {
    return this.http.get<PagedResult<AuditEntry>>(`${API}/audit`, { params: toParams(q) });
  }
  users() { return this.http.get<UserSummary[]>(`${API}/admin/users`); }
  createUser(body: { userName: string; displayName: string; email?: string; password: string; roles: string[] }) { return this.http.post<UserSummary>(`${API}/admin/users`, body); }
  updateUser(id: string, body: { displayName: string; email?: string; isActive: boolean; roles: string[] }) { return this.http.put<UserSummary>(`${API}/admin/users/${id}`, body); }
  resetPassword(id: string, newPassword: string) { return this.http.post<void>(`${API}/admin/users/${id}/reset-password`, { newPassword }); }
  setProductAccess(id: string, access: { productId: string; canEdit: boolean }[]) { return this.http.put<UserSummary>(`${API}/admin/users/${id}/product-access`, access); }
  roles() { return this.http.get<Role[]>(`${API}/admin/roles`); }
  permissions() { return this.http.get<Permission[]>(`${API}/admin/permissions`); }
  setRolePermissions(id: string, codes: string[]) { return this.http.put<Role>(`${API}/admin/roles/${id}/permissions`, codes); }
}

/** Saves a blob response as a file in the browser. */
export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
