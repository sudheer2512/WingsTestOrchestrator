import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { debounceTime, firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Product } from '../core/models';
import { Channel, ChannelType, Escalation, EventLog, EventType, Inbox, InboxItem, QualityApi, Rule, Template } from '../core/quality';
import { ChangesService, UiService } from '../core/ui.service';
import { StatusPill } from '../shared/display';

const SEVERITIES = ['Info', 'Warning', 'Critical'];
const sevTone = (s: string) => s === 'Critical' ? 'Failed' : s === 'Warning' ? 'Warning' : 'neutral';

/** Top-bar bell: unread count, the latest notifications, and a link to the inbox. */
@Component({
  selector: 'app-notification-bell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, MatButtonModule, MatIconModule, MatMenuModule, MatBadgeModule, MatTooltipModule],
  template: `
    <button mat-icon-button class="bell" [matMenuTriggerFor]="menu" (menuOpened)="load()" matTooltip="Notifications">
      <mat-icon [matBadge]="inbox().unread || null" matBadgeColor="warn" matBadgeSize="small">notifications</mat-icon>
    </button>
    <mat-menu #menu xPosition="before" class="bell-menu">
      <div class="head" (click)="$event.stopPropagation()"><b>Notifications</b><span class="spacer"></span>
        @if (inbox().unread) { <button mat-button (click)="readAll()">Mark all read</button> }</div>
      @for (n of inbox().items.slice(0, 8); track n.eventId) {
        <button mat-menu-item class="n" [class.unread]="!n.readAt" (click)="open(n)">
          <span class="dot" [attr.data-sev]="n.severity"></span><span class="t">{{ n.title }}</span><span class="when">{{ n.createdAt | date: 'short' }}</span>
        </button>
      } @empty { <div class="none">Nothing yet. Subscribe to events on the notifications page.</div> }
      <button mat-menu-item (click)="router.navigate(['/notifications'])"><mat-icon>inbox</mat-icon>All notifications & subscriptions</button>
    </mat-menu>`,
  styles: `
    .bell { color: var(--chrome-fg) !important; }
    .head { display: flex; align-items: center; padding: 4px 12px; min-width: 360px; } .none { padding: 12px 16px; color: var(--muted); max-width: 360px; }
    .n { height: auto !important; min-height: 40px; } .n .t { white-space: normal; line-height: 1.3; } .n.unread .t { font-weight: 600; }
    .when { display: block; font-size: 11px; color: var(--muted); }
    .dot { display: inline-block; width: 8px; height: 8px; border-radius: 50%; margin-right: 8px; background: var(--accent); }
    .dot[data-sev='Warning'] { background: var(--warn); } .dot[data-sev='Critical'] { background: var(--bad); }
  `,
})
export class NotificationBell {
  private q = inject(QualityApi);
  router = inject(Router);
  inbox = signal<Inbox>({ unread: 0, items: [] });

  constructor() {
    this.load();
    inject(ChangesService).events.pipe(debounceTime(500), takeUntilDestroyed()).subscribe(e => { if (e.topic === 'notificationsChanged') this.load(); });
  }

  load() { this.q.inbox(false, 20).subscribe({ next: i => this.inbox.set(i), error: () => { } }); }
  async readAll() { await firstValueFrom(this.q.markRead()); this.load(); }
  async open(n: InboxItem) {
    if (!n.readAt) await firstValueFrom(this.q.markRead([n.eventId]));
    this.load();
    if (n.link) this.router.navigateByUrl(n.link);
  }
}

