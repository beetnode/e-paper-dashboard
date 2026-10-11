import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { UserRenderingStatus } from '../../services/playwright-component.service';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-rendering-settings',
  imports: [CommonModule],
  template: `
    <div class="app-page">
      <div class="app-page-header"><div>
        <h1 class="app-page-title">Rendering</h1>
        <p class="app-page-description">Enable Home Assistant dashboard rendering {{ isAddonMode() ? 'for this add-on' : 'for your account' }}. Custom layouts work without it.</p>
      </div></div>
      <div class="app-form-shell app-form-shell-wide"><section class="app-section-card">
        @if (status(); as value) {
          <h2 class="app-section-title">{{ isAddonMode() ? 'Add-on rendering' : 'Your rendering capability' }}</h2>
          <p>{{ value.enabled ? 'Enabled' : 'Disabled' }} {{ isAddonMode() ? 'for this add-on.' : 'for your account.' }}</p>
          <p class="text-muted">{{ isAddonMode()
            ? 'Rendering is shared by everyone using this add-on. Disabling it affects the whole add-on.'
            : 'Each render uses a fresh browser session. Disabling rendering affects only your account.' }}</p>
          @if (value.enabled) {
            <p role="status">{{ value.component.ready ? 'Ready to render' : value.component.state === 'Installing' ? 'Preparing rendering…' : 'Rendering is not ready on this server.' }}</p>
          }
          @if (!value.component.supported) {
            <p class="text-muted">Browser rendering requires a Linux x64 or arm64 server.</p>
          }
          <div class="d-flex flex-wrap gap-2">
            <button class="btn" [class.btn-primary]="!value.enabled" [class.btn-outline-secondary]="value.enabled"
              [disabled]="busy() || (!value.enabled && !value.component.supported)" (click)="setEnabled(!value.enabled)">
              {{ value.enabled ? 'Disable' : 'Enable' }} {{ isAddonMode() ? 'for this add-on' : 'for my account' }}
            </button>
            @if (value.enabled && !value.component.ready && value.component.state !== 'Installing' && value.component.supported) {
              <button class="btn btn-outline-primary" [disabled]="busy()" (click)="setEnabled(true)">Prepare rendering</button>
            }
            @if (value.enabled && value.component.ready) {
              <button class="btn btn-outline-primary" [disabled]="busy()" (click)="test()">Test rendering</button>
            }
          </div>
        } @else { <p role="status">Loading rendering settings…</p> }
        @if (error()) { <div class="alert alert-danger mt-3" role="alert">{{ error() }}</div> }
        @if (message()) { <div class="alert alert-success mt-3" role="status">{{ message() }}</div> }
      </section></div>
    </div>
  `
})
export class RenderingSettingsComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  readonly isAddonMode = inject(AuthService).isAddonMode;
  private timer?: ReturnType<typeof setTimeout>;
  readonly status = signal<UserRenderingStatus | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly message = signal('');

  ngOnInit(): void { this.refresh(); }
  ngOnDestroy(): void { if (this.timer) clearTimeout(this.timer); }

  setEnabled(enabled: boolean): void {
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    this.http.put<UserRenderingStatus>('/api/rendering', { enabled }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: status => { this.busy.set(false); this.accept(status); },
      error: err => { this.busy.set(false); this.error.set(err.error?.message || 'Could not update rendering settings.'); }
    });
  }

  test(): void {
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    this.http.post<{ message: string }>('/api/rendering/test', {}).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => { this.busy.set(false); this.message.set(result.message); },
      error: err => { this.busy.set(false); this.error.set(err.error?.message || 'Rendering test failed.'); }
    });
  }

  private refresh(): void {
    this.http.get<UserRenderingStatus>('/api/rendering').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: status => this.accept(status),
      error: () => this.error.set('Could not load rendering settings. Reload to try again.')
    });
  }

  private accept(status: UserRenderingStatus): void {
    this.status.set(status);
    if (this.timer) clearTimeout(this.timer);
    if (status.enabled && !status.component.ready) this.timer = setTimeout(() => this.refresh(), 2000);
  }
}
