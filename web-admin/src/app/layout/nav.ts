import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';

export interface NavEntry {
  label: string;
  icon: string;
  routerLink: string;
  permission: string;
  /** The API only accepts this permission from an all-branches role for these screens. */
  tenantWide?: boolean;
  feature?: string;
}

export interface NavSection {
  title: string;
  entries: NavEntry[];
}

/** Built from the signed-in user's permissions/features (section 13). */
export const NAV_SECTIONS: NavSection[] = [
  {
    title: 'Organisation',
    entries: [
      { label: 'Users', icon: 'group', routerLink: '/setup/users', permission: 'Users.View' },
      { label: 'Roles', icon: 'admin_panel_settings', routerLink: '/setup/roles', permission: 'Roles.Manage', tenantWide: true },
      { label: 'Branches', icon: 'storefront', routerLink: '/setup/branches', permission: 'Branches.Manage' },
    ],
  },
  {
    title: 'Sales',
    entries: [
      { label: 'Orders', icon: 'receipt_long', routerLink: '/sales/orders', permission: 'Sales.View' },
    ],
  },
  {
    title: 'Catalog',
    entries: [
      { label: 'Menu', icon: 'restaurant_menu', routerLink: '/catalog/menu', permission: 'Catalog.View' },
      { label: 'Categories', icon: 'category', routerLink: '/catalog/categories', permission: 'Catalog.Manage', tenantWide: true },
    ],
  },
  {
    title: 'Configuration',
    entries: [
      { label: 'Feature toggles', icon: 'toggle_on', routerLink: '/setup/features', permission: 'Settings.Manage', tenantWide: true },
      { label: 'Devices', icon: 'smartphone', routerLink: '/setup/devices', permission: 'Devices.Manage' },
    ],
  },
  {
    title: 'Platform',
    entries: [
      { label: 'Tenants', icon: 'domain', routerLink: '/platform/tenants', permission: 'Platform.Tenants.Manage' },
    ],
  },
];

/** The menu as the signed-in user may see it (hidden != secure: the API re-checks everything). */
export function visibleNavSections(auth: AuthService): NavSection[] {
  return NAV_SECTIONS.map((section) => ({
    ...section,
    entries: section.entries.filter(
      (entry) =>
        (entry.tenantWide ? auth.hasTenantWidePermission(entry.permission) : auth.hasPermission(entry.permission)) &&
        (!entry.feature || auth.hasFeature(entry.feature)),
    ),
  })).filter((section) => section.entries.length > 0);
}

/**
 * "/" opens the first screen the user can use (a cashier lands on Orders, not Users). A fixed
 * redirect would loop for anyone the target's guard turns away, since guards send them to "/".
 */
export const homeRedirectGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const first = visibleNavSections(auth)[0]?.entries[0];
  return first ? inject(Router).createUrlTree([first.routerLink]) : true;
};
