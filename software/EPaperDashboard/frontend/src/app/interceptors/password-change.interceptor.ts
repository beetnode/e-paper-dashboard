import { Injectable, inject } from '@angular/core';
import { HttpErrorResponse, HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, catchError, throwError } from 'rxjs';

/**
 * Sends the user to their profile when the server refuses a request because an administrator issued a temporary
 * password that has not been replaced yet.
 */
@Injectable()
export class PasswordChangeInterceptor implements HttpInterceptor {
  private readonly router = inject(Router);

  intercept(req: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    return next.handle(req).pipe(
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 403 && error.error?.code === 'password_change_required') {
          this.router.navigate(['/users/profile']);
        }
        return throwError(() => error);
      })
    );
  }
}
