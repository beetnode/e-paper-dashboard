import { Component, inject, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-register',
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
    <div class="app-page py-4 py-sm-5">
      <div class="app-form-shell">
        <div class="text-center mb-4">
          <h1 class="app-page-title">{{ isSetup ? 'Create the service administrator' : 'Create your account' }}</h1>
          <p class="app-page-description mx-auto">{{ isSetup ? 'This account manages the service and its users. You can create a separate account for your dashboards.' : 'Create your own e-paper dashboards.' }}</p>
        </div>
        <form (ngSubmit)="onSubmit()" class="app-form-card" novalidate>
        @if (isSetup) {
        <div class="mb-3">
          <label class="form-label fw-semibold" for="setupCode">Setup code</label>
          <input id="setupCode" type="text" class="form-control font-monospace text-uppercase" [(ngModel)]="setupCode" name="setupCode"
            autocomplete="one-time-code" spellcheck="false" placeholder="XXXX-XXXX" aria-describedby="setupCodeHelp" required autofocus>
          <small id="setupCodeHelp" class="form-text text-muted">
            Printed in the service log at startup, for example with <code>docker logs &lt;container&gt;</code>.
          </small>
        </div>
        }
        <div class="mb-3">
          <label class="form-label fw-semibold" for="registerUsername">Username</label>
          <input id="registerUsername" type="text" class="form-control" [(ngModel)]="username" name="username"
            autocomplete="username" minlength="3" aria-describedby="registerUsernameHelp" required [autofocus]="!isSetup">
          <small id="registerUsernameHelp" class="form-text text-muted">At least 3 characters</small>
        </div>
        <div class="mb-3">
          <label class="form-label fw-semibold" for="registerPassword">Password</label>
          <div class="input-group">
            <input id="registerPassword" [type]="showPassword ? 'text' : 'password'" class="form-control"
              [(ngModel)]="password" name="password" autocomplete="new-password" minlength="6"
              aria-describedby="registerPasswordHelp" required>
            <button type="button" class="btn btn-outline-secondary app-icon-button"
              (click)="showPassword = !showPassword"
              [attr.aria-label]="showPassword ? 'Hide passwords' : 'Show passwords'"
              [attr.aria-pressed]="showPassword">
              <i class="fa-solid" [ngClass]="showPassword ? 'fa-eye-slash' : 'fa-eye'" aria-hidden="true"></i>
            </button>
          </div>
          <small id="registerPasswordHelp" class="form-text text-muted">At least 6 characters</small>
        </div>
        <div class="mb-3">
          <label class="form-label fw-semibold" for="registerPasswordConfirm">Confirm password</label>
          <input id="registerPasswordConfirm" [type]="showPassword ? 'text' : 'password'" class="form-control"
            [(ngModel)]="confirmPassword" name="confirmPassword" autocomplete="new-password" required>
        </div>
        @if (errorMessage()) {
          <div class="alert alert-danger" role="alert">{{ errorMessage() }}</div>
        }
        <button type="submit" class="btn btn-primary w-100" [disabled]="isLoading() || !registrationAllowed()">
          @if (isLoading()) {
            <span class="spinner-border spinner-border-sm me-2" aria-hidden="true"></span>
          }
          {{ isLoading() ? 'Creating account…' : isSetup ? 'Complete setup' : 'Create account' }}
        </button>
        <div class="mt-3 text-center">
          <span class="text-muted">Already have an account?</span><a class="ms-1" routerLink="/login">Sign in</a>
        </div>
        </form>
      </div>
    </div>
  `
})
export class RegisterComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly isSetup = inject(ActivatedRoute).snapshot.data['setup'] === true;
  readonly registrationAllowed = signal(false);

  setupCode = '';
  username = '';
  password = '';
  confirmPassword = '';
  showPassword = false;
  readonly errorMessage = signal('');
  readonly isLoading = signal(false);

  constructor() {
    this.authService.getSetupStatus().subscribe({
      next: status => {
        this.registrationAllowed.set(this.isSetup ? status.setupRequired : status.registrationEnabled);
        if (status.setupRequired && !this.isSetup) this.router.navigate(['/setup']);
        else if (!status.setupRequired && this.isSetup) this.router.navigate(['/login']);
        else if (!status.registrationEnabled && !status.setupRequired)
          this.errorMessage.set('Local account registration is unavailable in this deployment.');
      },
      error: () => this.errorMessage.set('Could not check account setup. Reload to try again.')
    });
    // Effect to redirect when authenticated
    effect(() => {
      if (this.authService.isAuthReady() && this.authService.isAuthenticated()) {
        this.router.navigate([this.isSetup ? '/users/manage' : '/dashboards']);
      }
    });
  }

  onSubmit(): void {
    if (!this.registrationAllowed()) return;
    const username = this.username.trim();
    if (this.isSetup && !this.setupCode.trim()) {
      this.errorMessage.set('Enter the setup code from the service log.');
      return;
    }

    if (!username || !this.password || !this.confirmPassword) {
      this.errorMessage.set('Please fill in all fields.');
      return;
    }

    if (username.length < 3) {
      this.errorMessage.set('Username must be at least 3 characters.');
      return;
    }

    if (this.password.length < 6) {
      this.errorMessage.set('Password must be at least 6 characters.');
      return;
    }

    if (this.password !== this.confirmPassword) {
      this.errorMessage.set('Passwords do not match.');
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set('');

    const request = { username, password: this.password };
    (this.isSetup
      ? this.authService.setup({ ...request, setupCode: this.setupCode.trim() })
      : this.authService.register(request)).subscribe({
      next: () => {
        this.router.navigate([this.isSetup ? '/users/manage' : '/dashboards']);
      },
      error: (error) => {
        this.errorMessage.set(error.error?.message || 'Registration failed. Please try again.');
        this.isLoading.set(false);
      }
    });
  }
}
