import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, firstValueFrom, throwError } from 'rxjs';
import { Session } from './models';

const STORAGE_KEY = 'wto.session';

/**
 * Holds the signed-in session. The token lives in sessionStorage (cleared when the tab closes); permissions are
 * re-read from /auth/me on start so role changes take effect without re-login.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);
  readonly session = signal<Session | null>(this.restore());
  readonly user = computed(() => this.session()?.user ?? null);
  readonly permissions = computed(() => new Set(this.session()?.permissions ?? []));

  get token() { return this.session()?.token ?? null; }

  can(permission: string) { return this.permissions().has(permission); }

  async login(userName: string, password: string) {
    const s = await firstValueFrom(this.http.post<Session>('/api/v1/auth/login', { userName, password }));
    this.store(s);
  }

  async refresh() {
    if (!this.token) return;
    try { this.store(await firstValueFrom(this.http.get<Session>('/api/v1/auth/me'))); }
    catch { this.logout(); }
  }

  logout() {
    this.session.set(null);
    try { sessionStorage.removeItem(STORAGE_KEY); } catch { /* storage unavailable */ }
    this.router.navigate(['/login']);
  }

  private store(s: Session) {
    this.session.set(s);
    try { sessionStorage.setItem(STORAGE_KEY, JSON.stringify(s)); } catch { /* storage unavailable */ }
  }

  private restore(): Session | null {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      const s = raw ? (JSON.parse(raw) as Session) : null;
      return s && new Date(s.expiresAt) > new Date() ? s : null;
    } catch { return null; }
  }
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.token;
  const authed = token && req.url.startsWith('/api/') ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
  return next(authed).pipe(catchError((err: HttpErrorResponse) => {
    if (err.status === 401 && !req.url.endsWith('/auth/login')) auth.logout();
    return throwError(() => err);
  }));
};

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.session() ? true : inject(Router).createUrlTree(['/login']);
};

export const permissionGuard = (permission: string): CanActivateFn => () =>
  inject(AuthService).can(permission) ? true : inject(Router).createUrlTree(['/']);

/** Extracts a readable message from an RFC 7807 problem response. */
export function problemMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const p = err.error as { title?: string; detail?: string; errors?: Record<string, string[]> } | null;
    if (p?.errors) return Object.values(p.errors).flat().join(' ');
    if (p?.detail) return p.detail;
    if (p?.title) return p.title;
    if (err.status === 0) return 'The server could not be reached.';
    return `${err.status} ${err.statusText}`;
  }
  return err instanceof Error ? err.message : String(err);
}
