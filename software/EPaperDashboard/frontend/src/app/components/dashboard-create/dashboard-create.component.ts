import { Component, HostListener, OnInit, ViewChild, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { DashboardService } from '../../services/dashboard.service';
import { PlaywrightComponentService, UserRenderingStatus } from '../../services/playwright-component.service';
import { CreateDashboardRequest, DashboardOrientation, DashboardSizePreset, DASHBOARD_SIZE_PRESETS, DEFAULT_DASHBOARD_SIZE, RenderingMode } from '../../models/types';
import { HomeAssistantTargetPickerComponent } from '../home-assistant-target-picker/home-assistant-target-picker.component';

interface DashboardKind {
  mode: RenderingMode;
  id: string;
  icon: string;
  label: string;
  description: string;
}

@Component({
  selector: 'app-dashboard-create',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, HomeAssistantTargetPickerComponent],
  styles: [`
    .kind-option { cursor: pointer; height: 100%; }
    .btn-check:checked + .kind-option { border-color: var(--bs-primary); box-shadow: 0 0 0 1px var(--bs-primary); }
    .btn-check:focus-visible + .kind-option { outline: 2px solid var(--bs-primary); outline-offset: 2px; }
  `],
  template: `
    <div class="app-page py-4">
      <div class="app-form-shell">
        <div class="app-page-header">
          <div>
            <h1 class="app-page-title">Create dashboard</h1>
            <p class="app-page-description">Choose what the display shows and its size.</p>
          </div>
        </div>
        <form (ngSubmit)="onSubmit()" class="app-form-card" novalidate>
        <fieldset class="mb-4">
          <legend class="form-label fw-semibold fs-6">Dashboard type</legend>
          <div class="row g-2">
            @for (kind of kinds; track kind.mode) {
              <div class="col-12 col-sm-6">
                <input type="radio" class="btn-check" name="renderingMode" [id]="kind.id" [value]="kind.mode"
                  [(ngModel)]="renderingMode" (ngModelChange)="errorMessage.set('')" />
                <label class="card card-body kind-option" [for]="kind.id">
                  <span class="fw-semibold"><i class="fa-solid me-2" [ngClass]="kind.icon" aria-hidden="true"></i>{{ kind.label }}</span>
                  <span class="small text-muted mt-1">{{ kind.description }}</span>
                </label>
              </div>
            }
          </div>
        </fieldset>
        <div class="mb-3">
          <label class="form-label fw-semibold" for="dashboardName">Name</label>
          <input id="dashboardName" type="text" class="form-control" [(ngModel)]="name" name="name"
            autocomplete="off" required autofocus>
        </div>
        <div class="mb-3">
          <label class="form-label fw-semibold" for="dashboardDescription">Description <span class="text-muted fw-normal">(optional)</span></label>
          <input id="dashboardDescription" type="text" class="form-control" [(ngModel)]="description" name="description">
        </div>
        <div class="mb-3">
          <label class="form-label fw-semibold" for="dashboardScreenSize">Screen size</label>
          <select id="dashboardScreenSize" class="form-select" [(ngModel)]="selectedSizeIndex" name="screenSize">
            @for (size of sizePresets; track size.label; let i = $index) {
              <option [ngValue]="i">{{ size.label }}</option>
            }
          </select>
        </div>
        <div class="mb-3">
          <span class="form-label fw-semibold d-block" id="dashboardOrientationLabel">Orientation</span>
          <div class="btn-group d-flex" role="group" aria-labelledby="dashboardOrientationLabel">
            <input type="radio" class="btn-check" name="orientation" id="createLandscape" value="Landscape" [(ngModel)]="orientation" />
            <label class="btn btn-outline-secondary flex-grow-1" for="createLandscape">
              <i class="fa-solid fa-display"></i> Landscape
            </label>
            <input type="radio" class="btn-check" name="orientation" id="createPortrait" value="Portrait" [(ngModel)]="orientation" />
            <label class="btn btn-outline-secondary flex-grow-1" for="createPortrait">
              <i class="fa-solid fa-mobile-screen"></i> Portrait
            </label>
          </div>
        </div>
        @if (renderingMode === 'HomeAssistant') {
          <hr>
          @if (renderingStatus(); as status) {
            @if (!status.enabled || !status.component.ready) {
              <div class="alert alert-warning small" role="alert">
                <strong>Rendering is not ready.</strong> Home Assistant dashboards are captured by the rendering component.
                <a routerLink="/settings" target="_blank" rel="noopener" class="alert-link">Enable it in Rendering settings</a>; you can finish setup later.
              </div>
            }
          }
          <app-home-assistant-target-picker #picker
            [connectionId]="homeAssistantConnectionId"
            (connectionIdChange)="homeAssistantConnectionId = $event"
            [path]="path"
            (pathChange)="path = $event"
            [autoSelect]="true"
            [disabled]="isLoading()" />
        } @else {
          <p class="small text-muted">
            Widgets can use data from your <a routerLink="/connections" target="_blank" rel="noopener">connections</a>, such as Home Assistant or Google.
          </p>
        }
        @if (errorMessage()) {
          <div class="alert alert-danger" role="alert">{{ errorMessage() }}</div>
        }
        <div class="d-flex flex-column-reverse flex-sm-row justify-content-end gap-2">
          <button type="button" class="btn btn-outline-secondary" (click)="onCancel()">Cancel</button>
          <button type="submit" class="btn btn-primary flex-grow-1" [disabled]="isLoading()">
            @if (isLoading()) {
              <span class="spinner-border spinner-border-sm me-2" aria-hidden="true"></span>
            }
            {{ isLoading() ? 'Creating dashboard…' : 'Create dashboard' }}
          </button>
        </div>
        </form>
      </div>
    </div>
  `
})
export class DashboardCreateComponent implements OnInit {
  private readonly dashboardService = inject(DashboardService);
  private readonly renderingService = inject(PlaywrightComponentService);
  private readonly router = inject(Router);

