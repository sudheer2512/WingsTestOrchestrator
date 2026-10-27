import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { debounceTime, firstValueFrom, interval } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Agent } from '../core/models';
import { ChangesService, UiService } from '../core/ui.service';

/** Execution agent registry: status, capabilities, enrolment keys, maintenance. */
@Component({
  selector: 'app-agents',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatMenuModule, MatTooltipModule],
  template: `
    <div class="page">
      <div class="page-head"><h1>Execution agents</h1><span class="muted">{{ online() }} of {{ agents().length }} online</span></div>

      @if (newKey(); as k) {
        <div class="panel key">
          <h3>Agent key for {{ k.name }} — shown only once</h3>
          <div class="row"><code class="mono">{{ k.key }}</code><button mat-stroked-button (click)="copy(k.key)"><mat-icon>content_copy</mat-icon>Copy</button></div>
          <p class="small">Start the agent on the target machine (in a logged-in user session for desktop agents):</p>
          <pre class="mono">{{ snippet(k.key) }}</pre>
          <button mat-button (click)="newKey.set(null)">I have stored the key</button>
        </div>
      }

      @if (auth.can('agents.manage')) {
        <div class="panel row wrap-row">
          <mat-form-field><mat-label>New agent name</mat-label><input matInput [(ngModel)]="name" placeholder="QA-WIN-01" /></mat-form-field>
          <mat-form-field><mat-label>Kind</mat-label><mat-select [(ngModel)]="kind">
            <mat-option value="Desktop">Windows desktop (FlaUI)</mat-option><mat-option value="Browser">Browser (Playwright)</mat-option>
            <mat-option value="Api">API</mat-option><mat-option value="Database">Database</mat-option></mat-select></mat-form-field>
          <mat-form-field><mat-label>Pool</mat-label><input matInput [(ngModel)]="pool" placeholder="qa" /></mat-form-field>
          <button mat-flat-button [disabled]="!name.trim()" (click)="create()"><mat-icon>add</mat-icon>Register agent</button>
        </div>
      }

      <table class="data">
        <thead><tr><th></th><th>Agent</th><th>Kind</th><th>Machine</th><th>Frameworks</th><th>Slots</th><th>Desktop session</th><th>Last heartbeat</th><th></th></tr></thead>
        <tbody>
          @for (a of agents(); track a.id) {
            <tr>
              <td><span class="dot" [class.on]="a.isOnline && a.isEnabled && !a.inMaintenance" [class.maint]="a.inMaintenance" [matTooltip]="state(a)"></span></td>
              <td><b>{{ a.name }}</b> @if (!a.isEnabled) { <span class="pill">disabled</span> } @if (a.inMaintenance) { <span class="pill" data-tone="warn">maintenance</span> }
                <div class="muted small">{{ a.pool }} · key {{ a.keyPrefix }}…</div></td>
              <td>{{ a.kind }}</td>
              <td>{{ a.machineName ?? 'never connected' }}<div class="muted small">{{ a.operatingSystem }}{{ a.agentVersion ? ' · agent ' + a.agentVersion : '' }}</div>
                @if (a.processorCount) { <div class="muted small">{{ a.processorCount }} CPU · {{ (a.totalMemoryMb / 1024).toFixed(1) }} GB</div> }
                @if (tools(a); as t) { <div class="muted small">{{ t }}</div> }</td>
              <td>@for (f of a.frameworks; track f) { <span class="tag">{{ f }}</span> }</td>
              <td class="num">{{ a.activeJobs }} / {{ a.slots }}</td>
              <td>@if (a.kind === 'Desktop') { @if (a.interactiveSession) { <span class="pill" data-tone="good">interactive</span> } @else { <span class="pill" data-tone="bad">none</span> }
                <div class="muted small">{{ a.screenResolution }}{{ a.dpiScalePercent ? ' @ ' + a.dpiScalePercent + '%' : '' }}</div> }</td>
              <td class="muted">{{ a.lastHeartbeatAt ? (a.lastHeartbeatAt | date: 'medium') : '—' }}</td>
              <td>@if (auth.can('agents.manage')) {
                <button mat-icon-button [matMenuTriggerFor]="m"><mat-icon>more_vert</mat-icon></button>
                <mat-menu #m>
                  <button mat-menu-item (click)="update(a, { isEnabled: !a.isEnabled })">{{ a.isEnabled ? 'Disable' : 'Enable' }}</button>
                  <button mat-menu-item (click)="update(a, { inMaintenance: !a.inMaintenance })">{{ a.inMaintenance ? 'End maintenance' : 'Put in maintenance (finish current jobs, take no new ones)' }}</button>
                  <button mat-menu-item (click)="rotate(a)">Rotate key</button>
                  <button mat-menu-item (click)="remove(a)">Delete</button>
                </mat-menu> }</td>
            </tr>
          } @empty { <tr><td colspan="9" class="empty">No agents registered. Register one, then start Wings.Agent on a test machine with its key.</td></tr> }
        </tbody>
      </table>
    </div>`,
  styles: `
    .dot { display: inline-block; width: 10px; height: 10px; border-radius: 50%; background: #c9ced6; } .dot.on { background: #2da44e; } .dot.maint { background: #d4a72c; }
    .small { font-size: 11px; } .key { background: var(--warn-bg); } code { background: var(--surface); padding: 4px 8px; border-radius: 4px; word-break: break-all; }
    pre { background: var(--surface); padding: 8px; border-radius: 4px; white-space: pre-wrap; font-size: 12px; }
  `,
})
export class Agents {
  private api = inject(ApiService);
  private ui = inject(UiService);
  auth = inject(AuthService);
  agents = signal<Agent[]>([]);
  newKey = signal<{ name: string; key: string; kind: string } | null>(null);
  name = '';
  kind = 'Browser';
  pool = '';
  online = () => this.agents().filter(a => a.isOnline).length;

