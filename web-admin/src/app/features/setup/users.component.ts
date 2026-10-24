import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { MatTableModule } from '@angular/material/table';
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
        <div class="form-row">
          <mat-form-field appearance="outline">
            <mat-label>Email</mat-label>
            <input matInput formControlName="email" type="email" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Phone</mat-label>
            <input matInput formControlName="phone" />
          </mat-form-field>
        </div>
        <p class="form-section-title">Access</p>
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
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  template: `
    <h2 mat-dialog-title>One-time password</h2>
    <mat-dialog-content>
      <p>Give this password to <strong>{{ data.username }}</strong>. It is shown only once; they must change it at first sign-in.</p>
      <div class="otp-box">
        <span class="otp mono">{{ data.oneTimePassword }}</span>
        <button mat-icon-button (click)="copy()" [attr.aria-label]="copied() ? 'Copied' : 'Copy password'">
          <mat-icon>{{ copied() ? 'check' : 'content_copy' }}</mat-icon>
        </button>
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-flat-button color="primary" mat-dialog-close>Done</button>
    </mat-dialog-actions>
  `,
  styles: `
    .otp-box {
      display: flex; align-items: center; justify-content: space-between; gap: 0.5rem;
      padding: 0.6rem 0.6rem 0.6rem 1rem; border-radius: 10px;
      background: var(--so-accent-soft); border: 1px dashed #a5b4fc;
    }
    .otp { font-size: 1.35rem; letter-spacing: 0.12em; color: var(--so-accent-strong); user-select: all; }
  `,
})
export class OneTimePasswordDialogComponent {
  readonly data = inject<{ username: string; oneTimePassword: string }>(MAT_DIALOG_DATA);
  readonly copied = signal(false);

  async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.data.oneTimePassword);
      this.copied.set(true);
    } catch {
      // Clipboard needs a secure context (HTTPS); the text is still selectable by hand.
    }
  }
}

@Component({
  selector: 'app-users',
  standalone: true,
  imports: [MatTableModule, MatProgressBarModule, MatButtonModule, MatIconModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1 class="page-title">Users</h1>
          <p class="page-subtitle">People who can sign in, and the role they hold at each branch.</p>
        </div>
        @if (canManageUsers()) {
          <div class="page-actions">
            <button mat-flat-button color="primary" (click)="openNewUserDialog()">
              <mat-icon>person_add</mat-icon>
              New user
            </button>
          </div>
        }
      </div>

      <div class="table-card">
        @if (loading()) {
          <mat-progress-bar mode="indeterminate" />
        }
        <table mat-table [dataSource]="users()">
          <ng-container matColumnDef="user">
            <th mat-header-cell *matHeaderCellDef>User</th>
            <td mat-cell *matCellDef="let user">
              <div class="user-cell">
                <span class="avatar">{{ initials(user.displayName) }}</span>
                <div>
                  <span class="cell-primary">{{ user.displayName }}</span>
                  <span class="cell-secondary">{{ user.username }}{{ user.email ? ' · ' + user.email : '' }}</span>
                </div>
              </div>
            </td>
          </ng-container>
          <ng-container matColumnDef="roles">
            <th mat-header-cell *matHeaderCellDef>Roles</th>
            <td mat-cell *matCellDef="let user">
              @for (role of user.roles; track role.userRoleId) {
                <span class="tag" [class.tag-static]="!user.canManage">
                  {{ role.roleName }} <span class="tag-scope">· {{ branchName(role.branchId) }}</span>
                  @if (user.canManage) {
                    <button type="button" (click)="removeRole(user, role)" [attr.aria-label]="'Remove ' + role.roleName">
                      <mat-icon>close</mat-icon>
                    </button>
                  }
                </span>
              } @empty {
                <span class="muted">No roles</span>
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>Status</th>
            <td mat-cell *matCellDef="let user">
              <span class="pill" [class.pill-success]="user.isActive">{{ user.isActive ? 'Active' : 'Inactive' }}</span>
            </td>
          </ng-container>
          <ng-container matColumnDef="mfa">
            <th mat-header-cell *matHeaderCellDef>MFA</th>
            <td mat-cell *matCellDef="let user">
              @if (user.mfaEnabled) {
                <span class="pill pill-info">On</span>
              } @else {
                <span class="muted">—</span>
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef></th>
            <td mat-cell *matCellDef="let user" class="cell-actions">
              @if (user.canManage) {
                <button mat-button (click)="openAssignRoleDialog(user)">
                  <mat-icon>add_moderator</mat-icon>
                  Assign role
                </button>
                <button mat-button (click)="resetPassword(user)">
                  <mat-icon>key</mat-icon>
                  Reset password
                </button>
              }
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>
        @if (!loading() && users().length === 0) {
          <div class="empty-state">
            <mat-icon>group_off</mat-icon>
            <div>No users yet.</div>
          </div>
        }
      </div>
    </div>
  `,
  styles: `
    .user-cell { display: flex; align-items: center; gap: 0.75rem; padding: 0.5rem 0; }
    .avatar {
      width: 36px; height: 36px; border-radius: 50%; flex-shrink: 0;
      display: flex; align-items: center; justify-content: center;
      background: var(--so-accent-soft); color: var(--so-accent-strong); font-weight: 700; font-size: 0.8rem;
    }
  `,
})
export class UsersComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);

  readonly users = signal<UserSummary[]>([]);
  readonly loading = signal(true);
  readonly columns = ['user', 'roles', 'status', 'mfa', 'actions'];
  readonly canManageUsers = computed(() => this.auth.hasPermission('Users.Manage'));

  private get usersUrl(): string {
    return `${this.config.apiBaseUrl}/api/v1/identity/users`;
  }

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  initials(name: string): string {
    const parts = name.split(/\s+/).filter(Boolean);
    return ((parts[0]?.[0] ?? '') + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase();
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
