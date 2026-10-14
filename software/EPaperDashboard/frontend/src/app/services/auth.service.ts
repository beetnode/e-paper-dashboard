import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { User, LoginRequest, RegisterRequest } from '../models/types';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly currentUserSignal = signal<User | null>(null);
  private readonly authReadySignal = signal<boolean>(false);

  readonly currentUser = this.currentUserSignal.asReadonly();
  readonly authReady = this.authReadySignal.asReadonly();
  readonly isAuthenticated = computed(() => this.currentUserSignal() !== null);
  readonly isAuthReady = computed(() => this.authReadySignal());
  readonly isUserManagementDisabled = computed(() => this.currentUserSignal()?.userManagementEnabled === false);
  readonly hasDefaultHomeAssistantConnection = computed(() => this.currentUserSignal()?.hasDefaultHomeAssistantConnection === true);

  getSetupStatus(): Observable<{ setupRequired: boolean; registrationEnabled: boolean }> {
    return this.http.get<{ setupRequired: boolean; registrationEnabled: boolean }>('/api/auth/setup');
  }

  setup(credentials: RegisterRequest): Observable<User> {
    return this.http.post<User>('/api/auth/setup', credentials).pipe(tap(user => {
      this.currentUserSignal.set(user);
      this.authReadySignal.set(true);
    }));
  }

  login(credentials: LoginRequest): Observable<User> {
    return this.http.post<User>('/api/auth/login', credentials).pipe(
      tap(user => {
        this.currentUserSignal.set(user);
        this.authReadySignal.set(true);
      })
    );
  }

  register(credentials: RegisterRequest): Observable<User> {
    return this.http.post<User>('/api/auth/register', credentials).pipe(
      tap(user => {
        this.currentUserSignal.set(user);
        this.authReadySignal.set(true);
      })
    );
  }

  logout(): Observable<any> {
    return this.http.post('/api/auth/logout', {}).pipe(
      tap(() => {
        this.currentUserSignal.set(null);
        this.authReadySignal.set(false);
      })
    );
  }

  getCurrentUser(): Observable<User> {
    return this.http.get<User>('/api/auth/current').pipe(
      tap(user => {
        this.currentUserSignal.set(user);
        this.authReadySignal.set(true);
      })
    );
  }

  checkAuth(): void {
    this.getCurrentUser().subscribe({
      next: () => { },
      error: () => {
        this.currentUserSignal.set(null);
        this.authReadySignal.set(true);
      }
    });
  }
}
