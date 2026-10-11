import { Component, inject, OnInit, OnDestroy, signal, computed, ChangeDetectorRef, ViewChild, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormGroup, FormControl, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { DashboardService } from '../../services/dashboard.service';
import { AuthService } from '../../services/auth.service';
import { ToastService } from '../../services/toast.service';
import { DialogService } from '../../services/dialog.service';
import { DeviceService, Device } from '../../services/device.service';
import { AiService } from '../../services/ai.service';
import { Dashboard, DashboardOrientation, DashboardSizePreset, DASHBOARD_SIZE_PRESETS, DEFAULT_DASHBOARD_SIZE, AiConfig, AiConnectionMode, ConversationAgent } from '../../models/types';
import { HOME_ASSISTANT_PLUGIN_ID, HomeAssistantTargetPickerComponent } from '../home-assistant-target-picker/home-assistant-target-picker.component';
import { DataProviderService, ProviderConnection } from '../../services/data-provider.service';
import { RenderedPreviewModalComponent } from '../rendered-preview-modal/rendered-preview-modal.component';
import { HasUnsavedChanges } from '../../guards/unsaved-changes.guard';
import { PlaywrightComponentService, UserRenderingComponentStatus } from '../../services/playwright-component.service';

@Component({
  selector: 'app-dashboard-edit',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, RouterModule, RenderedPreviewModalComponent, HomeAssistantTargetPickerComponent],
  styles: [`
    .btn-group[role="group"] {
      display: grid !important;
      grid-template-columns: 1fr 1fr;
      gap: 0;
    }

    .btn-group[role="group"] .btn {
      flex: none !important;
    }

    .d-flex.align-items-center.gap-3 {
      min-width: 0;
    }

    .action-bar-title,
    .min-width-0 {
      min-width: 0;
    }

    .update-time-input {
      width: 9.375rem;
    }

    h2 {
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  `],
  template: `

    @if (isLoading()) {
      <div class="app-loading-state">
        <div class="spinner-border" role="status">
          <span class="visually-hidden">Loading dashboard</span>
        </div>
      </div>
    } @else if (dashboard()) {
      <form (ngSubmit)="onSubmit()" [formGroup]="dashboardForm" class="app-page">
        <div class="app-action-bar">
          <div class="action-bar-title d-flex align-items-center gap-2 gap-sm-3">
            <button type="button" class="btn btn-sm btn-outline-secondary" (click)="onCancel()" aria-label="Back to dashboards">
              <i class="fa-solid fa-arrow-left"></i><span class="d-none d-sm-inline"> Back</span>
            </button>
            <div class="min-width-0">
              <h1 class="h4 mb-0 text-truncate">{{ dashboard()!.name }}</h1>
              <span class="small" [ngClass]="dashboardForm.dirty ? 'text-warning' : 'text-muted'">
                {{ dashboardForm.dirty ? 'Unsaved changes' : 'All changes saved' }}
              </span>
            </div>
          </div>
          <div class="d-flex gap-1 gap-sm-2 flex-shrink-0">
            <button type="button" class="btn btn-sm btn-success" (click)="openPreview()" 
              [disabled]="disablePreviewButton() || dashboardForm.dirty"
              [title]="dashboardForm.dirty ? 'Save your changes before previewing' : (previewMode() === 'ssr' ? 'Open custom layout preview' : 'Open Home Assistant dashboard preview')">
              <i class="fa-solid fa-eye"></i><span class="d-none d-sm-inline"> Preview</span>
            </button>
            <button type="button" class="btn btn-sm btn-outline-secondary" (click)="discardChanges()" [disabled]="!dashboardForm.dirty || isSaving()">
              <i class="fa-solid fa-rotate-left"></i><span class="d-none d-sm-inline"> Discard</span>
            </button>
            <button type="submit" class="btn btn-sm btn-primary"
              [disabled]="!dashboardForm.dirty || isSaving() || dashboardForm.invalid || !dashboardForm.get('name')?.value?.trim()">
              <i class="fa-solid fa-floppy-disk"></i><span class="d-none d-sm-inline"> Save</span>
            </button>
          </div>
        </div>
        <div class="card shadow-sm mb-3" aria-labelledby="generalSettingsTitle">
          <div class="card-body">
            <h2 id="generalSettingsTitle" class="app-section-title">Dashboard configuration</h2>
            <p class="app-section-description">Manage the dashboard identity, content source, display, and update schedule.</p>
            <div class="row g-4">
              <div class="col-12 col-lg-6">
                <h3 class="h6 text-uppercase text-muted mb-3">General</h3>
                <div class="mb-3">
                  <label class="form-label fw-semibold" for="editDashboardName">Name</label>
                  <input 
                    id="editDashboardName"
                    type="text" 
                    class="form-control" 
                    formControlName="name"
                    [class.is-invalid]="dashboardForm.get('name')?.touched && !dashboardForm.get('name')?.value?.trim()"
                    required 
                  />
                  @if (dashboardForm.get('name')?.touched && !dashboardForm.get('name')?.value?.trim()) {
                    <div class="invalid-feedback">Name is required.</div>
                  }
                </div>
                <div class="mb-3">
                  <label class="form-label fw-semibold" for="editDashboardDescription">Description</label>
                  <input 
                    id="editDashboardDescription"
                    type="text" 
                    class="form-control" 
                    formControlName="description"
                  />
                </div>
                
                <div class="mb-3">
                  <span class="form-label fw-semibold d-block">Displayed on</span>

                  @if (isLoadingDevices()) {
                    <div class="text-center py-3">
                      <div class="spinner-border spinner-border-sm" role="status">
                        <span class="visually-hidden">Loading devices</span>
                      </div>
                    </div>
                  } @else if (devices().length > 0) {
                    <div class="mb-2">
                      @for (device of devices(); track device.id) {
                        <div class="d-flex align-items-center gap-2 py-1">
                          <i class="fa-solid fa-display text-muted"></i>
                          <span>{{ device.name }}</span>
                          <code class="small text-muted">{{ device.deviceIdentifier }}</code>
                        </div>
                      }
                    </div>
                  } @else {
                    <div class="text-muted small mb-2">
                      <i class="fa-solid fa-info-circle"></i> Not assigned to any device yet. Go to <a routerLink="/devices">Devices</a> to assign this dashboard.
                    </div>
                  }
                </div>
              </div>

              <div class="col-12 col-lg-6">
                <h3 class="h6 text-uppercase text-muted mb-3">Content and display</h3>
                <div class="mb-3">
                  <span class="form-label fw-semibold d-block">Dashboard type</span>
                  <!-- The type is chosen when the dashboard is created and cannot be changed afterwards. -->
                  <span class="badge text-bg-secondary">
                    <i class="fa-solid me-1" [ngClass]="previewModeValue === 'homeassistant' ? 'fa-house-signal' : 'fa-table-cells-large'" aria-hidden="true"></i>
                    {{ previewModeValue === 'homeassistant' ? 'Home Assistant dashboard' : 'Custom layout' }}
                  </span>
                </div>

                @if (previewModeValue === 'homeassistant') {
                @if (!renderingEnabled() || !playwrightStatus()?.ready) {
                <div class="alert alert-warning d-flex align-items-start gap-2" role="alert">
                  <i class="fa-solid fa-puzzle-piece mt-1" aria-hidden="true"></i>
                  <div>
                    <strong>Rendering is not ready.</strong>
                    Enable rendering for your account and wait for preparation to complete.
                    <a routerLink="/rendering" target="_blank" class="alert-link">Enable it in your Rendering settings.</a>
                  </div>
                </div>
                }
                <div class="mb-3">
                  <label class="form-label fw-semibold" for="editScreenSize">Screen size</label>
                  <select id="editScreenSize" class="form-select" [(ngModel)]="selectedSizeIndex" [ngModelOptions]="{standalone: true}" (change)="onSizeChange()">
                    @for (size of sizePresets; track size.label; let i = $index) {
                      <option [ngValue]="i">{{ size.label }}</option>
                    }
                  </select>
                </div>

                <div class="mb-3">
                  <span class="form-label fw-semibold d-block">Orientation</span>
                  <div class="btn-group d-flex" role="group">
                    <input type="radio" class="btn-check" name="orientationMode" id="landscapeMode" value="Landscape" [(ngModel)]="orientationValue" [ngModelOptions]="{standalone: true}" (change)="onOrientationChange()" />
                    <label class="btn btn-outline-secondary flex-grow-1" for="landscapeMode">
                      <i class="fa-solid fa-display"></i> Landscape
                    </label>

                    <input type="radio" class="btn-check" name="orientationMode" id="portraitMode" value="Portrait" [(ngModel)]="orientationValue" [ngModelOptions]="{standalone: true}" (change)="onOrientationChange()" />
                    <label class="btn btn-outline-secondary flex-grow-1" for="portraitMode">
                      <i class="fa-solid fa-mobile-screen"></i> Portrait
                    </label>
                  </div>
                </div>

                <app-home-assistant-target-picker
                  [connectionId]="dashboardForm.controls.homeAssistantConnectionId.value"
                  (connectionIdChange)="onHomeAssistantConnectionChange($event)"
                  [path]="dashboardForm.controls.path.value"
                  (pathChange)="onHomeAssistantPathChange($event)"
                  [allowDashboardSettings]="!!dashboard()!.hasLegacyConnection"
                  dashboardSettingsLabel="Legacy dashboard settings"
                  [dashboardId]="dashboard()!.id"
                  [dashboardSettingsReady]="dashboardSettingsReady()" />

                @if (dashboard()!.hasLegacyConnection) {
                  <ng-container *ngTemplateOutlet="legacySettingsNotice" />
                }
                } @else {
                  <div class="mb-3">
                    <span class="form-label fw-semibold d-block">Layout</span>
                    <button type="button" class="btn btn-success w-100" (click)="openDesigner()">
                      <i class="fa-solid fa-paint-brush"></i> Open Layout Designer
                    </button>
                  </div>
                  <div class="mb-3">
                    <span class="form-label fw-semibold d-block">Data</span>
                    <small class="form-text text-muted d-block">
                      Widgets use data from your <a routerLink="/connections" target="_blank" rel="noopener">connections</a>.
                      @if (isAddonMode()) { Home Assistant is also connected through the add-on. }
                    </small>
                  </div>
                  @if (dashboard()!.hasLegacyConnection) {
                    <ng-container *ngTemplateOutlet="legacySettingsNotice" />
                  }
                }

                <div class="mb-3">
                  <label class="form-label fw-semibold" for="newUpdateTime">Update times</label>
                  <div class="d-flex align-items-center mb-2">
                    <input 
                      type="time" 
                      class="form-control me-2 update-time-input"
                      [(ngModel)]="newUpdateTime"
                      [ngModelOptions]="{standalone: true}"
                      name="newUpdateTime"
                      id="newUpdateTime"
                    />
                    <button type="button" class="btn btn-outline-primary" (click)="addUpdateTime()">Add</button>
                  </div>
                  <div id="updateTimesList" class="mb-2 d-flex flex-wrap gap-2">
                    @for (time of updateTimes(); track $index) {
                      <span class="badge bg-secondary">
                        {{ time }}
                        <button type="button" class="btn-close btn-close-white ms-2" (click)="removeUpdateTime($index)" [attr.aria-label]="'Remove update time ' + time"></button>
                      </span>
                    } @empty {
                      <span class="text-muted">No update times configured</span>
                    }
                  </div>
                  <small class="form-text text-muted">Add one or more times. Example: 06:00, 12:00, 18:00</small>
                </div>
              </div>
            </div>
          </div>
        </div>

        <details class="card shadow-sm mb-3">
          <summary class="card-header bg-transparent py-3 px-3 px-sm-4 fw-semibold">Advanced provider settings</summary>
          <div class="card-body">
            <h2 class="h5 mb-3">AI provider override</h2>
            <p class="text-muted small mb-3">By default, this dashboard uses the <a routerLink="/ai/config">global AI provider</a>. You can override it to use a Home Assistant conversation agent for this dashboard.</p>
            <div class="mb-3">
                <label class="form-label fw-semibold">AI provider</label>
              <select class="form-select"
                [ngModel]="aiConfig().connectionMode"
                (ngModelChange)="onAiConnectionModeChange($event)"
                [ngModelOptions]="{standalone: true}">
                <option value="None">Default (use global AI provider)</option>
                <option value="HomeAssistant">Home Assistant conversation agent</option>
              </select>
            </div>

            @if (aiConfig().connectionMode === 'HomeAssistant') {
              <hr>
              <h3 class="h6 mb-3">Home Assistant settings</h3>
              <div class="mb-3">
                <label class="form-label" for="aiHomeAssistantConnection">Home Assistant connection</label>
                <select id="aiHomeAssistantConnection" class="form-select"
                  [ngModel]="aiConfig().homeAssistantConnectionId || ''"
                  (ngModelChange)="onAiHomeAssistantConnectionChange($event)"
                  [ngModelOptions]="{standalone: true}">
                  @if (isAddonMode()) {
                    <option value="">Home Assistant add-on</option>
                  } @else if (dashboard()!.hasLegacyConnection) {
                    <option value="">Legacy dashboard settings</option>
                  } @else {
                    <option value="" disabled>Select a connection</option>
                  }
                  @for (connection of homeAssistantConnections(); track connection.id) {
                    <option [value]="connection.id">{{ connection.name }}{{ connection.connected ? '' : ' (sign-in required)' }}</option>
                  }
                </select>
                <small class="form-text text-muted">
                  <a routerLink="/connections" target="_blank" rel="noopener">Manage connections</a>; this list refreshes when you return.
                </small>
              </div>
              <div class="mb-3">
                <label class="form-label">Conversation agent</label>
                @if (isLoadingAgents()) {
                  <div class="d-flex align-items-center gap-2 mb-2">
                    <span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
                    <span class="text-muted">Loading agents…</span>
                  </div>
                } @else {
                  <select class="form-select"
                    [ngModel]="aiConfig().homeAssistantAgentId || ''"
                    (ngModelChange)="updateAiField('homeAssistantAgentId', $event)"
                    [ngModelOptions]="{standalone: true}">
                    <option value="">Default conversation agent</option>
                    @for (agent of conversationAgents(); track agent.id) {
                      <option [value]="agent.id">{{ agent.name }}</option>
                    }
                  </select>
                  @if (conversationAgents().length === 0) {
                    <small class="form-text text-muted">No agents found. Make sure the selected connection is signed in.</small>
                  } @else {
                    <small class="form-text text-muted">Select a conversation agent or leave as default</small>
                  }
                }
              </div>
            }
          </div>
        </details>

        <ng-template #legacySettingsNotice>
          <div class="alert alert-secondary small mb-3" role="status">
            <strong>Legacy Home Assistant settings.</strong>
            @if (previewModeValue === 'homeassistant') {
              @if (dashboardForm.controls.homeAssistantConnectionId.value) {
                This dashboard no longer uses them. Remove them once you no longer need to switch back.
              } @else {
                This dashboard still renders with its retired host and token. Select a connection above to replace them.
              }
            } @else {
              Widgets created before connections may still read data through them. Reselect their sources from a connection, then remove these settings.
            }
            <a routerLink="/connections" target="_blank" rel="noopener" class="alert-link">Manage connections</a>
            <div class="mt-2">
              <button type="button" class="btn btn-sm btn-outline-danger" (click)="clearLegacySettings()" [disabled]="isSaving()">
                <i class="fa-solid fa-trash" aria-hidden="true"></i> Remove legacy settings
              </button>
            </div>
          </div>
        </ng-template>

      </form>

      <!-- Preview Modal -->
      <app-rendered-preview-modal
        title="Dashboard Preview"
        [isOpen]="showPreviewModal"
        [isLoading]="previewLoading"
        [error]="previewError"
        [imageUrl]="previewImageUrl"
        (close)="showPreviewModal.set(false)"
        (reload)="openPreview()">
      </app-rendered-preview-modal>
    }
  `
})
export class DashboardEditComponent implements OnInit, OnDestroy, HasUnsavedChanges {
  private readonly dashboardService = inject(DashboardService);
  protected readonly authService = inject(AuthService);
  private readonly deviceService = inject(DeviceService);
  private readonly aiService = inject(AiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly http = inject(HttpClient);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly toastService = inject(ToastService);
  private readonly dialogService = inject(DialogService);
  private readonly playwrightComponentService = inject(PlaywrightComponentService);
  private readonly dataProviderService = inject(DataProviderService);

  readonly isAddonMode = this.authService.isAddonMode;
  readonly hideHostField = computed(() => this.isAddonMode() || this.authService.isHostMode());

  readonly dashboard = signal<Dashboard | null>(null);
  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly updateTimes = signal<string[]>([]);
  readonly showPreviewModal = signal(false);
  readonly previewLoading = signal(false);
  readonly previewError = signal('');
  readonly previewImageUrl = signal('');
  readonly previewMode = signal<'ssr' | 'homeassistant'>('ssr');
  readonly renderingEnabled = signal(false);
  readonly playwrightStatus = signal<UserRenderingComponentStatus | null>(null);
  
  readonly devices = signal<Device[]>([]);
  readonly isLoadingDevices = signal(false);

  readonly aiConfig = signal<AiConfig>({ connectionMode: 'None' });
  readonly conversationAgents = signal<ConversationAgent[]>([]);
  readonly isLoadingAgents = signal(false);
  readonly homeAssistantConnections = signal<ProviderConnection[]>([]);
  private originalAiConfig: AiConfig = { connectionMode: 'None' };

  readonly dashboardForm = new FormGroup({
    name: new FormControl('', { validators: Validators.required, nonNullable: true }),
    description: new FormControl('', { nonNullable: true }),
    path: new FormControl('', { nonNullable: true }),
    homeAssistantConnectionId: new FormControl('', { nonNullable: true }),
  });

  newUpdateTime: string = '';
  previewModeValue: 'ssr' | 'homeassistant' = 'ssr';
  orientationValue: DashboardOrientation = 'Landscape';
  sizePresets: DashboardSizePreset[] = DASHBOARD_SIZE_PRESETS;
  selectedSizeIndex = 0;
  private previewObjectUrl: string | null = null;
  private originalDashboard: Dashboard | null = null;
  private originalUpdateTimes: string[] = [];

  ngOnInit(): void {
    this.loadPlaywrightStatus();
    this.loadHomeAssistantConnections();
    const id = this.route.snapshot.paramMap.get('id');

    if (id) {
      this.loadDashboard(id);
    }
  }

  @HostListener('window:focus')
  onWindowFocus(): void {
    this.loadPlaywrightStatus();
    this.loadHomeAssistantConnections();
  }

  private loadHomeAssistantConnections(): void {
    this.dataProviderService.connections().subscribe({
      next: connections => this.homeAssistantConnections.set(connections.filter(c => c.pluginId === HOME_ASSISTANT_PLUGIN_ID)),
      error: () => this.homeAssistantConnections.set([]),
    });
  }

  private loadPlaywrightStatus(): void {
    this.playwrightComponentService.getUserStatus().subscribe({
      next: status => {
        this.playwrightStatus.set(status.component);
        this.renderingEnabled.set(status.enabled);
      }
    });
  }

  loadDashboard(id: string): void {
    this.isLoading.set(true);
    this.dashboardService.getDashboard(id).subscribe({
      next: (dashboard) => {
        this.dashboard.set(dashboard);
        this.originalDashboard = JSON.parse(JSON.stringify(dashboard));

        this.dashboardForm.reset({
          name: dashboard.name || '',
          description: dashboard.description || '',
          path: dashboard.path || '',
          homeAssistantConnectionId: dashboard.homeAssistantConnectionId || '',
        });

        if (dashboard.updateTimes) {
          let times: string[] = [];
          if (Array.isArray(dashboard.updateTimes)) {
            times = dashboard.updateTimes.filter(t => !!t);
          } else if (typeof (dashboard as any).updateTimes === 'string') {
            times = (dashboard as any).updateTimes
              .split(',')
              .map((t: string) => t.trim())
              .filter((t: string) => t.length > 0);
          }
          this.updateTimes.set(times);
          this.originalUpdateTimes = [...times];
        } else {
          this.updateTimes.set([]);
          this.originalUpdateTimes = [];
        }

        if (dashboard.renderingMode === 'HomeAssistant') {
          this.previewModeValue = 'homeassistant';
        } else {
          this.previewModeValue = 'ssr';
        }
        this.previewMode.set(this.previewModeValue);

        this.orientationValue = dashboard.orientation || 'Landscape';

        const sizeIdx = this.sizePresets.findIndex(
          s => s.width === dashboard.screenWidth && s.height === dashboard.screenHeight
        );
        this.selectedSizeIndex = sizeIdx >= 0 ? sizeIdx : 0;

        this.isLoading.set(false);

        this.loadDevices();

        const config = dashboard.aiConfig ?? { connectionMode: 'None' as AiConnectionMode };
        this.aiConfig.set(config);
        this.originalAiConfig = JSON.parse(JSON.stringify(config));
        if (config.connectionMode === 'HomeAssistant') {
          this.loadConversationAgents(dashboard.id);
        }

        this.cdr.detectChanges();
      },
      error: () => {
        this.router.navigate(['/dashboards']);
      }
    });
  }

  addUpdateTime(): void {
    if (this.newUpdateTime) {
      const current = this.updateTimes();
      if (!current.includes(this.newUpdateTime)) {
        const newTimes = [...current, this.newUpdateTime].sort();
        this.updateTimes.set(newTimes);
        this.newUpdateTime = '';
        this.checkUpdateTimesChanged(newTimes);
      }
    }
  }

  removeUpdateTime(index: number): void {
    const current = this.updateTimes();
    const newTimes = current.filter((_, i) => i !== index);
    this.updateTimes.set(newTimes);
    this.checkUpdateTimesChanged(newTimes);
  }

  private checkUpdateTimesChanged(currentTimes: string[]): void {
    const timesChanged = JSON.stringify(currentTimes.sort()) !== JSON.stringify(this.originalUpdateTimes.sort());

    if (timesChanged) {
      this.dashboardForm.markAsDirty();
    }
  }

  onSubmit(): void {
    const currentDashboard = this.dashboard();
    if (!currentDashboard) {
      return;
    }

    const formValue = this.dashboardForm.getRawValue();
    if (!this.dashboardForm.valid || !formValue.name.trim()) {
      this.dashboardForm.markAllAsTouched();
      this.toastService.error('Please fill in all required fields.');
      return;
    }

    this.isSaving.set(true);

    const selectedSize = this.sizePresets[this.selectedSizeIndex] ?? DEFAULT_DASHBOARD_SIZE;
    const updatePayload: any = {
      name: formValue.name.trim(),
      description: formValue.description?.trim() || undefined,
      path: formValue.path?.trim() || undefined,
      updateTimes: this.updateTimes().length > 0 ? this.updateTimes() : undefined,
      // Only Home Assistant mode edits the connection; an empty value selects the dashboard settings.
      ...(this.previewModeValue === 'homeassistant' ? { homeAssistantConnectionId: formValue.homeAssistantConnectionId } : {}),
      orientation: this.orientationValue,
      screenWidth: selectedSize.width,
      screenHeight: selectedSize.height,
      aiConfig: this.aiConfig(),
    };

    this.dashboardService.updateDashboard(currentDashboard.id, updatePayload).subscribe({
      next: (updated) => {
        this.dashboard.set(updated);
        this.dashboardForm.patchValue({
          name: updated.name || '',
          description: updated.description || '',
          path: updated.path || '',
          homeAssistantConnectionId: updated.homeAssistantConnectionId || '',
        });
        this.dashboardForm.markAsPristine();
        if (updated.updateTimes && Array.isArray(updated.updateTimes)) {
          this.originalUpdateTimes = [...updated.updateTimes];
        } else {
          this.originalUpdateTimes = [];
        }
        const savedConfig = updated.aiConfig ?? { connectionMode: 'None' as AiConnectionMode };
        this.aiConfig.set(savedConfig);
        this.originalAiConfig = JSON.parse(JSON.stringify(savedConfig));
        this.toastService.success('Dashboard updated successfully!');
        this.isSaving.set(false);
      },
      error: (error) => {
        this.toastService.error(error.error?.message || 'Failed to update dashboard.');
        this.isSaving.set(false);
      }
    });
  }

  onCancel(): void {
    this.router.navigate(['/dashboards']);
  }

  hasUnsavedChanges(): boolean {
    return this.dashboardForm.dirty;
  }

  discardChanges(): void {
    const dashboard = this.originalDashboard;
    if (!dashboard) return;

    this.dashboardForm.reset({
      name: dashboard.name || '',
      description: dashboard.description || '',
      path: dashboard.path || '',
      homeAssistantConnectionId: dashboard.homeAssistantConnectionId || '',
    });

    this.updateTimes.set([...this.originalUpdateTimes]);

    if (dashboard.renderingMode === 'HomeAssistant') {
      this.previewModeValue = 'homeassistant';
    } else {
      this.previewModeValue = 'ssr';
    }
    this.previewMode.set(this.previewModeValue);

    this.orientationValue = dashboard.orientation || 'Landscape';

    const sizeIdx = this.sizePresets.findIndex(
      s => s.width === dashboard.screenWidth && s.height === dashboard.screenHeight
    );
    this.selectedSizeIndex = sizeIdx >= 0 ? sizeIdx : 0;

    const config = JSON.parse(JSON.stringify(this.originalAiConfig));
    this.aiConfig.set(config);

  }

  openDesigner(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.router.navigate(['/dashboards', id, 'designer']);
    }
  }

