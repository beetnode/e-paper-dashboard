import { Component, inject, OnInit, OnDestroy, signal, ChangeDetectorRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormGroup, FormControl, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { DashboardService } from '../../services/dashboard.service';
import { AuthService } from '../../services/auth.service';
import { ToastService } from '../../services/toast.service';
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
    .action-bar-title {
      display: flex;
      flex: 1 1 auto;
      align-items: center;
      gap: 0.75rem;
      min-width: 0;
    }

    /* The name gives way first when Save and Discard appear on a narrow screen. */
    .action-bar-title h1 {
      min-width: 0;
    }

    .update-time-input {
      width: 9.375rem;
    }

    .update-time-chip {
      padding-right: 0.25rem;
    }

    .update-time-chip .btn-close {
      width: 0.5rem;
      height: 0.5rem;
      padding: 0.25rem;
      background-size: 0.5rem;
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
          <div class="action-bar-title">
            <button type="button" class="btn btn-sm btn-outline-secondary" (click)="onCancel()" aria-label="Back to dashboards" title="Back to dashboards">
              <i class="fa-solid fa-arrow-left" aria-hidden="true"></i><span class="d-none d-lg-inline"> Back</span>
            </button>
            <h1 class="h4 mb-0 text-truncate" [title]="dashboard()!.name">{{ dashboard()!.name }}</h1>
            <span class="visually-hidden" aria-live="polite">{{ dashboardForm.dirty ? 'Unsaved changes' : '' }}</span>
            @if (dashboardForm.dirty || isSaving()) {
              <div class="app-save-actions">
                <button type="submit" class="btn btn-sm btn-primary" title="Save changes" aria-label="Save changes"
                  [disabled]="isSaving() || dashboardForm.invalid || !dashboardForm.get('name')?.value?.trim()">
                  @if (isSaving()) {
                    <span class="spinner-border spinner-border-sm" aria-hidden="true"></span>
                  } @else {
                    <i class="fa-solid fa-floppy-disk" aria-hidden="true"></i>
                  }
                  <span class="d-none d-sm-inline">Save</span>
                </button>
                <button type="button" class="btn btn-sm btn-outline-secondary" (click)="discardChanges()" [disabled]="isSaving()"
                  title="Discard unsaved changes" aria-label="Discard unsaved changes">
                  <i class="fa-solid fa-rotate-left" aria-hidden="true"></i><span class="d-none d-md-inline">Discard</span>
                </button>
              </div>
            }
          </div>
          <div class="app-page-actions flex-nowrap">
            @if (previewModeValue === 'ssr') {
              <button type="button" class="btn btn-sm btn-outline-primary" (click)="openDesigner()" title="Edit the layout in the designer" aria-label="Open designer">
                <i class="fa-solid fa-paintbrush" aria-hidden="true"></i><span class="d-none d-sm-inline ms-1">Designer</span>
              </button>
            }
            <button type="button" class="btn btn-sm btn-success" (click)="openPreview()"
              [disabled]="disablePreviewButton() || dashboardForm.dirty"
              [title]="dashboardForm.dirty ? 'Save your changes before previewing' : 'Preview the device image'" aria-label="Preview">
              <i class="fa-solid fa-eye" aria-hidden="true"></i><span class="d-none d-sm-inline ms-1">Preview</span>
            </button>
          </div>
        </div>

        <div class="app-section-grid">
          <div class="app-section-stack">
            <section class="app-section-card" aria-labelledby="generalSettingsTitle">
              <h2 id="generalSettingsTitle" class="app-section-title mb-3">General</h2>
              <div class="mb-3">
                <label class="form-label fw-semibold" for="editDashboardName">Name</label>
                <input id="editDashboardName" type="text" class="form-control" formControlName="name" required
                  [class.is-invalid]="dashboardForm.get('name')?.touched && !dashboardForm.get('name')?.value?.trim()" />
                @if (dashboardForm.get('name')?.touched && !dashboardForm.get('name')?.value?.trim()) {
                  <div class="invalid-feedback">Name is required.</div>
                }
              </div>
              <div>
                <label class="form-label fw-semibold" for="editDashboardDescription">Description <span class="text-muted fw-normal">(optional)</span></label>
                <input id="editDashboardDescription" type="text" class="form-control" formControlName="description" />
              </div>
            </section>
            @if (previewModeValue === 'homeassistant') {
              <section class="app-section-card" aria-labelledby="contentSettingsTitle">
                <h2 id="contentSettingsTitle" class="app-section-title">Home Assistant dashboard</h2>
                <p class="app-section-description">The view captured and sent to the display.</p>
                @if (!renderingEnabled() || !playwrightStatus()?.ready) {
                  <div class="alert alert-warning small" role="alert">
                    <strong>Rendering is not ready.</strong> Home Assistant dashboards are captured by the rendering component.
                    <a routerLink="/rendering" target="_blank" rel="noopener" class="alert-link">Enable it in Rendering settings.</a>
                  </div>
                }
                <div class="row g-3 mb-3">
                  <div class="col-12 col-sm-6">
                    <label class="form-label fw-semibold" for="editScreenSize">Screen size</label>
                    <select id="editScreenSize" class="form-select" [(ngModel)]="selectedSizeIndex" [ngModelOptions]="{standalone: true}" (change)="onSizeChange()">
                      @for (size of sizePresets; track size.label; let i = $index) {
                        <option [ngValue]="i">{{ size.label }}</option>
                      }
                    </select>
                  </div>
                  <div class="col-12 col-sm-6">
                    <span class="form-label fw-semibold d-block" id="editOrientationLabel">Orientation</span>
                    <div class="btn-group d-flex" role="group" aria-labelledby="editOrientationLabel">
                      <input type="radio" class="btn-check" name="orientationMode" id="landscapeMode" value="Landscape" [(ngModel)]="orientationValue" [ngModelOptions]="{standalone: true}" (change)="onOrientationChange()" />
                      <label class="btn btn-outline-secondary flex-grow-1" for="landscapeMode">
                        <i class="fa-solid fa-display" aria-hidden="true"></i> Landscape
                      </label>
                      <input type="radio" class="btn-check" name="orientationMode" id="portraitMode" value="Portrait" [(ngModel)]="orientationValue" [ngModelOptions]="{standalone: true}" (change)="onOrientationChange()" />
                      <label class="btn btn-outline-secondary flex-grow-1" for="portraitMode">
                        <i class="fa-solid fa-mobile-screen" aria-hidden="true"></i> Portrait
                      </label>
                    </div>
                  </div>
                </div>
                <app-home-assistant-target-picker
                  [connectionId]="dashboardForm.controls.homeAssistantConnectionId.value"
                  (connectionIdChange)="onHomeAssistantConnectionChange($event)"
                  [path]="dashboardForm.controls.path.value"
                  (pathChange)="onHomeAssistantPathChange($event)" />
              </section>
            } @else {
              <ng-container *ngTemplateOutlet="scheduleSection" />
            }
          </div>

          <div class="app-section-stack">
            @if (previewModeValue === 'homeassistant') {
              <ng-container *ngTemplateOutlet="scheduleSection" />
            }
            <section class="app-section-card" aria-labelledby="devicesSettingsTitle">
              <div class="app-section-header mb-2">
                <h2 id="devicesSettingsTitle" class="app-section-title mb-0">Devices</h2>
                <a routerLink="/devices" class="btn btn-sm btn-outline-secondary">Manage devices</a>
              </div>
              @if (isLoadingDevices()) {
                <div class="spinner-border spinner-border-sm" role="status"><span class="visually-hidden">Loading devices</span></div>
              } @else if (devices().length > 0) {
                <ul class="list-unstyled mb-0">
                  @for (device of devices(); track device.id) {
                    <li class="d-flex align-items-center gap-2 py-1">
                      <i class="fa-solid fa-tablet-screen-button text-muted" aria-hidden="true"></i>
                      <span>{{ device.name }}</span>
                    </li>
                  }
                </ul>
              } @else {
                <p class="text-muted small mb-0">Not shown on any device yet. Assign it from the Devices page.</p>
              }
            </section>

            <!-- AI generated widgets exist only in custom layouts. -->
            @if (previewModeValue === 'ssr') {
              <section class="app-section-card" aria-labelledby="aiSettingsTitle">
                <h2 id="aiSettingsTitle" class="app-section-title">AI provider</h2>
                <p class="app-section-description">Used for AI generated widgets. Defaults to the <a routerLink="/ai/config">global AI provider</a>.</p>
                <div [class.mb-3]="aiConfig().connectionMode === 'HomeAssistant'">
                  <label class="form-label fw-semibold" for="aiConnectionMode">Provider</label>
                  <select id="aiConnectionMode" class="form-select"
                    [ngModel]="aiConfig().connectionMode"
                    (ngModelChange)="onAiConnectionModeChange($event)"
                    [ngModelOptions]="{standalone: true}">
                    <option value="None">Global AI provider</option>
                    <option value="HomeAssistant">Home Assistant conversation agent</option>
                  </select>
                </div>

                @if (aiConfig().connectionMode === 'HomeAssistant') {
                  <div class="row g-3">
                    <div class="col-12 col-sm-6">
                      <label class="form-label fw-semibold" for="aiHomeAssistantConnection">Connection</label>
                      <select id="aiHomeAssistantConnection" class="form-select"
                        [ngModel]="aiConfig().homeAssistantConnectionId || ''"
                        (ngModelChange)="onAiHomeAssistantConnectionChange($event)"
                        [ngModelOptions]="{standalone: true}">
                        @if (isAddonMode()) {
                          <option value="">Home Assistant add-on</option>
                        } @else {
                          <option value="" disabled>Select a connection</option>
                        }
                        @for (connection of homeAssistantConnections(); track connection.id) {
                          <option [value]="connection.id">{{ connection.name }}{{ connection.connected ? '' : ' (sign-in required)' }}</option>
                        }
                      </select>
                      <small class="form-text text-muted"><a routerLink="/connections" target="_blank" rel="noopener">Manage connections</a></small>
                    </div>
                    <div class="col-12 col-sm-6">
                      <label class="form-label fw-semibold" for="aiConversationAgent">Conversation agent</label>
                      @if (isLoadingAgents()) {
                        <div class="d-flex align-items-center gap-2 py-2" role="status">
                          <span class="spinner-border spinner-border-sm" aria-hidden="true"></span>
                          <span class="text-muted">Loading agents…</span>
                        </div>
                      } @else {
                        <select id="aiConversationAgent" class="form-select"
                          [ngModel]="aiConfig().homeAssistantAgentId || ''"
                          (ngModelChange)="updateAiField('homeAssistantAgentId', $event)"
                          [ngModelOptions]="{standalone: true}">
                          <option value="">Default agent</option>
                          @for (agent of conversationAgents(); track agent.id) {
                            <option [value]="agent.id">{{ agent.name }}</option>
                          }
                        </select>
                        @if (conversationAgents().length === 0) {
                          <small class="form-text text-muted">No agents found. Check that the connection is signed in.</small>
                        }
                      }
                    </div>
                  </div>
                }
              </section>
            }
          </div>
        </div>

        <ng-template #scheduleSection>
          <section class="app-section-card" aria-labelledby="scheduleSettingsTitle">
            <h2 id="scheduleSettingsTitle" class="app-section-title">Update schedule</h2>
            <p class="app-section-description">Times when displays showing this dashboard refresh.</p>
            <div class="d-flex align-items-center gap-2 mb-3">
              <label class="visually-hidden" for="newUpdateTime">New update time</label>
              <input type="time" class="form-control update-time-input" id="newUpdateTime" name="newUpdateTime"
                [(ngModel)]="newUpdateTime" [ngModelOptions]="{standalone: true}"
                (keydown.enter)="$event.preventDefault(); addUpdateTime()" />
              <button type="button" class="btn btn-outline-primary" (click)="addUpdateTime()" [disabled]="!newUpdateTime">
                <i class="fa-solid fa-plus me-1" aria-hidden="true"></i>Add
              </button>
            </div>
            <div class="d-flex flex-wrap gap-2" aria-label="Update times">
              @for (time of updateTimes(); track time) {
                <span class="app-status-chip app-status-chip-info update-time-chip">
                  {{ time }}
                  <button type="button" class="btn-close" (click)="removeUpdateTime($index)" [attr.aria-label]="'Remove update time ' + time"></button>
                </span>
              } @empty {
                <span class="text-muted small">No update times yet.</span>
              }
            </div>
          </section>
        </ng-template>

      </form>

      <!-- Preview Modal -->
      <app-rendered-preview-modal
        title="Preview"
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
  private readonly playwrightComponentService = inject(PlaywrightComponentService);
  private readonly dataProviderService = inject(DataProviderService);

  readonly isAddonMode = this.authService.isAddonMode;

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
      // The add-on's default connection is the empty "Home Assistant add-on" option.
      next: connections => this.homeAssistantConnections.set(connections.filter(c => c.pluginId === HOME_ASSISTANT_PLUGIN_ID && !c.isDefault)),
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
        if (config.connectionMode === 'HomeAssistant' && dashboard.renderingMode !== 'HomeAssistant') {
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
      this.toastService.error('Enter a name for the dashboard.');
      return;
    }

    this.isSaving.set(true);

    const selectedSize = this.sizePresets[this.selectedSizeIndex] ?? DEFAULT_DASHBOARD_SIZE;
    const updatePayload: any = {
      name: formValue.name.trim(),
      description: formValue.description?.trim() || undefined,
      path: formValue.path?.trim() || undefined,
      updateTimes: this.updateTimes().length > 0 ? this.updateTimes() : undefined,
      // Only Home Assistant mode edits the connection.
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
        this.toastService.success('Dashboard saved.');
        this.isSaving.set(false);
      },
      error: (error) => {
        this.toastService.httpError(error, 'Could not save the dashboard.');
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
      // Outside the add-on, a named connection is required; start with a signed-in one.
      const currentDashboard = this.dashboard();
      if (!this.aiConfig().homeAssistantConnectionId && !this.isAddonMode()) {
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
    return !this.dashboardForm.controls.path.value || !this.dashboardForm.controls.homeAssistantConnectionId.value;
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
      this.toastService.error('Add widgets in the designer before previewing.');
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
      }
    });
  }

  private openHomeAssistantPreview(): void {
    const currentDashboard = this.dashboard();
    if (!currentDashboard) return;

    if (this.disablePreviewButton()) {
      this.toastService.error('Select a Home Assistant connection and dashboard before previewing.');
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
        this.toastService.error('Could not load the devices showing this dashboard.');
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
