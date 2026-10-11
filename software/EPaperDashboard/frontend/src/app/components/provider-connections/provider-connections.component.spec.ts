import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ProviderConnectionsComponent } from './provider-connections.component';

describe('provider connections', () => {
  const homeAssistant = { id: 'home-assistant', name: 'Home Assistant', requiresOAuth: false, supportsSignIn: true, signInCredentialKeys: ['token'], credentials: [
    { key: 'host', label: 'Server URL', secret: false },
    { key: 'token', label: 'Long-lived access token', secret: true },
  ] };
  const plugins = [homeAssistant, { id: 'google', name: 'Google Calendar & Tasks', requiresOAuth: true, supportsSignIn: true, signInCredentialKeys: ['refreshToken'], setupInstructions: 'Use your own Google Cloud OAuth client.', credentials: [
    { key: 'clientId', label: 'OAuth client ID', secret: false },
    { key: 'clientSecret', label: 'OAuth client secret', secret: true },
    { key: 'redirectUri', label: 'OAuth redirect URI', secret: false },
  ] }];
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ProviderConnectionsComponent], providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function create() {
    const fixture = TestBed.createComponent(ProviderConnectionsComponent);
    fixture.detectChanges();
    http.expectOne('/api/providers').flush({ data: plugins });
    http.expectOne('/api/providers/connections').flush({ data: [] });
    return fixture;
  }
  it('shows plugin credential fields, setup instructions and its own callback URL', async () => {
    const fixture = create(); fixture.componentInstance.pluginId = 'google'; fixture.detectChanges();
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector<HTMLInputElement>('#clientSecret')?.type).toBe('password');
    expect(element.textContent).toContain('Use your own Google Cloud OAuth client.');
    expect(element.querySelector('code')?.textContent).toContain('/api/providers/google/callback');
    expect(element.textContent).toContain('No connections yet');
  });
  it('labels sign-in with the connection provider name', async () => {
    const fixture = TestBed.createComponent(ProviderConnectionsComponent);
    fixture.detectChanges();
    http.expectOne('/api/providers').flush({ data: plugins });
    http.expectOne('/api/providers/connections').flush({ data: [
      { id: 'one', name: 'Personal', pluginId: 'google', connected: false, credentials: {} },
    ] });
    fixture.detectChanges();
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Sign in to Google Calendar & Tasks');
  });
  it('creates a named connection using the entered credentials', () => {
    const fixture = create(); const component = fixture.componentInstance;
    component.name = 'Personal'; component.pluginId = 'google';
    component.credentials = { clientId: 'my-client', clientSecret: 'my-secret', redirectUri: 'https://example.test/api/providers/google/callback' };
    component.save();
    const request = http.expectOne('/api/providers/connections');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.credentials.clientSecret).toBe('my-secret');
    request.flush({});
    http.expectOne('/api/providers/connections').flush({ data: [] });
    expect(component.credentials).toEqual({});
    expect(component.notice()).toBe('Connection saved.');
  });
  it('edits public settings without putting saved secrets into the form', () => {
    const fixture = create(); const component = fixture.componentInstance;
    component.edit({ id: 'one', name: 'Personal', pluginId: 'google', connected: true, credentials: { clientId: 'my-client' } });
    expect(component.credentials['clientSecret']).toBeUndefined();
    component.save();
    const request = http.expectOne('/api/providers/connections/one');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.credentials.clientSecret).toBeUndefined();
    request.flush({});
    http.expectOne('/api/providers/connections').flush({ data: [] });
  });
  it('makes the Home Assistant token optional because sign-in can create it', async () => {
    const fixture = create(); fixture.componentInstance.pluginId = 'home-assistant'; fixture.detectChanges();
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector<HTMLInputElement>('#token')?.required).toBe(false);
    expect(element.querySelector<HTMLInputElement>('#host')?.required).toBe(true);
    expect(element.textContent).toContain('Save and sign in');
    expect(element.querySelector('code')).toBeNull();
  });
  it('saves a connection and then starts sign-in in the tab opened by the click', () => {
    const popup = { opener: {}, location: { href: '' }, close: vi.fn() };
    const open = vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window);
    const fixture = create(); const component = fixture.componentInstance;
    component.name = 'Home'; component.pluginId = 'home-assistant'; component.credentials = { host: 'https://ha.example.test' };
    component.save(true);
    expect(open).toHaveBeenCalledTimes(1);
    expect(popup.opener).toBeNull();
    http.expectOne('/api/providers/connections').flush({ data: { id: 'new-id', name: 'Home', pluginId: 'home-assistant', connected: false, credentials: {} } });
    http.expectOne('/api/providers/connections').flush({ data: [] });
    http.expectOne('/api/providers/connections/new-id/authorize').flush({ authUrl: 'https://ha.example.test/auth/authorize?x=1' });
    expect(popup.location.href).toBe('https://ha.example.test/auth/authorize?x=1');
    open.mockRestore();
  });
});
