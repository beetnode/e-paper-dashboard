import { Component, HostListener, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DataProviderService, ProviderConnection, ProviderManifest } from '../../services/data-provider.service';

@Component({
  selector: 'app-provider-connections', standalone: true, imports: [CommonModule, FormsModule],
  template: `
    <main class="container py-4" style="max-width: 960px">
      <h1>Connections</h1>
      <p class="text-muted">Connect your data once, then use it across your dashboards.</p>
      @if (error()) { <div class="alert alert-danger" role="alert">{{ error() }}</div> }
      @if (notice()) { <div class="alert alert-success" role="status">{{ notice() }}</div> }
      @if (loading()) { <p role="status">Loading connections…</p> }
      <div class="row g-4">
        <section class="col-md-6" aria-label="Your connections">
          @for (connection of connections(); track connection.id) {
            <article class="card mb-3"><div class="card-body">
              <h2 class="h5">{{ connection.name }}</h2>
              <p class="text-muted mb-2">{{ pluginName(connection.pluginId) }} · {{ connection.connected ? 'Configured' : 'Sign-in required' }}</p>
              <div class="d-flex flex-wrap gap-2">
                <button class="btn btn-outline-secondary btn-sm" [disabled]="busy()" (click)="edit(connection)">Edit</button>
                @if (requiresOAuth(connection.pluginId)) {
                  <button class="btn btn-primary btn-sm" [disabled]="busy()" (click)="authorize(connection)">{{ connection.connected ? 'Reconnect Google' : 'Connect Google' }}</button>
                }
                <button class="btn btn-outline-danger btn-sm" [disabled]="busy()" (click)="remove(connection)">Remove</button>
              </div>
            </div></article>
          } @empty { @if (!loading()) { <p>No connections yet. Add one to get started.</p> } }
          <p class="small text-muted">Existing dashboard Home Assistant connections remain available in their dashboard. Add a named connection here to reuse it across dashboards.</p>
        </section>
        <section class="col-md-6">
          <form class="card card-body" (ngSubmit)="save()">
            <h2 class="h5">{{ editingId ? 'Edit connection' : 'Add connection' }}</h2>
            <label class="form-label" for="provider">Provider</label>
            <select class="form-select mb-3" id="provider" name="provider" [(ngModel)]="pluginId" [disabled]="!!editingId || busy()" (ngModelChange)="credentials = {}">
              @for (plugin of plugins(); track plugin.id) { <option [value]="plugin.id">{{ plugin.name }}</option> }
            </select>
            <label class="form-label" for="connectionName">Connection name</label>
            <input class="form-control mb-3" id="connectionName" name="connectionName" [(ngModel)]="name" maxlength="100" required placeholder="Personal account" />
            @if (selectedPlugin(); as plugin) {
              @for (field of plugin.credentials; track field.key) {
                <label class="form-label" [for]="field.key">{{ field.label }}</label>
                <input class="form-control mb-3" [id]="field.key" [name]="field.key" [type]="field.secret ? 'password' : 'text'"
                  [(ngModel)]="credentials[field.key]" [required]="!editingId || !field.secret" autocomplete="off"
                  [placeholder]="editingId && field.secret ? 'Leave blank to keep saved value' : ''" />
              }
              @if (plugin.requiresOAuth) {
                <p class="small">Use your own Google Cloud OAuth client (Web application). Enable Calendar and Tasks APIs, add your account as a test user if needed, and register this redirect URI:</p>
                <code class="small text-break mb-3">{{ callbackUrl }}</code>
                <p class="small text-muted">Use HTTPS, or localhost for development. After saving, choose Connect Google and allow Calendar and Tasks access. Changing credentials requires reconnecting.</p>
              }
            }
            <div class="d-flex gap-2"><button class="btn btn-primary" type="submit" [disabled]="busy() || loading()">{{ busy() ? 'Saving…' : 'Save connection' }}</button>
              @if (editingId) { <button class="btn btn-outline-secondary" type="button" (click)="reset()">Cancel</button> }
            </div>
          </form>
        </section>
      </div>
    </main>
  `,
})
export class ProviderConnectionsComponent implements OnInit {
  private readonly api = inject(DataProviderService);
  readonly plugins = signal<ProviderManifest[]>([]);
  readonly connections = signal<ProviderConnection[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  pluginId = 'home-assistant';
  name = '';
  editingId: string | null = null;
  credentials: Record<string, string> = {};
  readonly callbackUrl = new URL('api/providers/google/callback', document.baseURI).href;
  ngOnInit() {
    this.api.plugins().subscribe({ next: plugins => this.plugins.set(plugins), error: e => this.fail(e) });
    this.load();
    this.error.set(new URLSearchParams(location.search).get('error') ?? '');
    if (new URLSearchParams(location.search).get('connected') === 'google') this.notice.set('Google account connected.');
  }
  selectedPlugin() { return this.plugins().find(p => p.id === this.pluginId); }
  pluginName(id: string) { return this.plugins().find(p => p.id === id)?.name ?? id; }
  requiresOAuth(id: string) { return this.plugins().find(p => p.id === id)?.requiresOAuth; }
  @HostListener('window:focus')
  refreshOnFocus() { if (!this.busy()) this.load(); }
  load() {
    this.api.connections().subscribe({ next: values => { this.connections.set(values); this.loading.set(false); }, error: e => this.fail(e) });
  }
  edit(connection: ProviderConnection) {
    this.editingId = connection.id; this.pluginId = connection.pluginId; this.name = connection.name;
    this.credentials = { ...connection.credentials }; this.error.set(''); this.notice.set('');
  }
  reset() { this.editingId = null; this.name = ''; this.credentials = {}; }
  save() {
    this.busy.set(true); this.error.set(''); this.notice.set('');
    this.api.save(this.editingId, { name: this.name, pluginId: this.pluginId, credentials: this.credentials }).subscribe({
      next: () => { this.busy.set(false); this.reset(); this.load(); this.notice.set('Connection saved.'); }, error: e => this.fail(e),
    });
  }
  authorize(connection: ProviderConnection) {
    const popup = window.open('about:blank', '_blank');
    if (!popup) { this.error.set('Allow pop-ups to connect your Google account.'); return; }
    popup.opener = null;
    this.busy.set(true); this.error.set('');
    this.api.authorize(connection.id).subscribe({
      next: result => { popup.location.href = result.authUrl; this.busy.set(false); },
      error: e => { popup.close(); this.fail(e); },
    });
  }
  remove(connection: ProviderConnection) {
    if (!window.confirm('Remove "' + connection.name + '"? Widgets using this connection will show unavailable data.')) return;
    this.busy.set(true); this.error.set('');
    this.api.remove(connection.id).subscribe({ next: () => { this.busy.set(false); if (this.editingId === connection.id) this.reset(); this.load(); }, error: e => this.fail(e) });
  }
  private fail(error: any) { this.error.set(error?.error?.message || 'Unable to update connections. Please try again.'); this.busy.set(false); this.loading.set(false); }
}
