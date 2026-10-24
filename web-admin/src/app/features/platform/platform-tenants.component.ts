import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
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
        <p class="form-section-title">Company</p>
        <div class="form-row code-name">
          <mat-form-field appearance="outline">
            <mat-label>Tenant code</mat-label>
            <input matInput formControlName="code" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Name</mat-label>
            <input matInput formControlName="name" />
          </mat-form-field>
        </div>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Industry</mat-label>
          <mat-select formControlName="industry">
            @for (industry of industries; track industry) {
              <mat-option [value]="industry">{{ industry }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <p class="form-section-title">Owner</p>
        <div class="form-row">
          <mat-form-field appearance="outline">
            <mat-label>Username</mat-label>
            <input matInput formControlName="ownerUsername" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Display name</mat-label>
            <input matInput formControlName="ownerDisplayName" />
          </mat-form-field>
        </div>
        <p class="form-section-title">First branch</p>
        <div class="form-row code-name">
          <mat-form-field appearance="outline">
            <mat-label>Code</mat-label>
            <input matInput formControlName="branchCode" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Name</mat-label>
            <input matInput formControlName="branchName" />
          </mat-form-field>
        </div>
        <p class="form-section-title">First warehouse</p>
        <div class="form-row code-name">
          <mat-form-field appearance="outline">
            <mat-label>Code</mat-label>
            <input matInput formControlName="warehouseCode" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Name</mat-label>
            <input matInput formControlName="warehouseName" />
          </mat-form-field>
        </div>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button mat-flat-button color="primary" [disabled]="form.invalid" (click)="submit()">Create</button>
    </mat-dialog-actions>
  `,
  styles: `.code-name { grid-template-columns: 2fr 3fr; } @media (max-width: 600px) { .code-name { grid-template-columns: 1fr; } }`,
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
  imports: [MatTableModule, MatButtonModule, MatIconModule, MatProgressBarModule, DatePipe],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1 class="page-title">Tenants</h1>
          <p class="page-subtitle">Every customer company on this installation.</p>
        </div>
        <div class="page-actions">
          <button mat-flat-button color="primary" (click)="openProvisionDialog()">
            <mat-icon>add</mat-icon>
            New tenant
          </button>
        </div>
      </div>

      @if (oneTimePassword()) {
        <div class="callout">
          <mat-icon>vpn_key</mat-icon>
          <div>
            Owner one-time password: <strong class="mono">{{ oneTimePassword() }}</strong><br />
            Relay it securely — it isn't shown again.
          </div>
        </div>
      }

      <div class="table-card">
        @if (loading()) {
          <mat-progress-bar mode="indeterminate" />
        }
        <table mat-table [dataSource]="tenants()">
          <ng-container matColumnDef="tenant">
            <th mat-header-cell *matHeaderCellDef>Tenant</th>
            <td mat-cell *matCellDef="let tenant">
              <div class="tenant-cell">
                <span class="tenant-mark" [class.pharma]="tenant.industry === 'Pharma'">{{ tenant.name.charAt(0) }}</span>
                <div>
                  <span class="cell-primary">{{ tenant.name }}</span>
                  <span class="cell-secondary mono">{{ tenant.code }}</span>
                </div>
              </div>
            </td>
          </ng-container>
          <ng-container matColumnDef="industry">
            <th mat-header-cell *matHeaderCellDef>Industry</th>
            <td mat-cell *matCellDef="let tenant">
              <span class="industry">
                <mat-icon>{{ industryIcon(tenant.industry) }}</mat-icon>
                {{ tenant.industry }}
              </span>
            </td>
          </ng-container>
          <ng-container matColumnDef="created">
            <th mat-header-cell *matHeaderCellDef>Created</th>
            <td mat-cell *matCellDef="let tenant" class="muted">{{ tenant.createdAtUtc | date: 'mediumDate' }}</td>
          </ng-container>
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>Status</th>
            <td mat-cell *matCellDef="let tenant">
              <span class="pill" [class.pill-success]="tenant.status === 'Active'" [class.pill-danger]="tenant.status !== 'Active'">
                {{ tenant.status }}
              </span>
            </td>
          </ng-container>
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef></th>
            <td mat-cell *matCellDef="let tenant" class="cell-actions">
              @if (tenant.status === 'Active') {
                <button mat-button color="warn" (click)="setStatus(tenant, 'suspend')">Suspend</button>
              } @else {
                <button mat-button color="primary" (click)="setStatus(tenant, 'activate')">Activate</button>
              }
              <button mat-button (click)="applyTemplate(tenant)" title="Adds units, product types and the default price list the tenant is missing">
                Add missing template data
              </button>
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>
        @if (!loading() && tenants().length === 0) {
          <div class="empty-state">
            <mat-icon>domain_add</mat-icon>
            <div>No tenants yet — create the first one.</div>
          </div>
        }
      </div>
    </div>
  `,
  styles: `
    .tenant-cell { display: flex; align-items: center; gap: 0.75rem; padding: 0.5rem 0; }
    .tenant-mark {
      width: 36px; height: 36px; border-radius: 10px; flex-shrink: 0;
      display: flex; align-items: center; justify-content: center;
      background: #fff7ed; color: #c2410c; font-weight: 700;
    }
    .tenant-mark.pharma { background: #ecfeff; color: #0e7490; }
    .industry { display: inline-flex; align-items: center; gap: 0.35rem; }
    .industry .mat-icon { width: 18px; height: 18px; font-size: 18px; color: var(--so-text-muted); }
  `,
})
export class PlatformTenantsComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly tenants = signal<TenantSummary[]>([]);
  readonly loading = signal(true);
  readonly oneTimePassword = signal<string | null>(null);
  readonly columns = ['tenant', 'industry', 'created', 'status', 'actions'];

  industryIcon(industry: string): string {
    return industry === 'Restaurant' ? 'restaurant' : industry === 'Pharma' ? 'medication' : 'shopping_bag';
  }

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

  async applyTemplate(tenant: TenantSummary): Promise<void> {
    const result = await firstValueFrom(
      this.http.post<{ rowsAdded: number }>(`${this.config.apiBaseUrl}/api/v1/platform/tenants/${tenant.id}/apply-template`, {}),
    );
    this.snackBar.open(
      result.rowsAdded > 0 ? `${tenant.name}: added ${result.rowsAdded} missing rows.` : `${tenant.name} already has everything.`,
      'OK',
      { duration: 4000 },
    );
  }

  async setStatus(tenant: TenantSummary, action: 'suspend' | 'activate'): Promise<void> {
    await firstValueFrom(this.http.post(`${this.config.apiBaseUrl}/api/v1/platform/tenants/${tenant.id}/${action}`, {}));
    await this.load();
  }
}