/** Inbox and the signed-in user's own subscriptions. */
@Component({
  selector: 'app-notifications',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, DatePipe, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, StatusPill],
  template: `
    <div class="page">
      <div class="page-head"><h1>Notifications</h1><span class="spacer"></span>
        @if (auth.can('notifications.manage')) { <a mat-stroked-button routerLink="/notifications/settings"><mat-icon>settings</mat-icon>Channels, rules & escalation</a> }
        <button mat-stroked-button (click)="readAll()" [disabled]="!inbox().unread">Mark all read</button></div>
      <div class="cols">
        <div class="panel">
          <div class="row"><h3>Inbox</h3><mat-checkbox [(ngModel)]="unreadOnly" (ngModelChange)="load()">Unread only</mat-checkbox></div>
          <table class="data"><tbody>
            @for (n of inbox().items; track n.eventId) {
              <tr [class.unread]="!n.readAt">
                <td><app-status [value]="tone(n.severity)" [label]="n.severity" /></td>
                <td><a (click)="open(n)" class="t">{{ n.title }}</a>@if (n.body) { <div class="pre muted body">{{ n.body }}</div> }
                  <div class="muted small">{{ n.type }} · {{ n.createdAt | date: 'medium' }}{{ n.escalatedAt ? ' · escalated' : '' }}{{ n.acknowledgedAt ? ' · acknowledged by ' + n.acknowledgedBy : '' }}</div></td>
                <td>@if (!n.acknowledgedAt) { <button mat-stroked-button (click)="ack(n)">Acknowledge</button> }</td>
              </tr>
            } @empty { <tr><td class="empty">No notifications.</td></tr> }
          </tbody></table>
        </div>
        <div class="panel">
          <h3>My subscriptions</h3>
          <p class="muted small">You receive matching events in the inbox (and optionally by email). Acknowledging stops escalation.</p>
          @for (r of rules(); track r.id) {
            <div class="rule"><b>{{ r.name }}</b> <span class="muted">{{ r.eventTypes.join(', ') }} · {{ r.productName ?? 'all products' }} · {{ r.minSeverity }}+{{ r.emailUser ? ' · email' : '' }}</span>
              <button mat-icon-button (click)="remove(r)"><mat-icon>delete</mat-icon></button></div>
          } @empty { <p class="muted">No subscriptions.</p> }
          <h3>Subscribe</h3>
          <mat-form-field class="full"><mat-label>Name</mat-label><input matInput [(ngModel)]="draft.name" placeholder="Failures in Wings Auto" /></mat-form-field>
          <mat-form-field class="full"><mat-label>Events</mat-label><mat-select multiple [(ngModel)]="draft.eventTypes">
            <mat-option value="*">Everything</mat-option>@for (t of types(); track t.type) { <mat-option [value]="t.type">{{ t.description }}</mat-option> }</mat-select></mat-form-field>
          <div class="grid-2">
            <mat-form-field><mat-label>Product</mat-label><mat-select [(ngModel)]="draft.productId"><mat-option [value]="undefined">All</mat-option>
              @for (p of products(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field><mat-label>Minimum severity</mat-label><mat-select [(ngModel)]="draft.minSeverity">@for (s of severities; track s) { <mat-option [value]="s">{{ s }}</mat-option> }</mat-select></mat-form-field>
          </div>
          <mat-checkbox [(ngModel)]="draft.emailUser">Also email me</mat-checkbox>
          <div><button mat-flat-button [disabled]="!draft.name || !draft.eventTypes.length" (click)="subscribe()">Subscribe</button></div>
        </div>
      </div>
    </div>`,
  styles: `
    .cols { display: grid; grid-template-columns: minmax(0, 1fr) 380px; gap: 12px; align-items: start; } .cols .panel + .panel { margin-top: 0; }
    @media (max-width: 1100px) { .cols { grid-template-columns: minmax(0, 1fr); } }
    tr.unread .t { font-weight: 600; } .t { cursor: pointer; } .body { font-size: 12px; max-height: 80px; overflow: hidden; } .small { font-size: 11px; }
    .rule { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; } h3 { margin-top: 12px; }
  `,
})
export class Notifications {
  private q = inject(QualityApi);
  private api = inject(ApiService);
  private ui = inject(UiService);
  private router = inject(Router);
  auth = inject(AuthService);
  inbox = signal<Inbox>({ unread: 0, items: [] });
  rules = signal<Rule[]>([]);
  types = signal<EventType[]>([]);
  products = signal<Product[]>([]);
  severities = SEVERITIES;
  tone = sevTone;
  unreadOnly = false;
  draft = { name: '', eventTypes: [] as string[], productId: undefined as string | undefined, minSeverity: 'Info', emailUser: false };

