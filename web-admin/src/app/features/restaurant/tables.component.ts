import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { AuthService } from '../../core/auth/auth.service';
import { ConfigService } from '../../core/config.service';
import { DiningArea, DiningTable } from './restaurant.models';

interface AreaDialogData {
  area: DiningArea | null;
}

@Component({
  selector: 'app-area-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.area ? 'Edit area' : 'New area' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" (ngSubmit)="submit()" class="form">
        <div class="form-row name-order">
          <mat-form-field appearance="outline">
            <mat-label>Name</mat-label>
            <input matInput formControlName="name" placeholder="e.g. Main hall, Family hall, Roof" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Order</mat-label>
            <input matInput type="number" formControlName="sortOrder" />
          </mat-form-field>
        </div>
        @if (data.area) {
          <mat-slide-toggle formControlName="isActive">In use</mat-slide-toggle>
        }
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button mat-flat-button color="primary" [disabled]="form.invalid" (click)="submit()">{{ data.area ? 'Save' : 'Add' }}</button>
    </mat-dialog-actions>
  `,
  styles: `.name-order { grid-template-columns: 3fr 1fr; }`,
})
export class AreaDialogComponent {
  readonly dialogRef = inject(MatDialogRef<AreaDialogComponent>);
  readonly data = inject<AreaDialogData>(MAT_DIALOG_DATA);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    name: [this.data.area?.name ?? '', [Validators.required, Validators.maxLength(60)]],
    sortOrder: [this.data.area?.sortOrder ?? 0, Validators.required],
    isActive: [this.data.area?.isActive ?? true],
  });

  submit(): void {
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    this.dialogRef.close({ name: v.name.trim(), sortOrder: Number(v.sortOrder), isActive: v.isActive });
  }
}

interface TableDialogData {
  table: DiningTable | null;
  areaId: string;
  areas: DiningArea[];
}

@Component({
  selector: 'app-table-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSlideToggleModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.table ? 'Edit table ' + data.table.name : 'New table' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" (ngSubmit)="submit()" class="form">
        <div class="form-row two">
          <mat-form-field appearance="outline">
            <mat-label>Name</mat-label>
            <input matInput formControlName="name" placeholder="e.g. 4 or T12" />
            <mat-hint>What staff call it</mat-hint>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Seats</mat-label>
            <input matInput type="number" formControlName="seats" min="1" max="100" />
          </mat-form-field>
        </div>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Area</mat-label>
          <mat-select formControlName="areaId">
            @for (a of data.areas; track a.id) {
              <mat-option [value]="a.id">{{ a.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        @if (data.table) {
          <mat-slide-toggle formControlName="isActive">In use</mat-slide-toggle>
        }
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button mat-flat-button color="primary" [disabled]="form.invalid" (click)="submit()">{{ data.table ? 'Save' : 'Add' }}</button>
    </mat-dialog-actions>
  `,
  styles: `.two { grid-template-columns: 2fr 1fr; }`,
})
export class TableDialogComponent {
  readonly dialogRef = inject(MatDialogRef<TableDialogComponent>);
  readonly data = inject<TableDialogData>(MAT_DIALOG_DATA);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    name: [this.data.table?.name ?? '', [Validators.required, Validators.maxLength(20)]],
    seats: [this.data.table?.seats ?? 4, [Validators.required, Validators.min(1), Validators.max(100)]],
    areaId: [this.data.table?.areaId ?? this.data.areaId, Validators.required],
    isActive: [this.data.table?.isActive ?? true],
  });

  submit(): void {
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    this.dialogRef.close({ name: v.name.trim(), seats: Number(v.seats), areaId: v.areaId, isActive: v.isActive, sortOrder: this.data.table?.sortOrder ?? 0 });
  }
}

/**
 * Restaurant → Tables: the branch's dining areas and their tables. Waiters see them as the table
 * map in the app. Nothing is deleted (old orders point at the tables): switch one off instead.
 */
