import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { UserRenderingStatus } from '../../services/playwright-component.service';
import { AuthService } from '../../services/auth.service';
import { ToastService } from '../../services/toast.service';

@Component({
  selector: 'app-rendering-settings',
  imports: [CommonModule],
  template: `
    <div class="app-page">
      <div class="app-page-header"><div>
        <h2 class="app-page-title">Rendering</h2>
        <p class="app-page-description">Capture Home Assistant dashboards for your displays. Custom layouts work without it.</p>
      </div></div>
      @if (status(); as value) {
        <section class="app-section-card" aria-labelledby="renderingTitle">
          <div class="app-section-header">
            <div>
              <h2 id="renderingTitle" class="app-section-title">{{ isAddonMode() ? 'Add-on rendering' : 'Rendering for your account' }}</h2>
              <p class="app-section-description">{{ isAddonMode()
                ? 'Rendering is shared by everyone using this add-on. Disabling it affects the whole add-on.'
                : 'Each render uses a fresh browser session. Disabling rendering affects only your account.' }}</p>
            </div>
            <span class="app-status-chip" [ngClass]="'app-status-chip-' + state(value).tone" role="status">{{ state(value).label }}</span>
          </div>
          @if (!value.component.supported) {
            <p class="small text-muted">Browser rendering requires a Linux x64 or arm64 server.</p>
          }
          <div class="d-flex flex-wrap gap-2">
            <button type="button" class="btn" [class.btn-primary]="!value.enabled" [class.btn-outline-secondary]="value.enabled"
              [disabled]="busy() || (!value.enabled && !value.component.supported)" (click)="setEnabled(!value.enabled)">
              {{ value.enabled ? 'Disable' : 'Enable' }} {{ isAddonMode() ? 'for this add-on' : 'for my account' }}
            </button>
            @if (value.enabled && !value.component.ready && value.component.state !== 'Installing' && value.component.supported) {
              <button type="button" class="btn btn-outline-primary" [disabled]="busy()" (click)="setEnabled(true)">Prepare rendering</button>
            }
            @if (value.enabled && value.component.ready) {
              <button type="button" class="btn btn-outline-primary" [disabled]="busy()" (click)="test()">
                <i class="fa-solid fa-vial me-1" aria-hidden="true"></i>Test rendering
              </button>
            }
          </div>
        </section>
      } @else if (loadFailed()) {
        <div class="app-empty-state" role="alert">
          <span class="app-empty-state-icon text-danger"><i class="fa-solid fa-triangle-exclamation" aria-hidden="true"></i></span>
          <div><h2 class="h5 mb-1">Rendering settings could not be loaded</h2><p class="text-muted mb-0">Check the server connection and try again.</p></div>
          <button type="button" class="btn btn-outline-primary" (click)="refresh()"><i class="fa-solid fa-rotate-right me-1" aria-hidden="true"></i>Try again</button>
        </div>
      } @else {
        <div class="app-loading-state">
          <div class="spinner-border" role="status"><span class="visually-hidden">Loading rendering settings</span></div>
        </div>
      }
    </div>
  `
})
export class RenderingSettingsComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly toastService = inject(ToastService);
  readonly isAddonMode = inject(AuthService).isAddonMode;
  private timer?: ReturnType<typeof setTimeout>;
  readonly status = signal<UserRenderingStatus | null>(null);
  readonly busy = signal(false);
  readonly loadFailed = signal(false);

  ngOnInit(): void { this.refresh(); }
  ngOnDestroy(): void { if (this.timer) clearTimeout(this.timer); }

  state(value: UserRenderingStatus): { label: string; tone: 'success' | 'info' | 'warning' | 'muted' } {
    if (!value.enabled) return { label: 'Disabled', tone: 'muted' };
    if (value.component.ready) return { label: 'Ready', tone: 'success' };
    if (value.component.state === 'Installing') return { label: 'Preparing…', tone: 'info' };
    return { label: 'Not ready', tone: 'warning' };
  }

  setEnabled(enabled: boolean): void {
    this.busy.set(true);
    this.http.put<UserRenderingStatus>('/api/rendering', { enabled }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: status => { this.busy.set(false); this.accept(status); },
      error: err => { this.busy.set(false); this.toastService.httpError(err, 'Could not update rendering.'); }
    });
  }

  test(): void {
    this.busy.set(true);
    this.http.post<{ message: string }>('/api/rendering/test', {}).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => { this.busy.set(false); this.toastService.success(result.message); },
      error: err => { this.busy.set(false); this.toastService.httpError(err, 'Could not render the test page.'); }
    });
  }

  refresh(): void {
    this.loadFailed.set(false);
    this.http.get<UserRenderingStatus>('/api/rendering').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: status => this.accept(status),
      error: () => this.loadFailed.set(true)
    });
  }

  private accept(status: UserRenderingStatus): void {
    this.status.set(status);
    if (this.timer) clearTimeout(this.timer);
    if (status.enabled && !status.component.ready) this.timer = setTimeout(() => this.refresh(), 2000);
  }
}
