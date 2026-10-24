import { DecimalPipe } from '@angular/common';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ConfigService } from '../../core/config.service';
import { CashSession, CompletedSale, PaymentMethod, SalesOrder } from './sales.models';

function problemMessage(error: unknown): string {
  return error instanceof HttpErrorResponse ? (error.error?.detail ?? error.error?.title ?? 'Something went wrong.') : 'Something went wrong.';
}

export interface PaymentDialogData {
  order: SalesOrder;
  methods: PaymentMethod[];
}

/**
 * Take payment for a confirmed order. The server re-prices nothing here: the total is what
 * was confirmed. Cash shows the change to hand back; other methods charge the exact total.
 */
@Component({
  selector: 'app-payment-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DecimalPipe, MatDialogModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatInputModule, MatIconModule],
  template: `
    <h2 mat-dialog-title>Take payment · {{ data.order.documentNo }}</h2>
    <mat-dialog-content>
      @if (done(); as sale) {
        <div class="done">
          <mat-icon class="done-icon">check_circle</mat-icon>
          <div class="done-title">Paid {{ sale.amountPaid | number: '1.0-2' }} by {{ sale.paymentMethod ?? 'nothing due' }}</div>
          @if (sale.change > 0) {
            <div class="change">Give back <strong>{{ sale.change | number: '1.0-2' }}</strong></div>
          }
          <div class="muted">Invoice {{ sale.invoiceNo }}@if (sale.paymentNo) { · Receipt {{ sale.paymentNo }} }</div>
        </div>
      } @else {
        <div class="total-row">
          <span>{{ data.order.notes || 'Walk-in' }}</span>
          <span class="total">PKR {{ data.order.grandTotal | number: '1.0-2' }}</span>
        </div>
        <form [formGroup]="form">
          <mat-button-toggle-group formControlName="methodId" class="methods" aria-label="Payment method">
            @for (m of data.methods; track m.id) {
              <mat-button-toggle [value]="m.id">{{ m.name }}</mat-button-toggle>
            }
          </mat-button-toggle-group>

          @if (isCash()) {
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Cash received (PKR)</mat-label>
              <input matInput type="number" formControlName="tendered" min="0" />
            </mat-form-field>
            <div class="quick">
              @for (amount of quickAmounts(); track amount) {
                <button mat-stroked-button type="button" (click)="form.controls.tendered.setValue(amount)">{{ amount | number: '1.0-0' }}</button>
              }
            </div>
            <div class="change" [class.short]="change() < 0">
              @if (change() < 0) {
                Short by {{ -change() | number: '1.0-2' }}
              } @else {
                Change: <strong>{{ change() | number: '1.0-2' }}</strong>
              }
            </div>
          } @else {
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Reference (optional)</mat-label>
              <input matInput formControlName="reference" placeholder="Card slip or transaction no." />
            </mat-form-field>
          }
        </form>
        @if (error()) {
          <div class="callout callout-danger">{{ error() }}</div>
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (done()) {
        <button mat-flat-button color="primary" (click)="dialogRef.close(done())">Done</button>
      } @else {
        <button mat-button (click)="dialogRef.close()">Cancel</button>
        <button mat-flat-button color="primary" [disabled]="busy() || !canPay()" (click)="pay()">
          {{ busy() ? 'Saving…' : 'Confirm payment' }}
        </button>
      }
    </mat-dialog-actions>
  `,
  styles: `
    .total-row { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 1rem; }
    .total { font-size: 1.6rem; font-weight: 700; font-variant-numeric: tabular-nums; }
    .methods { margin-bottom: 1rem; flex-wrap: wrap; }
    .quick { display: flex; gap: 0.5rem; flex-wrap: wrap; margin: -0.5rem 0 0.75rem; }
    .change { font-size: 1.1rem; }
    .change.short { color: var(--so-danger); }
    .done { text-align: center; padding: 0.5rem 0 1rem; }
    .done-icon { color: var(--so-success); font-size: 48px; width: 48px; height: 48px; }
    .done-title { font-size: 1.15rem; font-weight: 600; margin: 0.5rem 0; }
    .done .change { margin-bottom: 0.5rem; }
    .callout-danger { background: var(--so-danger-soft); color: var(--so-danger); margin-top: 0.75rem; }
  `,
})
export class PaymentDialogComponent {
  readonly dialogRef = inject(MatDialogRef<PaymentDialogComponent>);
  readonly data = inject<PaymentDialogData>(MAT_DIALOG_DATA);
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    methodId: [this.data.methods.find((m) => m.kind === 'Cash')?.id ?? this.data.methods[0]?.id ?? '', Validators.required],
    tendered: [this.data.order.grandTotal as number | null],
    reference: [''],
  });

  private readonly value = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<CompletedSale | null>(null);

  readonly isCash = computed(() => this.data.methods.find((m) => m.id === this.value().methodId)?.kind === 'Cash');
  readonly change = computed(() => Number(this.value().tendered ?? 0) - this.data.order.grandTotal);
  readonly canPay = computed(() => !!this.value().methodId && (!this.isCash() || this.change() >= 0));

  /** Exact, then the next round notes a customer is likely to hand over. */
  readonly quickAmounts = computed(() => {
    const total = this.data.order.grandTotal;
    const ups = [100, 500, 1000, 5000].map((step) => Math.ceil(total / step) * step);
    return [...new Set([total, ...ups])].filter((a) => a >= total).slice(0, 4);
  });

  async pay(): Promise<void> {
    const v = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    try {
      const sale = await firstValueFrom(
        this.http.post<CompletedSale>(
          `${this.config.apiBaseUrl}/api/v1/sales/orders/${this.data.order.id}/complete`,
          {
            paymentMethodId: v.methodId,
            amountTendered: this.isCash() ? Number(v.tendered) : null,
            reference: this.isCash() ? null : v.reference.trim() || null,
          },
          { headers: new HttpHeaders({ 'If-Match': `"${this.data.order.etag}"` }) },
        ),
      );
      this.done.set(sale);
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Cancel an order before it's paid; the reason goes into the order's history. */
@Component({
  selector: 'app-cancel-order-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>Cancel {{ order.documentNo }}?</h2>
    <mat-dialog-content>
      <p class="muted">{{ order.notes || 'Walk-in' }} · {{ order.lines.length }} item(s). This can't be undone.</p>
      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Reason</mat-label>
        <input matInput [formControl]="reason" placeholder="e.g. Customer left" />
      </mat-form-field>
      @if (error()) {
        <div class="callout callout-danger">{{ error() }}</div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Keep order</button>
      <button mat-flat-button color="warn" [disabled]="reason.invalid || busy()" (click)="cancel()">Cancel order</button>
    </mat-dialog-actions>
  `,
  styles: `.callout-danger { background: var(--so-danger-soft); color: var(--so-danger); }`,
})
export class CancelOrderDialogComponent {
  readonly dialogRef = inject(MatDialogRef<CancelOrderDialogComponent>);
  readonly order = inject<SalesOrder>(MAT_DIALOG_DATA);
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  readonly reason = inject(FormBuilder).nonNullable.control('', [Validators.required, Validators.maxLength(500)]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  async cancel(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(
        this.http.post(
          `${this.config.apiBaseUrl}/api/v1/sales/orders/${this.order.id}/cancel`,
          { reason: this.reason.value.trim() },
          { headers: new HttpHeaders({ 'If-Match': `"${this.order.etag}"` }) },
        ),
      );
      this.dialogRef.close(true);
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}

export interface ShiftDialogData {
  branchId: string;
  branchName: string;
  /** Present when closing. */
  session: CashSession | null;
}

/** Open a shift with the float in the drawer, or close it with the cash counted. */
@Component({
  selector: 'app-shift-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DecimalPipe, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>{{ data.session ? 'Close shift' : 'Open shift' }} · {{ data.branchName }}</h2>
    <mat-dialog-content>
      @if (closed(); as s) {
        <p>Shift closed. Expected cash <strong>{{ s.expectedCash | number: '1.0-2' }}</strong>, counted
          <strong>{{ s.countedCash | number: '1.0-2' }}</strong>.</p>
        @if ((s.countedCash ?? 0) - s.expectedCash; as diff) {
          <p [class.short]="diff < 0">{{ diff < 0 ? 'Short' : 'Over' }} by {{ (diff < 0 ? -diff : diff) | number: '1.0-2' }}.</p>
        } @else {
          <p>The drawer balances.</p>
        }
      } @else {
        @if (data.session; as s) {
          <div class="summary">
            <div><span class="muted">Opening float</span><span>{{ s.openingFloat | number: '1.0-2' }}</span></div>
            <div><span class="muted">Cash taken</span><span>{{ s.cashTaken | number: '1.0-2' }}</span></div>
            <div><span class="muted">Card / wallet</span><span>{{ s.otherTaken | number: '1.0-2' }}</span></div>
            <div class="strong"><span>Cash expected in drawer</span><span>{{ s.expectedCash | number: '1.0-2' }}</span></div>
          </div>
        } @else {
          <p class="muted">Count the cash already in the drawer before you start taking payments.</p>
        }
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>{{ data.session ? 'Cash counted in drawer (PKR)' : 'Opening float (PKR)' }}</mat-label>
          <input matInput type="number" min="0" [formControl]="amount" />
        </mat-form-field>
        @if (error()) {
          <div class="callout callout-danger">{{ error() }}</div>
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (closed()) {
        <button mat-flat-button color="primary" (click)="dialogRef.close(true)">Done</button>
      } @else {
        <button mat-button (click)="dialogRef.close()">Cancel</button>
        <button mat-flat-button color="primary" [disabled]="amount.invalid || busy()" (click)="submit()">
          {{ data.session ? 'Close shift' : 'Open shift' }}
        </button>
      }
    </mat-dialog-actions>
  `,
  styles: `
    .summary { display: grid; gap: 0.35rem; margin-bottom: 1rem; font-variant-numeric: tabular-nums; }
    .summary div { display: flex; justify-content: space-between; }
    .summary .strong { font-weight: 600; border-top: 1px solid var(--so-border); padding-top: 0.35rem; }
    .short { color: var(--so-danger); }
    .callout-danger { background: var(--so-danger-soft); color: var(--so-danger); }
  `,
})
export class ShiftDialogComponent {
  readonly dialogRef = inject(MatDialogRef<ShiftDialogComponent>);
  readonly data = inject<ShiftDialogData>(MAT_DIALOG_DATA);
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  readonly amount = inject(FormBuilder).control<number | null>(this.data.session ? null : 0, [Validators.required, Validators.min(0)]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly closed = signal<CashSession | null>(null);

  async submit(): Promise<void> {
    const base = `${this.config.apiBaseUrl}/api/v1/finance/cash-sessions`;
    this.busy.set(true);
    this.error.set(null);
    try {
      if (this.data.session) {
        this.closed.set(
          await firstValueFrom(this.http.post<CashSession>(`${base}/${this.data.session.id}/close`, { countedCash: Number(this.amount.value) })),
        );
      } else {
        await firstValueFrom(this.http.post<CashSession>(base, { branchId: this.data.branchId, openingFloat: Number(this.amount.value) }));
        this.dialogRef.close(true);
      }
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
