import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { DialogModule } from 'primeng/dialog';
import { TagModule } from 'primeng/tag';
import { ConfigService } from '../../core/config.service';

interface TenantSummary {
  id: string;
  code: string;
  name: string;
  industry: string;
  status: string;
  createdAtUtc: string;
}

/** Operator-tenant-only screen (section 13): create tenants, suspend/activate. */
@Component({
  selector: 'app-platform-tenants',
  standalone: true,
  imports: [TableModule, ButtonModule, InputTextModule, SelectModule, DialogModule, ReactiveFormsModule, TagModule],
  template: `
    <h2>Platform tenants</h2>
    <p-button label="New tenant" icon="pi pi-plus" (onClick)="showDialog.set(true)" styleClass="mb-3" />
    <p-table [value]="tenants()" [loading]="loading()">
      <ng-template pTemplate="header">
        <tr><th>Code</th><th>Name</th><th>Industry</th><th>Status</th><th></th></tr>
      </ng-template>
      <ng-template pTemplate="body" let-tenant>
        <tr>
          <td>{{ tenant.code }}</td>
          <td>{{ tenant.name }}</td>
          <td>{{ tenant.industry }}</td>
          <td><p-tag [severity]="tenant.status === 'Active' ? 'success' : 'warn'" [value]="tenant.status" /></td>
          <td>
            @if (tenant.status === 'Active') {
              <p-button label="Suspend" severity="warn" size="small" (onClick)="setStatus(tenant, 'suspend')" />
            } @else {
              <p-button label="Activate" severity="success" size="small" (onClick)="setStatus(tenant, 'activate')" />
            }
          </td>
        </tr>
      </ng-template>
    </p-table>

    <p-dialog header="Provision tenant" [(visible)]="showDialogVisible" [modal]="true" [style]="{ width: '420px' }">
      <form [formGroup]="form" (ngSubmit)="create()">
        <div class="field"><label>Tenant code</label><input pInputText formControlName="code" /></div>
        <div class="field"><label>Name</label><input pInputText formControlName="name" /></div>
        <div class="field">
          <label>Industry</label>
          <p-select formControlName="industry" [options]="industries" />
        </div>
        <div class="field"><label>Owner username</label><input pInputText formControlName="ownerUsername" /></div>
        <div class="field"><label>Owner display name</label><input pInputText formControlName="ownerDisplayName" /></div>
        <div class="field"><label>First branch code</label><input pInputText formControlName="branchCode" /></div>
        <div class="field"><label>First branch name</label><input pInputText formControlName="branchName" /></div>
        <div class="field"><label>First warehouse code</label><input pInputText formControlName="warehouseCode" /></div>
        <div class="field"><label>First warehouse name</label><input pInputText formControlName="warehouseName" /></div>
        <p-button type="submit" label="Create" [disabled]="form.invalid" [loading]="creating()" />
      </form>
      @if (oneTimePassword()) {
        <p class="otp">Owner one-time password: <strong>{{ oneTimePassword() }}</strong> — relay this securely.</p>
      }
    </p-dialog>
  `,
  styles: `
    .field { margin-bottom: 1rem; display: flex; flex-direction: column; gap: 0.25rem; }
    .otp { margin-top: 1rem; padding: 0.75rem; background: #fef9e7; border-radius: 4px; }
  `,
})
export class PlatformTenantsComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly fb = inject(FormBuilder);

  readonly tenants = signal<TenantSummary[]>([]);
  readonly loading = signal(true);
  readonly creating = signal(false);
  readonly showDialog = signal(false);
  readonly oneTimePassword = signal<string | null>(null);
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

  get showDialogVisible(): boolean {
    return this.showDialog();
  }
  set showDialogVisible(value: boolean) {
    this.showDialog.set(value);
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

  async create(): Promise<void> {
    if (this.form.invalid) {
      return;
    }
    this.creating.set(true);
    try {
      const v = this.form.getRawValue();
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
    } finally {
      this.creating.set(false);
    }
  }

  async setStatus(tenant: TenantSummary, action: 'suspend' | 'activate'): Promise<void> {
    await firstValueFrom(this.http.post(`${this.config.apiBaseUrl}/api/v1/platform/tenants/${tenant.id}/${action}`, {}));
    await this.load();
  }
}
