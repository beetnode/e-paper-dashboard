import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RenderingSettingsComponent } from './rendering-settings.component';
import { AuthService } from '../../services/auth.service';

describe('RenderingSettingsComponent', () => {
  let http: HttpTestingController;
  let addon: boolean;
  beforeEach(() => {
    addon = false;
    TestBed.configureTestingModule({ imports: [RenderingSettingsComponent], providers: [
      provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: { isAddonMode: () => addon } }
    ] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('describes the shared add-on scope without account isolation claims', () => {
    addon = true;
    const fixture = TestBed.createComponent(RenderingSettingsComponent);
    fixture.detectChanges();
    http.expectOne('/api/rendering').flush({ enabled: true, component: { state: 'Installed', ready: true, supported: true } });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Disabling it affects the whole add-on.');
    expect(fixture.nativeElement.textContent).toContain('Disable for this add-on');
    expect(fixture.nativeElement.textContent).not.toContain('your account');
    fixture.destroy();
  });

  it('disables only the current account through the user endpoint', () => {
    const fixture = TestBed.createComponent(RenderingSettingsComponent);
    fixture.detectChanges();
    http.expectOne('/api/rendering').flush({ enabled: true, component: { state: 'Installed', ready: true, supported: true } });
    fixture.componentInstance.setEnabled(false);
    const request = http.expectOne('/api/rendering');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ enabled: false });
    http.expectNone('/api/system/components/rendering');
    request.flush({ enabled: false, component: { state: 'Installed', ready: true, supported: true } });
    expect(fixture.componentInstance.status()?.enabled).toBe(false);
    fixture.destroy();
  });

  it('keeps the existing setting when an enable request fails', () => {
    const fixture = TestBed.createComponent(RenderingSettingsComponent);
    fixture.detectChanges();
    http.expectOne('/api/rendering').flush({ enabled: false, component: { state: 'NotInstalled', ready: false, supported: true } });
    fixture.componentInstance.setEnabled(true);
    http.expectOne('/api/rendering').flush({ message: 'Unsupported server' }, { status: 400, statusText: 'Bad Request' });
    expect(fixture.componentInstance.status()?.enabled).toBe(false);
    expect(fixture.componentInstance.error()).toBe('Unsupported server');
    expect(fixture.componentInstance.busy()).toBe(false);
    fixture.destroy();
  });
});