  constructor() {
    this.load();
    this.loadRules();
    this.q.eventTypes().subscribe(t => this.types.set(t));
    this.api.products().subscribe(p => this.products.set(p));
    inject(ChangesService).events.pipe(debounceTime(500), takeUntilDestroyed()).subscribe(e => { if (e.topic === 'notificationsChanged') this.load(); });
  }

  load() { this.q.inbox(this.unreadOnly, 200).subscribe({ next: i => this.inbox.set(i), error: e => this.ui.error(e) }); }
  loadRules() { this.q.rules().subscribe(r => this.rules.set(r)); }
  async readAll() { await firstValueFrom(this.q.markRead()); this.load(); }
  async open(n: InboxItem) { if (!n.readAt) await firstValueFrom(this.q.markRead([n.eventId])); if (n.link) this.router.navigateByUrl(n.link); else this.load(); }
  async ack(n: InboxItem) { try { await firstValueFrom(this.q.acknowledge(n.eventId)); this.load(); } catch (e) { this.ui.error(e); } }

  async subscribe() {
    try {
      await firstValueFrom(this.q.saveRule(null, this.draft));
      this.draft = { name: '', eventTypes: [], productId: undefined, minSeverity: 'Info', emailUser: false };
      this.loadRules();
      this.ui.ok('Subscribed');
    } catch (e) { this.ui.error(e); }
  }

  async remove(r: Rule) { try { await firstValueFrom(this.q.deleteRule(r.id)); this.loadRules(); } catch (e) { this.ui.error(e); } }
}

type SettingsTab = 'channels' | 'rules' | 'escalation' | 'templates' | 'log';

