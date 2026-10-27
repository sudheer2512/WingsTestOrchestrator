import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { AuthService, problemMessage } from './core/auth.service';
import { ChangesService, UiService } from './core/ui.service';
import { NotificationBell } from './quality/notifications';

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, NotificationBell],
  template: `
    <header class="topbar">
      <a class="brand" routerLink="/features">
        <mat-icon>flight_takeoff</mat-icon><span>Wings Test Orchestrator</span>
      </a>
      <nav>
        @if (auth.can('reports.view')) { <a routerLink="/dashboard" routerLinkActive="active">Dashboard</a> }
        <a routerLink="/features" routerLinkActive="active">Features</a>
        <a [matMenuTriggerFor]="testingMenu" [class.active]="isIn(['/test-cases', '/suites', '/plans', '/campaigns'])">Testing <mat-icon class="caret">expand_more</mat-icon></a>
        <a routerLink="/test-code" routerLinkActive="active">Test code</a>
        <a [matMenuTriggerFor]="execMenu" [class.active]="isIn(['/runs', '/schedules', '/agents', '/environments', '/test-data'])">Execution <mat-icon class="caret">expand_more</mat-icon></a>
        @if (auth.can('ai.use')) { <a routerLink="/ai" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }"><mat-icon class="caret">auto_awesome</mat-icon> AI</a> }
        <a [matMenuTriggerFor]="qualityMenu" [class.active]="isIn(['/defects', '/releases', '/reports', '/impact'])">Quality <mat-icon class="caret">expand_more</mat-icon></a>
        @if (auth.can('audit.view')) { <a routerLink="/audit" routerLinkActive="active">Audit trail</a> }
        @if (auth.can('users.manage') || auth.can('products.manage') || auth.can('masterdata.manage') || auth.can('notifications.manage') || auth.can('integrations.manage') || auth.can('ai.manage')) {
          <a [matMenuTriggerFor]="adminMenu" [class.active]="isIn(['/admin', '/notifications/settings', '/integrations', '/ai/settings'])">Administration <mat-icon class="caret">expand_more</mat-icon></a> }
      </nav>
      <span class="spacer"></span>
      <app-notification-bell />
      <span class="live" [class.on]="changes.connected()" [matTooltip]="changes.connected() ? 'Live updates connected' : 'Live updates offline'"></span>
      <button mat-button [matMenuTriggerFor]="userMenu" class="user">
        <mat-icon>account_circle</mat-icon> {{ auth.user()?.displayName }}
      </button>
      <mat-menu #testingMenu>
        <a mat-menu-item routerLink="/test-cases"><mat-icon>checklist</mat-icon>Test cases</a>
        <a mat-menu-item routerLink="/suites"><mat-icon>playlist_play</mat-icon>Suites</a>
        <a mat-menu-item routerLink="/plans"><mat-icon>assignment</mat-icon>Test plans</a>
        <a mat-menu-item routerLink="/campaigns"><mat-icon>campaign</mat-icon>Campaigns</a>
      </mat-menu>
      <mat-menu #execMenu>
        <a mat-menu-item routerLink="/runs"><mat-icon>play_circle</mat-icon>Runs</a>
        <a mat-menu-item routerLink="/schedules"><mat-icon>schedule</mat-icon>Schedules & triggers</a>
        <a mat-menu-item routerLink="/agents"><mat-icon>dns</mat-icon>Agents</a>
        <a mat-menu-item routerLink="/environments"><mat-icon>cloud</mat-icon>Environments</a>
        <a mat-menu-item routerLink="/test-data"><mat-icon>dataset</mat-icon>Test data</a>
      </mat-menu>
      <mat-menu #qualityMenu>
        <a mat-menu-item routerLink="/defects"><mat-icon>bug_report</mat-icon>Defects</a>
        <a mat-menu-item routerLink="/releases"><mat-icon>verified</mat-icon>Release readiness</a>
        <a mat-menu-item routerLink="/impact"><mat-icon>account_tree</mat-icon>Change impact</a>
        @if (auth.can('reports.view')) { <a mat-menu-item routerLink="/reports"><mat-icon>summarize</mat-icon>Reports</a> }
      </mat-menu>
      <mat-menu #adminMenu>
        @if (auth.can('users.manage') || auth.can('products.manage') || auth.can('masterdata.manage')) { <a mat-menu-item routerLink="/admin"><mat-icon>admin_panel_settings</mat-icon>Users, products & master data</a> }
        @if (auth.can('notifications.manage')) { <a mat-menu-item routerLink="/notifications/settings"><mat-icon>notifications_active</mat-icon>Notifications</a> }
        @if (auth.can('integrations.manage')) { <a mat-menu-item routerLink="/integrations"><mat-icon>hub</mat-icon>Integrations</a> }
        @if (auth.can('ai.manage')) { <a mat-menu-item routerLink="/ai/settings"><mat-icon>auto_awesome</mat-icon>AI assistant policy</a> }
      </mat-menu>
      <mat-menu #userMenu>
        <a mat-menu-item routerLink="/notifications"><mat-icon>notifications</mat-icon>Notifications & subscriptions</a>
        <div class="menu-head">{{ auth.user()?.userName }}<br><small>{{ auth.user()?.roles?.join(', ') }}</small></div>
        <button mat-menu-item (click)="auth.logout()"><mat-icon>logout</mat-icon>Sign out</button>
      </mat-menu>
    </header>
    <main class="content"><router-outlet /></main>`,
  styles: `
    :host { display: flex; flex-direction: column; height: 100vh; }
    .topbar { display: flex; align-items: center; gap: 16px; height: 44px; padding: 0 12px; background: var(--chrome); color: var(--chrome-fg); flex: none; }
    .brand { display: flex; align-items: center; gap: 6px; font-weight: 600; color: inherit; text-decoration: none; white-space: nowrap; }
    nav { display: flex; gap: 2px; overflow-x: auto; }
    nav a { color: var(--chrome-muted); text-decoration: none; padding: 6px 10px; border-radius: 4px; white-space: nowrap; }
    nav a:hover { color: var(--chrome-fg); background: rgba(255,255,255,.08); }
    nav a.active { color: var(--chrome-fg); background: rgba(255,255,255,.16); }
    nav a { cursor: pointer; display: inline-flex; align-items: center; } .caret { font-size: 16px; width: 16px; height: 16px; }
    .spacer { flex: 1; }
    .user { color: var(--chrome-fg) !important; }
    .live { width: 8px; height: 8px; border-radius: 50%; background: #888; } .live.on { background: #3fb950; }
    .menu-head { padding: 8px 16px; font-weight: 600; } .menu-head small { font-weight: 400; color: var(--muted); }
    .content { flex: 1; min-height: 0; overflow: hidden; }
  `,
})
export class Shell implements OnInit {
  auth = inject(AuthService);
  private router = inject(Router);
  private url = toSignal(this.router.events.pipe(filter(e => e instanceof NavigationEnd), map(() => this.router.url)), { initialValue: this.router.url });
  isIn(prefixes: string[]) { return prefixes.some(p => this.url().startsWith(p)); }
  changes = inject(ChangesService);
  private ui = inject(UiService);

