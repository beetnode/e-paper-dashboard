import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ProviderConnectionsComponent } from './provider-connections.component';

describe('provider connections', () => {
  const plugins = [{ id: 'google', name: 'Google Calendar & Tasks', requiresOAuth: true, credentials: [
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
  it('shows user-specific Google credential fields', async () => {
    const fixture = create(); fixture.componentInstance.pluginId = 'google'; fixture.detectChanges();
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector<HTMLInputElement>('#clientSecret')?.type).toBe('password');
    expect(element.textContent).toContain('Use your own Google Cloud OAuth client');
    expect(element.textContent).toContain('No connections yet');
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
});
