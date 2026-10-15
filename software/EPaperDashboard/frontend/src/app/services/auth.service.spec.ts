import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';

describe('AuthService account creation', () => {
  let auth: AuthService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('uses a dedicated setup endpoint and records the administrator returned by the server', () => {
    auth.setup({ username: 'admin', password: 'password', setupCode: 'ABCD-2345' }).subscribe();
    const request = http.expectOne('/api/auth/setup');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ username: 'admin', password: 'password', setupCode: 'ABCD-2345' });
    request.flush({ id: 'admin-id', username: 'admin', isSuperUser: true, userManagementEnabled: true });
    expect(auth.currentUser()?.isSuperUser).toBe(true);
    expect(auth.isAuthReady()).toBe(true);
  });

  it('keeps regular registration separate from administrator setup', () => {
    auth.register({ username: 'alice', password: 'password' }).subscribe();
    const request = http.expectOne('/api/auth/register');
    expect(request.request.body).toEqual({ username: 'alice', password: 'password' });
    request.flush({ id: 'alice-id', username: 'alice', isSuperUser: false });
    expect(auth.currentUser()?.isSuperUser).toBe(false);
  });
});
