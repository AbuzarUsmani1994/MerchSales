import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { BreakpointObserver } from '@angular/cdk/layout';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AuthService } from '../core/auth/auth.service';

interface NavEntry {
  label: string;
  icon: string;
  routerLink: string;
  permission: string;
  /** The API only accepts this permission from an all-branches role for these screens. */
  tenantWide?: boolean;
  feature?: string;
}

interface NavSection {
  title: string;
  entries: NavEntry[];
}

/** Built from the signed-in user's permissions/features (section 13). */
const NAV_SECTIONS: NavSection[] = [
  {
    title: 'Organisation',
    entries: [
      { label: 'Users', icon: 'group', routerLink: '/setup/users', permission: 'Users.View' },
      { label: 'Roles', icon: 'admin_panel_settings', routerLink: '/setup/roles', permission: 'Roles.Manage', tenantWide: true },
      { label: 'Branches', icon: 'storefront', routerLink: '/setup/branches', permission: 'Branches.Manage' },
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

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatSidenavModule, MatIconModule, MatButtonModule, MatTooltipModule],
  template: `
    <mat-sidenav-container class="shell">
      <mat-sidenav
        class="sidebar"
        [mode]="isCompact() ? 'over' : 'side'"
        [opened]="!isCompact() || menuOpen()"
        (closedStart)="menuOpen.set(false)"
      >
        <div class="sidebar-inner">
          <div class="brand">
            <span class="brand-mark">S</span>
            <div class="brand-text">
              <span class="brand-name">StockOrder</span>
              <span class="brand-tenant">{{ tenant()?.name ?? 'Back office' }}</span>
            </div>
          </div>

          <nav class="nav">
            @for (section of menuSections(); track section.title) {
              <div class="nav-section">
                <span class="nav-section-title">{{ section.title }}</span>
                @for (entry of section.entries; track entry.routerLink) {
                  <a
                    class="nav-link"
                    [routerLink]="entry.routerLink"
                    routerLinkActive="active"
                    (click)="menuOpen.set(false)"
                  >
                    <mat-icon class="nav-icon">{{ entry.icon }}</mat-icon>
                    <span>{{ entry.label }}</span>
                  </a>
                }
              </div>
            }
          </nav>

          <div class="user-card">
            <span class="avatar">{{ initials() }}</span>
            <div class="user-text">
              <span class="user-name">{{ me()?.displayName }}</span>
              <span class="user-meta">{{ me()?.username }}@{{ tenant()?.code }}</span>
            </div>
            <button mat-icon-button class="sign-out" matTooltip="Sign out" aria-label="Sign out" (click)="signOut()">
              <mat-icon>logout</mat-icon>
            </button>
          </div>
        </div>
      </mat-sidenav>

      <mat-sidenav-content class="main">
        <header class="topbar">
          @if (isCompact()) {
            <button mat-icon-button aria-label="Open menu" (click)="menuOpen.set(true)">
              <mat-icon>menu</mat-icon>
            </button>
          }
          <div class="crumbs">
            <span class="crumb-section">{{ currentSection() }}</span>
            @if (currentLabel()) {
              <mat-icon class="crumb-sep">chevron_right</mat-icon>
              <span class="crumb-page">{{ currentLabel() }}</span>
            }
          </div>
          <div class="topbar-right">
            <span class="scope-chip" [matTooltip]="branchTooltip()">
              <mat-icon>{{ allBranches() ? 'apartment' : 'store' }}</mat-icon>
              {{ branchLabel() }}
            </span>
          </div>
        </header>

        <main class="content">
          <router-outlet />
        </main>
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styles: `
    .shell { height: 100vh; background: var(--so-bg); }

    .sidebar {
      width: var(--so-sidebar-width);
      border: 0;
      border-radius: 0;
      background: linear-gradient(180deg, var(--so-navy-800) 0%, var(--so-navy-900) 100%);
      color: #fff;
    }
    .sidebar-inner { display: flex; flex-direction: column; height: 100%; padding: 1.25rem 0.9rem; box-sizing: border-box; }

    .brand { display: flex; align-items: center; gap: 0.75rem; padding: 0 0.5rem 1.5rem; }
    .brand-mark {
      width: 38px; height: 38px; border-radius: 11px; flex-shrink: 0;
      display: flex; align-items: center; justify-content: center;
      background: linear-gradient(135deg, #6366f1 0%, var(--so-accent) 100%);
      box-shadow: 0 6px 16px -4px rgba(79, 70, 229, 0.6);
      font-weight: 700; font-size: 1.1rem;
    }
    .brand-text { display: flex; flex-direction: column; min-width: 0; }
    .brand-name { font-weight: 700; font-size: 1.05rem; letter-spacing: -0.02em; }
    .brand-tenant { font-size: 0.78rem; color: rgba(255, 255, 255, 0.55); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

    .nav { flex: 1; overflow-y: auto; }
    .nav-section { margin-bottom: 1.25rem; }
    .nav-section-title {
      display: block; padding: 0 0.75rem 0.4rem;
      font-size: 0.68rem; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase;
      color: rgba(255, 255, 255, 0.38);
    }
    .nav-link {
      position: relative;
      display: flex; align-items: center; gap: 0.75rem;
      padding: 0.6rem 0.75rem; margin-bottom: 2px;
      border-radius: 10px;
      color: rgba(255, 255, 255, 0.72);
      text-decoration: none; font-size: 0.92rem; font-weight: 500;
      transition: background-color 120ms ease, color 120ms ease;
    }
    .nav-link:hover { background: rgba(255, 255, 255, 0.06); color: #fff; }
    .nav-link.active { background: rgba(99, 102, 241, 0.22); color: #fff; }
    .nav-link.active::before {
      content: ''; position: absolute; left: -0.9rem; top: 8px; bottom: 8px; width: 3px;
      border-radius: 0 3px 3px 0; background: #818cf8;
    }
    .nav-icon { width: 20px; height: 20px; font-size: 20px; opacity: 0.9; }
    .nav-link.active .nav-icon { color: #a5b4fc; }

    .user-card {
      display: flex; align-items: center; gap: 0.65rem;
      padding: 0.7rem; margin-top: 0.5rem;
      border-radius: 12px; background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.06);
    }
    .avatar {
      width: 36px; height: 36px; border-radius: 50%; flex-shrink: 0;
      display: flex; align-items: center; justify-content: center;
      background: var(--so-navy-600); color: #c7d2fe; font-weight: 700; font-size: 0.85rem;
    }
    .user-text { display: flex; flex-direction: column; min-width: 0; flex: 1; }
    .user-name { font-size: 0.88rem; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .user-meta { font-size: 0.75rem; color: rgba(255, 255, 255, 0.5); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .sign-out { color: rgba(255, 255, 255, 0.6); }
    .sign-out:hover { color: #fff; }

    .main { display: flex; flex-direction: column; background: var(--so-bg); }
    .topbar {
      position: sticky; top: 0; z-index: 2;
      display: flex; align-items: center; gap: 0.75rem;
      height: 64px; padding: 0 2rem;
      background: rgba(255, 255, 255, 0.85);
      backdrop-filter: saturate(180%) blur(8px);
      border-bottom: 1px solid var(--so-border);
    }
    .crumbs { display: flex; align-items: center; gap: 0.25rem; font-size: 0.9rem; min-width: 0; }
    .crumb-section { color: var(--so-text-muted); }
    .crumb-sep { width: 18px; height: 18px; font-size: 18px; color: var(--so-text-faint); }
    .crumb-page { font-weight: 600; color: var(--so-text); }
    .topbar-right { margin-left: auto; display: flex; align-items: center; gap: 0.5rem; }
    .scope-chip {
      display: inline-flex; align-items: center; gap: 0.4rem;
      padding: 0.35rem 0.75rem; border-radius: 999px;
      background: var(--so-accent-soft); color: var(--so-accent-strong);
      font-size: 0.82rem; font-weight: 600; white-space: nowrap;
    }
    .scope-chip .mat-icon { width: 18px; height: 18px; font-size: 18px; }

    .content { padding: 2rem; flex: 1; }

    @media (max-width: 900px) {
      .topbar { padding: 0 1rem; }
      .content { padding: 1.25rem 1rem; }
      .crumb-section, .crumb-sep { display: none; }
    }
  `,
})
export class LayoutComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly me = this.auth.me;
  readonly tenant = this.auth.tenant;
  readonly menuOpen = signal(false);

  readonly isCompact = toSignal(
    inject(BreakpointObserver).observe('(max-width: 900px)').pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  readonly menuSections = computed<NavSection[]>(() =>
    NAV_SECTIONS.map((section) => ({
      ...section,
      entries: section.entries.filter(
        (entry) =>
          (entry.tenantWide ? this.auth.hasTenantWidePermission(entry.permission) : this.auth.hasPermission(entry.permission)) &&
          (!entry.feature || this.auth.hasFeature(entry.feature)),
      ),
    })).filter((section) => section.entries.length > 0),
  );

  private readonly currentEntry = computed(() => {
    const url = this.url();
    for (const section of NAV_SECTIONS) {
      const entry = section.entries.find((e) => url.startsWith(e.routerLink));
      if (entry) {
        return { section: section.title, label: entry.label };
      }
    }
    return null;
  });

  readonly currentSection = computed(() => this.currentEntry()?.section ?? 'Home');
  readonly currentLabel = computed(() => this.currentEntry()?.label ?? '');

  readonly initials = computed(() => {
    const name = this.me()?.displayName ?? '';
    const parts = name.split(/\s+/).filter(Boolean);
    return ((parts[0]?.[0] ?? '') + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase() || '?';
  });

  /** Someone with any all-branches role sees every branch. */
  readonly allBranches = computed(() => this.auth.tenantWidePermissions().length > 0);

  readonly branchLabel = computed(() => {
    if (this.allBranches()) {
      return 'All branches';
    }
    const branches = this.auth.branches();
    return branches.length === 1 ? branches[0].name : `${branches.length} branches`;
  });

  readonly branchTooltip = computed(() => this.auth.branches().map((b) => b.name).join(', '));

  async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