  async clearLegacySettings(): Promise<void> {
    const currentDashboard = this.dashboard();
    if (!currentDashboard) return;
    const stillRendering = this.previewModeValue === 'homeassistant' && !this.dashboardForm.controls.homeAssistantConnectionId.value;
    await this.dialogService.confirm({
      title: 'Remove legacy settings',
      message: stillRendering
        ? 'This dashboard still renders with these settings. After removing them it cannot render until you select a connection. Remove them?'
        : 'Remove the retired Home Assistant host and token from this dashboard? Widgets that still read data through them will show unavailable data.',
      confirmLabel: 'Remove',
      isDangerous: true,
      onConfirm: () => {
        this.dashboardService.updateDashboard(currentDashboard.id, { clearAccessToken: true }).subscribe({
          next: updated => {
            this.dashboard.set(updated);
            this.originalDashboard = JSON.parse(JSON.stringify(updated));
            this.toastService.success('Legacy settings removed');
          },
          error: error => this.toastService.error(error.error?.message || 'Failed to remove legacy settings.'),
        });
      }
    });
  }

  onOrientationChange(): void {
    this.dashboardForm.markAsDirty();
  }

  onSizeChange(): void {
    this.dashboardForm.markAsDirty();
  }

  onAiConnectionModeChange(mode: AiConnectionMode): void {
    this.aiConfig.update(c => ({ ...c, connectionMode: mode }));
    this.dashboardForm.markAsDirty();
    if (mode === 'HomeAssistant') {
      // Without the add-on or legacy settings, a named connection is required; start with a signed-in one.
      const currentDashboard = this.dashboard();
      if (!this.aiConfig().homeAssistantConnectionId && !this.isAddonMode() && !currentDashboard?.hasLegacyConnection) {
        const connection = this.homeAssistantConnections().find(c => c.connected);
        if (connection) this.aiConfig.update(c => ({ ...c, homeAssistantConnectionId: connection.id }));
      }
      if (currentDashboard) {
        this.loadConversationAgents(currentDashboard.id);
      }
    }
  }

