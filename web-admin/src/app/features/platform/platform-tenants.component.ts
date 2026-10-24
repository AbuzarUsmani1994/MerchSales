import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { ConfigService } from '../../core/config.service';

interface TenantSummary {
  id: string;
  code: string;
  name: string;
  industry: string;
  status: string;
  createdAtUtc: string;
}

interface ProvisionTenantFormValue {
  code: string;
  name: string;
  industry: string;
  ownerUsername: string;
  ownerDisplayName: string;
  branchCode: string;
  branchName: string;
  warehouseCode: string;
  warehouseName: string;
}

@Component({
  selector: 'app-provision-tenant-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
  ],
  template: `
    <h2 mat-dialog-title>Provision tenant</h2>
    <mat-dialog-content>
      <form [formGroup]="form">
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Tenant code</mat-label>
          <input matInput formControlName="code" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Industry</mat-label>
          <mat-select formControlName="industry">
            @for (industry of industries; track industry) {
              <mat-option [value]="industry">{{ industry }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Owner username</mat-label>
          <input matInput formControlName="ownerUsername" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Owner display name</mat-label>
          <input matInput formControlName="ownerDisplayName" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>First branch code</mat-label>
          <input matInput formControlName="branchCode" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>First branch name</mat-label>
          <input matInput formControlName="branchName" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>First warehouse code</mat-label>
          <input matInput formControlName="warehouseCode" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>First warehouse name</mat-label>
          <input matInput formControlName="warehouseName" />
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
export class ProvisionTenantDialogComponent {
  readonly dialogRef = inject(MatDialogRef<ProvisionTenantDialogComponent>);
  private readonly fb = inject(FormBuilder);

  readonly industries = ['Restaurant', 'Pharma', 'Retail'];

  readonly form = this.fb.nonNullable.group({
    code: ['', Validators.required],
    name: ['', Validators.required],
    industry: ['Restaurant', Validators.required],
    ownerUsername: ['', Validators.required],
    ownerDisplayName: ['', Validators.required],
    branchCode: ['MAIN', Validators.required],
    branchName: ['Main Branch', Validators.required],
    warehouseCode: ['WH1', Validators.required],
    warehouseName: ['Main Warehouse', Validators.required],
  });

  submit(): void {
    if (this.form.invalid) {
      return;
    }
    this.dialogRef.close(this.form.getRawValue() as ProvisionTenantFormValue);
  }
}

/** Operator-tenant-only screen (section 13): create tenants, suspend/activate. */
@Component({
  selector: 'app-platform-tenants',
  standalone: true,
  imports: [MatTableModule, MatButtonModule, MatChipsModule, MatProgressBarModule],
  template: `
    <h2>Platform tenants</h2>
    <button mat-flat-button color="primary" (click)="openProvisionDialog()" class="mb-3">New tenant</button>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" />
    }
    @if (oneTimePassword()) {
      <p class="otp">Owner one-time password: <strong>{{ oneTimePassword() }}</strong> — relay this securely.</p>
    }
    <table mat-table [dataSource]="tenants()" class="mat-elevation-z1">
      <ng-container matColumnDef="code">
        <th mat-header-cell *matHeaderCellDef>Code</th>
        <td mat-cell *matCellDef="let tenant">{{ tenant.code }}</td>
      </ng-container>
      <ng-container matColumnDef="name">
        <th mat-header-cell *matHeaderCellDef>Name</th>
        <td mat-cell *matCellDef="let tenant">{{ tenant.name }}</td>
      </ng-container>
      <ng-container matColumnDef="industry">
        <th mat-header-cell *matHeaderCellDef>Industry</th>
        <td mat-cell *matCellDef="let tenant">{{ tenant.industry }}</td>
      </ng-container>
      <ng-container matColumnDef="status">
        <th mat-header-cell *matHeaderCellDef>Status</th>
        <td mat-cell *matCellDef="let tenant">
          <mat-chip [color]="tenant.status === 'Active' ? 'primary' : 'warn'" highlighted>{{ tenant.status }}</mat-chip>
        </td>
      </ng-container>
      <ng-container matColumnDef="actions">
        <th mat-header-cell *matHeaderCellDef></th>
        <td mat-cell *matCellDef="let tenant">
          @if (tenant.status === 'Active') {
            <button mat-button color="warn" (click)="setStatus(tenant, 'suspend')">Suspend</button>
          } @else {
            <button mat-button color="primary" (click)="setStatus(tenant, 'activate')">Activate</button>
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
    .otp { margin: 1rem 0; padding: 0.75rem; background: #fef9e7; border-radius: 4px; }
  `,
})
export class PlatformTenantsComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly dialog = inject(MatDialog);

  readonly tenants = signal<TenantSummary[]>([]);
  readonly loading = signal(true);
  readonly oneTimePassword = signal<string | null>(null);
  readonly columns = ['code', 'name', 'industry', 'status', 'actions'];

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.tenants.set(
        await firstValueFrom(this.http.get<TenantSummary[]>(`${this.config.apiBaseUrl}/api/v1/platform/tenants`)),
      );
    } finally {
      this.loading.set(false);
    }
  }

  openProvisionDialog(): void {
    const ref = this.dialog.open(ProvisionTenantDialogComponent, { width: '480px' });
    ref.afterClosed().subscribe(async (v: ProvisionTenantFormValue | undefined) => {
      if (!v) {
        return;
      }

      const result = await firstValueFrom(
        this.http.post<{ ownerOneTimePassword: string }>(`${this.config.apiBaseUrl}/api/v1/platform/tenants`, {
          code: v.code,
          name: v.name,
          industry: v.industry,
          owner: { username: v.ownerUsername, displayName: v.ownerDisplayName, email: null, phone: null },
          branch: { code: v.branchCode, name: v.branchName },
          warehouse: { code: v.warehouseCode, name: v.warehouseName },
        }),
      );
      this.oneTimePassword.set(result.ownerOneTimePassword);
      await this.load();
    });
  }

  async setStatus(tenant: TenantSummary, action: 'suspend' | 'activate'): Promise<void> {
    await firstValueFrom(this.http.post(`${this.config.apiBaseUrl}/api/v1/platform/tenants/${tenant.id}/${action}`, {}));
    await this.load();
  }
}
