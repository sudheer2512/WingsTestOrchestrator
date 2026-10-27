import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AiApi, AiPolicy, AiStatus } from '../core/ai';
import { UiService } from '../core/ui.service';

/** Administrator policy for the AI assistant: whether it runs, what may be sent, what is masked and how much each person may ask. */
@Component({
  selector: 'app-ai-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule],
  template: `
    <div class="page">
      <div class="page-head"><a routerLink="/ai" class="muted">AI assistant</a> › <h1>AI policy</h1></div>
      @if (status(); as s) {
        <div class="panel">
          <h3>Provider</h3>
          <p>{{ s.provider }} · model <span class="mono">{{ s.model || '–' }}</span>.
            @if (s.unavailable && s.enabled) { <span class="error">{{ s.unavailable }}</span> } @else if (s.ready) { <span class="ok">Ready.</span> }</p>
          <p class="muted small">The provider, model, effort and key are server configuration (<span class="mono">Ai:Provider</span>, <span class="mono">Ai:Model</span>,
            <span class="mono">Ai:Effort</span>, and the <span class="mono">ANTHROPIC_API_KEY</span> environment variable or a secret store). Keys are never stored in the database or shown here.</p>
        </div>
      }
      @if (p(); as p) {
        <div class="panel">
          <h3>Use</h3>
          <mat-checkbox [(ngModel)]="p.isEnabled">Enable the AI testing assistant</mat-checkbox>
          <mat-form-field class="narrow"><mat-label>Requests per user per day</mat-label><input matInput type="number" min="0" [(ngModel)]="p.dailyRequestsPerUser" /></mat-form-field>
          <h3>What may be sent</h3>
          <mat-checkbox [(ngModel)]="p.allowSourceCode">Test source code (style references for code generation, test repair, linked automation for suggestions)</mat-checkbox>
          <mat-checkbox [(ngModel)]="p.allowScreenshots">Failure screenshots (images cannot be masked; only enable when screens hold no confidential data)</mat-checkbox>
          <p class="muted small">Always sent when relevant: feature specifications, requirements, test cases, error messages, stack traces, text evidence, execution history and build changes, after masking.
            Videos and trace archives are never sent.</p>
          <h3>Masking</h3>
          <p class="muted small">Always masked: secret values of every environment, passwords and secrets in connection strings and settings, bearer tokens, JWTs, private keys and API keys.</p>
          <mat-checkbox [(ngModel)]="p.maskEmails">Email addresses</mat-checkbox>
          <mat-checkbox [(ngModel)]="p.maskPhoneNumbers">Phone numbers</mat-checkbox>
          <mat-form-field class="full"><mat-label>Additional patterns to mask (regular expressions, one per line)</mat-label>
            <textarea matInput rows="3" [(ngModel)]="p.additionalRedactionPatterns" placeholder="CUST-\\d{6}"></textarea></mat-form-field>
          <h3>Code generation</h3>
          <mat-form-field class="full"><mat-label>House conventions (shared libraries, page objects, naming)</mat-label>
            <textarea matInput rows="5" [(ngModel)]="p.projectConventions" placeholder="Use Wings.Automation.Common page objects; one class per form; ..."></textarea></mat-form-field>
          <button mat-flat-button (click)="save(p)"><mat-icon>save</mat-icon>Save policy</button>
        </div>
      }
    </div>`,
  styles: `.panel { display: flex; flex-direction: column; gap: 4px; } .narrow { width: 240px; } .small { font-size: 12px; } .ok { color: var(--good); } h3 { margin-top: 12px; }`,
})
export class AiSettings {
  private ai = inject(AiApi);
  private ui = inject(UiService);
  p = signal<AiPolicy | null>(null);
  status = signal<AiStatus | null>(null);

  constructor() {
    this.ai.policy().subscribe({ next: p => this.p.set({ ...p }), error: e => this.ui.error(e) });
    this.ai.status().subscribe(s => this.status.set(s));
  }

  async save(p: AiPolicy) {
    try {
      this.p.set({ ...await firstValueFrom(this.ai.savePolicy(p)) });
      this.ai.status().subscribe(s => this.status.set(s));
      this.ui.ok('AI policy saved');
    } catch (e) { this.ui.error(e); }
  }
}
