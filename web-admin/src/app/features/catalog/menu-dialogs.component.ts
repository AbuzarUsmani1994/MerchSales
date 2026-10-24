import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { BranchSummary } from '../../core/auth/auth.models';
import { CatalogLookups, MenuItem, SHARED_MENU } from './catalog.models';

const pricePattern = /^\d+(\.\d{1,2})?$/;

export interface NewItemDialogData {
  lookups: CatalogLookups;
  /** Branches this user may add branch-only items to. */
  branches: BranchSummary[];
  /** Owner-level: may add to the shared menu. */
  canAddShared: boolean;
  /** Pre-selected "sold at" value (the branch currently shown on the menu page). */
  defaultSoldAt: string;
}

@Component({
  selector: 'app-new-item-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>New menu item</h2>
    <mat-dialog-content>
      <form [formGroup]="form" (ngSubmit)="submit()">
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" placeholder="e.g. Large Pizza" />
        </mat-form-field>
        <div class="form-row">
          <mat-form-field appearance="outline">
            <mat-label>Price (PKR)</mat-label>
            <input matInput formControlName="price" inputmode="decimal" />
            @if (form.controls.price.hasError('pattern')) {
              <mat-error>Numbers only, up to 2 decimals</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Short name (receipts)</mat-label>
            <input matInput formControlName="shortName" />
          </mat-form-field>
        </div>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Category</mat-label>
          <mat-select formControlName="categoryId">
            <mat-option value="">No category</mat-option>
            @for (c of activeCategories; track c.id) {
              <mat-option [value]="c.id">{{ c.name }}</mat-option>
            }
          </mat-select>
          @if (activeCategories.length === 0) {
            <mat-hint>No categories yet{{ data.canAddShared ? ' — add them under Catalog → Categories' : '' }}.</mat-hint>
          }
        </mat-form-field>
        <p class="form-section-title">Details</p>
        <div class="form-row">
          <mat-form-field appearance="outline">
            <mat-label>Item type</mat-label>
            <mat-select formControlName="productTypeId">
              @for (t of data.lookups.productTypes; track t.id) {
                <mat-option [value]="t.id">{{ t.name }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Sold per</mat-label>
            <mat-select formControlName="uomId">
              @for (u of data.lookups.units; track u.id) {
                <mat-option [value]="u.id">{{ u.name }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        </div>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Sold at</mat-label>
          <mat-select formControlName="soldAt">
            @if (data.canAddShared) {
              <mat-option [value]="shared">All branches (shared menu)</mat-option>
            }
            @for (b of data.branches; track b.id) {
              <mat-option [value]="b.id">{{ b.name }} only</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Description (optional)</mat-label>
          <textarea matInput formControlName="description" rows="2"></textarea>
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Code (optional)</mat-label>
          <input matInput formControlName="sku" />
          <mat-hint>Left empty, one is made for you (ITEM-0001…).</mat-hint>
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button mat-flat-button color="primary" [disabled]="form.invalid" (click)="submit()">Add item</button>
    </mat-dialog-actions>
  `,
})
export class NewItemDialogComponent {
  readonly dialogRef = inject(MatDialogRef<NewItemDialogComponent>);
  readonly data = inject<NewItemDialogData>(MAT_DIALOG_DATA);
  private readonly fb = inject(FormBuilder);
  readonly shared = SHARED_MENU;
  readonly activeCategories = this.data.lookups.categories.filter((c) => c.isActive);

  /** Restaurants sell dishes per portion; pre-select those when the template has them. */
  private readonly defaultType =
    this.data.lookups.productTypes.find((t) => t.name === 'Dish') ?? this.data.lookups.productTypes[0];
  private readonly defaultUnit =
    this.data.lookups.units.find((u) => u.code === 'portion') ??
    this.data.lookups.units.find((u) => u.code === 'pcs') ??
    this.data.lookups.units[0];

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    price: ['', [Validators.required, Validators.pattern(pricePattern)]],
    shortName: ['', Validators.maxLength(60)],
    categoryId: [''],
    productTypeId: [this.defaultType?.id ?? '', Validators.required],
    uomId: [this.defaultUnit?.id ?? '', Validators.required],
    soldAt: [this.data.defaultSoldAt, Validators.required],
    description: ['', Validators.maxLength(2000)],
    sku: ['', Validators.maxLength(64)],
  });

  submit(): void {
    if (this.form.invalid) {
      return;
    }
    const v = this.form.getRawValue();
    this.dialogRef.close({
      name: v.name.trim(),
      shortName: v.shortName.trim() || null,
      description: v.description.trim() || null,
      sku: v.sku.trim() || null,
      categoryId: v.categoryId || null,
      productTypeId: v.productTypeId,
      uomId: v.uomId,
      price: Number(v.price),
      ownerBranchId: v.soldAt === SHARED_MENU ? null : v.soldAt,
    });
  }
}

export interface EditItemDialogData {
  item: MenuItem;
  lookups: CatalogLookups;
}

@Component({
  selector: 'app-edit-item-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSlideToggleModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Edit {{ data.item.name }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" (ngSubmit)="submit()">
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" />
        </mat-form-field>
        <div class="form-row">
          <mat-form-field appearance="outline">
            <mat-label>{{ data.item.ownerBranchId ? 'Price (PKR)' : 'Base price (PKR)' }}</mat-label>
            <input matInput formControlName="price" inputmode="decimal" />
            @if (!data.item.ownerBranchId) {
              <mat-hint>Used at every branch without its own price.</mat-hint>
            }
            @if (form.controls.price.hasError('pattern')) {
              <mat-error>Numbers only, up to 2 decimals</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Short name (receipts)</mat-label>
            <input matInput formControlName="shortName" />
          </mat-form-field>
        </div>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Category</mat-label>
          <mat-select formControlName="categoryId">
            <mat-option value="">No category</mat-option>
            @for (c of data.lookups.categories; track c.id) {
              <mat-option [value]="c.id">{{ c.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Description (optional)</mat-label>
          <textarea matInput formControlName="description" rows="2"></textarea>
        </mat-form-field>
        <mat-slide-toggle formControlName="isActive">On the menu</mat-slide-toggle>
        <p class="form-section-hint toggle-hint">Off hides the item everywhere; it's kept for old orders.</p>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button mat-flat-button color="primary" [disabled]="form.invalid" (click)="submit()">Save</button>
    </mat-dialog-actions>
  `,
  styles: `.toggle-hint { margin-top: 0.5rem; }`,
})
export class EditItemDialogComponent {
  readonly dialogRef = inject(MatDialogRef<EditItemDialogComponent>);
  readonly data = inject<EditItemDialogData>(MAT_DIALOG_DATA);
  private readonly fb = inject(FormBuilder);

  private readonly ownPrice = this.data.item.ownerBranchId ? this.data.item.branchPrice : this.data.item.basePrice;

  readonly form = this.fb.nonNullable.group({
    name: [this.data.item.name, [Validators.required, Validators.maxLength(200)]],
    price: [this.ownPrice === null ? '' : String(this.ownPrice), Validators.pattern(pricePattern)],
    shortName: [this.data.item.shortName ?? '', Validators.maxLength(60)],
    categoryId: [this.data.item.categoryId ?? ''],
    description: [this.data.item.description ?? '', Validators.maxLength(2000)],
    isActive: [this.data.item.isActive],
  });

  submit(): void {
    if (this.form.invalid) {
      return;
    }
    const v = this.form.getRawValue();
    this.dialogRef.close({
      name: v.name.trim(),
      shortName: v.shortName.trim() || null,
      description: v.description.trim() || null,
      categoryId: v.categoryId || null,
      isActive: v.isActive,
      price: v.price === '' ? null : Number(v.price),
    });
  }
}

export interface BranchPriceDialogData {
  item: MenuItem;
  branchName: string;
}

@Component({
  selector: 'app-branch-price-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.item.name }} at {{ data.branchName }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" (ngSubmit)="submit()">
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Price at {{ data.branchName }} (PKR)</mat-label>
          <input matInput formControlName="price" inputmode="decimal" />
          @if (isShared) {
            <mat-hint>Leave empty to charge the base price ({{ data.item.basePrice ?? '—' }}).</mat-hint>
          }
          @if (form.controls.price.hasError('pattern')) {
            <mat-error>Numbers only, up to 2 decimals</mat-error>
          }
          @if (form.controls.price.hasError('required')) {
            <mat-error>This item is sold only here, so it needs a price</mat-error>
          }
        </mat-form-field>
        <mat-slide-toggle formControlName="isAvailable">Available at {{ data.branchName }}</mat-slide-toggle>
        <p class="form-section-hint toggle-hint">Off hides it from this branch's order takers, e.g. when sold out.</p>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button mat-flat-button color="primary" [disabled]="form.invalid" (click)="submit()">Save</button>
    </mat-dialog-actions>
  `,
  styles: `.toggle-hint { margin-top: 0.5rem; }`,
})
export class BranchPriceDialogComponent {
  readonly dialogRef = inject(MatDialogRef<BranchPriceDialogComponent>);
  readonly data = inject<BranchPriceDialogData>(MAT_DIALOG_DATA);
  private readonly fb = inject(FormBuilder);
  readonly isShared = this.data.item.ownerBranchId === null;

  readonly form = this.fb.nonNullable.group({
    price: [
      this.data.item.branchPrice === null ? '' : String(this.data.item.branchPrice),
      this.isShared ? [Validators.pattern(pricePattern)] : [Validators.required, Validators.pattern(pricePattern)],
    ],
    isAvailable: [this.data.item.isAvailable],
  });

  submit(): void {
    if (this.form.invalid) {
      return;
    }
    const v = this.form.getRawValue();
    this.dialogRef.close({ price: v.price === '' ? null : Number(v.price), isAvailable: v.isAvailable });
  }
}