  constructor() {
    this.load();
    inject(ChangesService).events.pipe(debounceTime(300), takeUntilDestroyed()).subscribe(e => { if (e.topic === 'agentsChanged' || e.topic === 'runChanged') this.load(); });
    interval(15_000).pipe(takeUntilDestroyed()).subscribe(() => this.load()); // online state is time based
  }

  load() { this.api.agents().subscribe({ next: a => this.agents.set(a), error: e => this.ui.error(e) }); }

  async create() {
    try {
      const r = await firstValueFrom(this.api.createAgent({ name: this.name.trim(), kind: this.kind, pool: this.pool || undefined }));
      this.newKey.set({ name: r.agent.name, key: r.key, kind: r.agent.kind });
      this.name = '';
      this.load();
    } catch (e) { this.ui.error(e); }
  }

  async update(a: Agent, patch: Partial<Pick<Agent, 'isEnabled' | 'inMaintenance'>>) {
    try { await firstValueFrom(this.api.updateAgent(a.id, { name: a.name, kind: a.kind, pool: a.pool, description: a.description, isEnabled: a.isEnabled, inMaintenance: a.inMaintenance, version: a.version, ...patch })); this.load(); }
    catch (e) { this.ui.error(e); }
  }

  async rotate(a: Agent) {
    if (!await this.ui.confirm({ title: 'Rotate agent key', message: `The current key of ${a.name} stops working immediately. Configure the new key on the machine.`, confirmText: 'Rotate', danger: true })) return;
    try { const r = await firstValueFrom(this.api.rotateAgentKey(a.id)); this.newKey.set({ name: a.name, key: r.key, kind: a.kind }); this.load(); } catch (e) { this.ui.error(e); }
  }

  async remove(a: Agent) {
    if (!await this.ui.confirm({ title: 'Delete agent', message: `Delete ${a.name}? Its history in past runs is kept.`, confirmText: 'Delete', danger: true })) return;
    try { await firstValueFrom(this.api.deleteAgent(a.id)); this.load(); } catch (e) { this.ui.error(e); }
  }

  copy(k: string) { navigator.clipboard?.writeText(k); this.ui.ok('Key copied'); }

  snippet(key: string) {
    const server = location.origin;
    const kind = this.newKey()?.kind ?? 'Browser';
    return `set WINGS_AGENT__SERVERURL=${server}\nset WINGS_AGENT__AGENTKEY=${key}\nset WINGS_AGENT__KIND=${kind}\nWings.Agent.exe\n\n# macOS / Linux\nWINGS_AGENT__SERVERURL=${server} WINGS_AGENT__AGENTKEY=${key} WINGS_AGENT__KIND=${kind} ./Wings.Agent`;
  }

  state(a: Agent) { return !a.isEnabled ? 'Disabled' : a.inMaintenance ? 'Maintenance' : a.isOnline ? 'Online' : 'Offline'; }

  tools(a: Agent) {
    try { const c = JSON.parse(a.capabilitiesJson ?? '{}') as { tools?: Record<string, string> }; return Object.entries(c.tools ?? {}).map(([k, v]) => `${k} ${v}`).join(' · '); }
    catch { return ''; }
  }
}
