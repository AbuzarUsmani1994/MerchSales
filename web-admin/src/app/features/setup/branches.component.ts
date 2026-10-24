import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { ConfigService } from '../../core/config.service';
import { AuthService } from '../../core/auth/auth.service';
import { BranchSummary } from '../../core/auth/auth.models';

interface LocationSummary {
  id: string;
  code: string;
  name: string;
  locationType: string;
  isSellable: boolean;
  isActive: boolean;
}

interface WarehouseSummary {
  id: string;
  branchId: string;
  code: string;
  name: string;
  isActive: boolean;
  locations: LocationSummary[];
}

interface BranchRow extends BranchSummary {
  warehouses: WarehouseSummary[];
}

@Component({
  selector: 'app-new-branch-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>New branch</h2>
    <mat-dialog-content>
      <form [formGroup]="form" (ngSubmit)="submit()">
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Code</mat-label>
          <input matInput formControlName="code" (input)="suggestWarehouse()" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" (input)="suggestWarehouse()" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Address</mat-label>
          <textarea matInput formControlName="address" rows="2"></textarea>
          <mat-hint>Printed on invoices and receipts.</mat-hint>
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Phone</mat-label>
          <input matInput formControlName="phone" />
        </mat-form-field>
        <h3>First warehouse</h3>
        <p class="muted">Gets MAIN, TRANSIT and QUARANTINE locations automatically.</p>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Warehouse code</mat-label>
          <input matInput formControlName="warehouseCode" (input)="warehouseEdited = true" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Warehouse name</mat-label>
          <input matInput formControlName="warehouseName" (input)="warehouseEdited = true" />
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button mat-flat-button color="primary" [disabled]="form.invalid" (click)="submit()">Create</button>
    </mat-dialog-actions>
  `,
  styles: `.full-width { width: 100%; } .muted { color: #757575; margin-top: 0; }`,
})
export class NewBranchDialogComponent {
  readonly dialogRef = inject(MatDialogRef<NewBranchDialogComponent>);
  private readonly fb = inject(FormBuilder);
  warehouseEdited = false;

  readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(32)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    address: ['', Validators.maxLength(500)],
    phone: ['', Validators.maxLength(40)],
    warehouseCode: ['', [Validators.required, Validators.maxLength(32)]],
    warehouseName: ['', [Validators.required, Validators.maxLength(150)]],
  });

  /** Pre-fills the warehouse from the branch until the user types their own. */
  suggestWarehouse(): void {
    if (this.warehouseEdited) {
      return;
    }
    const { code, name } = this.form.getRawValue();
    this.form.patchValue({
      warehouseCode: code ? `${code}-WH`.slice(0, 32) : '',
      warehouseName: name ? `${name} Store`.slice(0, 150) : '',
    });
  }

  submit(): void {
    if (this.form.invalid) {
      return;
    }
    const value = this.form.getRawValue();
    this.dialogRef.close({
      code: value.code.trim(),
      name: value.name.trim(),
      address: value.address.trim() || null,
      phone: value.phone.trim() || null,
      warehouse: { code: value.warehouseCode.trim(), name: value.warehouseName.trim() },
    });
  }
}

@Component({
  selector: 'app-new-warehouse-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>New warehouse at {{ data.name }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" (ngSubmit)="submit()">
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Code</mat-label>
          <input matInput formControlName="code" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" />
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
export class NewWarehouseDialogComponent {
  readonly dialogRef = inject(MatDialogRef<NewWarehouseDialogComponent>);
  readonly data = inject<BranchSummary>(MAT_DIALOG_DATA);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(32)]],
    name: ['', [Validators.required, Validators.maxLength(150)]],
  });

  submit(): void {
    if (this.form.invalid) {
      return;
    }
    const value = this.form.getRawValue();
    this.dialogRef.close({ branchId: this.data.id, code: value.code.trim(), name: value.name.trim() });
  }
}

