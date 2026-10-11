import { Component, inject, OnInit, OnDestroy, signal, computed, ChangeDetectorRef, ViewChild, HostListener } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormGroup, FormControl, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { DashboardService } from '../../services/dashboard.service';
import { HomeAssistantService } from '../../services/home-assistant.service';
import { AuthService } from '../../services/auth.service';
import { ToastService } from '../../services/toast.service';
import { DialogService } from '../../services/dialog.service';
import { DeviceService, Device } from '../../services/device.service';
import { AiService } from '../../services/ai.service';
import { Dashboard, DashboardOrientation, DashboardSizePreset, DASHBOARD_SIZE_PRESETS, DEFAULT_DASHBOARD_SIZE, AiConfig, AiConnectionMode, ConversationAgent } from '../../models/types';
import { DashboardSelectorDialogComponent } from '../dashboard-selector-dialog/dashboard-selector-dialog.component';
import { RenderedPreviewModalComponent } from '../rendered-preview-modal/rendered-preview-modal.component';
import { HasUnsavedChanges } from '../../guards/unsaved-changes.guard';
import { PlaywrightComponentService, UserRenderingComponentStatus } from '../../services/playwright-component.service';

@Component({
  selector: 'app-dashboard-edit',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, RouterModule, DashboardSelectorDialogComponent, RenderedPreviewModalComponent],
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
    <app-dashboard-selector-dialog></app-dashboard-selector-dialog>

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
                @if (!hideHostField()) {
                  <div class="mb-3">
                    <label class="form-label fw-semibold" for="dashboardHost">Dashboard host</label>
                    <input 
                      id="dashboardHost"
                      type="text" 
                      class="form-control" 
                      formControlName="host"
                      placeholder="https://your-ha-instance.com" 
                    />
                  </div>
                }

                @if (!isAddonMode() || previewModeValue === 'homeassistant') {
                <div class="mb-3">
                  <label class="form-label fw-semibold" for="dashboardAccessToken">Access token</label>
                  @if (isAddonMode()) {
                    <small class="form-text text-muted d-block mb-2">
                      <i class="fa-solid fa-info-circle"></i> The "Home Assistant Dashboard" mode requires an Access Token.
                      Paste a <strong>Long-Lived Access Token</strong> below.
                    </small>
                  }
                  <div class="input-group">
                    <input 
                      id="dashboardAccessToken"
                      type="password" 
                      class="form-control" 
                      formControlName="accessToken"
                      [placeholder]="isAddonMode() ? 'Paste a long-lived access token…' : 'Paste a token or select Fetch…'"
                    />
                    @if (!isAddonMode()) {
                      <button 
                        type="button" 
                        class="btn btn-outline-primary" 
                        (click)="authenticateWithHomeAssistant()"
                        [disabled]="isAuthenticating()"
                        title="Authenticate via Home Assistant"
                      >
                        <i class="fa-solid fa-key" aria-hidden="true"></i> {{ isAuthenticating() ? 'Authenticating…' : 'Fetch' }}
                      </button>
                    }
                    @if (dashboard()?.hasAccessToken || dashboardForm.get('accessToken')?.value) {
                      <button 
                        type="button" 
                        class="btn btn-outline-danger" 
                        (click)="clearAccessToken()"
                        title="Clear the access token"
                      >
                        <i class="fa-solid fa-trash"></i> Clear
                      </button>
                    }
                  </div>
                  <small class="form-text text-muted d-block mt-2">
                    @if (dashboard()?.hasAccessToken) {
                      <span class="d-block mt-1 text-success">Token is configured</span>
                    }
                  </small>
                </div>
                } @else {
                  <div class="mb-3">
                    <span class="d-block text-success"><i class="fa-solid fa-check-circle"></i> Connected to Home Assistant via add-on</span>
                  </div>
                }

                <div class="mb-3">
                  <span class="form-label fw-semibold d-block">Rendering mode</span>
                  <div class="btn-group d-flex" role="group">
                    <input type="radio" class="btn-check" name="previewMode" id="ssrMode" value="ssr" [(ngModel)]="previewModeValue" [ngModelOptions]="{standalone: true}" (change)="onRenderingModeChange()" />
                    <label class="btn btn-outline-secondary flex-grow-1" for="ssrMode">Custom Layout</label>

                    <input type="radio" class="btn-check" name="previewMode" id="haMode" value="homeassistant" [(ngModel)]="previewModeValue" [ngModelOptions]="{standalone: true}" (change)="onRenderingModeChange()" />
                    <label class="btn btn-outline-secondary flex-grow-1" for="haMode">Home Assistant Dashboard</label>
                  </div>
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
                }

                @if (previewModeValue === 'ssr') {
                  <div class="mb-3">
                    <span class="form-label fw-semibold d-block">Layout</span>
                    <button type="button" class="btn btn-success w-100" (click)="openDesigner()">
                      <i class="fa-solid fa-paint-brush"></i> Open Layout Designer
                    </button>
                  </div>
                } @else {
                  <div class="mb-3">
                    <label class="form-label fw-semibold" for="pathField">Dashboard path</label>
                    <div class="input-group">
                      <input 
                        type="text" 
                        class="form-control" 
                        formControlName="path"
                        id="pathField"
                      />
                      <button 
                        type="button" 
                        class="btn btn-outline-secondary" 
                        (click)="openDashboardSelector()"
                      [disabled]="hideHostField() ? false : (!dashboardForm.get('host')?.value || (!dashboard()!.hasAccessToken && !dashboardForm.get('accessToken')?.value))"
                      >
                        <i class="fa-solid fa-list"></i> Select
                      </button>
                    </div>
                    <small class="form-text text-muted">Example: lovelace/0 or lovelace/energy</small>
                  </div>
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
                    <small class="form-text text-muted">No agents found. Make sure this dashboard is connected to Home Assistant.</small>
                  } @else {
                    <small class="form-text text-muted">Select a conversation agent or leave as default</small>
                  }
                }
              </div>
            }
          </div>
        </details>

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
  private readonly homeAssistantService = inject(HomeAssistantService);
  protected readonly authService = inject(AuthService);
  private readonly deviceService = inject(DeviceService);
  private readonly aiService = inject(AiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly http = inject(HttpClient);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly toastService = inject(ToastService);
  private readonly dialogService = inject(DialogService);
  private readonly location = inject(Location);
  private readonly playwrightComponentService = inject(PlaywrightComponentService);

  readonly isAddonMode = this.authService.isAddonMode;
  readonly isHostMode = this.authService.isHostMode;
  readonly hideHostField = computed(() => this.isAddonMode() || this.isHostMode());

  @ViewChild(DashboardSelectorDialogComponent) dashboardSelectorDialog!: DashboardSelectorDialogComponent;

  readonly dashboard = signal<Dashboard | null>(null);
  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly isAuthenticating = signal(false);
  readonly updateTimes = signal<string[]>([]);
  readonly showPreviewModal = signal(false);
  readonly previewLoading = signal(false);
  readonly previewError = signal('');
  readonly previewImageUrl = signal('');
  readonly shouldClearAccessToken = signal(false);
  readonly previewMode = signal<'ssr' | 'homeassistant'>('ssr');
  readonly renderingEnabled = signal(false);
  readonly playwrightStatus = signal<UserRenderingComponentStatus | null>(null);
  
  readonly devices = signal<Device[]>([]);
  readonly isLoadingDevices = signal(false);

  readonly aiConfig = signal<AiConfig>({ connectionMode: 'None' });
  readonly conversationAgents = signal<ConversationAgent[]>([]);
  readonly isLoadingAgents = signal(false);
  private originalAiConfig: AiConfig = { connectionMode: 'None' };

  readonly dashboardForm = new FormGroup({
    name: new FormControl('', { validators: Validators.required, nonNullable: true }),
    description: new FormControl('', { nonNullable: true }),
    host: new FormControl('', { nonNullable: true }),
    path: new FormControl('', { nonNullable: true }),
    accessToken: new FormControl('', { nonNullable: true }),
  });

  newUpdateTime: string = '';
  previewModeValue: 'ssr' | 'homeassistant' = 'ssr';
  orientationValue: DashboardOrientation = 'Landscape';
  sizePresets: DashboardSizePreset[] = DASHBOARD_SIZE_PRESETS;
  selectedSizeIndex = 0;
  private previewObjectUrl: string | null = null;
  private oauthProcessed = false;
  private oauthToken: string | null = null;
  private originalDashboard: Dashboard | null = null;
  private originalUpdateTimes: string[] = [];

  ngOnInit(): void {
    this.loadPlaywrightStatus();
    const id = this.route.snapshot.paramMap.get('id');

    if (id) {
      this.route.queryParams.subscribe(params => {
        if (params['access_token'] && params['auth_callback'] === 'true' && !this.oauthProcessed) {
          this.oauthProcessed = true;
          this.oauthToken = params['access_token'];
        }
      });

      this.loadDashboard(id);
    }
  }

  @HostListener('window:focus')
  onWindowFocus(): void {
    this.loadPlaywrightStatus();
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
          host: dashboard.host || '',
          path: dashboard.path || '',
          accessToken: '',
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

        if (this.oauthToken) {
          this.saveOAuthToken(id);
        }

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

  private saveOAuthToken(dashboardId: string): void {
    if (!this.oauthToken) {
      return;
    }

    const updatePayload: any = {
      accessToken: this.oauthToken
    };

    const hostValue = this.dashboardForm.get('host')?.value;
    if (hostValue) {
      updatePayload.host = hostValue;
    }

    this.dashboardService.updateDashboard(dashboardId, updatePayload).subscribe({
      next: (updated) => {
        this.dashboard.set(updated);
        this.toastService.success('Home Assistant token saved successfully!');

        setTimeout(() => {
          this.location.replaceState(`/dashboards/${dashboardId}/edit`);
        }, 500);

        this.oauthToken = null;
      },
      error: (error) => {
        this.toastService.error('Failed to save Home Assistant token. Please try again.');
        this.oauthToken = null;
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

  authenticateWithHomeAssistant(): void {
    if (this.isAddonMode()) {
      this.toastService.error('Token fetching is not available in add-on mode. Please paste a Long-Lived Access Token manually.');
      return;
    }

    const currentDashboard = this.dashboard();
    if (!currentDashboard) {
      return;
    }

    const hostValue = this.dashboardForm.get('host')?.value;
    if (!this.hideHostField() && !hostValue) {
      this.toastService.error('Please enter Home Assistant host first.');
      return;
    }

    this.isAuthenticating.set(true);

    const host = this.hideHostField() ? window.location.origin : (hostValue || '');
    this.homeAssistantService.startAuth(host, currentDashboard.id).subscribe({
      next: (response: any) => {
        if (response.directAuth) {
          this.isAuthenticating.set(false);
          this.loadDashboard(currentDashboard.id);
          this.toastService.success('Access token fetched successfully!');
        } else {
          this.isAuthenticating.set(false);
          window.location.href = response.authUrl;
        }
      },
      error: (error) => {
        this.isAuthenticating.set(false);
        const errorMsg = 'Failed to start authentication: ' + (error.error?.error || error.error?.message || 'Unknown error');
        this.toastService.error(errorMsg);
      }
    });
  }

  openDashboardSelector(): void {
    const currentDashboard = this.dashboard();
    if (!currentDashboard) {
      this.toastService.error('Dashboard not loaded.');
      return;
    }

    if (!this.hideHostField()) {
      const hostValue = this.dashboardForm.get('host')?.value;
      const accessTokenValue = this.dashboardForm.get('accessToken')?.value;
      if (!hostValue || (!currentDashboard.hasAccessToken && !accessTokenValue)) {
        this.toastService.error('Please configure host and access token first.');
        return;
      }
    }

    this.dashboardSelectorDialog.openWithLoading();

    this.homeAssistantService.getDashboards(currentDashboard.id)
      .subscribe({
        next: (dashboards) => {
          const transformedDashboards = dashboards.map((item: any) => ({
            url_path: item.id,
            title: item.title,
            id: item.id
          }));

          this.dashboardSelectorDialog.open(transformedDashboards).then((selectedPath) => {
            if (selectedPath) {
              this.dashboardForm.patchValue({ path: selectedPath });
              this.dashboardForm.markAsDirty();
            }
          });
        },
        error: (error) => {
          this.dashboardSelectorDialog.setError(error.error?.message || 'Failed to fetch dashboards');
        }
      });
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
      host: formValue.host?.trim() || undefined,
      path: formValue.path?.trim() || undefined,
      updateTimes: this.updateTimes().length > 0 ? this.updateTimes() : undefined,
      renderingMode: this.previewModeValue === 'homeassistant' ? 'HomeAssistant' : 'Custom',
      orientation: this.orientationValue,
      screenWidth: selectedSize.width,
      screenHeight: selectedSize.height,
      aiConfig: this.aiConfig(),
    };

    if (formValue.accessToken?.trim().length > 0) {
      updatePayload.accessToken = formValue.accessToken;
    }

    if (this.shouldClearAccessToken()) {
      updatePayload.clearAccessToken = true;
      this.shouldClearAccessToken.set(false);
    }

    this.dashboardService.updateDashboard(currentDashboard.id, updatePayload).subscribe({
      next: (updated) => {
        this.dashboard.set(updated);
        this.dashboardForm.patchValue({
          name: updated.name || '',
          description: updated.description || '',
          host: updated.host || '',
          path: updated.path || '',
          accessToken: ''
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
      host: dashboard.host || '',
      path: dashboard.path || '',
      accessToken: '',
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

    this.shouldClearAccessToken.set(false);
  }

  openDesigner(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.router.navigate(['/dashboards', id, 'designer']);
    }
  }

  async clearAccessToken(): Promise<void> {
    await this.dialogService.confirm({
      title: 'Clear Access Token',
      message: 'Are you sure you want to clear the Home Assistant access token? The dashboard will no longer be able to render until you authenticate again.',
      confirmLabel: 'Clear token',
      isDangerous: true,
      onConfirm: () => {
        this.shouldClearAccessToken.set(true);
        this.dashboardForm.markAsDirty();
        this.onSubmit();
      }
    });
  }

  onRenderingModeChange(): void {
    this.dashboardForm.markAsDirty();
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
      const currentDashboard = this.dashboard();
      if (currentDashboard) {
        this.loadConversationAgents(currentDashboard.id);
      }
    }
  }

  updateAiField(field: keyof AiConfig, value: string): void {
    this.aiConfig.update(c => ({ ...c, [field]: value }));
    this.dashboardForm.markAsDirty();
  }

  private loadConversationAgents(dashboardId: string): void {
    this.isLoadingAgents.set(true);
    this.aiService.getConversationAgents(dashboardId).subscribe({
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
    } else {
      const pathValue = this.dashboardForm.get('path')?.value;
      const currentDashboard = this.dashboard();
      const accessTokenValue = this.dashboardForm.get('accessToken')?.value;
      // In addon mode, host is auto-managed but HA rendering still needs a long-lived token
      if (this.isAddonMode()) {
        return !pathValue || (!currentDashboard?.hasAccessToken && !accessTokenValue);
      }
      const hostValue = this.dashboardForm.get('host')?.value;
      return !hostValue || !pathValue || (!currentDashboard?.hasAccessToken && !accessTokenValue);
    }
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

    const hostValue = this.dashboardForm.get('host')?.value;
    const pathValue = this.dashboardForm.get('path')?.value;
    const accessTokenValue = this.dashboardForm.get('accessToken')?.value;

    if (this.isAddonMode()) {
      if (!pathValue || (!currentDashboard.hasAccessToken && !accessTokenValue)) {
        this.toastService.error('Preview requires a Dashboard Path and an Access Token. Click "Fetch" to authenticate with Home Assistant.');
        return;
      }
    } else if (!hostValue || !pathValue || (!currentDashboard.hasAccessToken && !accessTokenValue)) {
      this.toastService.error('Preview requires Home Assistant configuration and access token. Please configure Host, Dashboard Path, and add an access token first.');
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
        const dashboard = this.dashboard();
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
        } else if (error.status === 404) {
          const configDebug = dashboard ? `Host: ${dashboard.host || 'MISSING'}, Path: ${dashboard.path || 'MISSING'}, Token: ${dashboard.hasAccessToken ? 'SET' : 'MISSING'}` : 'No dashboard';
          errorMessage = `404 Not Found. Config: [${configDebug}] - Make sure Home Assistant settings are complete and saved.`;
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