  onAiHomeAssistantConnectionChange(connectionId: string): void {
    // Agents differ per server, so a previously chosen agent no longer applies.
    this.aiConfig.update(c => ({ ...c, homeAssistantConnectionId: connectionId || undefined, homeAssistantAgentId: undefined }));
    this.dashboardForm.markAsDirty();
    const currentDashboard = this.dashboard();
    if (currentDashboard) this.loadConversationAgents(currentDashboard.id);
  }

  updateAiField(field: keyof AiConfig, value: string): void {
    this.aiConfig.update(c => ({ ...c, [field]: value }));
    this.dashboardForm.markAsDirty();
  }

  private loadConversationAgents(dashboardId: string): void {
    this.isLoadingAgents.set(true);
    this.aiService.getConversationAgents(dashboardId, this.aiConfig().homeAssistantConnectionId).subscribe({
      next: (agents) => {
        this.conversationAgents.set(agents);
        this.isLoadingAgents.set(false);
      },
      error: () => {
        this.conversationAgents.set([]);
        this.isLoadingAgents.set(false);
      }
    });
  }

  disablePreviewButton(): boolean {
    const mode = this.previewMode();
    if (mode === 'ssr') {
      return !this.dashboard()?.layoutConfig;
    }
    if (!this.dashboardForm.controls.path.value) {
      return true;
    }
    return !this.dashboardForm.controls.homeAssistantConnectionId.value && !this.dashboardSettingsReady();
  }

