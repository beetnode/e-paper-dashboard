import { Component, inject, OnInit, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule, ActivatedRoute } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-login',
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
    <div class="app-page py-4 py-sm-5">
      <div class="app-form-shell">
        <div class="text-center mb-4">
          <h1 class="app-page-title">Welcome back</h1>
          <p class="app-page-description mx-auto">Sign in to manage your dashboards and devices.</p>
        </div>
        <form (ngSubmit)="onSubmit()" class="app-form-card" novalidate>
        <div class="mb-3">
          <label class="form-label fw-semibold" for="loginUsername">Username</label>
          <input id="loginUsername" type="text" class="form-control" [(ngModel)]="username" name="username"
            autocomplete="username" required autofocus>
        </div>
        <div class="mb-3">
          <label class="form-label fw-semibold" for="loginPassword">Password</label>
          <div class="input-group">
            <input id="loginPassword" [type]="showPassword ? 'text' : 'password'" class="form-control"
              [(ngModel)]="password" name="password" autocomplete="current-password" required>
            <button type="button" class="btn btn-outline-secondary app-icon-button"
              (click)="showPassword = !showPassword"
              [attr.aria-label]="showPassword ? 'Hide password' : 'Show password'"
              [attr.aria-pressed]="showPassword">
              <i class="fa-solid" [ngClass]="showPassword ? 'fa-eye-slash' : 'fa-eye'" aria-hidden="true"></i>
            </button>
          </div>
        </div>
        @if (errorMessage()) {
          <div class="alert alert-danger" role="alert">{{ errorMessage() }}</div>
        }
        <button type="submit" class="btn btn-primary w-100" [disabled]="isLoading()">
          @if (isLoading()) {
            <span class="spinner-border spinner-border-sm me-2" aria-hidden="true"></span>
          }
          {{ isLoading() ? 'Signing in…' : 'Sign in' }}
        </button>
        <div class="mt-3 text-center">
          <span class="text-muted">New to izBoard?</span><a class="ms-1" routerLink="/register">Create an account</a>
        </div>
        </form>
      </div>
    </div>
  `
})
export class LoginComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  username = '';
  password = '';
  showPassword = false;
  readonly errorMessage = signal('');
  readonly isLoading = signal(false);
  private returnUrl = '/dashboards';
  private hasRedirected = false;

  constructor() {
    this.returnUrl = this.route.snapshot.queryParams['returnUrl'] || '/dashboards';

    effect(() => {
      if (!this.hasRedirected &&
        this.router.url.startsWith('/login') &&
        this.authService.isAuthReady() &&
        this.authService.isAuthenticated()) {
        this.hasRedirected = true;
        this.router.navigate([this.returnUrl === '/dashboards' && this.authService.currentUser()?.isSuperUser ? '/users/manage' : this.returnUrl]);
      }
    });
  }

  ngOnInit(): void {
    // Return URL is already set in constructor
  }

  onSubmit(): void {
    const username = this.username.trim();
    if (!username || !this.password) {
      this.errorMessage.set('Please fill in all fields.');
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set('');

    this.authService.login({ username, password: this.password }).subscribe({
      next: () => {
        this.router.navigate([this.returnUrl === '/dashboards' && this.authService.currentUser()?.isSuperUser ? '/users/manage' : this.returnUrl]);
      },
      error: (error) => {
        this.errorMessage.set(error.error?.message || 'Invalid username or password.');
        this.isLoading.set(false);
      }
    });
  }
}