/** Administration of channels (email, Teams, webhook), routing rules, escalation policies, templates and the delivery log. */
@Component({
  selector: 'app-notification-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, MatTooltipModule, StatusPill],
  template: `
    <div class="page">
      <div class="page-head"><h1>Notification settings</h1></div>
      <nav class="tabs">@for (t of tabs; track t[0]) { <a [class.on]="tab() === t[0]" (click)="tab.set(t[0]); refresh()">{{ t[1] }}</a> }</nav>
      @switch (tab()) {
        @case ('channels') {
          <div class="cols">
            <table class="data"><thead><tr><th>Name</th><th>Type</th><th>Destination</th><th>Enabled</th><th></th></tr></thead><tbody>
              @for (c of channels(); track c.id) {
                <tr class="clickable" (click)="editChannel(c)"><td>{{ c.name }}</td><td>{{ c.type }}</td><td>{{ c.recipients ?? c.urlHint }}{{ c.hasSigningSecret ? ' · signed' : '' }}</td>
                  <td>{{ c.isEnabled ? 'Yes' : 'No' }}</td><td><button mat-button (click)="$event.stopPropagation(); test(c)">Send test</button></td></tr>
              } @empty { <tr><td colspan="5" class="empty">No channels.</td></tr> }</tbody></table>
            <div class="panel">
              <h3>{{ ch.id ? 'Edit channel' : 'New channel' }}</h3>
              <mat-form-field class="full"><mat-label>Name</mat-label><input matInput [(ngModel)]="ch.name" /></mat-form-field>
              <mat-form-field class="full"><mat-label>Type</mat-label><mat-select [(ngModel)]="ch.type">@for (t of channelTypes; track t) { <mat-option [value]="t">{{ t }}</mat-option> }</mat-select></mat-form-field>
              @if (ch.type === 'Email') { <mat-form-field class="full"><mat-label>Recipients (comma separated)</mat-label><input matInput [(ngModel)]="ch.recipients" /></mat-form-field> }
              @else {
                <mat-form-field class="full"><mat-label>{{ ch.type === 'Teams' ? 'Teams workflow / incoming webhook URL' : 'Webhook URL' }}</mat-label>
                  <input matInput [(ngModel)]="ch.url" [placeholder]="ch.hasUrl ? 'stored encrypted; leave empty to keep' : 'https://…'" /></mat-form-field>
                @if (ch.type === 'Webhook') { <mat-form-field class="full"><mat-label>Signing secret (HMAC-SHA256, X-Wings-Signature)</mat-label>
                  <input matInput [(ngModel)]="ch.signingSecret" [placeholder]="ch.hasSigningSecret ? 'stored encrypted; leave empty to keep' : 'optional'" /></mat-form-field> }
              }
              <mat-checkbox [(ngModel)]="ch.isEnabled">Enabled</mat-checkbox>
              <div class="row"><button mat-flat-button (click)="saveChannel()">Save</button><button mat-button (click)="newChannel()">New</button>
                @if (ch.id) { <span class="spacer"></span><button mat-button (click)="deleteChannel()">Delete</button> }</div>
              <p class="muted small">Email uses the SMTP server in configuration (Notifications:Smtp); without one, messages are written as .eml files to the storage mail-outbox folder.</p>
            </div>
          </div>
        }
        @case ('rules') {
          <div class="cols">
            <table class="data"><thead><tr><th>Name</th><th>Events</th><th>Product</th><th>Min severity</th><th>Target</th><th></th></tr></thead><tbody>
              @for (r of rules(); track r.id) { <tr><td>{{ r.name }}</td><td class="small">{{ r.eventTypes.join(', ') }}</td><td>{{ r.productName ?? 'All' }}</td><td>{{ r.minSeverity }}</td>
                <td>{{ r.channelName ? 'Channel ' + r.channelName : 'User ' + r.userName + (r.emailUser ? ' (+email)' : '') }}</td>
                <td><button mat-icon-button (click)="deleteRule(r)"><mat-icon>delete</mat-icon></button></td></tr> }
              @empty { <tr><td colspan="6" class="empty">No rules.</td></tr> }</tbody></table>
            <div class="panel"><h3>Route events to a channel</h3>
              <mat-form-field class="full"><mat-label>Name</mat-label><input matInput [(ngModel)]="rule.name" /></mat-form-field>
              <mat-form-field class="full"><mat-label>Events</mat-label><mat-select multiple [(ngModel)]="rule.eventTypes"><mat-option value="*">Everything</mat-option>
                @for (t of types(); track t.type) { <mat-option [value]="t.type">{{ t.description }}</mat-option> }</mat-select></mat-form-field>
              <mat-form-field class="full"><mat-label>Channel</mat-label><mat-select [(ngModel)]="rule.channelId">@for (c of channels(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select></mat-form-field>
              <mat-form-field class="full"><mat-label>Product</mat-label><mat-select [(ngModel)]="rule.productId"><mat-option [value]="undefined">All</mat-option>
                @for (p of products(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }</mat-select></mat-form-field>
              <mat-form-field class="full"><mat-label>Minimum severity</mat-label><mat-select [(ngModel)]="rule.minSeverity">@for (s of severities; track s) { <mat-option [value]="s">{{ s }}</mat-option> }</mat-select></mat-form-field>
              <button mat-flat-button [disabled]="!rule.name || !rule.channelId || !rule.eventTypes.length" (click)="saveRule()">Add rule</button></div>
          </div>
        }
        @case ('escalation') {
          <div class="cols">
            <table class="data"><thead><tr><th>Name</th><th>Events</th><th>After</th><th>Escalate to</th><th></th></tr></thead><tbody>
              @for (p of escalations(); track p.id) { <tr><td>{{ p.name }}</td><td class="small">{{ p.eventTypes.join(', ') }}</td><td>{{ p.afterMinutes }} min</td><td>{{ p.channelName }}</td>
                <td><button mat-icon-button (click)="deleteEscalation(p)"><mat-icon>delete</mat-icon></button></td></tr> }
              @empty { <tr><td colspan="5" class="empty">No escalation policies.</td></tr> }</tbody></table>
            <div class="panel"><h3>New policy</h3>
              <p class="muted small">Events nobody acknowledged within the delay are re-sent, marked ESCALATED, to the chosen channel.</p>
              <mat-form-field class="full"><mat-label>Name</mat-label><input matInput [(ngModel)]="esc.name" /></mat-form-field>
              <mat-form-field class="full"><mat-label>Events</mat-label><mat-select multiple [(ngModel)]="esc.eventTypes">@for (t of types(); track t.type) { <mat-option [value]="t.type">{{ t.description }}</mat-option> }</mat-select></mat-form-field>
              <mat-form-field class="full"><mat-label>After (minutes)</mat-label><input matInput type="number" [(ngModel)]="esc.afterMinutes" /></mat-form-field>
              <mat-form-field class="full"><mat-label>Channel</mat-label><mat-select [(ngModel)]="esc.channelId">@for (c of channels(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select></mat-form-field>
              <button mat-flat-button [disabled]="!esc.name || !esc.channelId || !esc.eventTypes.length" (click)="saveEscalation()">Add policy</button></div>
          </div>
        }
        @case ('templates') {
          <p class="muted">Placeholders: {{ '{{title}} {{body}} {{link}} {{severity}} {{type}} {{time}} {{data.key}}' }}</p>
          @for (t of templates(); track t.eventType) {
            <div class="panel"><div class="row"><b>{{ t.description }}</b><span class="mono muted">{{ t.eventType }}</span>@if (t.isCustomized) { <span class="pill" data-tone="neutral">customised</span> }</div>
              <mat-form-field class="full"><mat-label>Subject</mat-label><input matInput [(ngModel)]="t.subject" /></mat-form-field>
              <mat-form-field class="full"><mat-label>Body</mat-label><textarea matInput rows="3" [(ngModel)]="t.body"></textarea></mat-form-field>
              <button mat-stroked-button (click)="saveTemplate(t)">Save</button>@if (t.isCustomized) { <button mat-button (click)="resetTemplate(t)">Reset to default</button> }</div>
          }
        }
        @case ('log') {
          <table class="data"><thead><tr><th>When</th><th>Severity</th><th>Event</th><th class="n">In-app</th><th>Deliveries</th><th>Acknowledged</th></tr></thead><tbody>
            @for (e of log(); track e.id) {
              <tr><td>{{ e.createdAt | date: 'short' }}</td><td><app-status [value]="tone(e.severity)" [label]="e.severity" /></td><td>{{ e.title }}<div class="muted small">{{ e.type }}</div></td>
                <td class="n">{{ e.recipients }}</td>
                <td>@for (d of e.deliveries; track d.id) { <div class="small" [matTooltip]="d.error ?? ''"><app-status [value]="d.status === 'Sent' ? 'Passed' : d.status === 'Failed' ? 'Failed' : 'Pending'" [label]="d.status" />
                  {{ d.target }}{{ d.isEscalation ? ' (escalation)' : '' }} · {{ d.attempts }} attempt(s)</div> }</td>
                <td>{{ e.acknowledgedBy ?? '' }}</td></tr>
            } @empty { <tr><td colspan="6" class="empty">No events yet.</td></tr> }</tbody></table>
        }
      }
    </div>`,
  styles: `
    .tabs { display: flex; gap: 4px; margin-bottom: 12px; } .tabs a { padding: 6px 12px; border-radius: 4px; cursor: pointer; color: var(--muted); }
    .tabs a.on { background: var(--selected); color: var(--text); font-weight: 600; }
    .cols { display: grid; grid-template-columns: minmax(0, 1fr) 380px; gap: 12px; align-items: start; } .small { font-size: 11px; }
    @media (max-width: 1100px) { .cols { grid-template-columns: minmax(0, 1fr); } } td.n, th.n { text-align: right; }
  `,
})
export class NotificationSettings {
  private q = inject(QualityApi);
  private api = inject(ApiService);
  private ui = inject(UiService);
  tabs: [SettingsTab, string][] = [['channels', 'Channels'], ['rules', 'Routing rules'], ['escalation', 'Escalation'], ['templates', 'Templates'], ['log', 'Delivery log']];
  tab = signal<SettingsTab>('channels');
  channels = signal<Channel[]>([]);
  rules = signal<Rule[]>([]);
  escalations = signal<Escalation[]>([]);
  templates = signal<Template[]>([]);
  log = signal<EventLog[]>([]);
  types = signal<EventType[]>([]);
  products = signal<Product[]>([]);
  channelTypes: ChannelType[] = ['Email', 'Teams', 'Webhook'];
  severities = SEVERITIES;
  tone = sevTone;
  ch: { id: string | null; name: string; type: ChannelType; recipients?: string; url?: string; signingSecret?: string; hasUrl?: boolean; hasSigningSecret?: boolean; isEnabled: boolean; version?: number } = this.blank();
  rule = { name: '', eventTypes: [] as string[], channelId: undefined as string | undefined, productId: undefined as string | undefined, minSeverity: 'Warning' };
  esc = { name: '', eventTypes: [] as string[], afterMinutes: 30, channelId: undefined as string | undefined };