  /** Whether the dashboard's retired host/token settings can still reach Home Assistant. */
  dashboardSettingsReady(): boolean {
    const dashboard = this.dashboard();
    // The add-on and host modes manage the host; Home Assistant rendering still needs a long-lived token.
    return !!dashboard?.hasLegacyConnection && dashboard.hasAccessToken && (this.hideHostField() || !!dashboard.host);
  }

  onHomeAssistantConnectionChange(connectionId: string): void {
    if (connectionId === this.dashboardForm.controls.homeAssistantConnectionId.value) return;
    this.dashboardForm.patchValue({ homeAssistantConnectionId: connectionId });
    this.dashboardForm.markAsDirty();
  }

  onHomeAssistantPathChange(path: string): void {
    this.dashboardForm.patchValue({ path });
    this.dashboardForm.markAsDirty();
  }

  openPreview(): void {
    this.previewMode.set(this.previewModeValue);
    
    if (this.previewMode() === 'ssr') {
      this.openSsrPreview();
    } else {
      this.openHomeAssistantPreview();
    }
  }

  private openSsrPreview(): void {
    const currentDashboard = this.dashboard();
    if (!currentDashboard?.layoutConfig) {
      this.toastService.error('Please configure the dashboard layout in the designer first');
      return;
    }

    this.showPreviewModal.set(true);
    this.previewLoading.set(true);
    this.previewError.set('');
    this.previewImageUrl.set('');

    if (this.previewObjectUrl) {
      try {
        URL.revokeObjectURL(this.previewObjectUrl);
      } catch (e) {}
      this.previewObjectUrl = null;
    }

    const url = `/api/dashboards/${currentDashboard.id}/render-image?format=png`;

    this.http.get(url, {
      responseType: 'blob'
    }).subscribe({
      next: (blob) => {
        const imageUrl = URL.createObjectURL(blob);
        this.previewObjectUrl = imageUrl;
        this.previewImageUrl.set(imageUrl);
        this.previewLoading.set(false);
      },
      error: async (error) => {
        this.previewLoading.set(false);
        let errorMessage = 'Failed to load SSR preview';

        if (error.error instanceof Blob) {
          try {
            const text = await error.error.text();
            try {
              const json = JSON.parse(text);
              errorMessage = json.title || json.error || json.message || text;
            } catch (jsonError) {
              errorMessage = text || `HTTP Error ${error.status}`;
            }
          } catch (e) {
            errorMessage = `HTTP Error ${error.status}`;
          }
        } else if (error.status) {
          errorMessage = `HTTP Error ${error.status}`;
        }

        this.previewError.set(errorMessage);
        this.toastService.error(errorMessage);
      }
    });
  }

