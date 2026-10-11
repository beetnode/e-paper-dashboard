import { Component, computed, effect, inject, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { marked } from 'marked';
import { firstValueFrom } from 'rxjs';
import { DeviceService, Device } from '../../services/device.service';
import { DashboardService } from '../../services/dashboard.service';
import { FirmwareService } from '../../services/firmware.service';
import { AuthService } from '../../services/auth.service';
import { DialogService } from '../../services/dialog.service';
import { ToastService } from '../../services/toast.service';
import { ClipboardService } from '../../services/clipboard.service';
import { SearchableSelectComponent, SelectOption } from '../searchable-select/searchable-select.component';
import { Dashboard } from '../../models/types';

@Component({
  selector: 'app-device-list',
  standalone: true,
  imports: [CommonModule, SearchableSelectComponent],
  styles: [`
    .device-assignment {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
      margin-top: 0.75rem;
    }

    .dashboard-select {
      flex: 0 1 18rem;
      min-width: 12rem;
    }

    .pairing-steps {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 0.75rem;
      counter-reset: pairing-step;
    }

    .pairing-step {
      position: relative;
      padding-left: 2.25rem;
      min-height: 2rem;
      color: var(--bs-secondary-color);
      font-size: 0.875rem;
    }

    .pairing-step::before {
      counter-increment: pairing-step;
      content: counter(pairing-step);
      position: absolute;
      left: 0;
      display: grid;
      place-items: center;
      width: 1.65rem;
      height: 1.65rem;
      border-radius: 50%;
      color: var(--bs-primary-text-emphasis);
      background: var(--bs-primary-bg-subtle);
      font-weight: 700;
    }

    @media (max-width: 768px) {
      .pairing-steps {
        grid-template-columns: 1fr;
      }

      .dashboard-select {
        flex-basis: 100%;
      }
    }

    .release-notes {
      max-height: 200px;
      overflow-y: auto;
      color: var(--bs-secondary-color);
    }

    .release-notes :first-child {
      margin-top: 0;
    }

    .release-notes :last-child {
      margin-bottom: 0;
    }

    .release-notes h1, .release-notes h2, .release-notes h3 {
      font-size: 0.95rem;
      font-weight: 600;
      margin: 0.5rem 0 0.25rem;
    }

    .release-notes p {
      margin: 0.25rem 0;
    }

    .release-notes ul, .release-notes ol {
      margin: 0.25rem 0;
      padding-left: 1.25rem;
    }

    .release-notes code {
      font-size: 0.8rem;
      padding: 0.1rem 0.3rem;
      background: var(--bs-secondary-bg);
      border-radius: 0.2rem;
    }

    .release-notes a {
      color: var(--bs-primary);
    }

    .release-notes-summary {
      cursor: pointer;
    }
  `],
  template: `
    <div class="app-page">
    <div class="app-page-header">
      <div>
        <h1 class="app-page-title">Devices</h1>
        <p class="app-page-description">Pair displays, assign compatible dashboards, and check firmware status.</p>
      </div>
      @if (!isPairingActive()) {
        <button type="button" class="btn btn-primary" (click)="startPairing()">
          <i class="fa-solid fa-plus" aria-hidden="true"></i>
          <span class="d-none d-sm-inline">Pair device</span>
          <span class="visually-hidden d-sm-none">Pair device</span>
        </button>
      }
    </div>

    @if (isPairingActive()) {
      <section class="app-section-card mb-4" aria-labelledby="pairDeviceTitle">
        <div class="d-flex flex-column gap-2">
          <div class="app-section-header mb-0">
            <div>
              <h2 id="pairDeviceTitle" class="app-section-title">Pair a new device</h2>
              <p class="app-section-description">
                {{ pairingMode() === 'device'
                  ? 'The display creates the claim code. Keep this page open while you configure it.'
                  : 'Legacy pairing for firmware older than 0.4.0.' }}
              </p>
            </div>
            <button type="button" class="btn btn-sm btn-outline-secondary" (click)="cancelPairing()">
              <i class="fa-solid fa-xmark me-1" aria-hidden="true"></i>Cancel
            </button>
          </div>

          @if (pairingMode() === 'device') {
            <div class="pairing-steps mt-3">
              <div class="pairing-step">
                Copy the server URL before leaving this network:
                @if (serverUrl()) {
                  <code>{{ serverUrl() }}</code>
                } @else {
                  <span class="text-danger">CLIENT_URL is not configured</span>
                }
                <button type="button" class="btn btn-sm btn-link p-1" (click)="copyServerUrl()" aria-label="Copy server URL">
                  <i class="fa-solid" [ngClass]="serverUrlCopied() ? 'fa-check' : 'fa-copy'" aria-hidden="true"></i>
                </button>
              </div>
              <div class="pairing-step">Scan the Wi-Fi QR on the display, open its setup page, then enter your home Wi-Fi and paste the server URL. Submit once.</div>
              <div class="pairing-step">
                After submitting, reconnect to any network that can reach this dashboard. Enter the display code, then claim it.
                <div class="input-group mt-2">
                  <input type="text" class="form-control font-monospace text-uppercase"
                    aria-label="Device claim code" placeholder="ABC123" maxlength="6"
                    autocomplete="one-time-code" [value]="pairingCode()" (input)="updateClaimCode($event)"
                    [disabled]="isAwaitingDevice()">
                  <button type="button" class="btn btn-primary" (click)="claimDevice()"
                    [disabled]="pairingCode().length !== 6 || isClaiming() || isAwaitingDevice() || !serverUrl()">
                    {{ isClaiming() ? 'Claiming…' : isAwaitingDevice() ? 'Waiting for display…' : 'Claim device' }}
                  </button>
                </div>
                @if (isAwaitingDevice()) {
                  <div class="small mt-2" role="status">
                    <i class="fa-solid fa-spinner fa-spin me-1" aria-hidden="true"></i>
                    Claim accepted. Waiting for the display to confirm receipt ({{ formattedPairingTime() }}).
                  </div>
                }
              </div>
            </div>
            <button type="button" class="btn btn-sm btn-link align-self-start px-0" (click)="startLegacyPairing()">
              Pair a display running firmware older than 0.4.0
            </button>
          } @else {
            <div class="alert alert-warning mb-2" role="status">
              Enter code <strong class="font-monospace fs-5">{{ pairingCode() }}</strong> in the display setup portal.
              <button type="button" class="btn btn-sm btn-link" (click)="copyPairingCode()" aria-label="Copy legacy pairing code">
                <i class="fa-solid" [ngClass]="pairingCodeCopied() ? 'fa-check' : 'fa-copy'" aria-hidden="true"></i>
              </button>
              The code expires in {{ formattedPairingTime() }}.
            </div>
            <div class="pairing-steps">
              <div class="pairing-step">Copy server URL <code>{{ serverUrl() }}</code> and pairing code <strong>{{ pairingCode() }}</strong>.</div>
              <div class="pairing-step">Connect to the display setup Wi-Fi and enter home Wi-Fi, the server URL, and pairing code.</div>
              <div class="pairing-step"><i class="fa-solid fa-spinner fa-spin me-1" aria-hidden="true"></i>Waiting for the display to register.</div>
            </div>
            <button type="button" class="btn btn-sm btn-link align-self-start px-0" (click)="useCurrentPairing()">
              Back to current pairing
            </button>
          }
        </div>
      </section>
    }

    @if (isLoading()) {
      <div class="app-loading-state">
        <div class="spinner-border" role="status">
          <span class="visually-hidden">Loading devices</span>
        </div>
      </div>
    } @else if (loadError()) {
      <div class="app-empty-state" role="alert">
        <span class="app-empty-state-icon text-danger"><i class="fa-solid fa-triangle-exclamation" aria-hidden="true"></i></span>
        <div><h2 class="h5 mb-1">Devices could not be loaded</h2><p class="text-muted mb-0">{{ loadError() }}</p></div>
        <button type="button" class="btn btn-outline-primary" (click)="loadDevices()">
          <i class="fa-solid fa-rotate-right me-1" aria-hidden="true"></i>Try again
        </button>
      </div>
    } @else if (devices().length > 0) {
      @if (dashboardsLoadError()) {
        <div class="alert alert-warning d-flex flex-wrap align-items-center justify-content-between gap-2" role="alert">
          <span><i class="fa-solid fa-triangle-exclamation me-1" aria-hidden="true"></i>{{ dashboardsLoadError() }}</span>
          <button type="button" class="btn btn-sm btn-outline-warning" (click)="loadDashboards()">Retry dashboards</button>
        </div>
      }
      <div class="app-list">
        @for (device of devices(); track device.id) {
          <article class="app-list-item">
            <div style="min-width: 0">
              <div class="app-list-item-heading">
                <h2 class="app-list-item-title">{{ device.name }}</h2>
                <span class="app-status-chip" [ngClass]="isDeviceOnline(device) ? 'app-status-chip-success' : 'app-status-chip-muted'">
                  <i class="fa-solid fa-circle" aria-hidden="true"></i>{{ isDeviceOnline(device) ? 'Online' : 'Offline' }}
                </span>
                @if (hasFirmwareUpdate(device)) {
                  <span class="app-status-chip app-status-chip-warning"><i class="fa-solid fa-arrow-up" aria-hidden="true"></i>Update available</span>
                }
              </div>
              <div class="app-list-item-meta">
                @if (device.lastSeenAt) {
                  <span><i class="fa-regular fa-clock" aria-hidden="true"></i>Last seen {{ device.lastSeenAt | date:'short' }}</span>
                }
                @if (device.screenWidth && device.screenHeight) {
                  <span><i class="fa-solid fa-expand" aria-hidden="true"></i>{{ device.screenWidth }}×{{ device.screenHeight }}</span>
                }
                @if (device.firmwareVersion) {
                  <span><i class="fa-solid fa-microchip" aria-hidden="true"></i>Firmware v{{ device.firmwareVersion }}</span>
                }
              </div>
            </div>
            <div class="app-list-item-actions">
              <button type="button" class="btn btn-outline-danger app-icon-button" (click)="removeDevice(device)" [attr.aria-label]="'Remove ' + device.name" title="Remove">
                <i class="fa-solid fa-trash" aria-hidden="true"></i>
              </button>
            </div>
            <div class="app-list-item-body">
              <div class="device-assignment">
                <app-searchable-select
                  class="dashboard-select"
                  [options]="compatibleDashboardOptions(device)"
                  [value]="device.dashboardId || ''"
                  emptyLabel="No dashboard"
                  searchPlaceholder="Search dashboards…"
                  [ariaLabel]="'Dashboard shown on ' + device.name"
                  [disabled]="isAssigningDashboard(device.id)"
                  (selectionChange)="assignDashboard(device, $event)"
                ></app-searchable-select>
                @if (isAssigningDashboard(device.id)) {
                  <span class="small text-muted" role="status"><span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>Saving</span>
                }
              </div>
              <details class="mt-2 small text-muted">
                <summary>Technical details</summary>
                <div class="pt-2"><i class="fa-solid fa-fingerprint me-1" aria-hidden="true"></i><code>{{ device.deviceIdentifier }}</code></div>
                <div><i class="fa-solid fa-calendar me-1" aria-hidden="true"></i>Paired {{ device.pairedAt | date:'mediumDate' }}</div>
              </details>
            </div>
          </article>
        }
      </div>
    } @else {
      <div class="app-empty-state">
        <span class="app-empty-state-icon"><i class="fa-solid fa-tablet-screen-button" aria-hidden="true"></i></span>
        <div><h2 class="h5 mb-1">Pair your first display</h2><p class="text-muted mb-0">Pair an e-paper device, then assign a compatible dashboard.</p></div>
        <button type="button" class="btn btn-primary" (click)="startPairing()"><i class="fa-solid fa-plus me-1" aria-hidden="true"></i> Pair device</button>
      </div>
    }

    @if (devices().length > 0) {
      <section class="app-section-card mt-4" aria-labelledby="firmwareTitle">
        <div class="app-section-header">
          <div class="d-flex flex-wrap align-items-center gap-2">
            <h2 id="firmwareTitle" class="app-section-title mb-0">Firmware</h2>
            @if (firmwareInfo()?.version) {
              <span class="app-status-chip app-status-chip-info">Latest v{{ firmwareInfo()!.version }}</span>
            }
          </div>
          <button type="button" class="btn btn-sm btn-outline-secondary" (click)="refreshFirmware()" [disabled]="isFirmwareLoading()">
            <i class="fa-solid fa-rotate me-1" [class.fa-spin]="isFirmwareLoading()" aria-hidden="true"></i>{{ isFirmwareLoading() ? 'Checking…' : 'Check for updates' }}
          </button>
        </div>
        @if (firmwareError()) {
          <p class="small text-danger mb-0"><i class="fa-solid fa-circle-exclamation me-1" aria-hidden="true"></i>{{ firmwareError() }}</p>
        } @else if (firmwareInfo(); as info) {
          @if (info.version) {
            <div class="small">
              @if (deviceUpdateSummary(); as summary) {
                @if (summary.upToDate === summary.total) {
                  <span class="text-success"><i class="fa-solid fa-check-double me-1" aria-hidden="true"></i>All devices are up to date.</span>
                } @else {
                  <span>{{ summary.upToDate }} of {{ summary.total }} devices on the latest version</span>
                  @if (summary.unknown > 0) {
                    <span class="text-muted"> · {{ summary.unknown }} not reporting a version</span>
                  }
                }
              }
              @if (info.publishedAt) {
                <span class="text-muted"> · Released {{ info.publishedAt | date:'mediumDate' }}</span>
              }
              @if (!info.hasDownload) {
                <div class="text-muted mt-1">No firmware binary is published for over-the-air updates yet.</div>
              }
            </div>
            @if (info.releaseNotes) {
              <details class="mt-2">
                <summary class="release-notes-summary small text-muted">Release notes</summary>
                <div class="release-notes small mt-1" [innerHTML]="renderMarkdown(info.releaseNotes)"></div>
              </details>
            }
          } @else {
            <p class="small text-muted mb-0">{{ info.message || 'No firmware release information available.' }}</p>
          }
        }
      </section>
    }
    </div>
  `
})
export class DeviceListComponent implements OnDestroy {
  private readonly deviceService = inject(DeviceService);
  private readonly dashboardService = inject(DashboardService);
  private readonly firmwareService = inject(FirmwareService);
  private readonly authService = inject(AuthService);
  private readonly dialogService = inject(DialogService);
  private readonly toastService = inject(ToastService);
  private readonly clipboardService = inject(ClipboardService);

  readonly devices = signal<Device[]>([]);
  readonly dashboards = signal<Dashboard[]>([]);
  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly dashboardsLoadError = signal('');
  readonly assigningDashboardIds = signal<Set<string>>(new Set());
  readonly serverUrl = signal('');
  private hasLoaded = false;

  readonly isPairingActive = signal(false);
  readonly pairingCode = signal('');
  readonly pairingMode = signal<'device' | 'legacy'>('device');
  readonly isClaiming = signal(false);
  readonly isAwaitingDevice = signal(false);
  readonly pairingCodeCopied = signal(false);
  readonly pairingTimeRemaining = signal(0);
  readonly serverUrlCopied = signal(false);

  readonly firmwareInfo = this.firmwareService.firmwareInfo;
  readonly isFirmwareLoading = this.firmwareService.isLoading;
  readonly firmwareError = this.firmwareService.error;

  readonly dashboardOptions = computed<SelectOption[]>(() =>
    this.dashboards().map(d => ({ value: d.id, label: d.name }))
  );

  compatibleDashboardOptions(device: Device): SelectOption[] {
    const allDashboards = this.dashboards();
    if (!device.screenWidth || !device.screenHeight) {
      return allDashboards.map(d => ({ value: d.id, label: d.name }));
    }
    return allDashboards
      .filter(d => d.screenWidth === device.screenWidth && d.screenHeight === device.screenHeight)
      .map(d => ({ value: d.id, label: d.name }));
  }

  readonly deviceUpdateSummary = computed(() => {
    const fw = this.firmwareInfo();
    const devs = this.devices();
    if (!fw?.version || devs.length === 0) return null;
    const latest = fw.version;
    const upToDate = devs.filter(d => d.firmwareVersion && !this.isVersionLower(d.firmwareVersion, latest)).length;
    const outdated = devs.filter(d => d.firmwareVersion && this.isVersionLower(d.firmwareVersion, latest)).length;
    const unknown = devs.filter(d => !d.firmwareVersion).length;
    return { total: devs.length, upToDate, outdated, unknown, latest };
  });

  private pairingTimer: ReturnType<typeof setInterval> | null = null;
  private pairingStatusTimer: ReturnType<typeof setInterval> | null = null;
  private pairingExpiresAt: Date | null = null;

  constructor() {
    effect(() => {
      if (!this.hasLoaded && this.authService.isAuthReady()) {
        this.hasLoaded = true;
        this.loadData();
      }
    });
  }

  private loadData(): void {
    this.loadDevices();
    this.loadDashboards();
    this.firmwareService.loadLatestFirmware();
    this.loadPairingConfiguration();
  }

  private loadPairingConfiguration(): void {
    this.deviceService.getPairingConfiguration().subscribe({
      next: (configuration) => this.serverUrl.set(configuration.clientUrl),
      error: () => this.serverUrl.set('')
    });
  }

  loadDevices(): void {
    this.isLoading.set(true);
    this.loadError.set('');
    this.deviceService.getDevices().subscribe({
      next: (devices) => {
        this.devices.set(devices);
        this.isLoading.set(false);
      },
      error: () => {
        this.loadError.set('Check the server connection and try again.');
        this.isLoading.set(false);
      }
    });
  }

  loadDashboards(): void {
    this.dashboardsLoadError.set('');
    this.dashboardService.getDashboards().subscribe({
      next: (dashboards) => {
        this.dashboards.set(dashboards);
      },
      error: () => {
        this.dashboardsLoadError.set('Dashboard assignments are temporarily unavailable.');
      }
    });
  }

  assignDashboard(device: Device, dashboardId: string): void {
    this.assigningDashboardIds.update(ids => new Set(ids).add(device.id));
    this.deviceService.updateDevice(device.id, {
      dashboardId: dashboardId
    }).subscribe({
      next: (updated) => {
        this.setDashboardAssignmentComplete(device.id);
        this.devices.update(list =>
          list.map(d => d.id === device.id ? updated : d)
        );
        if (dashboardId) {
          const dashboard = this.dashboards().find(d => d.id === dashboardId);
          this.toastService.success(`${device.name} now shows ${dashboard?.name ?? 'the dashboard'}.`);
        } else {
          this.toastService.success(`${device.name} no longer shows a dashboard.`);
        }
      },
      error: (err) => {
        this.setDashboardAssignmentComplete(device.id);
        this.toastService.httpError(err, 'Could not change the dashboard on this device.');
      }
    });
  }

  isAssigningDashboard(deviceId: string): boolean {
    return this.assigningDashboardIds().has(deviceId);
  }

  isDeviceOnline(device: Device): boolean {
    if (!device.lastSeenAt) return false;
    return Date.now() - new Date(device.lastSeenAt).getTime() < 30 * 60 * 1000;
  }

  hasFirmwareUpdate(device: Device): boolean {
    const latest = this.firmwareInfo()?.version;
    return !!device.firmwareVersion && !!latest && this.isVersionLower(device.firmwareVersion, latest);
  }

  formattedPairingTime(): string {
    const seconds = this.pairingTimeRemaining();
    const minutes = Math.floor(seconds / 60);
    return `${minutes}:${String(seconds % 60).padStart(2, '0')}`;
  }

  private setDashboardAssignmentComplete(deviceId: string): void {
    this.assigningDashboardIds.update(ids => {
      const next = new Set(ids);
      next.delete(deviceId);
      return next;
    });
  }

  removeDevice(device: Device): void {
    this.dialogService.confirm({
      title: 'Remove Device',
      message: `Are you sure you want to remove device "${device.name}"? The device will need to be paired again.`,
      confirmLabel: 'Remove',
      isDangerous: true,
      onConfirm: async () => {
        try {
          await firstValueFrom(this.deviceService.deleteDevice(device.id));
          this.devices.update(list => list.filter(d => d.id !== device.id));
          this.toastService.success('Device removed.');
        } catch (error: any) {
          this.toastService.httpError(error, 'Could not remove the device.');
        }
      }
    });
  }

  startPairing(): void {
    this.stopPairingTimers();
    this.pairingMode.set('device');
    this.pairingCode.set('');
    this.isAwaitingDevice.set(false);
    this.isPairingActive.set(true);
    if (!this.serverUrl()) this.loadPairingConfiguration();
  }

  cancelPairing(): void {
    this.stopPairingTimers();
    this.isPairingActive.set(false);
    this.pairingCode.set('');
    this.isAwaitingDevice.set(false);
    this.pairingMode.set('device');
    this.pairingCodeCopied.set(false);
    this.serverUrlCopied.set(false);
  }

  startLegacyPairing(): void {
    this.deviceService.startPairing().subscribe({
      next: (response) => {
        this.stopPairingTimers();
        this.pairingMode.set('legacy');
        this.pairingCode.set(response.code);
        this.pairingExpiresAt = new Date(response.expiresAt);
        this.startLegacyPairingTimers();
      },
      error: err => this.toastService.httpError(err, 'Could not start legacy pairing.')
    });
  }

  useCurrentPairing(): void {
    this.stopPairingTimers();
    this.pairingMode.set('device');
    this.pairingCode.set('');
  }

  async copyPairingCode(): Promise<void> {
    const code = this.pairingCode();
    if (!code) return;

    const success = await this.clipboardService.copy(code);
    if (success) {
      this.pairingCodeCopied.set(true);
      setTimeout(() => this.pairingCodeCopied.set(false), 2000);
    }
  }

  updateClaimCode(event: Event): void {
    const input = event.target as HTMLInputElement;
    const code = input.value.toUpperCase().replace(/[^0-9A-Z]/g, '').slice(0, 6);
    input.value = code;
    this.pairingCode.set(code);
  }

  claimDevice(): void {
    const code = this.pairingCode();
    if (code.length !== 6 || this.isClaiming()) return;

    this.isClaiming.set(true);
    this.deviceService.claimDevice(code).subscribe({
      next: (response) => {
        this.isClaiming.set(false);
        this.isAwaitingDevice.set(true);
        this.startDeviceAcknowledgementPolling(code, response.acknowledgementExpiresAt);
      },
      error: (err) => {
        this.isClaiming.set(false);
        this.toastService.httpError(err, 'The claim code is invalid, expired, or not announced yet.');
      }
    });
  }

  async copyServerUrl(): Promise<void> {
    const success = await this.clipboardService.copy(this.serverUrl());
    if (success) {
      this.serverUrlCopied.set(true);
      setTimeout(() => this.serverUrlCopied.set(false), 2000);
    }
  }

  refreshFirmware(): void {
    this.firmwareService.refreshFirmwareCheck();
  }

  renderMarkdown(text: string): string {
    return marked(text) as string;
  }

  isVersionLower(deviceVersion: string, latestVersion: string): boolean {
    const parse = (v: string) => v.split('.').map(n => parseInt(n, 10) || 0);
    const [dMaj, dMin, dPat] = parse(deviceVersion);
    const [lMaj, lMin, lPat] = parse(latestVersion);
    if (dMaj !== lMaj) return dMaj < lMaj;
    if (dMin !== lMin) return dMin < lMin;
    return dPat < lPat;
  }

  private startLegacyPairingTimers(): void {
    this.updatePairingTime();
    this.pairingTimer = setInterval(() => {
      this.updatePairingTime();
      if (this.pairingTimeRemaining() <= 0) {
        this.cancelPairing();
        this.toastService.info('Legacy pairing code expired.');
      }
    }, 1000);
    this.pairingStatusTimer = setInterval(() => {
      const code = this.pairingCode();
      if (!code) return;
      this.deviceService.getPairingStatus(code).subscribe({
        next: (response) => {
          if (response.status === 'completed') {
            this.cancelPairing();
            this.loadDevices();
            this.toastService.success('Device paired.');
          }
        }
      });
    }, 2000);
  }

  private startDeviceAcknowledgementPolling(code: string, acknowledgementExpiresAt: string): void {
    this.stopPairingTimers();
    this.pairingExpiresAt = new Date(acknowledgementExpiresAt);
    this.updatePairingTime();

    const poll = () => {
      this.deviceService.getPairingStatus(code).subscribe({
        next: (response) => {
          if (!this.isAwaitingDevice()) return;
          this.pairingExpiresAt = new Date(response.expiresAt);
          if (response.status === 'completed') {
            this.cancelPairing();
            this.loadDevices();
            this.toastService.success('Device paired.');
          }
        },
        error: (err) => {
          if (!this.isAwaitingDevice()) return;
          if (err.status === 404 || err.status === 410) {
            this.cancelPairing();
            this.toastService.error('The display did not confirm pairing in time. Start pairing again.');
          }
        }
      });
    };

    poll();
    this.pairingStatusTimer = setInterval(poll, 2000);
    this.pairingTimer = setInterval(() => {
      this.updatePairingTime();
      if (this.pairingTimeRemaining() <= 0) {
        this.cancelPairing();
        this.toastService.error('The display did not confirm pairing in time. Start pairing again.');
      }
    }, 1000);
  }

  private updatePairingTime(): void {
    const remaining = this.pairingExpiresAt
      ? Math.max(0, Math.floor((this.pairingExpiresAt.getTime() - Date.now()) / 1000))
      : 0;
    this.pairingTimeRemaining.set(remaining);
  }

  private stopPairingTimers(): void {
    if (this.pairingTimer) clearInterval(this.pairingTimer);
    if (this.pairingStatusTimer) clearInterval(this.pairingStatusTimer);
    this.pairingTimer = null;
    this.pairingStatusTimer = null;
    this.pairingExpiresAt = null;
  }

  ngOnDestroy(): void {
    this.stopPairingTimers();
  }
}
