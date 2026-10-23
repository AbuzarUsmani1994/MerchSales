import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MessageService } from 'primeng/api';
import { catchError, throwError } from 'rxjs';
import { ProblemDetails } from '../auth/auth.models';

/**
 * Surfaces a ProblemDetails failure as a toast (section 13). Field-level validation errors
 * (the `errors` extension) are left for the calling component to render inline — this only
 * covers the generic "something went wrong" case.
 */
export const errorToastInterceptor: HttpInterceptorFn = (req, next) => {
  const messages = inject(MessageService);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status !== 401) {
        const problem = error.error as ProblemDetails | undefined;
        messages.add({
          severity: 'error',
          summary: problem?.title ?? 'Request failed',
          detail: problem?.detail ?? 'An unexpected error occurred.',
        });
      }

      return throwError(() => error);
    }),
  );
};
