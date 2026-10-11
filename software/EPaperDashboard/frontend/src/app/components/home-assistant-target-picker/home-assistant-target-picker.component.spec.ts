import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { HomeAssistantTargetPickerComponent } from './home-assistant-target-picker.component';

describe('home assistant target picker', () => {
  const homeAssistant = { id: 'ha-1', name: 'Home', pluginId: 'home-assistant', connected: true, credentials: {} };
  const google = { id: 'g-1', name: 'Google', pluginId: 'google', connected: true, credentials: {} };
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HomeAssistantTargetPickerComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function create(setup?: (picker: HomeAssistantTargetPickerComponent) => void) {
    const fixture = TestBed.createComponent(HomeAssistantTargetPickerComponent);
    setup?.(fixture.componentInstance);
    fixture.detectChanges();
    return fixture;
  }

  it('lists only Home Assistant connections', async () => {
    const fixture = create();
    http.expectOne('/api/providers/connections').flush({ data: [homeAssistant, google] });
    fixture.detectChanges();
    await fixture.whenStable();
    const options = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('#haConnection option')).map(o => o.textContent?.trim());
    expect(options).toContain('Home');
    expect(options).not.toContain('Google');
  });

  it('picks up a connection added in another tab when the window regains focus', () => {
    const selected: string[] = [];
    const fixture = create(picker => { picker.autoSelect = true; picker.connectionIdChange.subscribe(id => selected.push(id)); });
    http.expectOne('/api/providers/connections').flush({ data: [] });
    expect(selected).toEqual([]);
    window.dispatchEvent(new Event('focus'));
    http.expectOne('/api/providers/connections').flush({ data: [homeAssistant] });
    expect(selected).toEqual(['ha-1']);
    expect(fixture.componentInstance.connectionId).toBe('ha-1');
  });

  it('flags a selected connection that no longer exists', () => {
    const fixture = create(picker => picker.connectionId = 'deleted');
    http.expectOne('/api/providers/connections').flush({ data: [homeAssistant] });
    fixture.detectChanges();
    expect(fixture.componentInstance.isSelectedMissing()).toBe(true);
    expect(fixture.componentInstance.canBrowse()).toBe(false);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('The selected connection was removed');
  });

  it('browses dashboards of the selected connection', () => {
    const paths: string[] = [];
    const fixture = create(picker => { picker.connectionId = 'ha-1'; picker.pathChange.subscribe(p => paths.push(p)); });
    http.expectOne('/api/providers/connections').flush({ data: [homeAssistant] });
    fixture.detectChanges();
    fixture.componentInstance.browse();
    http.expectOne('/api/homeassistant/connections/ha-1/dashboards').flush({ data: [{ id: 'lovelace/0', title: 'Overview' }] });
  });

  it('browses the dashboard settings only when they are ready', () => {
    const fixture = create(picker => { picker.allowDashboardSettings = true; picker.dashboardId = 'd-1'; });
    http.expectOne('/api/providers/connections').flush({ data: [] });
    expect(fixture.componentInstance.canBrowse()).toBe(false);
    fixture.componentInstance.dashboardSettingsReady = true;
    expect(fixture.componentInstance.canBrowse()).toBe(true);
    fixture.componentInstance.browse();
    http.expectOne('/api/dashboards/d-1/homeassistant/dashboards').flush({ data: [] });
  });
});
