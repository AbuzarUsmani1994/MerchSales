import { DecimalPipe } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { ConfigService } from '../../core/config.service';
import { AuthService } from '../../core/auth/auth.service';
import { CatalogLookups, MenuItem, SHARED_MENU } from './catalog.models';
import {
  BranchPriceDialogComponent,
  BranchPriceDialogData,
  EditItemDialogComponent,
  EditItemDialogData,
  NewItemDialogComponent,
  NewItemDialogData,
} from './menu-dialogs.component';
import { PictureChange, ProductImageComponent, ProductImageService } from './product-image';

/**
 * Catalog → Menu. The Owner manages the shared menu; a branch manager sees it as charged at
 * their branch, sets their branch's prices and availability, and adds items only their branch
 * sells. The API enforces all of it; flags on each row (canEdit, canSetBranchPricing) only
 * decide which buttons to show.
 */
@Component({
  selector: 'app-menu',
  standalone: true,
  imports: [
    DecimalPipe,
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatTableModule,
    MatTooltipModule,
    ProductImageComponent,
  ],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1 class="page-title">Menu</h1>
          <p class="page-subtitle">
            @if (atBranch(); as branch) {
              Prices and availability as at <strong>{{ branch.name }}</strong>.
            } @else {
              The shared menu every branch sells, with base prices.
            }
          </p>
        </div>
        @if (canAddItems()) {
          <div class="page-actions">
            <button mat-flat-button color="primary" (click)="openNewItem()" [disabled]="!lookups()?.units?.length">
              <mat-icon>add</mat-icon>
              New item
            </button>
          </div>
        }
      </div>

      @if (lookups() && (!lookups()!.units.length || !lookups()!.hasDefaultPriceList)) {
        <div class="callout">
          <mat-icon>info</mat-icon>
          <div>
            This company is missing its starter units and price list. Ask your platform administrator to click
            <strong>Add missing template data</strong> for it (Platform → Tenants).
          </div>
        </div>
      }

      <div class="toolbar">
        <mat-form-field appearance="outline" class="branch-field" subscriptSizing="dynamic">
          <mat-label>Show menu at</mat-label>
          <mat-select [ngModel]="branchSelection()" (ngModelChange)="selectBranch($event)">
            @if (seesAllBranches()) {
              <mat-option [value]="shared">Shared menu (base prices)</mat-option>
            }
            @for (b of branches(); track b.id) {
              <mat-option [value]="b.id">{{ b.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" class="search-field" subscriptSizing="dynamic">
          <mat-label>Search</mat-label>
          <mat-icon matPrefix>search</mat-icon>
          <input matInput [ngModel]="search()" (ngModelChange)="search.set($event)" placeholder="Name or code" />
        </mat-form-field>
      </div>

      @if (categoryChips().length > 1) {
        <div class="chips">
          @for (c of categoryChips(); track c.id) {
            <button type="button" class="chip" [class.active]="categoryFilter() === c.id" (click)="categoryFilter.set(c.id)">
              {{ c.name }} <span class="chip-count">{{ c.count }}</span>
            </button>
          }
        </div>
      }

      <div class="table-card">
        @if (loading()) {
          <mat-progress-bar mode="indeterminate" />
        }
        <table mat-table [dataSource]="visibleItems()">
          <ng-container matColumnDef="item">
            <th mat-header-cell *matHeaderCellDef>Item</th>
            <td mat-cell *matCellDef="let item" [class.inactive]="!item.isActive">
              <div class="item-cell">
                <app-product-image [productId]="item.id" [attachmentId]="item.imageAttachmentId" [name]="item.name" [size]="44" />
                <div>
                  <span class="cell-primary">{{ item.name }}</span>
                  <span class="cell-secondary">
                    <span class="mono">{{ item.sku }}</span>
                    @if (item.shortName) { · {{ item.shortName }} }
                    · per {{ item.uomCode }}
                    @if (!item.imageAttachmentId && item.canEdit) { · <span class="no-picture">No picture</span> }
                  </span>
                </div>
              </div>
            </td>
          </ng-container>
          <ng-container matColumnDef="category">
            <th mat-header-cell *matHeaderCellDef>Category</th>
            <td mat-cell *matCellDef="let item">{{ item.categoryName ?? '—' }}</td>
          </ng-container>
          <ng-container matColumnDef="scope">
            <th mat-header-cell *matHeaderCellDef>Sold at</th>
            <td mat-cell *matCellDef="let item">
              @if (item.ownerBranchId) {
                <span class="pill pill-info pill-plain">{{ branchName(item.ownerBranchId) }} only</span>
              } @else {
                <span class="pill pill-plain">All branches</span>
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="price">
            <th mat-header-cell *matHeaderCellDef class="num">Price</th>
            <td mat-cell *matCellDef="let item" class="num">
              @if (item.price !== null) {
                <span class="price">{{ item.price | number: '1.0-2' }}</span>
                @if (item.branchPrice !== null && item.basePrice !== null && item.branchPrice !== item.basePrice) {
                  <span class="cell-secondary" matTooltip="Base price on the shared menu">base {{ item.basePrice | number: '1.0-2' }}</span>
                }
              } @else {
                <span class="muted">No price</span>
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="available">
            <th mat-header-cell *matHeaderCellDef>Available</th>
            <td mat-cell *matCellDef="let item">
              @if (!item.isActive) {
                <span class="pill">Off the menu</span>
              } @else if (atBranch()) {
                <mat-slide-toggle
                  [checked]="item.isAvailable"
                  [disabled]="!item.canSetBranchPricing"
                  (change)="setAvailable(item, $event.checked)"
                  [attr.aria-label]="'Available: ' + item.name"
                />
              } @else {
                <span class="pill pill-success">On the menu</span>
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef></th>
            <td mat-cell *matCellDef="let item" class="cell-actions">
              @if (item.canSetBranchPricing && atBranch() && !item.ownerBranchId) {
                <button mat-button (click)="openBranchPrice(item)">
                  <mat-icon>sell</mat-icon>
                  Branch price
                </button>
              }
              @if (item.canEdit) {
                <button mat-button (click)="openEdit(item)">
                  <mat-icon>edit</mat-icon>
                  Edit
                </button>
              }
            </td>
          </ng-container>
          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>
        @if (!loading() && visibleItems().length === 0) {
          <div class="empty-state">
            <mat-icon>restaurant_menu</mat-icon>
            <div>{{ items().length === 0 ? 'No items on the menu yet.' : 'Nothing matches.' }}</div>
          </div>
        }
      </div>
    </div>
  `,
  styles: `
    .item-cell { display: flex; gap: 0.75rem; align-items: center; }
    .item-cell .cell-primary, .item-cell .cell-secondary { display: block; }
    .no-picture { color: var(--so-warning); }
    .toolbar { display: flex; gap: 0.75rem; flex-wrap: wrap; margin-bottom: 0.75rem; }
    .branch-field { width: 260px; }
    .search-field { flex: 1; min-width: 200px; }
    .chips { display: flex; gap: 0.5rem; flex-wrap: wrap; margin-bottom: 1rem; }
    .chip {
      border: 1px solid var(--so-border); background: var(--so-surface); color: var(--so-text);
      border-radius: 999px; padding: 0.35rem 0.85rem; font: inherit; font-size: 0.85rem; cursor: pointer;
    }
    .chip.active { background: var(--so-accent); border-color: var(--so-accent); color: #fff; }
    .chip-count { opacity: 0.6; margin-left: 0.25rem; }
    .num { text-align: right; }
    .price { font-weight: 600; font-variant-numeric: tabular-nums; }
    .inactive .cell-primary { color: var(--so-text-faint); text-decoration: line-through; }
  `,
})
export class MenuComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly images = inject(ProductImageService);

  readonly shared = SHARED_MENU;
  readonly columns = ['item', 'category', 'scope', 'price', 'available', 'actions'];

  readonly items = signal<MenuItem[]>([]);
  readonly lookups = signal<CatalogLookups | null>(null);
  readonly loading = signal(true);
  readonly search = signal('');
  readonly categoryFilter = signal<string>('all');
  readonly branchSelection = signal<string>(SHARED_MENU);

  readonly branches = this.auth.branches;
  readonly seesAllBranches = computed(() => this.auth.hasTenantWidePermission('Catalog.View'));
  readonly canAddShared = computed(() => this.auth.hasTenantWidePermission('Catalog.Manage'));
  readonly canAddItems = computed(() => this.auth.hasPermission('Catalog.Manage'));

  readonly atBranch = computed(() => this.branches().find((b) => b.id === this.branchSelection()) ?? null);

  readonly categoryChips = computed(() => {
    const items = this.items();
    const counts = new Map<string, number>();
    for (const item of items) {
      const key = item.categoryId ?? 'none';
      counts.set(key, (counts.get(key) ?? 0) + 1);
    }
    const chips = [{ id: 'all', name: 'All', count: items.length }];
    for (const c of this.lookups()?.categories ?? []) {
      if (counts.has(c.id)) chips.push({ id: c.id, name: c.name, count: counts.get(c.id)! });
    }
    if (counts.has('none')) chips.push({ id: 'none', name: 'No category', count: counts.get('none')! });
    return chips;
  });

  readonly visibleItems = computed(() => {
    const term = this.search().trim().toLowerCase();
    const category = this.categoryFilter();
    return this.items().filter(
      (item) =>
        (category === 'all' || (item.categoryId ?? 'none') === category) &&
        (!term || item.name.toLowerCase().includes(term) || item.sku.toLowerCase().includes(term)),
    );
  });

  private get baseUrl(): string {
    return `${this.config.apiBaseUrl}/api/v1/catalog`;
  }

  async ngOnInit(): Promise<void> {
    // Branch managers can't see the shared menu on its own; start at their (first) branch.
    if (!this.seesAllBranches() && this.branches().length > 0) {
      this.branchSelection.set(this.branches()[0].id);
    }
    this.lookups.set(await firstValueFrom(this.http.get<CatalogLookups>(`${this.baseUrl}/lookups`)));
    await this.load();
  }

  branchName(id: string): string {
    return this.branches().find((b) => b.id === id)?.name ?? 'One branch';
  }

  async selectBranch(value: string): Promise<void> {
    this.branchSelection.set(value);
    await this.load();
  }

  openNewItem(): void {
    const atBranch = this.atBranch();
    // The Owner sees every branch here, a branch manager only theirs (from /me); the API checks too.
    const manageable = this.branches();
    const data: NewItemDialogData = {
      lookups: this.lookups()!,
      branches: manageable,
      canAddShared: this.canAddShared(),
      defaultSoldAt: this.canAddShared() ? SHARED_MENU : (atBranch?.id ?? manageable[0]?.id ?? ''),
    };
    this.dialog
      .open(NewItemDialogComponent, { width: '520px', data })
      .afterClosed()
      .subscribe(async (request) => {
        if (!request) return;
        const { picture, ...item } = request;
        const id = await firstValueFrom(this.http.post<string>(`${this.baseUrl}/products`, item));
        await this.savePicture(id, picture);
        await this.load();
      });
  }

  /** Undefined = unchanged, null = remove, a Blob = upload (already shrunk by the dialog). */
  private async savePicture(productId: string, picture: PictureChange): Promise<void> {
    if (picture === null) {
      await this.images.remove(productId);
    } else if (picture) {
      await this.images.upload(productId, picture);
    }
  }

  openEdit(item: MenuItem): void {
    const data: EditItemDialogData = { item, lookups: this.lookups()! };
    this.dialog
      .open(EditItemDialogComponent, { width: '520px', data })
      .afterClosed()
      .subscribe(async (request) => {
        if (!request) return;
        const { picture, ...changes } = request;
        await firstValueFrom(this.http.put(`${this.baseUrl}/products/${item.id}`, changes));
        await this.savePicture(item.id, picture);
        await this.load();
      });
  }

  openBranchPrice(item: MenuItem): void {
    const branch = this.atBranch();
    if (!branch) return;
    const data: BranchPriceDialogData = { item, branchName: branch.name };
    this.dialog
      .open(BranchPriceDialogComponent, { width: '440px', data })
      .afterClosed()
      .subscribe(async (request) => {
        if (!request) return;
        await firstValueFrom(this.http.put(`${this.baseUrl}/products/${item.id}/branches/${branch.id}`, request));
        await this.load();
      });
  }

  async setAvailable(item: MenuItem, isAvailable: boolean): Promise<void> {
    const branch = this.atBranch();
    if (!branch) return;
    try {
      await firstValueFrom(
        this.http.put(`${this.baseUrl}/products/${item.id}/branches/${branch.id}`, {
          price: item.branchPrice,
          isAvailable,
        }),
      );
    } finally {
      await this.load();
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      let params = new HttpParams();
      if (this.branchSelection() !== SHARED_MENU) {
        params = params.set('branchId', this.branchSelection());
      }
      this.items.set(await firstValueFrom(this.http.get<MenuItem[]>(`${this.baseUrl}/products`, { params })));
    } finally {
      this.loading.set(false);
    }
  }
}
