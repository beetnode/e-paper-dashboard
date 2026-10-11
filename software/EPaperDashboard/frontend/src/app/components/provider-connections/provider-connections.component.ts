import { Component, HostListener, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DataProviderService, ProviderConnection, ProviderManifest } from '../../services/data-provider.service';
import { DialogService } from '../../services/dialog.service';
import { ToastService } from '../../services/toast.service';

@Component({
  selector: 'app-provider-connections', standalone: true, imports: [CommonModule, FormsModule],
  template: `
    <div class="app-page">
      <div class="app-page-header">
        <div>
          <h1 class="app-page-title">Connections</h1>
          <p class="app-page-description">Connect your data once, then use it in widgets across all of your dashboards.</p>
        </div>
        @if (!formOpen()) {
          <button type="button" class="btn btn-primary" (click)="startAdd()" [disabled]="loading()">
            <i class="fa-solid fa-plus" aria-hidden="true"></i><span class="d-none d-sm-inline"> Add connection</span><span class="visually-hidden d-sm-none">Add connection</span>
          </button>
        }
      </div>

      @if (formOpen()) {
        <form id="connectionForm" class="app-section-card mb-4" (ngSubmit)="save()" aria-labelledby="connectionFormTitle">
          <div class="app-section-header">
            <h2 id="connectionFormTitle" class="app-section-title mb-0">{{ editingId ? 'Edit connection' : 'Add connection' }}</h2>
            <button type="button" class="btn btn-sm btn-outline-secondary" (click)="reset()">
              <i class="fa-solid fa-xmark me-1" aria-hidden="true"></i>Cancel
            </button>
          </div>
          <div class="row g-3 mb-3">
            <div class="col-12 col-sm-6">
              <label class="form-label fw-semibold" for="provider">Provider</label>
              <select class="form-select" id="provider" name="provider" [(ngModel)]="pluginId" [disabled]="!!editingId || busy()" (ngModelChange)="credentials = {}">
                @for (plugin of plugins(); track plugin.id) { <option [value]="plugin.id">{{ plugin.name }}</option> }
              </select>
            </div>
            <div class="col-12 col-sm-6">
              <label class="form-label fw-semibold" for="connectionName">Name</label>
              <input class="form-control" id="connectionName" name="connectionName" [(ngModel)]="name" maxlength="100" required placeholder="Personal account" />
            </div>
          </div>
          @if (selectedPlugin(); as plugin) {
            @for (field of plugin.credentials; track field.key) {
              <div class="mb-3">
                <label class="form-label fw-semibold" [for]="field.key">{{ field.label }}</label>
                <input class="form-control" [id]="field.key" [name]="field.key" [type]="field.secret ? 'password' : 'text'"
                  [(ngModel)]="credentials[field.key]" [required]="isRequired(plugin, field)" autocomplete="off"
                  [placeholder]="placeholder(plugin, field)" />
              </div>
            }
            @if (plugin.setupInstructions) { <p class="small text-muted">{{ plugin.setupInstructions }}</p> }
            @if (plugin.requiresOAuth) {
              <p class="small text-muted mb-1">Register this redirect URI with the provider:</p>
              <code class="d-block small text-break mb-3">{{ callbackUrl(plugin.id) }}</code>
            }
          }
          <div class="d-flex flex-column-reverse flex-sm-row justify-content-end gap-2">
            @if (selectedPlugin()?.supportsSignIn) {
              <button class="btn btn-outline-primary" type="button" [disabled]="busy() || loading()" (click)="save(true)">Save and sign in</button>
            }
            <button class="btn btn-primary" type="submit" [disabled]="busy() || loading()">
              @if (busy()) { <span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span> }
              Save connection
            </button>
          </div>
        </form>
      }

      @if (loading()) {
        <div class="app-loading-state">
          <div class="spinner-border" role="status"><span class="visually-hidden">Loading connections</span></div>
        </div>
      } @else if (connections().length > 0) {
        <div class="app-list">
          @for (connection of connections(); track connection.id) {
            <article class="app-list-item app-list-item-wrap">
              <div style="min-width: 0">
                <div class="app-list-item-heading">
                  <h2 class="app-list-item-title">{{ connection.name }}</h2>
                  <span class="app-status-chip" [ngClass]="connection.connected ? 'app-status-chip-success' : 'app-status-chip-warning'">
                    <i class="fa-solid" [ngClass]="connection.connected ? 'fa-circle-check' : 'fa-triangle-exclamation'" aria-hidden="true"></i>{{ connection.connected ? 'Connected' : 'Sign-in required' }}
                  </span>
                </div>
                <p class="app-list-item-description">{{ pluginName(connection.pluginId) }}</p>
              </div>
              <div class="app-list-item-actions">
                @if (supportsSignIn(connection.pluginId) && !connection.connected) {
                  <button type="button" class="btn btn-primary" [disabled]="busy()" (click)="authorize(connection)">Sign in to {{ pluginName(connection.pluginId) }}</button>
                }
                <button type="button" class="btn btn-outline-secondary app-icon-button" [disabled]="busy() || !connection.connected" (click)="test(connection)"
                  [attr.aria-label]="'Test ' + connection.name" title="Test connection">
                  <i class="fa-solid fa-plug-circle-check" aria-hidden="true"></i>
                </button>
                @if (supportsSignIn(connection.pluginId) && connection.connected) {
                  <button type="button" class="btn btn-outline-secondary app-icon-button" [disabled]="busy()" (click)="authorize(connection)"
                    [attr.aria-label]="'Sign in to ' + pluginName(connection.pluginId) + ' again'" title="Sign in again">
                    <i class="fa-solid fa-right-to-bracket" aria-hidden="true"></i>
                  </button>
                }
                <button type="button" class="btn btn-outline-primary app-icon-button" [disabled]="busy()" (click)="edit(connection)"
                  [attr.aria-label]="'Edit ' + connection.name" title="Edit">
                  <i class="fa-solid fa-pen" aria-hidden="true"></i>
                </button>
                <button type="button" class="btn btn-outline-danger app-icon-button" [disabled]="busy()" (click)="remove(connection)"
                  [attr.aria-label]="'Remove ' + connection.name" title="Remove">
                  <i class="fa-solid fa-trash" aria-hidden="true"></i>
                </button>
              </div>
            </article>
          }
        </div>
      } @else if (!formOpen()) {
        <div class="app-empty-state">
          <span class="app-empty-state-icon"><i class="fa-solid fa-plug" aria-hidden="true"></i></span>
          <div>
            <h2 class="h5 mb-1">No connections yet</h2>
            <p class="text-muted mb-0">Add Home Assistant, Google, or another provider to feed your widgets.</p>
          </div>
          <button type="button" class="btn btn-primary" (click)="startAdd()"><i class="fa-solid fa-plus me-1" aria-hidden="true"></i>Add connection</button>
        </div>
      }
    </div>
  `,
})
export class ProviderConnectionsComponent implements OnInit {
  private readonly api = inject(DataProviderService);
  private readonly dialogService = inject(DialogService);
  private readonly toastService = inject(ToastService);
  readonly plugins = signal<ProviderManifest[]>([]);
  readonly connections = signal<ProviderConnection[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly formOpen = signal(false);
  pluginId = 'home-assistant';
  name = '';
  editingId: string | null = null;
  credentials: Record<string, string> = {};
  callbackUrl(pluginId: string) { return new URL(`api/providers/${encodeURIComponent(pluginId)}/callback`, document.baseURI).href; }
  ngOnInit() {
    const query = new URLSearchParams(location.search);
    const connected = query.get('connected');
    this.api.plugins().subscribe({
      next: plugins => {
        this.plugins.set(plugins);
        if (connected && plugins.some(p => p.id === connected)) this.toastService.success(`${this.pluginName(connected)} connected.`);
      },
      error: e => this.fail(e),
    });
    this.load();
    const error = query.get('error');
    if (error) this.toastService.error(error);
  }
  selectedPlugin() { return this.plugins().find(p => p.id === this.pluginId); }
  pluginName(id: string) { return this.plugins().find(p => p.id === id)?.name ?? id; }
  supportsSignIn(id: string) { return !!this.plugins().find(p => p.id === id)?.supportsSignIn; }
  private fromSignIn(plugin: ProviderManifest, key: string) { return !!plugin.signInCredentialKeys?.includes(key); }
  isRequired(plugin: ProviderManifest, field: { key: string; secret: boolean }) {
    return !this.fromSignIn(plugin, field.key) && (!this.editingId || !field.secret);
  }
  placeholder(plugin: ProviderManifest, field: { key: string; secret: boolean }) {
    if (this.editingId && field.secret) return 'Leave blank to keep saved value';
    return this.fromSignIn(plugin, field.key) ? 'Optional: or sign in after saving' : '';
  }
  @HostListener('window:focus')
  refreshOnFocus() { if (!this.busy()) this.load(); }
  load() {
    this.api.connections().subscribe({ next: values => { this.connections.set(values); this.loading.set(false); }, error: e => this.fail(e) });
  }
  startAdd() { this.reset(); this.formOpen.set(true); }
  edit(connection: ProviderConnection) {
    this.editingId = connection.id; this.pluginId = connection.pluginId; this.name = connection.name;
    this.credentials = { ...connection.credentials }; this.formOpen.set(true);
    setTimeout(() => document.getElementById('connectionForm')?.scrollIntoView({ block: 'nearest', behavior: 'smooth' }));
  }
  reset() { this.editingId = null; this.name = ''; this.credentials = {}; this.formOpen.set(false); }
  save(signIn = false) {
    // The sign-in tab must open in the click handler, before any request, or pop-up blockers stop it.
    const popup = signIn ? this.openSignInWindow(this.pluginId) : null;
    if (signIn && !popup) return;
    this.busy.set(true);
    this.api.save(this.editingId, { name: this.name, pluginId: this.pluginId, credentials: this.credentials }).subscribe({
      next: response => {
        this.busy.set(false); this.reset(); this.load(); this.toastService.success('Connection saved.');
        if (popup && response?.data) this.startSignIn(response.data.id, popup);
      },
      error: e => { popup?.close(); this.fail(e); },
    });
  }
  authorize(connection: ProviderConnection) {
    const popup = this.openSignInWindow(connection.pluginId);
    if (popup) this.startSignIn(connection.id, popup);
  }
  private openSignInWindow(pluginId: string): Window | null {
    const popup = window.open('about:blank', '_blank');
    if (!popup) { this.toastService.error('Allow pop-ups to sign in to ' + this.pluginName(pluginId) + '.'); return null; }
    popup.opener = null;
    return popup;
  }
  private startSignIn(connectionId: string, popup: Window) {
    this.busy.set(true);
    this.api.authorize(connectionId).subscribe({
      next: result => { popup.location.href = result.authUrl; this.busy.set(false); },
      error: e => { popup.close(); this.fail(e); },
    });
  }
  test(connection: ProviderConnection) {
    this.busy.set(true);
    this.api.test(connection.id).subscribe({
      next: result => { this.busy.set(false); this.toastService.success(result.message); },
      error: e => this.fail(e),
    });
  }
  async remove(connection: ProviderConnection) {
    await this.dialogService.confirm({
      title: 'Remove connection?',
      message: `Remove "${connection.name}"? Widgets using this connection will show unavailable data.`,
      confirmLabel: 'Remove',
      isDangerous: true,
      onConfirm: () => {
        this.busy.set(true);
        this.api.remove(connection.id).subscribe({
          next: () => { this.busy.set(false); if (this.editingId === connection.id) this.reset(); this.load(); this.toastService.success('Connection removed.'); },
          error: e => this.fail(e),
        });
      },
    });
  }
  private fail(error: any) { this.toastService.error(error?.error?.message || 'Unable to update connections. Please try again.'); this.busy.set(false); this.loading.set(false); }
}