  constructor() {
    this.refresh();
    this.q.eventTypes().subscribe(t => this.types.set(t));
    this.api.products().subscribe(p => this.products.set(p));
  }

  private blank() { return { id: null, name: '', type: 'Email' as ChannelType, isEnabled: true }; }

  refresh() {
    const err = (e: unknown) => this.ui.error(e);
    this.q.channels().subscribe({ next: c => this.channels.set(c), error: err });
    switch (this.tab()) {
      case 'rules': this.q.rules(true).subscribe({ next: r => this.rules.set(r), error: err }); break;
      case 'escalation': this.q.escalations().subscribe({ next: r => this.escalations.set(r), error: err }); break;
      case 'templates': this.q.templates().subscribe({ next: r => this.templates.set(r), error: err }); break;
      case 'log': this.q.eventLog(200).subscribe({ next: r => this.log.set(r), error: err }); break;
    }
  }

  newChannel() { this.ch = this.blank(); }
  editChannel(c: Channel) { this.ch = { id: c.id, name: c.name, type: c.type, recipients: c.recipients, hasUrl: c.hasUrl, hasSigningSecret: c.hasSigningSecret, isEnabled: c.isEnabled, version: c.version }; }

  private async act(work: Promise<unknown>, ok: string) { try { await work; this.ui.ok(ok); this.refresh(); return true; } catch (e) { this.ui.error(e); return false; } }

