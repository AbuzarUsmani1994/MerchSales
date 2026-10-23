import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TableModule } from 'primeng/table';
import { CheckboxModule } from 'primeng/checkbox';
import { ButtonModule } from 'primeng/button';
import { FormsModule } from '@angular/forms';
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
  imports: [TableModule, CheckboxModule, ButtonModule, FormsModule],
  template: `
    <h2>Roles &amp; permission matrix</h2>
    <p-table [value]="catalogue()" [loading]="loading()" [scrollable]="true">
      <ng-template pTemplate="header">
        <tr>
          <th>Permission</th>
          @for (role of roles(); track role.id) {
            <th>{{ role.name }}</th>
          }
        </tr>
      </ng-template>
      <ng-template pTemplate="body" let-permission>
        <tr>
          <td>{{ permission.code }}</td>
          @for (role of roles(); track role.id) {
            <td>
              <p-checkbox
                [binary]="true"
                [ngModel]="role.permissionCodes.includes(permission.code)"
                (onChange)="toggle(role, permission.code, $event.checked)"
              />
            </td>
          }
        </tr>
      </ng-template>
    </p-table>
    <p-button label="Save changes" icon="pi pi-save" (onClick)="save()" [loading]="saving()" styleClass="mt-3" />
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
