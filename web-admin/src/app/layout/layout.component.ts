import { Component, computed, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { MenuModule } from 'primeng/menu';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { MenuItem } from 'primeng/api';
import { AuthService } from '../core/auth/auth.service';

interface NavEntry {
  label: string;
  icon: string;
  routerLink: string;
  permission: string;
  feature?: string;
}

/** Built from the signed-in user's permissions/features (section 13). */
const NAV_ENTRIES: NavEntry[] = [
  { label: 'Users', icon: 'pi pi-users', routerLink: '/setup/users', permission: 'Users.View' },
  { label: 'Roles', icon: 'pi pi-shield', routerLink: '/setup/roles', permission: 'Roles.Manage' },
  { label: 'Branches', icon: 'pi pi-building', routerLink: '/setup/branches', permission: 'Branches.Manage' },
  { label: 'Feature toggles', icon: 'pi pi-sliders-h', routerLink: '/setup/features', permission: 'Settings.Manage' },
  { label: 'Devices', icon: 'pi pi-mobile', routerLink: '/setup/devices', permission: 'Devices.Manage' },
  { label: 'Platform tenants', icon: 'pi pi-globe', routerLink: '/platform/tenants', permission: 'Platform.Tenants.Manage' },
];

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [RouterOutlet, MenuModule, ButtonModule, ToastModule],
  template: `
    <p-toast />
    <div class="shell">
      <aside class="sidebar">
        <div class="brand">StockOrder</div>
        <p-menu [model]="menuItems()" />
        <p-button
          label="Sign out"
          icon="pi pi-sign-out"
          [text]="true"
          styleClass="sign-out"
          (onClick)="signOut()"
        />
      </aside>
      <main class="content">
        <router-outlet />
      </main>
    </div>
  `,
  styles: `
    .shell { display: flex; height: 100vh; }
    .sidebar { width: 240px; background: #1e293b; color: white; display: flex; flex-direction: column; padding: 1rem; }
    .brand { font-size: 1.25rem; font-weight: 600; margin-bottom: 1rem; }
    .content { flex: 1; overflow: auto; padding: 1.5rem; background: #f4f6f8; }
    .sign-out { margin-top: auto; }
  `,
})
export class LayoutComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly menuItems = computed<MenuItem[]>(() =>
    NAV_ENTRIES.filter(
      (entry) => this.auth.hasPermission(entry.permission) && (!entry.feature || this.auth.hasFeature(entry.feature)),
    ).map((entry) => ({ label: entry.label, icon: entry.icon, routerLink: entry.routerLink })),
  );

  async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