  async saveChannel() {
    const c = this.ch;
    // Secrets are write-only: send them only when typed.
    if (await this.act(firstValueFrom(this.q.saveChannel(c.id, { name: c.name, type: c.type, recipients: c.recipients, url: c.url || null, signingSecret: c.signingSecret || null, isEnabled: c.isEnabled, version: c.version })), 'Channel saved'))
      this.newChannel();
  }
  async deleteChannel() {
    if (!await this.ui.confirm({ title: 'Delete channel', message: 'Rules and escalation policies using it are deleted too.', danger: true, confirmText: 'Delete' })) return;
    if (await this.act(firstValueFrom(this.q.deleteChannel(this.ch.id!)), 'Channel deleted')) this.newChannel();
  }
  test(c: Channel) { this.act(firstValueFrom(this.q.testChannel(c.id)), 'Test message queued: see the delivery log'); }
  async saveRule() {
    if (await this.act(firstValueFrom(this.q.saveRule(null, { ...this.rule, emailUser: false })), 'Rule added'))
      this.rule = { name: '', eventTypes: [], channelId: undefined, productId: undefined, minSeverity: 'Warning' };
  }
  deleteRule(r: Rule) { this.act(firstValueFrom(this.q.deleteRule(r.id)), 'Rule deleted'); }
  async saveEscalation() {
    if (await this.act(firstValueFrom(this.q.saveEscalation(null, { ...this.esc, isEnabled: true } as Partial<Escalation>)), 'Policy added'))
      this.esc = { name: '', eventTypes: [], afterMinutes: 30, channelId: undefined };
  }
  deleteEscalation(p: Escalation) { this.act(firstValueFrom(this.q.deleteEscalation(p.id)), 'Policy deleted'); }
  saveTemplate(t: Template) { this.act(firstValueFrom(this.q.saveTemplate(t.eventType, t.subject, t.body)), 'Template saved'); }
  resetTemplate(t: Template) { this.act(firstValueFrom(this.q.resetTemplate(t.eventType)), 'Template reset'); }
}
