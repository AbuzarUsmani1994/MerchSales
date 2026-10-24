import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
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
  imports: [MatCheckboxModule, MatButtonModule, MatProgressBarModule],
  template: `
    <h2>Roles &amp; permission matrix</h2>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" />
    }
    <div class="matrix-scroll">
      <table class="matrix">
        <thead>
          <tr>
            <th>Permission</th>
            @for (role of roles(); track role.id) {
              <th>{{ role.name }}</th>
            }
          </tr>
        </thead>
        <tbody>
          @for (permission of catalogue(); track permission.code) {
            <tr>
              <td>{{ permission.code }}</td>
              @for (role of roles(); track role.id) {
                <td class="center">
                  <mat-checkbox
                    [checked]="role.permissionCodes.includes(permission.code)"
                    (change)="toggle(role, permission.code, $event.checked)"
                  />
                </td>
              }
            </tr>
          }
        </tbody>
      </table>
    </div>
    <button mat-flat-button color="primary" (click)="save()" [disabled]="saving()" class="mt-3">Save changes</button>
  `,
  styles: `
    .matrix-scroll { overflow-x: auto; }
    .matrix { border-collapse: collapse; width: 100%; }
    .matrix th, .matrix td { padding: 0.4rem 0.75rem; border-bottom: 1px solid #e0e0e0; white-space: nowrap; text-align: left; }
    .center { text-align: center; }
    .mt-3 { margin-top: 1rem; }
  `,
})
export class RolesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  readonly roles = signal<RoleSummary[]>([]);
  readonly catalogue = signal<PermissionCatalogueEntry[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  private readonly dirtyRoleIds = new Set<string>();

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
    this.dirtyRoleIds.add(role.id);
  }

  async save(): Promise<void> {
    this.saving.set(true);
    try {
      const dirtyRoles = this.roles().filter((r) => this.dirtyRoleIds.has(r.id));
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
      this.dirtyRoleIds.clear();
    } finally {
      this.saving.set(false);
    }
  }
}
