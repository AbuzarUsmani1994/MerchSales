import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, throwError } from 'rxjs';
import { ProblemDetails } from '../auth/auth.models';

/**
 * Surfaces a ProblemDetails failure as a toast (section 13). Field-level validation errors
 * (the `errors` extension) are left for the calling component to render inline — this only
 * covers the generic "something went wrong" case.
 */
export const errorToastInterceptor: HttpInterceptorFn = (req, next) => {
  const snackBar = inject(MatSnackBar);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status !== 401) {
        const problem = error.error as ProblemDetails | undefined;
        const message = problem?.detail ?? problem?.title ?? 'An unexpected error occurred.';
        snackBar.open(message, 'Dismiss', { duration: 6000 });
      }

      return throwError(() => error);
    }),
  );
};
