import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
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
  { label: 'Users', icon: 'people', routerLink: '/setup/users', permission: 'Users.View' },
  { label: 'Roles', icon: 'shield', routerLink: '/setup/roles', permission: 'Roles.Manage' },
  { label: 'Branches', icon: 'store', routerLink: '/setup/branches', permission: 'Branches.Manage' },
  { label: 'Feature toggles', icon: 'tune', routerLink: '/setup/features', permission: 'Settings.Manage' },
  { label: 'Devices', icon: 'smartphone', routerLink: '/setup/devices', permission: 'Devices.Manage' },
  { label: 'Platform tenants', icon: 'public', routerLink: '/platform/tenants', permission: 'Platform.Tenants.Manage' },
];

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [RouterOutlet, RouterLink, MatToolbarModule, MatSidenavModule, MatListModule, MatIconModule, MatButtonModule],
  template: `
    <mat-sidenav-container class="shell">
      <mat-sidenav mode="side" opened class="sidebar">
        <div class="brand">StockOrder</div>
        <mat-nav-list>
          @for (entry of menuItems(); track entry.routerLink) {
            <a mat-list-item [routerLink]="entry.routerLink">
              <mat-icon matListItemIcon>{{ entry.icon }}</mat-icon>
              <span matListItemTitle>{{ entry.label }}</span>
            </a>
          }
        </mat-nav-list>
        <button mat-button class="sign-out" (click)="signOut()">
          <mat-icon>logout</mat-icon>
          Sign out
        </button>
      </mat-sidenav>
      <mat-sidenav-content class="content">
        <router-outlet />
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styles: `
    .shell { height: 100vh; }
    .sidebar { width: 240px; display: flex; flex-direction: column; padding: 1rem; box-sizing: border-box; }
    .brand { font-size: 1.25rem; font-weight: 600; margin-bottom: 1rem; }
    .content { padding: 1.5rem; background: #f4f6f8; }
    .sign-out { margin-top: auto; justify-content: flex-start; }
  `,
})
export class LayoutComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly menuItems = computed<NavEntry[]>(() =>
    NAV_ENTRIES.filter(
      (entry) => this.auth.hasPermission(entry.permission) && (!entry.feature || this.auth.hasFeature(entry.feature)),
    ),
  );

  async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
