import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { MatTableModule } from '@angular/material/table';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { ConfigService } from '../../core/config.service';
import { AuthService } from '../../core/auth/auth.service';

interface UserRoleAssignment {
  userRoleId: string;
  roleId: string;
  roleName: string;
  branchId: string | null;
}

interface UserSummary {
  id: string;
  username: string;
  displayName: string;
  email: string | null;
  phone: string | null;
  isActive: boolean;
  mfaEnabled: boolean;
  defaultBranchId: string | null;
  roles: UserRoleAssignment[];
  canManage: boolean;
}

interface AssignableRole {
  id: string;
  name: string;
  description: string | null;
}

interface CreateUserResult {
  userId: string;
  oneTimePassword: string;
}

/** Sentinel for the "All branches" option; mat-select can't use null as a value reliably. */
const ALL_BRANCHES = '__all__';

/**
 * Branch + role pickers shared by the new-user and assign-role dialogs. The role list comes
 * from the API (`assignable-roles`), so it only ever offers roles this admin may hand out at
 * the chosen branch — a branch manager sees staff roles for their own branch only.
 */
abstract class BranchRolePicker {
  protected readonly http = inject(HttpClient);
  protected readonly config = inject(ConfigService);
  protected readonly auth = inject(AuthService);

  readonly allBranches = ALL_BRANCHES;
  readonly branches = this.auth.branches;
  readonly canAssignAllBranches = computed(() => this.auth.hasTenantWidePermission('Users.Manage'));
  readonly roles = signal<AssignableRole[]>([]);

  protected async loadRoles(branchValue: string): Promise<void> {
    this.roles.set([]);
    let params = new HttpParams();
    if (branchValue && branchValue !== ALL_BRANCHES) {
      params = params.set('branchId', branchValue);
    }
    this.roles.set(
      await firstValueFrom(
        this.http.get<AssignableRole[]>(`${this.config.apiBaseUrl}/api/v1/identity/users/assignable-roles`, { params }),
      ),
    );
  }

  protected static toBranchId(branchValue: string): string | null {
    return branchValue === ALL_BRANCHES ? null : branchValue;
  }
}

