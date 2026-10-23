import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/login']);
};

/**
 * Hidden != secure (section 13): this only controls menu/route visibility. The API is the
 * real enforcement point and re-checks every request regardless of what the UI shows.
 */
export const permissionGuard =
  (permissionCode: string): CanActivateFn =>
  () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    return auth.hasPermission(permissionCode) ? true : router.createUrlTree(['/']);
  };
