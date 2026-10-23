import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { DialogModule } from 'primeng/dialog';
import { ConfigService } from '../../core/config.service';

interface BranchSummary {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
}

@Component({
  selector: 'app-branches',
  standalone: true,
  imports: [TableModule, ButtonModule, InputTextModule, DialogModule, ReactiveFormsModule],
  template: `
    <h2>Branches &amp; warehouses</h2>
    <p-button label="New branch" icon="pi pi-plus" (onClick)="showDialog.set(true)" styleClass="mb-3" />
    <p-table [value]="branches()" [loading]="loading()">
      <ng-template pTemplate="header">
        <tr><th>Code</th><th>Name</th><th>Status</th></tr>
      </ng-template>
      <ng-template pTemplate="body" let-branch>
        <tr><td>{{ branch.code }}</td><td>{{ branch.name }}</td><td>{{ branch.isActive ? 'Active' : 'Inactive' }}</td></tr>
      </ng-template>
    </p-table>

    <p-dialog header="New branch" [(visible)]="showDialogVisible" [modal]="true" [style]="{ width: '360px' }">
      <form [formGroup]="form" (ngSubmit)="create()">
        <div class="field"><label for="code">Code</label><input id="code" pInputText formControlName="code" /></div>
        <div class="field"><label for="name">Name</label><input id="name" pInputText formControlName="name" /></div>
        <p-button type="submit" label="Create" [disabled]="form.invalid" [loading]="creating()" />
      </form>
    </p-dialog>
  `,
  styles: `.field { margin-bottom: 1rem; display: flex; flex-direction: column; gap: 0.25rem; }`,
})
export class BranchesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly fb = inject(FormBuilder);

  readonly branches = signal<BranchSummary[]>([]);
  readonly loading = signal(true);
  readonly creating = signal(false);
  readonly showDialog = signal(false);

  readonly form = this.fb.nonNullable.group({
    code: ['', Validators.required],
    name: ['', Validators.required],
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
      this.branches.set(
        await firstValueFrom(this.http.get<BranchSummary[]>(`${this.config.apiBaseUrl}/api/v1/tenancy/branches`)),
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
      await firstValueFrom(
        this.http.post(`${this.config.apiBaseUrl}/api/v1/tenancy/branches`, this.form.getRawValue()),
      );
      this.showDialog.set(false);
      this.form.reset();
      await this.load();
    } finally {
      this.creating.set(false);
    }
  }
}
