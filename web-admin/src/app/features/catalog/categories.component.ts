import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTableModule } from '@angular/material/table';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { ConfigService } from '../../core/config.service';
import { CatalogLookups, CategoryItem } from './catalog.models';

@Component({
  selector: 'app-category-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data ? 'Edit category' : 'New category' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" (ngSubmit)="submit()">
        <div class="form-row name-order">
          <mat-form-field appearance="outline">
            <mat-label>Name</mat-label>
            <input matInput formControlName="name" placeholder="e.g. Pizzas" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Order</mat-label>
            <input matInput type="number" formControlName="sortOrder" />
          </mat-form-field>
        </div>
        @if (data) {
          <mat-slide-toggle formControlName="isActive">Shown on the menu</mat-slide-toggle>
        }
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button mat-flat-button color="primary" [disabled]="form.invalid" (click)="submit()">{{ data ? 'Save' : 'Add' }}</button>
    </mat-dialog-actions>
  `,
  styles: `.name-order { grid-template-columns: 3fr 1fr; }`,
})
export class CategoryDialogComponent {
  readonly dialogRef = inject(MatDialogRef<CategoryDialogComponent>);
  readonly data = inject<CategoryItem | null>(MAT_DIALOG_DATA);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    name: [this.data?.name ?? '', [Validators.required, Validators.maxLength(150)]],
    sortOrder: [this.data?.sortOrder ?? 0, Validators.required],
    isActive: [this.data?.isActive ?? true],
  });

  submit(): void {
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    this.dialogRef.close({ name: v.name.trim(), sortOrder: Number(v.sortOrder), isActive: v.isActive });
  }
}

/** Catalog → Categories: shared by every branch, so only all-branch admins (the Owner) change them. */
@Component({
  selector: 'app-categories',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, MatTableModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1 class="page-title">Categories</h1>
          <p class="page-subtitle">How the menu is grouped for order takers, e.g. Pizzas, Burgers, Drinks.</p>
        </div>
        <div class="page-actions">
          <button mat-flat-button color="primary" (click)="open(null)">
            <mat-icon>add</mat-icon>
            New category
          </button>
        </div>
      </div>
      <div class="table-card">
        @if (loading()) {
          <mat-progress-bar mode="indeterminate" />
        }
        <table mat-table [dataSource]="categories()">
          <ng-container matColumnDef="order">
            <th mat-header-cell *matHeaderCellDef>Order</th>
            <td mat-cell *matCellDef="let c" class="mono">{{ c.sortOrder }}</td>
          </ng-container>
          <ng-container matColumnDef="name">
            <th mat-header-cell *matHeaderCellDef>Name</th>
            <td mat-cell *matCellDef="let c"><span class="cell-primary">{{ c.name }}</span></td>
          </ng-container>
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>Status</th>
            <td mat-cell *matCellDef="let c">
              <span class="pill" [class.pill-success]="c.isActive">{{ c.isActive ? 'Shown' : 'Hidden' }}</span>
            </td>
          </ng-container>
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef></th>
            <td mat-cell *matCellDef="let c" class="cell-actions">
              <button mat-button (click)="open(c)"><mat-icon>edit</mat-icon> Edit</button>
            </td>
          </ng-container>
          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>
        @if (!loading() && categories().length === 0) {
          <div class="empty-state">
            <mat-icon>category</mat-icon>
            <div>No categories yet.</div>
          </div>
        }
      </div>
    </div>
  `,
})
export class CategoriesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly dialog = inject(MatDialog);

  readonly categories = signal<CategoryItem[]>([]);
  readonly loading = signal(true);
  readonly columns = ['order', 'name', 'status', 'actions'];

  private get baseUrl(): string {
    return `${this.config.apiBaseUrl}/api/v1/catalog`;
  }

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  open(category: CategoryItem | null): void {
    this.dialog
      .open(CategoryDialogComponent, { width: '420px', data: category })
      .afterClosed()
      .subscribe(async (request) => {
        if (!request) return;
        if (category) {
          await firstValueFrom(this.http.put(`${this.baseUrl}/categories/${category.id}`, request));
        } else {
          await firstValueFrom(this.http.post(`${this.baseUrl}/categories`, { name: request.name, sortOrder: request.sortOrder }));
        }
        await this.load();
      });
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const lookups = await firstValueFrom(this.http.get<CatalogLookups>(`${this.baseUrl}/lookups`));
      this.categories.set(lookups.categories);
    } finally {
      this.loading.set(false);
    }
  }
}