@Component({
  selector: 'app-branches',
  standalone: true,
  imports: [MatTableModule, MatButtonModule, MatProgressBarModule],
  template: `
    <h2>Branches &amp; warehouses</h2>
    @if (canCreateBranches()) {
      <button mat-flat-button color="primary" (click)="openNewBranchDialog()" class="mb-3">New branch</button>
    }
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" />
    }
    <table mat-table [dataSource]="rows()" class="mat-elevation-z1">
      <ng-container matColumnDef="code">
        <th mat-header-cell *matHeaderCellDef>Code</th>
        <td mat-cell *matCellDef="let branch">{{ branch.code }}</td>
      </ng-container>
      <ng-container matColumnDef="name">
        <th mat-header-cell *matHeaderCellDef>Name</th>
        <td mat-cell *matCellDef="let branch">
          <div>{{ branch.name }}</div>
          @if (branch.address || branch.phone) {
            <div class="muted">{{ branch.address }}{{ branch.address && branch.phone ? ' · ' : '' }}{{ branch.phone }}</div>
          }
        </td>
      </ng-container>
      <ng-container matColumnDef="warehouses">
        <th mat-header-cell *matHeaderCellDef>Warehouses</th>
        <td mat-cell *matCellDef="let branch">
          @for (warehouse of branch.warehouses; track warehouse.id) {
            <div>
              <strong>{{ warehouse.code }}</strong> {{ warehouse.name }}
              <span class="muted">({{ locationCodes(warehouse) }})</span>
            </div>
          } @empty {
            <span class="warn">No warehouse — this branch can't hold stock yet</span>
          }
        </td>
      </ng-container>
      <ng-container matColumnDef="status">
        <th mat-header-cell *matHeaderCellDef>Status</th>
        <td mat-cell *matCellDef="let branch">{{ branch.isActive ? 'Active' : 'Inactive' }}</td>
      </ng-container>
      <ng-container matColumnDef="actions">
        <th mat-header-cell *matHeaderCellDef></th>
        <td mat-cell *matCellDef="let branch" class="actions">
          @if (branch.isActive) {
            <button mat-button (click)="openNewWarehouseDialog(branch)">Add warehouse</button>
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
    .muted { color: #757575; font-size: 0.85em; }
    .warn { color: #b00020; }
    .actions { white-space: nowrap; text-align: right; }
  `,
})
export class BranchesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);

  private readonly branches = signal<BranchSummary[]>([]);
  private readonly warehouses = signal<WarehouseSummary[]>([]);
  readonly loading = signal(true);
  readonly columns = ['code', 'name', 'warehouses', 'status', 'actions'];

  /** Creating branches is tenant-wide; a branch manager only sees and extends their own branch. */
  readonly canCreateBranches = computed(() => this.auth.hasTenantWidePermission('Branches.Manage'));

  readonly rows = computed<BranchRow[]>(() =>
    this.branches().map((branch) => ({
      ...branch,
      warehouses: this.warehouses().filter((w) => w.branchId === branch.id),
    })),
  );

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  locationCodes(warehouse: WarehouseSummary): string {
    return warehouse.locations.map((l) => l.code).join(', ');
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const [branches, warehouses] = await Promise.all([
        firstValueFrom(this.http.get<BranchSummary[]>(`${this.config.apiBaseUrl}/api/v1/tenancy/branches`)),
        firstValueFrom(this.http.get<WarehouseSummary[]>(`${this.config.apiBaseUrl}/api/v1/inventory/warehouses`)),
      ]);
      this.branches.set(branches);
      this.warehouses.set(warehouses);
    } finally {
      this.loading.set(false);
    }
  }

  openNewBranchDialog(): void {
    const ref = this.dialog.open(NewBranchDialogComponent, { width: '420px' });
    ref.afterClosed().subscribe(async (result) => {
      if (!result) {
        return;
      }
      await firstValueFrom(this.http.post(`${this.config.apiBaseUrl}/api/v1/tenancy/branches`, result));
      // The new branch must also appear in branch pickers (user/role dialogs) which read /me.
      await Promise.all([this.load(), this.auth.loadMe()]);
    });
  }

  openNewWarehouseDialog(branch: BranchSummary): void {
    const ref = this.dialog.open(NewWarehouseDialogComponent, { width: '420px', data: branch });
    ref.afterClosed().subscribe(async (result) => {
      if (!result) {
        return;
      }
      await firstValueFrom(this.http.post(`${this.config.apiBaseUrl}/api/v1/inventory/warehouses`, result));
      await this.load();
    });
  }
}