  private openHomeAssistantPreview(): void {
    const currentDashboard = this.dashboard();
    if (!currentDashboard) return;

    if (this.disablePreviewButton()) {
      this.toastService.error('Preview requires a Home Assistant connection and a dashboard path.');
      return;
    }

    this.showPreviewModal.set(true);
    this.previewLoading.set(true);
    this.previewError.set('');
    this.previewImageUrl.set('');

    if (this.previewObjectUrl) {
      try {
        URL.revokeObjectURL(this.previewObjectUrl);
      } catch (e) {}
      this.previewObjectUrl = null;
    }

    const url = `/api/dashboards/${currentDashboard.id}/preview?format=png`;

    this.http.get(url, {
      responseType: 'blob'
    }).subscribe({
      next: (blob) => {
        const imageUrl = URL.createObjectURL(blob);
        this.previewObjectUrl = imageUrl;
        this.previewImageUrl.set(imageUrl);
        this.previewLoading.set(false);
      },
      error: async (error) => {
        this.previewLoading.set(false);
        let errorMessage = 'Failed to load Home Assistant preview';

        if (error.error instanceof Blob) {
          try {
            const text = await error.error.text();
            try {
              const json = JSON.parse(text);
              errorMessage = json.title || json.error || json.message || text;
            } catch (jsonError) {
              errorMessage = text || `HTTP Error ${error.status}`;
            }
          } catch (e) {
            errorMessage = `HTTP Error ${error.status}`;
          }
        } else if (error.error && typeof error.error === 'string') {
          errorMessage = error.error;
        } else if (error.error && error.error.error) {
          errorMessage = error.error.error;
        } else if (error.error && error.error.message) {
          errorMessage = error.error.message;
        } else if (error.status) {
          errorMessage = `HTTP Error ${error.status}`;
        }

        this.previewError.set(errorMessage);
        this.toastService.error(errorMessage);
      }
    });
  }

  loadDevices(): void {
    const currentDashboard = this.dashboard();
    if (!currentDashboard) return;

    this.isLoadingDevices.set(true);
    this.deviceService.getDevicesForDashboard(currentDashboard.id).subscribe({
      next: (devices) => {
        this.devices.set(devices);
        this.isLoadingDevices.set(false);
      },
      error: () => {
        this.isLoadingDevices.set(false);
        this.toastService.error('Failed to load devices');
      }
    });
  }

  ngOnDestroy(): void {
    if (this.previewObjectUrl) {
      try {
        URL.revokeObjectURL(this.previewObjectUrl);
      } catch (e) {
      }
    }
  }
}
