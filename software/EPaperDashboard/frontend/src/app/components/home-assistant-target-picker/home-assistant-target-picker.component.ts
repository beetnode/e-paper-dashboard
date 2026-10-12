import { Component, EventEmitter, HostListener, Input, OnInit, Output, ViewChild, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { DataProviderService, ProviderConnection } from '../../services/data-provider.service';
import { HomeAssistantService } from '../../services/home-assistant.service';
import { DashboardSelectorDialogComponent } from '../dashboard-selector-dialog/dashboard-selector-dialog.component';

export const HOME_ASSISTANT_PLUGIN_ID = 'home-assistant';

/**
 * Picks the Home Assistant page rendered by "Home Assistant dashboard" mode: a named connection from
 * Connections and a dashboard path.
 * The connection list reloads when the window regains focus, so connections added in another tab appear here.
 */
@Component({
  selector: 'app-home-assistant-target-picker',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, DashboardSelectorDialogComponent],
  template: `
    <app-dashboard-selector-dialog></app-dashboard-selector-dialog>
    <div class="mb-3">
      <label class="form-label fw-semibold" for="haConnection">Home Assistant connection</label>
      <div class="input-group">
        <select id="haConnection" class="form-select" [ngModel]="connectionId" (ngModelChange)="selectConnection($event)" [disabled]="disabled">
          @if (!connectionId) {
            <option value="" disabled>{{ loading() ? 'Loading connections…' : 'Select a connection' }}</option>
          }
          @for (connection of connections(); track connection.id) {
            <option [value]="connection.id">{{ connection.name }}{{ connection.connected ? '' : ' (sign-in required)' }}</option>
          }
          @if (isSelectedMissing()) {
            <option [value]="connectionId">Unavailable connection</option>
          }
        </select>
        <button type="button" class="btn btn-outline-secondary" (click)="refresh()" [disabled]="loading()"
          title="Reload connections" aria-label="Reload connections">
          <i class="fa-solid fa-rotate" [class.fa-spin]="loading()" aria-hidden="true"></i>
        </button>
      </div>
      <small class="form-text text-muted d-block">
        <a routerLink="/connections" target="_blank" rel="noopener">Manage connections</a> in a new tab; this list refreshes when you return.
      </small>
      @if (isSelectedMissing()) {
        <div class="alert alert-warning small mt-2 mb-0" role="alert">The selected connection was removed. Choose another connection.</div>
      } @else if (isSelectedUnauthorized()) {
        <div class="alert alert-warning small mt-2 mb-0" role="alert">
          This connection has no access token yet. <a routerLink="/connections" target="_blank" rel="noopener" class="alert-link">Sign in or add a token</a>, then come back to this tab.
        </div>
      } @else if (!loading() && connections().length === 0) {
        <div class="alert alert-info small mt-2 mb-0" role="status">
          Rendering needs a Home Assistant connection with a long-lived access token. <a routerLink="/connections" target="_blank" rel="noopener" class="alert-link">Add one</a>, then come back to this tab.
        </div>
      }
    </div>
    <div class="mb-3">
      <label class="form-label fw-semibold" for="haPath">Dashboard path</label>
      <div class="input-group">
        <input id="haPath" type="text" class="form-control" [ngModel]="path" (ngModelChange)="pathChange.emit($event)"
          [disabled]="disabled" placeholder="lovelace/0" autocomplete="off" />
        <button type="button" class="btn btn-outline-secondary" (click)="browse()" [disabled]="disabled || !canBrowse()">
          <i class="fa-solid fa-list" aria-hidden="true"></i> Select
        </button>
      </div>
      <small class="form-text text-muted">Example: lovelace/0 or lovelace/energy</small>
    </div>
  `,
})
export class HomeAssistantTargetPickerComponent implements OnInit {
  private readonly providers = inject(DataProviderService);
  private readonly homeAssistant = inject(HomeAssistantService);

  @ViewChild(DashboardSelectorDialogComponent) private selector!: DashboardSelectorDialogComponent;

  @Input() connectionId = '';
  @Input() path = '';
  @Input() disabled = false;
  /** Selects the first connection when nothing is selected yet. */
  @Input() autoSelect = false;
  @Output() readonly connectionIdChange = new EventEmitter<string>();
  @Output() readonly pathChange = new EventEmitter<string>();

  readonly connections = signal<ProviderConnection[]>([]);
  readonly loading = signal(true);
  private readonly loaded = signal(false);

  ngOnInit(): void { this.refresh(); }

  @HostListener('window:focus')
  refresh(): void {
    this.loading.set(true);
    this.providers.connections().subscribe({
      next: connections => {
        // The add-on's default connection has no long-lived token, so it cannot render dashboards.
        this.connections.set(connections.filter(c => c.pluginId === HOME_ASSISTANT_PLUGIN_ID && !c.isDefault));
        this.loading.set(false);
        this.loaded.set(true);
        // Prefer a connection that can already render; one that still needs sign-in is the fallback.
        const candidate = this.connections().find(c => c.connected) ?? this.connections()[0];
        if (this.autoSelect && !this.connectionId && candidate) this.selectConnection(candidate.id);
      },
      error: () => { this.loading.set(false); this.loaded.set(true); },
    });
  }

  /** True when the selection exists and can be used for rendering and browsing. */
  isSelectedReady(): boolean {
    return !!this.selected() && !!this.selected()!.connected;
  }

  isSelectedUnauthorized(): boolean {
    return !!this.selected() && !this.selected()!.connected;
  }

  private selected(): ProviderConnection | undefined {
    return this.connections().find(c => c.id === this.connectionId);
  }

  isSelectedMissing(): boolean {
    return this.loaded() && !!this.connectionId && !this.connections().some(c => c.id === this.connectionId);
  }

  selectConnection(id: string): void {
    this.connectionId = id;
    this.connectionIdChange.emit(id);
  }

  canBrowse(): boolean {
    return !!this.connectionId && this.isSelectedReady();
  }

  browse(): void {
    if (!this.canBrowse()) return;
    this.selector.openWithLoading();
    this.homeAssistant.getConnectionDashboards(this.connectionId).subscribe({
      next: dashboards => {
        this.selector.open(dashboards.map(item => ({ url_path: item.id, title: item.title, id: item.id })))
          .then(selected => { if (selected) { this.path = selected; this.pathChange.emit(selected); } });
      },
      error: error => this.selector.setError(error.error?.error || error.error?.message || 'Failed to fetch dashboards'),
    });
  }
}
