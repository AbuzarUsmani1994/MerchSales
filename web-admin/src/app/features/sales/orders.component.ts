import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { AuthService } from '../../core/auth/auth.service';
import { ConfigService } from '../../core/config.service';
import { ReceiptPrinter } from './receipt-printer';
import { CancelOrderDialogComponent, PaymentDialogComponent, ShiftDialogComponent } from './order-dialogs.component';
import { CashSession, PagedList, PaymentMethod, SalesOrder } from './sales.models';

type Tab = 'toPay' | 'paid' | 'cancelled';

const TAB_STATUSES: Record<Tab, string> = {
  toPay: 'Confirmed,Fulfilling',
  paid: 'Closed,Paid',
  cancelled: 'Cancelled',
};

/** Orders seen from the cashier's desk: new orders from the staff phones arrive in "To pay". */
const REFRESH_MS = 10_000;

/**
 * Sales → Orders. The cashier's queue: orders taken on the phones by staff, newest first, with
 * the note the waiter typed (table, name). Payment closes the order in one step on the server.
 */
@Component({
  selector: 'app-orders',
  standalone: true,
  imports: [DatePipe, DecimalPipe, MatButtonModule, MatFormFieldModule, MatIconModule, MatProgressBarModule, MatSelectModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1 class="page-title">Orders</h1>
          <p class="page-subtitle">Orders taken by staff on their phones. Take payment to complete them.</p>
        </div>
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
      </div>

      @if (usesShifts()) {
        <div class="shift card" [class.open]="session()">
          <mat-icon>{{ session() ? 'point_of_sale' : 'lock_clock' }}</mat-icon>
          @if (session(); as s) {
            <div class="shift-text">
              <strong>Shift open</strong> since {{ s.openedAtUtc | date: 'shortTime' }} ·
              cash {{ s.cashTaken | number: '1.0-0' }} · card/wallet {{ s.otherTaken | number: '1.0-0' }} ·
              <span class="muted">expected in drawer {{ s.expectedCash | number: '1.0-0' }}</span>
            </div>
            @if (canCloseShift()) {
              <button mat-stroked-button (click)="openShiftDialog()">Close shift</button>
            }
          } @else {
            <div class="shift-text"><strong>No shift open.</strong> Open one to start taking payments.</div>
            <button mat-flat-button color="primary" (click)="openShiftDialog()">Open shift</button>
          }
        </div>
      }

      <div class="tabs" role="tablist">
        <button class="chip" role="tab" [class.active]="tab() === 'toPay'" (click)="selectTab('toPay')">
          To pay @if (tab() === 'toPay') { <span class="chip-count">{{ orders().length }}</span> }
        </button>
        <button class="chip" role="tab" [class.active]="tab() === 'paid'" (click)="selectTab('paid')">Paid</button>
        <button class="chip" role="tab" [class.active]="tab() === 'cancelled'" (click)="selectTab('cancelled')">Cancelled</button>
      </div>

      @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
      }

      <div class="grid">
        @for (o of orders(); track o.id) {
          <article class="order card" [class.fresh]="fresh().has(o.id)" [class.bill]="o.billRequested && tab() === 'toPay'">
            <header>
              <div>
                <div class="label">
                  @if (o.orderNo) { <span class="no">#{{ o.orderNo }}</span> }
                  {{ o.tableName ? 'Table ' + o.tableName : (o.notes || 'Walk-in') }}
                </div>
                @if (o.tableName && (o.covers || o.notes)) {
                  <div class="meta">@if (o.covers) { {{ o.covers }} guests }@if (o.covers && o.notes) { · }{{ o.notes }}</div>
                }
                <div class="meta">
                  <span class="mono">{{ o.documentNo }}</span> · {{ o.orderDate | date: 'shortTime' }}
                  @if (o.takenBy) { · {{ o.takenBy }} }
                </div>
              </div>
              @if (o.billRequested && tab() === 'toPay') {
                <span class="pill pill-danger"><mat-icon class="pill-icon">receipt</mat-icon> Bill asked</span>
              } @else {
                <span class="pill" [class.pill-success]="o.status === 'Closed'" [class.pill-danger]="o.status === 'Cancelled'"
                      [class.pill-warning]="o.status === 'Confirmed'">{{ statusLabel(o.status) }}</span>
              }
            </header>
            <ul class="lines">
              @for (l of o.lines; track l.id) {
                <li>
                  <span class="qty">{{ l.quantity | number: '1.0-3' }}×</span>
                  <span class="name">{{ l.productName }}@if (l.notes) { <em>{{ l.notes }}</em> }</span>
                  <span class="amount">{{ l.lineTotal | number: '1.0-2' }}</span>
                </li>
              }
            </ul>
            <footer>
              <div class="total">PKR {{ o.grandTotal | number: '1.0-2' }}</div>
              @if (tab() === 'toPay') {
                <div class="actions">
                  @if (canCancel()) {
                    <button mat-button color="warn" (click)="cancel(o)">Cancel</button>
                  }
                  @if (canTakePayment()) {
                    <button mat-flat-button color="primary" [disabled]="usesShifts() && !session()" (click)="pay(o)">
                      <mat-icon>payments</mat-icon> Take payment
                    </button>
                  }
                </div>
              } @else if (o.status === 'Closed' || o.status === 'Paid') {
                <div class="actions">
                  <button mat-button (click)="reprint(o)"><mat-icon>print</mat-icon> Receipt</button>
                </div>
              }
            </footer>
          </article>
        }
      </div>

      @if (!loading() && orders().length === 0) {
        <div class="empty-state">
          <mat-icon>{{ tab() === 'toPay' ? 'hourglass_empty' : 'receipt_long' }}</mat-icon>
          <div>{{ tab() === 'toPay' ? 'No orders waiting. New ones from the phones appear here automatically.' : 'Nothing here yet.' }}</div>
        </div>
      }
    </div>
  `,
  styles: `
    .branch-field { width: 220px; }
    .shift { display: flex; align-items: center; gap: 0.75rem; padding: 0.75rem 1rem; margin-bottom: 1rem; flex-wrap: wrap; }
    .shift.open { border-color: var(--so-success); background: var(--so-success-soft); }
    .shift-text { flex: 1; min-width: 220px; }
    .tabs { display: flex; gap: 0.5rem; margin-bottom: 1rem; }
    .chip {
      border: 1px solid var(--so-border); background: var(--so-surface); color: var(--so-text);
      border-radius: 999px; padding: 0.35rem 0.95rem; font: inherit; font-size: 0.9rem; cursor: pointer;
    }
    .chip.active { background: var(--so-accent); border-color: var(--so-accent); color: #fff; }
    .chip-count { opacity: 0.8; margin-left: 0.25rem; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 1rem; }
    .order { display: flex; flex-direction: column; padding: 1rem; gap: 0.75rem; transition: box-shadow 0.3s; }
    .order.fresh { box-shadow: 0 0 0 3px var(--so-accent-ring); }
    .order.bill { border-color: var(--so-danger); }
    .no { color: var(--so-accent); margin-right: 0.25rem; font-variant-numeric: tabular-nums; }
    .pill-icon { font-size: 14px; width: 14px; height: 14px; vertical-align: -2px; }
    .order header { display: flex; justify-content: space-between; gap: 0.5rem; align-items: flex-start; }
    .label { font-size: 1.15rem; font-weight: 700; }
    .meta { font-size: 0.8rem; color: var(--so-text-muted); margin-top: 0.15rem; }
    .lines { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.3rem; flex: 1; }
    .lines li { display: flex; gap: 0.5rem; font-size: 0.92rem; }
    .qty { color: var(--so-text-muted); min-width: 2.2rem; font-variant-numeric: tabular-nums; }
    .name { flex: 1; }
    .name em { display: block; color: var(--so-warning); font-size: 0.8rem; }
    .amount { font-variant-numeric: tabular-nums; }
    .order footer { display: flex; justify-content: space-between; align-items: center; gap: 0.5rem;
      border-top: 1px solid var(--so-border); padding-top: 0.75rem; flex-wrap: wrap; }
    .total { font-size: 1.15rem; font-weight: 700; font-variant-numeric: tabular-nums; }
    .actions { display: flex; gap: 0.25rem; }
  `,
})
export class OrdersComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly printer = inject(ReceiptPrinter);

  readonly branches = this.auth.branches;
  readonly branchId = signal<string>('');
  readonly tab = signal<Tab>('toPay');
  readonly orders = signal<SalesOrder[]>([]);
  readonly loading = signal(true);
  readonly session = signal<CashSession | null>(null);
  /** Orders that arrived since the last refresh, briefly highlighted. */
  readonly fresh = signal<Set<string>>(new Set());
  private methods: PaymentMethod[] = [];
  private timer?: ReturnType<typeof setInterval>;

  readonly canTakePayment = computed(() => this.auth.hasPermission('Sales.Order.Fulfil') && this.auth.hasPermission('Finance.Payment.Receive'));
  readonly canCancel = computed(() => this.auth.hasPermission('Sales.Order.Cancel'));
  readonly canCloseShift = computed(() => this.auth.hasPermission('Finance.CashSession.Close'));
  readonly usesShifts = computed(
    () => this.auth.hasFeature('Finance.CashSessions') && this.auth.hasPermission('Finance.CashSession.Operate'),
  );

  private get api(): string {
    return `${this.config.apiBaseUrl}/api/v1`;
  }

  async ngOnInit(): Promise<void> {
    this.branchId.set(this.branches()[0]?.id ?? '');
    if (this.canTakePayment()) {
      this.methods = await firstValueFrom(this.http.get<PaymentMethod[]>(`${this.api}/finance/payment-methods`));
    }
    await this.refresh(true);
    this.timer = setInterval(() => {
      if (document.visibilityState === 'visible' && this.dialog.openDialogs.length === 0) {
        void this.refresh(false);
      }
    }, REFRESH_MS);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  async selectBranch(id: string): Promise<void> {
    this.branchId.set(id);
    await this.refresh(true);
  }

  /** A copy of a paid order's receipt (marked COPY; no cash-received line, which isn't stored). */
  async reprint(order: SalesOrder): Promise<void> {
    await this.printer.print(order.id, { copy: true });
  }

  async selectTab(tab: Tab): Promise<void> {
    this.tab.set(tab);
    await this.refresh(true);
  }

  statusLabel(status: string): string {
    return status === 'Closed' ? 'Paid' : status === 'Confirmed' ? 'To pay' : status;
  }

  pay(order: SalesOrder): void {
    this.dialog
      .open(PaymentDialogComponent, { width: '440px', data: { order, methods: this.methods }, disableClose: true })
      .afterClosed()
      .subscribe(() => void this.refresh(false));
  }

  cancel(order: SalesOrder): void {
    this.dialog
      .open(CancelOrderDialogComponent, { width: '420px', data: order })
      .afterClosed()
      .subscribe((cancelled) => cancelled && void this.refresh(false));
  }

  openShiftDialog(): void {
    const branch = this.branches().find((b) => b.id === this.branchId());
    this.dialog
      .open(ShiftDialogComponent, {
        width: '420px',
        data: { branchId: this.branchId(), branchName: branch?.name ?? '', session: this.session() },
      })
      .afterClosed()
      .subscribe((changed) => changed && void this.refresh(false));
  }

  private async refresh(showSpinner: boolean): Promise<void> {
    if (!this.branchId()) {
      this.loading.set(false);
      return;
    }

    if (showSpinner) {
      this.loading.set(true);
    }
    try {
      const params = { branchId: this.branchId(), status: TAB_STATUSES[this.tab()], pageSize: 100 };
      const [page, session] = await Promise.all([
        firstValueFrom(this.http.get<PagedList<SalesOrder>>(`${this.api}/sales/orders`, { params })),
        this.usesShifts()
          ? firstValueFrom(this.http.get<{ session: CashSession | null }>(`${this.api}/finance/cash-sessions/current`, { params: { branchId: this.branchId() } }))
          : Promise.resolve({ session: null }),
      ]);
      const before = new Set(this.orders().map((o) => o.id));
      this.fresh.set(showSpinner ? new Set() : new Set(page.items.filter((o) => !before.has(o.id)).map((o) => o.id)));
      // The queue reads oldest first, like a ticket rail; history newest first.
      this.orders.set(this.tab() === 'toPay' ? [...page.items].reverse() : page.items);
      this.session.set(session.session);
    } finally {
      this.loading.set(false);
    }
  }
}
