import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, provideRouter } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from '../services/auth.service';

describe('authGuard', () => {
  let router: Router;

  function signIn(mustChangePassword: boolean): void {
    const auth = TestBed.inject(AuthService);
    auth.getCurrentUser().subscribe();
    TestBed.inject(HttpTestingController).expectOne('/api/auth/current')
      .flush({ id: 'u', username: 'alice', isSuperUser: false, mustChangePassword });
  }

  function canActivate(url: string): unknown {
    return TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot));
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  it('sends a user with a temporary password to their profile', () => {
    signIn(true);

    expect(canActivate('/dashboards')).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/users/profile']);
  });

  it('lets a user with a temporary password open their profile', () => {
    signIn(true);

    expect(canActivate('/users/profile')).toBe(true);
  });

  it('lets other signed-in users through', () => {
    signIn(false);

    expect(canActivate('/dashboards')).toBe(true);
    expect(router.navigate).not.toHaveBeenCalled();
  });
});
