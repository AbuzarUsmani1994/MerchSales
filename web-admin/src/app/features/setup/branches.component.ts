import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatIconModule } from '@angular/material/icon';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { ConfigService } from '../../core/config.service';
import { AuthService } from '../../core/auth/auth.service';
import { BranchSummary } from '../../core/auth/auth.models';

interface PublicMenuLink {
  branchId: string;
  isOn: boolean;
  code: string | null;
}

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
        <div class="form-row code-name">
          <mat-form-field appearance="outline">
            <mat-label>Code</mat-label>
            <input matInput formControlName="code" (input)="suggestWarehouse()" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Name</mat-label>
            <input matInput formControlName="name" (input)="suggestWarehouse()" />
          </mat-form-field>
        </div>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Address</mat-label>
          <textarea matInput formControlName="address" rows="2"></textarea>
          <mat-hint>Printed on invoices and receipts.</mat-hint>
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Phone</mat-label>
          <input matInput formControlName="phone" />
        </mat-form-field>
        <p class="form-section-title">First warehouse</p>
        <p class="form-section-hint">Gets MAIN, TRANSIT and QUARANTINE locations automatically.</p>
        <div class="form-row code-name">
          <mat-form-field appearance="outline">
            <mat-label>Code</mat-label>
            <input matInput formControlName="warehouseCode" (input)="warehouseEdited = true" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Name</mat-label>
            <input matInput formControlName="warehouseName" (input)="warehouseEdited = true" />
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
        <p class="form-section-hint">Gets MAIN, TRANSIT and QUARANTINE locations automatically.</p>
        <div class="form-row code-name">
          <mat-form-field appearance="outline">
            <mat-label>Code</mat-label>
            <input matInput formControlName="code" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Name</mat-label>
            <input matInput formControlName="name" />
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

/**
 * A restaurant branch's customer menu: guests scan the QR on a waiter's phone and see the
 * branch's menu, prices and pictures in their own browser. The QR itself lives in the app.
 */
@Component({
  selector: 'app-customer-menu-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatProgressBarModule],
  template: `
    <h2 mat-dialog-title>Customer menu · {{ data.name }}</h2>
    <mat-dialog-content>
      @if (busy()) {
        <mat-progress-bar mode="indeterminate" />
      }
      <p class="intro">
        Waiters show a QR on their phone (Home, then Menu QR). Guests scan it with their own camera and see
        this branch's menu, prices and pictures in their browser. No app or login, and nothing but the menu.
      </p>
      @if (link(); as current) {
        @if (current.isOn && current.code) {
          <div class="state on"><mat-icon>check_circle</mat-icon> On</div>
          <a class="link" [href]="url(current.code)" target="_blank" rel="noopener">{{ url(current.code) }}</a>
          <p class="hint">Waiters' phones get changes at their next sync. A new link stops the old QR working.</p>
        } @else {
          <div class="state"><mat-icon>do_not_disturb_on</mat-icon> Off</div>
          <p class="hint">Switch it on to give waiters a menu QR for this branch.</p>
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Close</button>
      @if (link()?.isOn) {
        <button mat-button color="warn" [disabled]="busy()" (click)="change('switch-off')">Switch off</button>
        <button mat-stroked-button [disabled]="busy()" (click)="change('new-link')">New link</button>
      } @else if (link()) {
        <button mat-flat-button color="primary" [disabled]="busy()" (click)="change('new-link')">Switch on</button>
      }
    </mat-dialog-actions>
  `,
  styles: `
    .intro { margin-top: 0; color: var(--so-text-muted); }
    .state { display: inline-flex; align-items: center; gap: 0.35rem; font-weight: 600; color: var(--so-text-muted); }
    .state.on { color: var(--so-success); }
    .state .mat-icon { width: 20px; height: 20px; font-size: 20px; }
    .link { display: block; margin: 0.5rem 0; word-break: break-all; font-family: var(--so-font-mono, monospace); font-size: 0.85rem; }
    .hint { font-size: 0.8rem; color: var(--so-text-muted); }
  `,
})
export class CustomerMenuDialogComponent implements OnInit {
  readonly dialogRef = inject(MatDialogRef<CustomerMenuDialogComponent>);
  readonly data = inject<BranchSummary>(MAT_DIALOG_DATA);
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  readonly link = signal<PublicMenuLink | null>(null);
  readonly busy = signal(true);

  async ngOnInit(): Promise<void> {
    try {
      this.link.set(await firstValueFrom(this.http.get<PublicMenuLink>(this.base())));
    } finally {
      this.busy.set(false);
    }
  }

  /** The page a QR opens: served by the API's own site. */
  url(code: string): string {
    return `${this.config.apiBaseUrl}/m/${code}`;
  }

  async change(action: 'new-link' | 'switch-off'): Promise<void> {
    if (action === 'new-link' && this.link()?.isOn && !confirm('Make a new link? The current QR stops working, and waiters get the new one at their next sync.')) {
      return;
    }
    this.busy.set(true);
    try {
      this.link.set(await firstValueFrom(this.http.post<PublicMenuLink>(`${this.base()}/${action}`, null)));
    } finally {
      this.busy.set(false);
    }
  }

