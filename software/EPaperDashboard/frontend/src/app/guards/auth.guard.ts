import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthReady()) {
    return new Promise<boolean>((resolve) => {
      let resolved = false;
      const maxAttempts = 500;
      let attempts = 0;

      const checkInterval = setInterval(() => {
        attempts++;
        if (authService.isAuthReady()) {
          clearInterval(checkInterval);
          if (!resolved) {
            resolved = true;
            resolve(decide());
          }
        } else if (attempts >= maxAttempts) {
          clearInterval(checkInterval);
          if (!resolved) {
            resolved = true;
            router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
            resolve(false);
          }
        }
      }, 10);
    });
  }

  return decide();

  function decide(): boolean {
    if (!authService.isAuthenticated()) {
      router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
      return false;
    }
    // A temporary password from an administrator has to be replaced before anything else.
    if (authService.mustChangePassword() && !state.url.startsWith(PROFILE_URL)) {
      router.navigate([PROFILE_URL]);
      return false;
    }
    return true;
  }
};

const PROFILE_URL = '/users/profile';