  ngOnInit() {
    this.auth.refresh();
    this.ui.loadLookups().catch(e => this.ui.error(e));
    this.changes.start();
  }
}

@Component({
  selector: 'app-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, MatProgressBarModule],
  template: `
    <div class="wrap">
      <form class="card" (ngSubmit)="submit()">
        <div class="title"><mat-icon>flight_takeoff</mat-icon><h1>Wings Test Orchestrator</h1></div>
        <p class="muted">Sign in to manage features, test cases and test code.</p>
        <mat-form-field class="full"><mat-label>User name</mat-label><input matInput name="u" [(ngModel)]="userName" autocomplete="username" required /></mat-form-field>
        <mat-form-field class="full"><mat-label>Password</mat-label><input matInput name="p" type="password" [(ngModel)]="password" autocomplete="current-password" required /></mat-form-field>
        @if (error()) { <p class="error">{{ error() }}</p> }
        <button mat-flat-button class="full" [disabled]="busy() || !userName || !password">Sign in</button>
        @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
      </form>
    </div>`,
  styles: `
    .wrap { display: grid; place-items: center; height: 100vh; background: var(--canvas); padding: 16px; }
    .card { width: min(380px, 100%); background: var(--surface); padding: 28px; border-radius: 10px; box-shadow: 0 6px 24px rgba(0,0,0,.08); }
    .title { display: flex; align-items: center; gap: 8px; } h1 { font-size: 20px; margin: 0; }
    .error { color: var(--bad); }
  `,
})
export class Login {
  private auth = inject(AuthService);
  private router = inject(Router);
  userName = '';
  password = '';
  busy = signal(false);
  error = signal('');

  async submit() {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.auth.login(this.userName, this.password);
      this.router.navigate(['/features']);
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