  private base(): string {
    return `${this.config.apiBaseUrl}/api/v1/tenancy/branches/${this.data.id}/public-menu`;
  }
}

@Component({
  selector: 'app-branches',
  standalone: true,
  imports: [MatTableModule, MatButtonModule, MatIconModule, MatProgressBarModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1 class="page-title">Branches &amp; warehouses</h1>
          <p class="page-subtitle">Outlets and the stock-holding warehouses inside each one.</p>
        </div>
        @if (canCreateBranches()) {
          <div class="page-actions">
            <button mat-flat-button color="primary" (click)="openNewBranchDialog()">
              <mat-icon>add_business</mat-icon>
              New branch
            </button>
          </div>
        }
      </div>

      <div class="table-card">
        @if (loading()) {
          <mat-progress-bar mode="indeterminate" />
        }
        <table mat-table [dataSource]="rows()">
          <ng-container matColumnDef="branch">
            <th mat-header-cell *matHeaderCellDef>Branch</th>
            <td mat-cell *matCellDef="let branch">
              <div class="branch-cell">
                <span class="branch-icon"><mat-icon>storefront</mat-icon></span>
                <div>
                  <span class="cell-primary">{{ branch.name }}</span>
                  <span class="cell-secondary">
                    <span class="mono">{{ branch.code }}</span>
                    @if (branch.address) { · {{ branch.address }} }
                    @if (branch.phone) { · {{ branch.phone }} }
                  </span>
                </div>
              </div>
            </td>
          </ng-container>
          <ng-container matColumnDef="warehouses">
            <th mat-header-cell *matHeaderCellDef>Warehouses</th>
            <td mat-cell *matCellDef="let branch">
              @for (warehouse of branch.warehouses; track warehouse.id) {
                <div class="warehouse">
                  <mat-icon class="warehouse-icon">warehouse</mat-icon>
                  <span class="warehouse-name">{{ warehouse.name }}</span>
                  <span class="mono muted">{{ warehouse.code }}</span>
                  <span class="locations">
                    @for (location of warehouse.locations; track location.id) {
                      <span class="location" [class.sellable]="location.isSellable"
                        [title]="location.isSellable ? 'Sellable stock' : 'Not sellable'">{{ location.code }}</span>
                    }
                  </span>
                </div>
              } @empty {
                <span class="pill pill-warning">No warehouse — can't hold stock</span>
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>Status</th>
            <td mat-cell *matCellDef="let branch">
              <span class="pill" [class.pill-success]="branch.isActive">{{ branch.isActive ? 'Active' : 'Inactive' }}</span>
            </td>
          </ng-container>
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef></th>
            <td mat-cell *matCellDef="let branch" class="cell-actions">
              @if (branch.isActive && isRestaurant()) {
                <button mat-button (click)="openCustomerMenu(branch)">
                  <mat-icon>qr_code_2</mat-icon>
                  Customer menu
                </button>
              }
              @if (branch.isActive) {
                <button mat-button (click)="openNewWarehouseDialog(branch)">
                  <mat-icon>add</mat-icon>
                  Warehouse
                </button>
              }
            </td>
          </ng-container>
          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>
        @if (!loading() && rows().length === 0) {
          <div class="empty-state">
            <mat-icon>store</mat-icon>
            <div>No branches yet.</div>
          </div>
        }
      </div>
    </div>
  `,
  styles: `
    .branch-cell { display: flex; align-items: center; gap: 0.75rem; padding: 0.6rem 0; }
    .branch-icon {
      width: 38px; height: 38px; border-radius: 10px; flex-shrink: 0;
      display: flex; align-items: center; justify-content: center;
      background: var(--so-accent-soft); color: var(--so-accent);
    }
    .branch-icon .mat-icon { width: 20px; height: 20px; font-size: 20px; }
    .warehouse { display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap; padding: 0.2rem 0; }
    .warehouse-icon { width: 18px; height: 18px; font-size: 18px; color: var(--so-text-faint); }
    .warehouse-name { font-weight: 500; }
    .locations { display: inline-flex; gap: 0.25rem; flex-wrap: wrap; }
    .location {
      padding: 0.05rem 0.45rem; border-radius: 6px; font-size: 0.7rem; font-weight: 600; letter-spacing: 0.03em;
      background: var(--so-surface-muted); color: var(--so-text-muted); border: 1px solid var(--so-border);
    }
    .location.sellable { background: var(--so-success-soft); color: var(--so-success); border-color: transparent; }
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
  readonly columns = ['branch', 'warehouses', 'status', 'actions'];

  /** Creating branches is tenant-wide; a branch manager only sees and extends their own branch. */
  readonly canCreateBranches = computed(() => this.auth.hasTenantWidePermission('Branches.Manage'));

  /** The customer menu (QR) is for restaurants only. */
  readonly isRestaurant = computed(() => this.auth.tenant()?.industry === 'Restaurant');

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

  openCustomerMenu(branch: BranchSummary): void {
    this.dialog.open(CustomerMenuDialogComponent, { width: '480px', data: branch });
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
