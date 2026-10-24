import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isAuthenticated()) {
    return router.createUrlTree(['/login']);
  }

  // After a page reload the token survives in sessionStorage but the profile (permissions,
  // features, branches) doesn't; load it before any permission-guarded route or menu runs.
  if (auth.me() === null) {
    try {
      await auth.loadMe();
    } catch {
      await auth.logout();
      return router.createUrlTree(['/login']);
    }
  }

  return true;
};

/**
 * Hidden != secure (section 13): this only controls menu/route visibility. The API is the
 * real enforcement point and re-checks every request regardless of what the UI shows.
 */
export const permissionGuard =
  (permissionCode: string, options: { tenantWide?: boolean } = {}): CanActivateFn =>
  () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const allowed = options.tenantWide
      ? auth.hasTenantWidePermission(permissionCode)
      : auth.hasPermission(permissionCode);
    return allowed ? true : router.createUrlTree(['/']);
  };
