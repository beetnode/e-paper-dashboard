import { Component, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
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
      <div class="dashboard-list">
        @for (dashboard of dashboards(); track dashboard.id) {
          <article class="dashboard-item">
            <a class="dashboard-main" [routerLink]="['/dashboards', dashboard.id, 'edit']">
              <div class="dashboard-heading">
                <h2 class="dashboard-title">{{ dashboard.name }}</h2>
                <span class="app-status-chip app-status-chip-muted">
                  {{ dashboard.renderingMode === 'HomeAssistant' ? 'Home Assistant' : 'Custom layout' }}
                </span>
              </div>
              @if (dashboard.description) {
                <p class="dashboard-description">{{ dashboard.description }}</p>
              }
              <div class="dashboard-meta">
                <span><i class="fa-solid fa-expand" aria-hidden="true"></i> {{ dashboard.screenWidth }}×{{ dashboard.screenHeight }}</span>
                <span><i class="fa-solid" [ngClass]="dashboard.orientation === 'Portrait' ? 'fa-mobile-screen' : 'fa-display'" aria-hidden="true"></i> {{ dashboard.orientation || 'Landscape' }}</span>
                <span><i class="fa-regular fa-clock" aria-hidden="true"></i> {{ dashboard.updateTimes?.length || 0 }} scheduled {{ dashboard.updateTimes?.length === 1 ? 'update' : 'updates' }}</span>
              </div>
            </a>
            <div class="dashboard-actions">
              <button type="button" class="btn btn-outline-primary app-icon-button" (click)="editDashboard(dashboard.id)" [attr.aria-label]="'Edit ' + dashboard.name">
                <i class="fa-solid fa-pen-to-square" aria-hidden="true"></i>
              </button>
              <button type="button" class="btn btn-outline-danger app-icon-button" (click)="deleteDashboard(dashboard.id)" [attr.aria-label]="'Delete ' + dashboard.name">
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
  styles: [`
    .dashboard-list {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      margin-bottom: 2rem;
    }

    .dashboard-item {
      display: grid;
      grid-template-columns: 1fr auto;
      align-items: center;
      gap: 1rem;
      overflow: hidden;
      background: var(--bs-body-bg);
      border: 1px solid var(--bs-border-color);
      border-radius: 0.375rem;
      transition: all 0.15s ease;
    }

    .dashboard-main {
      min-width: 0;
      padding: 1rem 1.125rem;
      color: inherit;
      text-decoration: none;
    }

    .dashboard-main:focus-visible {
      border-radius: 0.375rem;
      outline-offset: -4px;
    }

    .dashboard-heading {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: 0.625rem;
    }

    .dashboard-item:hover {
      background: var(--bs-secondary-bg);
      border-color: var(--bs-primary);
      box-shadow: 0 2px 6px rgba(0, 0, 0, 0.08);
    }

    .dashboard-title {
      margin: 0;
      font-size: 1.1rem;
      font-weight: 600;
      color: var(--bs-body-color);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .dashboard-description {
      margin: 0.35rem 0 0;
      color: var(--bs-secondary-color);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .dashboard-meta {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem 1rem;
      margin-top: 0.65rem;
      color: var(--bs-secondary-color);
      font-size: 0.8rem;
    }

    .dashboard-actions {
      display: flex;
      gap: 0.375rem;
      justify-self: end;
      padding-right: 1rem;
    }

    .dashboard-actions .btn {
      flex: 0 0 auto;
    }

    @media (max-width: 768px) {
      .dashboard-item {
        grid-template-columns: minmax(0, 1fr) auto;
      }

      .dashboard-title {
        white-space: normal;
      }

      .dashboard-actions {
        align-self: stretch;
        align-items: center;
        padding-right: 0.75rem;
      }

      .dashboard-meta {
        flex-direction: column;
        gap: 0.25rem;
      }
    }
  `]
})
export class DashboardListComponent {
  private readonly dashboardService = inject(DashboardService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
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

  editDashboard(id: string): void {
    this.router.navigate(['/dashboards', id, 'edit']);
  }

  async deleteDashboard(id: string): Promise<void> {
    const dashboard = this.dashboards().find(d => d.id === id);
    if (!dashboard) return;

    await this.dialogService.confirm({
      title: 'Delete dashboard?',
      message: `Are you sure you want to delete "${dashboard.name}"? This action cannot be undone.`,
      confirmLabel: 'Delete',
      isDangerous: true,
      onConfirm: async () => {
        // Optimistically remove from UI and allow undo
        this.dashboards.update(list => list.filter(d => d.id !== id));

        let didUndo = false;

        const performDelete = async () => {
          try {
            await firstValueFrom(this.dashboardService.deleteDashboard(id));
            if (!didUndo) {
              this.toastService.success('Dashboard deleted successfully');
            }
          } catch (error: any) {
            // On error, reload list and show error
            this.toastService.error(error.error?.message || 'Failed to delete dashboard');
            this.loadDashboards();
          }
        };

        const timeoutMs = 5000;
        const timeoutId = setTimeout(() => {
          performDelete();
        }, timeoutMs);

        this.toastService.showWithAction(
          `Dashboard "${dashboard.name}" deleted`,
          'Undo',
          () => {
            didUndo = true;
            clearTimeout(timeoutId);
            // restore dashboard in UI
            this.dashboards.update(list => [dashboard, ...list]);
            this.toastService.info('Deletion undone');
          },
          'info',
          timeoutMs
        );
      }
    });
  }
}
