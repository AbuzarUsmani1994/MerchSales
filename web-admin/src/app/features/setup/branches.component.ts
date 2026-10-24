import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { ConfigService } from '../../core/config.service';

interface BranchSummary {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
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
export class NewBranchDialogComponent {
  readonly dialogRef = inject(MatDialogRef<NewBranchDialogComponent>);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    code: ['', Validators.required],
    name: ['', Validators.required],
  });

  submit(): void {
    if (this.form.invalid) {
      return;
    }
    this.dialogRef.close(this.form.getRawValue());
  }
}

@Component({
  selector: 'app-branches',
  standalone: true,
  imports: [MatTableModule, MatButtonModule, MatProgressBarModule],
  template: `
    <h2>Branches &amp; warehouses</h2>
    <button mat-flat-button color="primary" (click)="openNewBranchDialog()" class="mb-3">New branch</button>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" />
    }
    <table mat-table [dataSource]="branches()" class="mat-elevation-z1">
      <ng-container matColumnDef="code">
        <th mat-header-cell *matHeaderCellDef>Code</th>
        <td mat-cell *matCellDef="let branch">{{ branch.code }}</td>
      </ng-container>
      <ng-container matColumnDef="name">
        <th mat-header-cell *matHeaderCellDef>Name</th>
        <td mat-cell *matCellDef="let branch">{{ branch.name }}</td>
      </ng-container>
      <ng-container matColumnDef="status">
        <th mat-header-cell *matHeaderCellDef>Status</th>
        <td mat-cell *matCellDef="let branch">{{ branch.isActive ? 'Active' : 'Inactive' }}</td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns"></tr>
    </table>
  `,
  styles: `table { width: 100%; } .mb-3 { margin-bottom: 1rem; }`,
})
export class BranchesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly dialog = inject(MatDialog);

  readonly branches = signal<BranchSummary[]>([]);
  readonly loading = signal(true);
  readonly columns = ['code', 'name', 'status'];

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

  openNewBranchDialog(): void {
    const ref = this.dialog.open(NewBranchDialogComponent, { width: '360px' });
    ref.afterClosed().subscribe(async (result) => {
      if (!result) {
        return;
      }
      await firstValueFrom(this.http.post(`${this.config.apiBaseUrl}/api/v1/tenancy/branches`, result));
      await this.load();
    });
  }
}
