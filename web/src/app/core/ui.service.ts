import { Injectable, inject, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import * as signalR from '@microsoft/signalr';
import { Subject, firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { AuthService, problemMessage } from './auth.service';
import { Lookups } from './models';
import { ConfirmDialog, ConfirmData, PromptDialog, PromptData } from '../shared/dialogs';

/** Snackbars, confirmation/prompt dialogs and cached lookups shared by every screen. */
@Injectable({ providedIn: 'root' })
export class UiService {
  private snack = inject(MatSnackBar);
  private dialog = inject(MatDialog);
  private api = inject(ApiService);
  readonly lookups = signal<Lookups | null>(null);

  ok(message: string) { this.snack.open(message, undefined, { duration: 2500 }); }
  error(err: unknown) { this.snack.open(problemMessage(err), 'Dismiss', { duration: 8000, panelClass: 'snack-error' }); }

  confirm(data: ConfirmData) {
    return firstValueFrom(this.dialog.open(ConfirmDialog, { data, width: '440px' }).afterClosed()).then(r => r === true);
  }

  prompt(data: PromptData) {
    return firstValueFrom(this.dialog.open(PromptDialog, { data, width: '440px' }).afterClosed()) as Promise<string | undefined>;
  }

  async loadLookups(force = false) {
    if (this.lookups() && !force) return this.lookups()!;
    const l = await firstValueFrom(this.api.lookups());
    this.lookups.set(l);
    return l;
  }

  label(list: 'featureTypes' | 'featureStatuses' | 'testTypes', code: string | undefined) {
    return this.lookups()?.[list].find(i => i.code === code)?.name ?? code ?? '';
  }
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export interface ChangeEvent { topic: string; payload: Record<string, any>; }

/** SignalR connection to /hubs/changes; screens subscribe to refresh when others change data. */
@Injectable({ providedIn: 'root' })
export class ChangesService {
  private auth = inject(AuthService);
  private connection?: signalR.HubConnection;
  readonly events = new Subject<ChangeEvent>();
  readonly connected = signal(false);

  start() {
    if (this.connection) return;
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/changes', { accessTokenFactory: () => this.auth.token ?? '' })
      // Keep retrying (up to every 30 s) so a server restart or network blip never leaves the UI without live updates.
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: ctx => Math.min(30_000, 1_000 * 2 ** Math.min(ctx.previousRetryCount, 5)) })
      .configureLogging(signalR.LogLevel.Warning)
      .build();
    for (const topic of ['featuresChanged', 'productsChanged', 'packageVersionChanged', 'runChanged', 'jobLog', 'agentsChanged', 'environmentsChanged', 'schedulesChanged', 'defectsChanged', 'releasesChanged', 'buildsChanged', 'notificationsChanged'])
      this.connection.on(topic, (payload: ChangeEvent['payload']) => this.events.next({ topic, payload }));
    this.connection.onreconnected(() => this.connected.set(true));
    this.connection.onreconnecting(() => this.connected.set(false));
    this.connection.onclose(() => { this.connected.set(false); setTimeout(() => this.connect(), 5_000); });
    this.connect();
  }

  private connect() {
    const c = this.connection;
    if (!c || c.state !== signalR.HubConnectionState.Disconnected) return;
    c.start().then(() => this.connected.set(true)).catch(() => { this.connected.set(false); setTimeout(() => this.connect(), 5_000); });
  }

  stop() {
    this.connection?.stop();
    this.connection = undefined;
  }
}
