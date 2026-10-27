import { ChangeDetectionStrategy, Component, Injectable, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { problemMessage } from '../core/auth.service';
import { StartRunRequest, TestEnvironment } from '../core/models';
import { UiService } from '../core/ui.service';

export interface StartRunData { title: string; scope: StartRunRequest; description: string; }

const LAST_VARS = 'wto.runVariables';

/** Confirms an execution scope and collects run options (variables, priority, retries, timeout). */
@Component({
  selector: 'app-start-run-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, FormsModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      <p>{{ data.description }}</p>
      <p class="muted small">Published automated tests in scope are queued and dispatched to compatible agents. Selected test cases without automation are recorded as not run.</p>
      <mat-form-field class="full"><mat-label>Run name (optional)</mat-label><input matInput [(ngModel)]="name" /></mat-form-field>
      <div class="grid-2">
        <mat-form-field><mat-label>Environment</mat-label><mat-select [(ngModel)]="environmentId">
          <mat-option [value]="undefined">None (agent defaults)</mat-option>
          @for (e of envs(); track e.id) { <mat-option [value]="e.id" [disabled]="e.inMaintenance">{{ e.name }}{{ e.isProduction ? ' (PRODUCTION)' : '' }}{{ e.inMaintenance ? ' — maintenance' : e.health === 'Unavailable' ? ' — unavailable' : '' }}</mat-option> }
        </mat-select></mat-form-field>
        <mat-form-field><mat-label>Build version</mat-label><input matInput [(ngModel)]="buildVersion" placeholder="from the environment if empty" /></mat-form-field>
      </div>
      @if (selectedEnv()?.isProduction) { <p class="warn">Production: destructive test cases are skipped unless explicitly authorised for this environment.</p> }
      <div class="row">
        <mat-checkbox [(ngModel)]="includeManual">Include manual test cases (testers record results in the run)</mat-checkbox>
        <mat-checkbox [(ngModel)]="ignoreHealth">Ignore environment health</mat-checkbox>
      </div>
      <mat-form-field class="full">
        <mat-label>Variables (one NAME=value per line)</mat-label>
        <textarea matInput rows="3" class="mono" [(ngModel)]="variables" placeholder="WINGS_BASE_URL=https://qa.wings.local/portal/"></textarea>
        <mat-hint>Passed to tests as environment variables. Do not put secrets here — configure them on the agent.</mat-hint>
      </mat-form-field>
      <div class="grid-3">
        <mat-form-field><mat-label>Priority (0–100)</mat-label><input matInput type="number" min="0" max="100" [(ngModel)]="priority" /></mat-form-field>
        <mat-form-field><mat-label>Retries after agent failure</mat-label><input matInput type="number" min="0" max="5" [(ngModel)]="retries" /></mat-form-field>
        <mat-form-field><mat-label>Job timeout (minutes)</mat-label><input matInput type="number" min="1" [(ngModel)]="timeout" /></mat-form-field>
      </div>
      @if (error()) { <p class="error">{{ error() }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [disabled]="busy()" (click)="start()">Execute</button>
    </mat-dialog-actions>`,
  styles: `.small { font-size: 12px; } .warn { color: var(--bad); font-size: 12px; } .row { flex-wrap: wrap; }`,
})
export class StartRunDialog {
  data = inject<StartRunData>(MAT_DIALOG_DATA);
  private ref = inject(MatDialogRef<StartRunDialog>);
  private api = inject(ApiService);
  name = '';
  variables = (() => { try { return localStorage.getItem(LAST_VARS) ?? ''; } catch { return ''; } })();
  priority = 50;
  retries = 1;
  timeout = 30;
  environmentId?: string = this.data.scope.environmentId;
  buildVersion = '';
  includeManual = false;
  ignoreHealth = false;
  envs = signal<TestEnvironment[]>([]);
  selectedEnv = () => this.envs().find(e => e.id === this.environmentId);
  constructor() { this.api.environments().subscribe(e => this.envs.set(e)); }
  busy = signal(false);
  error = signal('');

  async start() {
    const vars: Record<string, string> = {};
    for (const line of this.variables.split('\n').map(l => l.trim()).filter(Boolean)) {
      const i = line.indexOf('=');
      if (i < 1) { this.error.set(`"${line}" is not NAME=value.`); return; }
      vars[line.slice(0, i).trim()] = line.slice(i + 1).trim();
    }
    this.busy.set(true);
    try {
      try { localStorage.setItem(LAST_VARS, this.variables); } catch { /* storage unavailable */ }
      const run = await firstValueFrom(this.api.startRun({
        ...this.data.scope, name: this.name || undefined, variables: vars, environmentId: this.environmentId, buildVersion: this.buildVersion || undefined,
        includeManual: this.includeManual, ignoreEnvironmentHealth: this.ignoreHealth, priority: this.priority, maxInfrastructureRetries: this.retries, timeoutMinutes: this.timeout,
      }));
      this.ref.close(run.id);
    } catch (e) { this.error.set(problemMessage(e)); }
    finally { this.busy.set(false); }
  }
}

/** Opens the dialog and navigates to the live monitor of the new run. */
@Injectable({ providedIn: 'root' })
export class RunLauncher {
  private dialog = inject(MatDialog);
  private router = inject(Router);
  private ui = inject(UiService);

  async launch(data: StartRunData) {
    const runId = await firstValueFrom(this.dialog.open(StartRunDialog, { data, width: '620px' }).afterClosed()) as string | undefined;
    if (runId) { this.ui.ok('Execution queued'); this.router.navigate(['/runs', runId]); }
  }
}
