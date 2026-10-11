import { Component, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { DashboardService } from '../../services/dashboard.service';
import { AuthService } from '../../services/auth.service';
import { DialogService } from '../../services/dialog.service';
import { ToastService } from '../../services/toast.service';
import { Dashboard } from '../../models/types';

@Component({
  selector: 'app-dashboard-list',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <div class="app-page">
    <div class="app-page-header">
      <div>
        <h1 class="app-page-title">Dashboards</h1>
        <p class="app-page-description">Create layouts, connect data, and choose what appears on each display.</p>
      </div>
      <a routerLink="/dashboards/create" class="btn btn-primary">
        <i class="fa-solid fa-plus" aria-hidden="true"></i><span class="d-none d-sm-inline"> New dashboard</span><span class="visually-hidden d-sm-none">New dashboard</span>
      </a>
    </div>

    @if (isLoading()) {
      <div class="app-loading-state">
        <div class="spinner-border" role="status">
          <span class="visually-hidden">Loading dashboards</span>
        </div>
      </div>
    } @else if (errorMessage()) {
      <div class="app-empty-state" role="alert">
        <span class="app-empty-state-icon text-danger"><i class="fa-solid fa-triangle-exclamation" aria-hidden="true"></i></span>
        <div>
          <h2 class="h5 mb-1">Dashboards could not be loaded</h2>
          <p class="text-muted mb-0">{{ errorMessage() }}</p>
        </div>
        <button type="button" class="btn btn-outline-primary" (click)="loadDashboards()">
          <i class="fa-solid fa-rotate-right me-1" aria-hidden="true"></i>Try again
        </button>
      </div>
    } @else if (dashboards().length > 0) {
      <div class="app-list">
        @for (dashboard of dashboards(); track dashboard.id) {
          <article class="app-list-item">
            <a class="app-list-item-link" [routerLink]="['/dashboards', dashboard.id, 'edit']" [attr.aria-label]="dashboard.name + ' settings'">
              <div class="app-list-item-heading">
                <h2 class="app-list-item-title">{{ dashboard.name }}</h2>
                @if (dashboard.renderingMode === 'HomeAssistant') {
                  <span class="app-status-chip app-status-chip-muted"><i class="fa-solid fa-house-signal" aria-hidden="true"></i>Home Assistant</span>
                }
              </div>
              @if (dashboard.description) {
                <p class="app-list-item-description">{{ dashboard.description }}</p>
              }
              <div class="app-list-item-meta">
                <span><i class="fa-solid" [ngClass]="dashboard.orientation === 'Portrait' ? 'fa-mobile-screen' : 'fa-display'" aria-hidden="true"></i>{{ dashboard.screenWidth }}×{{ dashboard.screenHeight }}, {{ (dashboard.orientation || 'Landscape') | lowercase }}</span>
                <span><i class="fa-regular fa-clock" aria-hidden="true"></i>{{ scheduleLabel(dashboard) }}</span>
              </div>
            </a>
            <div class="app-list-item-actions">
              @if (dashboard.renderingMode !== 'HomeAssistant') {
                <a class="btn btn-ghost app-icon-button" [routerLink]="['/dashboards', dashboard.id, 'designer']"
                  [attr.aria-label]="'Open ' + dashboard.name + ' in the designer'" title="Open designer">
                  <i class="fa-solid fa-paintbrush" aria-hidden="true"></i>
                </a>
              }
              <button type="button" class="btn btn-ghost btn-ghost-danger app-icon-button" (click)="deleteDashboard(dashboard.id)"
                [attr.aria-label]="'Delete ' + dashboard.name" title="Delete">
                <i class="fa-solid fa-trash" aria-hidden="true"></i>
              </button>
            </div>
          </article>
        }
      </div>
    } @else {
      <div class="app-empty-state">
        <span class="app-empty-state-icon"><i class="fa-solid fa-table-cells-large" aria-hidden="true"></i></span>
        <div>
          <h2 class="h5 mb-1">Create your first dashboard</h2>
          <p class="text-muted mb-0">Build a custom layout from widgets, or show an existing Home Assistant dashboard.</p>
        </div>
        <a routerLink="/dashboards/create" class="btn btn-primary"><i class="fa-solid fa-plus me-1" aria-hidden="true"></i> Create dashboard</a>
      </div>
    }
    </div>
  `,
})
export class DashboardListComponent {
  private readonly dashboardService = inject(DashboardService);
  private readonly authService = inject(AuthService);
  private readonly dialogService = inject(DialogService);
  private readonly toastService = inject(ToastService);

  // Signal-based state
  readonly dashboards = signal<Dashboard[]>([]);
  readonly isLoading = signal(true);
  readonly errorMessage = signal('');
  private hasLoaded = false;

  constructor() {
    effect(() => {
      if (!this.hasLoaded && this.authService.isAuthReady()) {
        this.hasLoaded = true;
        this.loadDashboards();
      }
    });
  }

  loadDashboards(): void {
    this.isLoading.set(true);
    this.errorMessage.set('');
    this.dashboardService.getDashboards().subscribe({
      next: (dashboards) => {
        this.dashboards.set(dashboards);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.errorMessage.set('Failed to load dashboards. Please try again.');
        this.isLoading.set(false);
      }
    });
  }

  scheduleLabel(dashboard: Dashboard): string {
    const times = dashboard.updateTimes ?? [];
    if (times.length === 0) return 'No update schedule';
    return times.length <= 3 ? `Updates at ${times.join(', ')}` : `${times.length} updates a day`;
  }

  async deleteDashboard(id: string): Promise<void> {
    const dashboard = this.dashboards().find(d => d.id === id);
    if (!dashboard) return;

    await this.dialogService.confirm({
      title: 'Delete dashboard?',
      message: `Delete "${dashboard.name}"? This cannot be undone.`,
      confirmLabel: 'Delete',
      isDangerous: true,
      onConfirm: async () => {
        try {
          await firstValueFrom(this.dashboardService.deleteDashboard(id));
          this.dashboards.update(list => list.filter(d => d.id !== id));
          this.toastService.success('Dashboard deleted.');
        } catch (error: any) {
          this.toastService.httpError(error, 'Could not delete the dashboard.');
          this.loadDashboards();
        }
      }
    });
  }
}