  readonly kinds: readonly DashboardKind[] = [
    {
      mode: 'Custom', id: 'kindCustom', icon: 'fa-table-cells-large', label: 'Custom layout',
      description: 'Design your own layout with widgets fed by your connected data providers.',
    },
    {
      mode: 'HomeAssistant', id: 'kindHomeAssistant', icon: 'fa-house-signal', label: 'Home Assistant dashboard',
      description: 'Show an existing Home Assistant dashboard, captured by the rendering component.',
    },
  ];

  name = '';
  description = '';
  renderingMode: RenderingMode = 'Custom';
  homeAssistantConnectionId = '';
  path = '';
  orientation: DashboardOrientation = 'Landscape';
  sizePresets: DashboardSizePreset[] = DASHBOARD_SIZE_PRESETS;
  selectedSizeIndex = 0;
  readonly errorMessage = signal('');
  readonly isLoading = signal(false);
  readonly renderingStatus = signal<UserRenderingStatus | null>(null);
  @ViewChild('picker') private picker?: HomeAssistantTargetPickerComponent;

  get selectedSize(): DashboardSizePreset {
    return this.sizePresets[this.selectedSizeIndex] ?? DEFAULT_DASHBOARD_SIZE;
  }

  ngOnInit(): void {
    this.loadRenderingStatus();
  }

  @HostListener('window:focus')
  loadRenderingStatus(): void {
    this.renderingService.getUserStatus().subscribe({ next: status => this.renderingStatus.set(status), error: () => {} });
  }

  onSubmit(): void {
    const name = this.name.trim();
    const description = this.description.trim();
    if (!name) {
      this.errorMessage.set('Name is required.');
      return;
    }

    const isHomeAssistant = this.renderingMode === 'HomeAssistant';
    const path = this.path.trim();
    if (isHomeAssistant && !this.homeAssistantConnectionId) {
      this.errorMessage.set('Select a Home Assistant connection.');
      return;
    }
    if (isHomeAssistant && this.picker && !this.picker.isSelectedReady()) {
      this.errorMessage.set('The selected Home Assistant connection needs a sign-in or access token first.');
      return;
    }
    if (isHomeAssistant && !path) {
      this.errorMessage.set('Select the Home Assistant dashboard path.');
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set('');

    const size = this.selectedSize;
    const request: CreateDashboardRequest = {
      name,
      description: description || undefined,
      orientation: this.orientation,
      screenWidth: size.width,
      screenHeight: size.height,
      renderingMode: this.renderingMode,
      ...(isHomeAssistant ? { homeAssistantConnectionId: this.homeAssistantConnectionId, path } : {}),
    };
    this.dashboardService.createDashboard(request).subscribe({
      // A custom layout is empty, so continue in the designer; a Home Assistant dashboard is ready to preview in its settings.
      next: (dashboard) => {
        this.router.navigate(['/dashboards', dashboard.id, isHomeAssistant ? 'edit' : 'designer']);
      },
      error: (error) => {
        this.errorMessage.set(error.error?.message || 'Failed to create dashboard.');
        this.isLoading.set(false);
      }
    });
  }

  onCancel(): void {
    this.router.navigate(['/dashboards']);
  }
}
