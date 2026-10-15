import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptorsFromDi, HTTP_INTERCEPTORS } from '@angular/common/http';

import { routes } from './app.routes';
import { BaseHrefInterceptor } from './interceptors/base-href.interceptor';
import { PasswordChangeInterceptor } from './interceptors/password-change.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptorsFromDi()),
    { provide: HTTP_INTERCEPTORS, useClass: BaseHrefInterceptor, multi: true },
    { provide: HTTP_INTERCEPTORS, useClass: PasswordChangeInterceptor, multi: true }
  ]
};