@Component({
  selector: 'app-new-user-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>New user</h2>
    <mat-dialog-content>
      <form [formGroup]="form" (ngSubmit)="submit()">
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Username</mat-label>
          <input matInput formControlName="username" autocomplete="off" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Display name</mat-label>
          <input matInput formControlName="displayName" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Email</mat-label>
          <input matInput formControlName="email" type="email" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Phone</mat-label>
          <input matInput formControlName="phone" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Branch</mat-label>
          <mat-select formControlName="branch" (selectionChange)="branchChanged($event.value)">
            @if (canAssignAllBranches()) {
              <mat-option [value]="allBranches">All branches</mat-option>
            }
            @for (branch of branches(); track branch.id) {
              <mat-option [value]="branch.id">{{ branch.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Role</mat-label>
          <mat-select formControlName="roleId">
            @for (role of roles(); track role.id) {
              <mat-option [value]="role.id">{{ role.name }}</mat-option>
            }
          </mat-select>
          <mat-hint>The role applies at the branch above.</mat-hint>
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button mat-flat-button color="primary" [disabled]="form.invalid" (click)="submit()">Create</button>
    </mat-dialog-actions>
  `,
  styles: `.full-width { width: 100%; }`,
})
export class NewUserDialogComponent extends BranchRolePicker {
  readonly dialogRef = inject(MatDialogRef<NewUserDialogComponent>);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    username: ['', [Validators.required, Validators.maxLength(100)]],
    displayName: ['', [Validators.required, Validators.maxLength(200)]],
    email: ['', [Validators.email, Validators.maxLength(256)]],
    phone: ['', Validators.maxLength(40)],
    branch: ['', Validators.required],
    roleId: ['', Validators.required],
  });

  async branchChanged(branchValue: string): Promise<void> {
    this.form.controls.roleId.setValue('');
    await this.loadRoles(branchValue);
  }

  submit(): void {
    if (this.form.invalid) {
      return;
    }
    const value = this.form.getRawValue();
    this.dialogRef.close({
      username: value.username.trim(),
      displayName: value.displayName.trim(),
      email: value.email.trim() || null,
      phone: value.phone.trim() || null,
      defaultBranchId: BranchRolePicker.toBranchId(value.branch),
      roleId: value.roleId,
    });
  }
}

@Component({
  selector: 'app-assign-role-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatSelectModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Assign role to {{ data.displayName }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" (ngSubmit)="submit()">
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Branch</mat-label>
          <mat-select formControlName="branch" (selectionChange)="branchChanged($event.value)">
            @if (canAssignAllBranches()) {
              <mat-option [value]="allBranches">All branches</mat-option>
            }
            @for (branch of branches(); track branch.id) {
              <mat-option [value]="branch.id">{{ branch.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Role</mat-label>
          <mat-select formControlName="roleId">
            @for (role of roles(); track role.id) {
              <mat-option [value]="role.id">{{ role.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button mat-flat-button color="primary" [disabled]="form.invalid" (click)="submit()">Assign</button>
    </mat-dialog-actions>
  `,
  styles: `.full-width { width: 100%; }`,
})
export class AssignRoleDialogComponent extends BranchRolePicker {
  readonly dialogRef = inject(MatDialogRef<AssignRoleDialogComponent>);
  readonly data = inject<UserSummary>(MAT_DIALOG_DATA);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    branch: ['', Validators.required],
    roleId: ['', Validators.required],
  });

  async branchChanged(branchValue: string): Promise<void> {
    this.form.controls.roleId.setValue('');
    await this.loadRoles(branchValue);
  }

  submit(): void {
    if (this.form.invalid) {
      return;
    }
    const value = this.form.getRawValue();
    this.dialogRef.close({ userId: this.data.id, roleId: value.roleId, branchId: BranchRolePicker.toBranchId(value.branch) });
  }
}

@Component({
  selector: 'app-one-time-password-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>One-time password</h2>
    <mat-dialog-content>
      <p>Give this password to <strong>{{ data.username }}</strong>. It is shown only once; they must change it at first sign-in.</p>
      <p class="otp">{{ data.oneTimePassword }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-flat-button color="primary" mat-dialog-close>Done</button>
    </mat-dialog-actions>
  `,
  styles: `.otp { font-family: monospace; font-size: 1.4rem; letter-spacing: 0.1em; user-select: all; }`,
})
export class OneTimePasswordDialogComponent {
  readonly data = inject<{ username: string; oneTimePassword: string }>(MAT_DIALOG_DATA);
}

@Component({
  selector: 'app-users',
  standalone: true,
  imports: [MatTableModule, MatChipsModule, MatProgressBarModule, MatButtonModule, MatIconModule],
  template: `
    <h2>Users</h2>
    @if (canManageUsers()) {
      <button mat-flat-button color="primary" (click)="openNewUserDialog()" class="mb-3">New user</button>
    }
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" />
    }
    <table mat-table [dataSource]="users()" class="mat-elevation-z1">
      <ng-container matColumnDef="username">
        <th mat-header-cell *matHeaderCellDef>Username</th>
        <td mat-cell *matCellDef="let user">{{ user.username }}</td>
      </ng-container>
      <ng-container matColumnDef="displayName">
        <th mat-header-cell *matHeaderCellDef>Display name</th>
        <td mat-cell *matCellDef="let user">{{ user.displayName }}</td>
      </ng-container>
      <ng-container matColumnDef="roles">
        <th mat-header-cell *matHeaderCellDef>Roles</th>
        <td mat-cell *matCellDef="let user">
          <mat-chip-set>
            @for (role of user.roles; track role.userRoleId) {
              <mat-chip [removable]="user.canManage" (removed)="removeRole(user, role)">
                {{ role.roleName }} · {{ branchName(role.branchId) }}
                @if (user.canManage) {
                  <button matChipRemove [attr.aria-label]="'Remove ' + role.roleName">
                    <mat-icon>cancel</mat-icon>
                  </button>
                }
              </mat-chip>
            } @empty {
              <span class="muted">No roles</span>
            }
          </mat-chip-set>
        </td>
      </ng-container>
      <ng-container matColumnDef="status">
        <th mat-header-cell *matHeaderCellDef>Status</th>
        <td mat-cell *matCellDef="let user">
          <mat-chip [color]="user.isActive ? 'primary' : 'warn'" highlighted>
            {{ user.isActive ? 'Active' : 'Inactive' }}
          </mat-chip>
        </td>
      </ng-container>
      <ng-container matColumnDef="mfa">
        <th mat-header-cell *matHeaderCellDef>MFA</th>
        <td mat-cell *matCellDef="let user">{{ user.mfaEnabled ? 'Enabled' : '—' }}</td>
      </ng-container>
      <ng-container matColumnDef="actions">
        <th mat-header-cell *matHeaderCellDef></th>
        <td mat-cell *matCellDef="let user" class="actions">
          @if (user.canManage) {
            <button mat-button (click)="openAssignRoleDialog(user)">Assign role</button>
            <button mat-button (click)="resetPassword(user)">Reset password</button>
          }
        </td>
      </ng-container>

      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns"></tr>
    </table>
  `,
  styles: `
    table { width: 100%; }
    .mb-3 { margin-bottom: 1rem; }
    .muted { color: #757575; }
    .actions { white-space: nowrap; text-align: right; }
  `,
})
export class UsersComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);

  readonly users = signal<UserSummary[]>([]);
  readonly loading = signal(true);
  readonly columns = ['username', 'displayName', 'roles', 'status', 'mfa', 'actions'];
  readonly canManageUsers = computed(() => this.auth.hasPermission('Users.Manage'));

  private get usersUrl(): string {
    return `${this.config.apiBaseUrl}/api/v1/identity/users`;
  }

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  branchName(branchId: string | null): string {
    if (branchId === null) {
      return 'All branches';
    }
    return this.auth.branches().find((b) => b.id === branchId)?.name ?? 'Other branch';
  }

  openNewUserDialog(): void {
    const ref = this.dialog.open(NewUserDialogComponent, { width: '420px' });
    ref.afterClosed().subscribe(async (request) => {
      if (!request) {
        return;
      }
      const result = await firstValueFrom(this.http.post<CreateUserResult>(this.usersUrl, request));
      this.showOneTimePassword(request.username, result.oneTimePassword);
      await this.load();
    });
  }

  openAssignRoleDialog(user: UserSummary): void {
    const ref = this.dialog.open(AssignRoleDialogComponent, { width: '420px', data: user });
    ref.afterClosed().subscribe(async (request) => {
      if (!request) {
        return;
      }
      await firstValueFrom(this.http.post(`${this.usersUrl}/roles`, request));
      await this.load();
    });
  }

  async removeRole(user: UserSummary, role: UserRoleAssignment): Promise<void> {
    if (!confirm(`Remove ${role.roleName} (${this.branchName(role.branchId)}) from ${user.displayName}?`)) {
      return;
    }
    await firstValueFrom(this.http.delete(`${this.usersUrl}/roles/${role.userRoleId}`));
    await this.load();
  }

  async resetPassword(user: UserSummary): Promise<void> {
    if (!confirm(`Reset the password for ${user.displayName}? Their current password stops working.`)) {
      return;
    }
    // A bare string result is sent as text/plain by ASP.NET Core, not as a JSON string.
    const oneTimePassword = await firstValueFrom(
      this.http.post(`${this.usersUrl}/${user.id}/reset-password`, {}, { responseType: 'text' }),
    );
    this.showOneTimePassword(user.username, oneTimePassword);
  }

  private showOneTimePassword(username: string, oneTimePassword: string): void {
    this.dialog.open(OneTimePasswordDialogComponent, { width: '420px', data: { username, oneTimePassword } });
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.users.set(await firstValueFrom(this.http.get<UserSummary[]>(this.usersUrl)));
    } finally {
      this.loading.set(false);
    }
  }
}