@Component({
  selector: 'app-tables',
  standalone: true,
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatProgressBarModule, MatSelectModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1 class="page-title">Tables</h1>
          <p class="page-subtitle">Dining areas and tables. Waiters open orders on them in the app; the table map shows which are taken.</p>
        </div>
        <div class="page-actions">
          @if (branches().length > 1) {
            <mat-form-field appearance="outline" class="branch-field" subscriptSizing="dynamic">
              <mat-label>Branch</mat-label>
              <mat-select [value]="branchId()" (selectionChange)="selectBranch($event.value)">
                @for (b of branches(); track b.id) {
                  <mat-option [value]="b.id">{{ b.name }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
          }
          <button mat-flat-button color="primary" (click)="editArea(null)"><mat-icon>add</mat-icon> New area</button>
        </div>
      </div>

      @if (!hasFeature) {
        <div class="callout">Tables are switched off for this company. Turn on "Restaurant.Tables" in Feature toggles.</div>
      }
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
      }

      @for (area of areas(); track area.id) {
        <section class="area card" [class.off]="!area.isActive">
          <header>
            <h2>{{ area.name }}</h2>
            @if (!area.isActive) { <span class="pill">Not in use</span> }
            <span class="muted">{{ activeCount(area) }} table(s)</span>
            <span class="spacer"></span>
            <button mat-button (click)="editArea(area)"><mat-icon>edit</mat-icon> Edit</button>
            <button mat-stroked-button (click)="editTable(null, area)"><mat-icon>add</mat-icon> Table</button>
          </header>
          <div class="tables">
            @for (t of area.tables; track t.id) {
              <button class="table" [class.off]="!t.isActive" (click)="editTable(t, area)">
                <span class="name">{{ t.name }}</span>
                <span class="seats"><mat-icon>chair</mat-icon>{{ t.seats }}</span>
              </button>
            } @empty {
              <div class="muted">No tables yet. Add the first one.</div>
            }
          </div>
        </section>
      }

      @if (!loading() && areas().length === 0) {
        <div class="empty-state">
          <mat-icon>table_restaurant</mat-icon>
          <div>No areas yet. Start with one, e.g. "Main hall", then add its tables.</div>
        </div>
      }
    </div>
  `,
  styles: `
    .branch-field { width: 220px; }
    .callout { margin-bottom: 1rem; }
    .area { padding: 1rem; margin-bottom: 1rem; }
    .area.off { opacity: 0.6; }
    .area header { display: flex; align-items: center; gap: 0.75rem; flex-wrap: wrap; margin-bottom: 0.75rem; }
    .area h2 { margin: 0; font-size: 1.1rem; }
    .spacer { flex: 1; }
    .tables { display: grid; grid-template-columns: repeat(auto-fill, minmax(96px, 1fr)); gap: 0.6rem; }
    .table {
      display: flex; flex-direction: column; align-items: center; gap: 0.2rem; padding: 0.75rem 0.5rem;
      border: 1px solid var(--so-border); border-radius: var(--so-radius-sm); background: var(--so-surface);
      color: var(--so-text); font: inherit; cursor: pointer;
    }
    .table:hover { border-color: var(--so-accent); }
    .table.off { border-style: dashed; color: var(--so-text-muted); }
    .table .name { font-size: 1.2rem; font-weight: 700; }
    .table .seats { display: inline-flex; align-items: center; gap: 0.15rem; font-size: 0.8rem; color: var(--so-text-muted); }
    .table .seats .mat-icon { font-size: 14px; width: 14px; height: 14px; }
  `,
})
export class TablesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);

  readonly branches = this.auth.branches;
  readonly branchId = signal('');
  readonly areas = signal<DiningArea[]>([]);
  readonly loading = signal(true);
  readonly hasFeature = this.auth.hasFeature('Restaurant.Tables');

  private get api(): string {
    return `${this.config.apiBaseUrl}/api/v1/restaurant`;
  }

  async ngOnInit(): Promise<void> {
    this.branchId.set(this.branches()[0]?.id ?? '');
    await this.load();
  }

  async selectBranch(id: string): Promise<void> {
    this.branchId.set(id);
    await this.load();
  }

  activeCount(area: DiningArea): number {
    return area.tables.filter((t) => t.isActive).length;
  }

  editArea(area: DiningArea | null): void {
    this.dialog.open(AreaDialogComponent, { width: '420px', data: { area } satisfies AreaDialogData })
      .afterClosed()
      .subscribe(async (v) => {
        if (!v) return;
        const body = { branchId: this.branchId(), ...v };
        await firstValueFrom(area ? this.http.put(`${this.api}/areas/${area.id}`, body) : this.http.post(`${this.api}/areas`, body));
        await this.load();
      });
  }

  editTable(table: DiningTable | null, area: DiningArea): void {
    const areas = this.areas().filter((a) => a.isActive || a.id === area.id);
    this.dialog.open(TableDialogComponent, { width: '420px', data: { table, areaId: area.id, areas } satisfies TableDialogData })
      .afterClosed()
      .subscribe(async (v) => {
        if (!v) return;
        const body = { branchId: this.branchId(), ...v };
        await firstValueFrom(table ? this.http.put(`${this.api}/tables/${table.id}`, body) : this.http.post(`${this.api}/tables`, body));
        await this.load();
      });
  }

  private async load(): Promise<void> {
    if (!this.branchId()) {
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    try {
      this.areas.set(await firstValueFrom(this.http.get<DiningArea[]>(`${this.api}/areas`, { params: { branchId: this.branchId() } })));
    } finally {
      this.loading.set(false);
    }
  }
}
