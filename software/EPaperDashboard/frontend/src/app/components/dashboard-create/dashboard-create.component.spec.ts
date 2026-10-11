import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { DashboardCreateComponent } from './dashboard-create.component';

describe('dashboard create', () => {
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DashboardCreateComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });
  afterEach(() => http.verify());

  function create() {
    const fixture = TestBed.createComponent(DashboardCreateComponent);
    fixture.detectChanges();
    http.expectOne('/api/rendering').flush({ enabled: true, component: { state: 'ready', supported: true, ready: true } });
    return fixture;
  }

  async function chooseHomeAssistant(fixture: ReturnType<typeof create>) {
    await fixture.whenStable();
    (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#kindHomeAssistant')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('creates a custom layout and opens it in the designer', () => {
    const component = create().componentInstance;
    component.name = 'Kitchen';
    component.onSubmit();
    const request = http.expectOne('/api/dashboards');
    expect(request.request.body.renderingMode).toBe('Custom');
    expect(request.request.body.homeAssistantConnectionId).toBeUndefined();
    request.flush({ id: 'new-id' });
    expect(navigate).toHaveBeenCalledWith(['/dashboards', 'new-id', 'designer']);
  });

  it('creates a Home Assistant dashboard with its connection and path, then opens its settings', async () => {
    const fixture = create();
    const component = fixture.componentInstance;
    await chooseHomeAssistant(fixture);
    expect(component.renderingMode).toBe('HomeAssistant');
    http.expectOne('/api/providers/connections').flush({ data: [
      { id: 'ha-1', name: 'Home', pluginId: 'home-assistant', connected: true, credentials: {} },
    ] });
    expect(component.homeAssistantConnectionId).toBe('ha-1');
    component.name = 'Hallway';
    component.path = 'lovelace/0';
    component.onSubmit();
    const request = http.expectOne('/api/dashboards');
    expect(request.request.body).toEqual(expect.objectContaining({
      renderingMode: 'HomeAssistant', homeAssistantConnectionId: 'ha-1', path: 'lovelace/0',
    }));
    request.flush({ id: 'new-id' });
    expect(navigate).toHaveBeenCalledWith(['/dashboards', 'new-id', 'edit']);
  });

  it('requires a connection and a path for Home Assistant dashboards', async () => {
    const fixture = create();
    const component = fixture.componentInstance;
    await chooseHomeAssistant(fixture);
    http.expectOne('/api/providers/connections').flush({ data: [] });
    component.name = 'Hallway';
    component.onSubmit();
    expect(component.errorMessage()).toContain('connection');
    window.dispatchEvent(new Event('focus'));
    http.expectOne('/api/rendering').flush({ enabled: true, component: { state: 'ready', supported: true, ready: true } });
    http.expectOne('/api/providers/connections').flush({ data: [
      { id: 'ha-1', name: 'Home', pluginId: 'home-assistant', connected: true, credentials: {} },
    ] });
    component.onSubmit();
    expect(component.errorMessage()).toContain('path');
  });

  it('rejects a Home Assistant connection that still needs sign-in', async () => {
    const fixture = create();
    const component = fixture.componentInstance;
    await chooseHomeAssistant(fixture);
    http.expectOne('/api/providers/connections').flush({ data: [
      { id: 'ha-1', name: 'Home', pluginId: 'home-assistant', connected: false, credentials: {} },
    ] });
    fixture.detectChanges();
    expect(component.homeAssistantConnectionId).toBe('ha-1');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('sign-in required');
    component.name = 'Hallway';
    component.path = 'lovelace/0';
    component.onSubmit();
    expect(component.errorMessage()).toContain('sign-in');
  });
});
