import { Routes } from '@angular/router';
import { authGuard, permissionGuard } from './core/auth/auth.guard';
import { homeRedirectGuard } from './layout/nav';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'change-password',
    loadComponent: () => import('./features/auth/change-password.component').then((m) => m.ChangePasswordComponent),
    canActivate: [authGuard],
  },
  {
    path: '',
    loadComponent: () => import('./layout/layout.component').then((m) => m.LayoutComponent),
    canActivate: [authGuard],
    children: [
      {
        path: 'setup/users',
        loadComponent: () => import('./features/setup/users.component').then((m) => m.UsersComponent),
        canActivate: [permissionGuard('Users.View')],
      },
      {
        path: 'setup/roles',
        loadComponent: () => import('./features/setup/roles.component').then((m) => m.RolesComponent),
        canActivate: [permissionGuard('Roles.Manage', { tenantWide: true })],
      },
      {
        path: 'setup/branches',
        loadComponent: () => import('./features/setup/branches.component').then((m) => m.BranchesComponent),
        canActivate: [permissionGuard('Branches.Manage')],
      },
      {
        path: 'setup/features',
        loadComponent: () =>
          import('./features/setup/feature-toggles.component').then((m) => m.FeatureTogglesComponent),
        canActivate: [permissionGuard('Settings.Manage', { tenantWide: true })],
      },
      {
        path: 'setup/devices',
        loadComponent: () => import('./features/setup/devices.component').then((m) => m.DevicesComponent),
        canActivate: [permissionGuard('Devices.Manage')],
      },
      {
        path: 'sales/orders',
        loadComponent: () => import('./features/sales/orders.component').then((m) => m.OrdersComponent),
        canActivate: [permissionGuard('Sales.View')],
      },
      {
        path: 'sales/rewards',
        loadComponent: () => import('./features/sales/rewards.component').then((m) => m.RewardsComponent),
        canActivate: [permissionGuard('Sales.Loyalty.Manage')],
      },
      {
        path: 'catalog/menu',
        loadComponent: () => import('./features/catalog/menu.component').then((m) => m.MenuComponent),
        canActivate: [permissionGuard('Catalog.View')],
      },
      {
        path: 'catalog/categories',
        loadComponent: () => import('./features/catalog/categories.component').then((m) => m.CategoriesComponent),
        canActivate: [permissionGuard('Catalog.Manage', { tenantWide: true })],
      },
      {
        path: 'platform/tenants',
        loadComponent: () =>
          import('./features/platform/platform-tenants.component').then((m) => m.PlatformTenantsComponent),
        canActivate: [permissionGuard('Platform.Tenants.Manage')],
      },
      {
        path: '',
        pathMatch: 'full',
        canActivate: [homeRedirectGuard],
        loadComponent: () => import('./features/home/no-access.component').then((m) => m.NoAccessComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
