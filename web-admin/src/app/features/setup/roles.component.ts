import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatIconModule } from '@angular/material/icon';
import { ConfigService } from '../../core/config.service';

interface RoleSummary {
  id: string;
  name: string;
  description: string | null;
  isSystem: boolean;
  permissionCodes: string[];
}

interface PermissionCatalogueEntry {
  code: string;
  module: string;
  description: string;
  isPlatformOnly: boolean;
}

/** Section 13 "Setup" screen: the role x permission matrix. */
@Component({
  selector: 'app-roles',
  standalone: true,
  imports: [MatCheckboxModule, MatButtonModule, MatIconModule, MatProgressBarModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1 class="page-title">Roles &amp; permissions</h1>
          <p class="page-subtitle">Tick what each role may do. Changes apply to everyone holding the role, at every branch.</p>
        </div>
        <div class="page-actions">
          @if (dirtyCount() > 0) {
            <span class="pill pill-warning">{{ dirtyCount() }} unsaved {{ dirtyCount() === 1 ? 'role' : 'roles' }}</span>
          }
          <button mat-flat-button color="primary" (click)="save()" [disabled]="saving() || dirtyCount() === 0">
            <mat-icon>save</mat-icon>
            Save changes
          </button>
        </div>
      </div>

      <div class="table-card">
        @if (loading()) {
          <mat-progress-bar mode="indeterminate" />
        }
        <div class="matrix-scroll">
          <table class="matrix">
            <thead>
              <tr>
                <th class="perm-col">Permission</th>
                @for (role of roles(); track role.id) {
                  <th class="role-col">{{ role.name }}</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (group of groups(); track group.module) {
                <tr class="module-row">
                  <td [attr.colspan]="roles().length + 1">{{ group.module }}</td>
                </tr>
                @for (permission of group.permissions; track permission.code) {
                  <tr>
                    <td class="perm-col">
                      <span class="perm-name">{{ permission.description }}</span>
                      <span class="perm-code mono">{{ permission.code }}</span>
                    </td>
                    @for (role of roles(); track role.id) {
                      <td class="center">
                        <mat-checkbox
                          [checked]="role.permissionCodes.includes(permission.code)"
                          (change)="toggle(role, permission.code, $event.checked)"
                          [attr.aria-label]="role.name + ': ' + permission.code"
                        />
                      </td>
                    }
                  </tr>
                }
              }
            </tbody>
          </table>
        </div>
      </div>
    </div>
  `,
  styles: `
    .matrix-scroll { overflow: auto; max-height: calc(100vh - 230px); }
    .matrix { border-collapse: separate; border-spacing: 0; width: 100%; font-size: 0.88rem; }
    .matrix th {
      position: sticky; top: 0; z-index: 2;
      background: var(--so-surface-muted);
      padding: 0.75rem; border-bottom: 1px solid var(--so-border);
      font-size: 0.75rem; font-weight: 600; text-transform: uppercase; letter-spacing: 0.04em;
      color: var(--so-text-muted); white-space: nowrap; text-align: center;
    }
    .matrix td { padding: 0.15rem 0.75rem; border-bottom: 1px solid var(--so-border); }
    .matrix tbody tr:not(.module-row):hover td { background: var(--so-surface-muted); }
    .matrix .perm-col { position: sticky; left: 0; z-index: 1; background: var(--so-surface); text-align: left; min-width: 280px; }
    .matrix th.perm-col { z-index: 3; background: var(--so-surface-muted); }
    .perm-name { display: block; font-weight: 500; color: var(--so-text); padding-top: 0.45rem; }
    .perm-code { display: block; color: var(--so-text-faint); font-size: 0.72rem; padding-bottom: 0.45rem; }
    .module-row td {
      position: sticky; left: 0;
      padding: 0.55rem 0.75rem; background: var(--so-accent-soft); color: var(--so-accent-strong);
      font-size: 0.75rem; font-weight: 700; letter-spacing: 0.06em; text-transform: uppercase;
    }
    .center { text-align: center; }
  `,
})
export class RolesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  readonly roles = signal<RoleSummary[]>([]);
  readonly catalogue = signal<PermissionCatalogueEntry[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  private readonly dirtyRoleIds = signal<ReadonlySet<string>>(new Set());
  readonly dirtyCount = computed(() => this.dirtyRoleIds().size);

  readonly groups = computed(() => {
    const byModule = new Map<string, PermissionCatalogueEntry[]>();
    for (const permission of this.catalogue()) {
      byModule.set(permission.module, [...(byModule.get(permission.module) ?? []), permission]);
    }
    return [...byModule.entries()].map(([module, permissions]) => ({ module, permissions }));
  });

  async ngOnInit(): Promise<void> {
    try {
      const [roles, catalogue] = await Promise.all([
        firstValueFrom(this.http.get<RoleSummary[]>(`${this.config.apiBaseUrl}/api/v1/identity/roles`)),
        firstValueFrom(
          this.http.get<PermissionCatalogueEntry[]>(`${this.config.apiBaseUrl}/api/v1/identity/roles/permissions`),
        ),
      ]);
      this.roles.set(roles);
      this.catalogue.set(catalogue.filter((p) => !p.isPlatformOnly));
    } finally {
      this.loading.set(false);
    }
  }

  toggle(role: RoleSummary, permissionCode: string, checked: boolean): void {
    role.permissionCodes = checked
      ? [...role.permissionCodes, permissionCode]
      : role.permissionCodes.filter((c) => c !== permissionCode);
    this.dirtyRoleIds.update((ids) => new Set([...ids, role.id]));
  }

  async save(): Promise<void> {
    this.saving.set(true);
    try {
      const dirtyRoles = this.roles().filter((r) => this.dirtyRoleIds().has(r.id));
      await Promise.all(
        dirtyRoles.map((role) =>
          firstValueFrom(
            this.http.put(
              `${this.config.apiBaseUrl}/api/v1/identity/roles/${role.id}/permissions`,
              role.permissionCodes,
            ),
          ),
        ),
      );
      this.dirtyRoleIds.set(new Set());
    } finally {
      this.saving.set(false);
    }
  }
}
