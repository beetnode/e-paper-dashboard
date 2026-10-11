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
                <button class="btn btn-outline-secondary btn-sm" [disabled]="busy() || !connection.connected" (click)="test(connection)">Test</button>
                @if (supportsSignIn(connection.pluginId)) {
                  <button class="btn btn-sm" [ngClass]="connection.connected ? 'btn-outline-primary' : 'btn-primary'" [disabled]="busy()" (click)="authorize(connection)">
                    {{ connection.connected ? 'Sign in again' : 'Sign in' }} to {{ pluginName(connection.pluginId) }}
                  </button>
                }
                <button class="btn btn-outline-danger btn-sm" [disabled]="busy()" (click)="remove(connection)">Remove</button>
              </div>
            </div></article>
          } @empty { @if (!loading()) { <p>No connections yet. Add one to get started.</p> } }
          <p class="small text-muted">Connections belong to your account and can be used by all of your dashboards.</p>
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
                  [(ngModel)]="credentials[field.key]" [required]="isRequired(plugin, field)" autocomplete="off"
                  [placeholder]="placeholder(plugin, field)" />
              }
              @if (plugin.setupInstructions) { <p class="small">{{ plugin.setupInstructions }}</p> }
              @if (plugin.requiresOAuth) {
                <p class="small mb-1">Register this redirect URI with the provider:</p>
                <code class="d-block small text-break mb-3">{{ callbackUrl(plugin.id) }}</code>
              }
            }
            <div class="d-flex flex-wrap gap-2"><button class="btn btn-primary" type="submit" [disabled]="busy() || loading()">{{ busy() ? 'Saving…' : 'Save connection' }}</button>
              @if (selectedPlugin()?.supportsSignIn) {
                <button class="btn btn-outline-primary" type="button" [disabled]="busy() || loading()" (click)="save(true)">Save and sign in</button>
              }
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
  callbackUrl(pluginId: string) { return new URL(`api/providers/${encodeURIComponent(pluginId)}/callback`, document.baseURI).href; }
  ngOnInit() {
    const query = new URLSearchParams(location.search);
    const connected = query.get('connected');
    this.api.plugins().subscribe({
      next: plugins => {
        this.plugins.set(plugins);
        if (connected && plugins.some(p => p.id === connected)) this.notice.set(`${this.pluginName(connected)} connected.`);
      },
      error: e => this.fail(e),
    });
    this.load();
    this.error.set(query.get('error') ?? '');
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
  edit(connection: ProviderConnection) {
    this.editingId = connection.id; this.pluginId = connection.pluginId; this.name = connection.name;
    this.credentials = { ...connection.credentials }; this.error.set(''); this.notice.set('');
  }
  reset() { this.editingId = null; this.name = ''; this.credentials = {}; }
  save(signIn = false) {
    // The sign-in tab must open in the click handler, before any request, or pop-up blockers stop it.
    const popup = signIn ? this.openSignInWindow(this.pluginId) : null;
    if (signIn && !popup) return;
    this.busy.set(true); this.error.set(''); this.notice.set('');
    this.api.save(this.editingId, { name: this.name, pluginId: this.pluginId, credentials: this.credentials }).subscribe({
      next: response => {
        this.busy.set(false); this.reset(); this.load(); this.notice.set('Connection saved.');
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
    if (!popup) { this.error.set('Allow pop-ups to sign in to ' + this.pluginName(pluginId) + '.'); return null; }
    popup.opener = null;
    return popup;
  }
  private startSignIn(connectionId: string, popup: Window) {
    this.busy.set(true); this.error.set('');
    this.api.authorize(connectionId).subscribe({
      next: result => { popup.location.href = result.authUrl; this.busy.set(false); },
      error: e => { popup.close(); this.fail(e); },
    });
  }
  test(connection: ProviderConnection) {
    this.busy.set(true); this.error.set(''); this.notice.set('');
    this.api.test(connection.id).subscribe({
      next: result => { this.busy.set(false); this.notice.set(result.message); },
      error: e => this.fail(e),
    });
  }
  remove(connection: ProviderConnection) {
    if (!window.confirm('Remove "' + connection.name + '"? Widgets using this connection will show unavailable data.')) return;
    this.busy.set(true); this.error.set('');
    this.api.remove(connection.id).subscribe({ next: () => { this.busy.set(false); if (this.editingId === connection.id) this.reset(); this.load(); }, error: e => this.fail(e) });
  }
  private fail(error: any) { this.error.set(error?.error?.message || 'Unable to update connections. Please try again.'); this.busy.set(false); this.loading.set(false); }
}
