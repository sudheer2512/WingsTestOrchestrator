import { Routes, UrlMatchResult, UrlSegment } from '@angular/router';
import { authGuard } from './core/auth.service';
import { Login, Shell } from './shell';

export function featuresMatcher(segments: UrlSegment[]): UrlMatchResult | null {
  if (segments[0]?.path !== 'features' || segments.length > 2) return null;
  return { consumed: segments, posParams: segments[1] ? { id: segments[1] } : {} };
}

export const routes: Routes = [
  { path: 'login', component: Login, title: 'Sign in · Wings Test Orchestrator' },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'features' },
      // One route for both /features and /features/:id so selecting a feature reuses the workspace (tree state is kept).
      { matcher: featuresMatcher, loadComponent: () => import('./features/feature-workspace').then(m => m.FeatureWorkspace), title: 'Features' },
      { path: 'test-cases', loadComponent: () => import('./testing/test-case-list').then(m => m.TestCaseList), title: 'Test cases' },
      { path: 'test-cases/new', loadComponent: () => import('./testing/test-case-editor').then(m => m.TestCaseEditor), title: 'New test case' },
      { path: 'test-cases/:id', loadComponent: () => import('./testing/test-case-editor').then(m => m.TestCaseEditor), title: 'Test case' },
      { path: 'suites', loadComponent: () => import('./testing/suites').then(m => m.Suites), title: 'Test suites' },
      { path: 'suites/:id', loadComponent: () => import('./testing/suites').then(m => m.Suites), title: 'Test suites' },
      { path: 'test-code', loadComponent: () => import('./packages/package-list').then(m => m.PackageList), title: 'Test code' },
      { path: 'test-code/upload', loadComponent: () => import('./packages/package-upload').then(m => m.PackageUpload), title: 'Upload test code' },
      { path: 'test-code/versions/:versionId', loadComponent: () => import('./packages/package-version').then(m => m.PackageVersion), title: 'Test package' },
      { path: 'runs', loadComponent: () => import('./execution/runs').then(m => m.Runs), title: 'Test runs' },
      { path: 'runs/:id', loadComponent: () => import('./execution/run-monitor').then(m => m.RunMonitor), title: 'Run monitor' },
      { path: 'agents', loadComponent: () => import('./execution/agents').then(m => m.Agents), title: 'Execution agents' },
      { path: 'environments', loadComponent: () => import('./orchestration/environments').then(m => m.Environments), title: 'Environments' },
      { path: 'test-data', loadComponent: () => import('./orchestration/test-data').then(m => m.TestData), title: 'Test data' },
      { path: 'plans', loadComponent: () => import('./orchestration/plans').then(m => m.Plans), title: 'Test plans' },
      { path: 'plans/:id', loadComponent: () => import('./orchestration/plans').then(m => m.Plans), title: 'Test plans' },
      { path: 'campaigns', loadComponent: () => import('./orchestration/campaigns').then(m => m.Campaigns), title: 'Campaigns' },
      { path: 'schedules', loadComponent: () => import('./orchestration/schedules').then(m => m.Schedules), title: 'Schedules' },
      { path: 'dashboard', loadComponent: () => import('./quality/dashboards').then(m => m.GlobalDashboardPage), title: 'Quality dashboard' },
      { path: 'dashboard/execution', loadComponent: () => import('./quality/dashboards').then(m => m.ExecutionDashboardPage), title: 'Execution dashboard' },
      { path: 'dashboard/products/:id', loadComponent: () => import('./quality/dashboards').then(m => m.ProductDashboardPage), title: 'Product dashboard' },
      { path: 'defects', loadComponent: () => import('./quality/defects').then(m => m.Defects), title: 'Defects' },
      { path: 'defects/:id', loadComponent: () => import('./quality/defects').then(m => m.DefectDetailPage), title: 'Defect' },
      { path: 'releases', loadComponent: () => import('./quality/releases').then(m => m.Releases), title: 'Release readiness' },
      { path: 'releases/:id', loadComponent: () => import('./quality/releases').then(m => m.Releases), title: 'Release readiness' },
      { path: 'reports', loadComponent: () => import('./quality/reports').then(m => m.Reports), title: 'Reports' },
      { path: 'impact', loadComponent: () => import('./quality/integrations').then(m => m.Impact), title: 'Change impact' },
      { path: 'impact/builds/:id', loadComponent: () => import('./quality/integrations').then(m => m.Impact), title: 'Build impact' },
      { path: 'integrations', loadComponent: () => import('./quality/integrations').then(m => m.Integrations), title: 'Integrations' },
      { path: 'notifications', loadComponent: () => import('./quality/notifications').then(m => m.Notifications), title: 'Notifications' },
      { path: 'notifications/settings', loadComponent: () => import('./quality/notifications').then(m => m.NotificationSettings), title: 'Notification settings' },
      { path: 'ai', loadComponent: () => import('./ai/assistant').then(m => m.AiAssistant), title: 'AI testing assistant' },
      { path: 'ai/settings', loadComponent: () => import('./ai/ai-settings').then(m => m.AiSettings), title: 'AI policy' },
      { path: 'audit', loadComponent: () => import('./admin/audit').then(m => m.AuditTrail), title: 'Audit trail' },
      { path: 'admin', loadComponent: () => import('./admin/admin').then(m => m.Admin), title: 'Administration' },
    ],
  },
  { path: '**', redirectTo: '' },
];
