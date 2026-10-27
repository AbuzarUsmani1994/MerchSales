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
import { LoyaltyChoice, LoyaltyPanelComponent } from './loyalty-panel.component';
import { ReceiptPrinter } from './receipt-printer';

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
 * Nothing is saved until the cashier has reviewed a summary and said yes; the receipt then
 * prints straight away.
 */
@Component({
  selector: 'app-payment-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DecimalPipe, MatDialogModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatInputModule, MatIconModule, LoyaltyPanelComponent],
  template: `
    <h2 mat-dialog-title>{{ reviewing() && !done() ? 'Check and take payment' : 'Take payment' }} · {{ data.order.documentNo }}</h2>
    <mat-dialog-content>
      @if (done(); as sale) {
        <div class="done">
          <mat-icon class="done-icon">check_circle</mat-icon>
          <div class="done-title">Paid {{ sale.amountPaid | number: '1.0-2' }} by {{ sale.paymentMethod ?? 'nothing due' }}</div>
          @if (sale.change > 0) {
            <div class="change">Give back <strong>{{ sale.change | number: '1.0-2' }}</strong></div>
          }
          <div class="muted">Invoice {{ sale.invoiceNo }}@if (sale.paymentNo) { · Receipt {{ sale.paymentNo }} }</div>
          <div class="printing">
            <mat-icon>print</mat-icon>
            @if (printError()) { <span class="danger">{{ printError() }}</span> } @else { <span>Receipt sent to the printer.</span> }
          </div>
          @if (sale.loyalty; as l) {
            <div class="loyalty-done">
              <mat-icon>loyalty</mat-icon>
              <span>
                <strong>{{ l.name ?? l.phone }}</strong>
                @if (l.rewardUsed) { · Reward used (Rs {{ l.rewardAmount | number: '1.0-0' }} off) }
                @if (l.visitCounted) { · Visit counted }
                · {{ l.visits }} of {{ l.visitsForReward }} visits@if (l.rewardAvailable) { · Reward ready next time }
                @if (l.message) { <br /><span class="muted">{{ l.message }}</span> }
              </span>
            </div>
          }
        </div>
      } @else {
        @if (reviewing()) {
          <div class="review">
            <div class="review-total">PKR {{ total() | number: '1.0-2' }}</div>
            <div class="muted">by <strong>{{ methodName() }}</strong></div>
            <dl>
              <dt>Order</dt><dd>{{ data.order.documentNo }} · {{ data.order.notes || 'Walk-in' }}</dd>
              <dt>Items</dt><dd>{{ itemCount() }}</dd>
              <dt>Bill</dt><dd>{{ data.order.grandTotal | number: '1.0-2' }}</dd>
              @if (reward() > 0) { <dt>Visit reward</dt><dd>-{{ reward() | number: '1.0-2' }}</dd> }
              @if (loyalty().phone) { <dt>Member</dt><dd>{{ loyalty().phone }}</dd> }
              @if (isCash()) {
                <dt>Cash received</dt><dd>{{ value().tendered | number: '1.0-2' }}</dd>
                <dt>Change</dt><dd class="strong">{{ change() | number: '1.0-2' }}</dd>
              } @else if (value().reference) {
                <dt>Reference</dt><dd>{{ value().reference }}</dd>
              }
            </dl>
            <div class="muted small">Taking payment closes the order and prints the receipt. It can't be undone here.</div>
          </div>
        }
        <div [hidden]="reviewing()">
        <div class="total-row">
          <span>{{ data.order.notes || 'Walk-in' }}</span>
          <span class="total">
            @if (reward() > 0) { <s class="was">{{ data.order.grandTotal | number: '1.0-2' }}</s> }
            PKR {{ total() | number: '1.0-2' }}
          </span>
        </div>
        <app-loyalty-panel [branchId]="data.order.branchId" [total]="data.order.grandTotal"
                           [initialPhone]="data.order.loyaltyPhone" (changed)="loyaltyChanged($event)" />
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
        </div>
        @if (error()) {
          <div class="callout callout-danger">{{ error() }}</div>
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (done()) {
        <button mat-button (click)="printAgain()"><mat-icon>print</mat-icon> Print again</button>
        <button mat-flat-button color="primary" (click)="dialogRef.close(done())">Done</button>
      } @else if (reviewing()) {
        <button mat-button [disabled]="busy()" (click)="reviewing.set(false)">Back</button>
        <button mat-flat-button color="primary" [disabled]="busy()" (click)="pay()">
          {{ busy() ? 'Saving…' : 'Yes, take payment' }}
        </button>
      } @else {
        <button mat-button (click)="dialogRef.close()">Cancel</button>
        <button mat-flat-button color="primary" [disabled]="!canPay()" (click)="review()">Collect payment</button>
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
    .printing { display: flex; gap: 0.4rem; align-items: center; justify-content: center; margin-top: 0.6rem; color: var(--so-text-muted); }
    .printing .danger { color: var(--so-danger); }
    .review { text-align: center; padding: 0.25rem 0 0.5rem; }
    .review-total { font-size: 2rem; line-height: 1.25; font-weight: 700; font-variant-numeric: tabular-nums; margin-bottom: 0.15rem; }
    .review dl { display: grid; grid-template-columns: auto 1fr; gap: 0.35rem 1rem; text-align: left; margin: 1rem auto; max-width: 24rem; }
    .review dt { color: var(--so-text-muted); }
    .review dd { margin: 0; text-align: right; font-variant-numeric: tabular-nums; }
    .review dd.strong { font-weight: 700; font-size: 1.1rem; }
    .small { font-size: 0.8rem; }
    .callout-danger { background: var(--so-danger-soft); color: var(--so-danger); margin-top: 0.75rem; }
    .was { color: var(--so-text-muted); font-size: 1rem; font-weight: 400; margin-right: 0.4rem; }
    .loyalty-done { display: flex; gap: 0.4rem; align-items: flex-start; justify-content: center; margin-top: 0.75rem; text-align: left; }
    .loyalty-done .mat-icon { color: var(--so-accent); }
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

  readonly value = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<CompletedSale | null>(null);
  readonly reviewing = signal(false);
  readonly printError = signal<string | null>(null);
  private readonly printer = inject(ReceiptPrinter);
  private tenderedAtPayment: number | null = null;
  readonly methodName = computed(() => this.data.methods.find((m) => m.id === this.value().methodId)?.name ?? '');
  readonly itemCount = computed(() => this.data.order.lines.reduce((n, l) => n + l.quantity, 0));
  readonly loyalty = signal<LoyaltyChoice>({ phone: null, useReward: false, rewardAmount: 0 });

  /** The visit reward comes off the bill before payment; the server checks it again. */
  readonly reward = computed(() => (this.loyalty().useReward ? Math.min(this.loyalty().rewardAmount, this.data.order.grandTotal) : 0));
  readonly total = computed(() => this.data.order.grandTotal - this.reward());

  readonly isCash = computed(() => this.data.methods.find((m) => m.id === this.value().methodId)?.kind === 'Cash');
  readonly change = computed(() => Number(this.value().tendered ?? 0) - this.total());
  readonly canPay = computed(() => !!this.value().methodId && (!this.isCash() || this.change() >= 0));

  /** Exact, then the next round notes a customer is likely to hand over. */
  readonly quickAmounts = computed(() => {
    const total = this.total();
    const ups = [100, 500, 1000, 5000].map((step) => Math.ceil(total / step) * step);
    return [...new Set([total, ...ups])].filter((a) => a >= total).slice(0, 4);
  });

  loyaltyChanged(choice: LoyaltyChoice): void {
    const before = this.total();
    this.loyalty.set(choice);
    // Keep "cash received" in step when it was simply the exact total.
    if (Number(this.form.controls.tendered.value) === before) {
      this.form.controls.tendered.setValue(this.total());
    }
  }

  /** The last look before anything is saved. */
  review(): void {
    this.error.set(null);
    this.reviewing.set(true);
  }

  async printAgain(): Promise<void> {
    await this.printReceipt(true);
  }

  private async printReceipt(copy: boolean): Promise<void> {
    this.printError.set(null);
    try {
      await this.printer.print(this.data.order.id, { tendered: copy ? null : this.tenderedAtPayment, copy });
    } catch {
      this.printError.set("The receipt didn't print. Use Print again.");
    }
  }

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
            loyaltyPhone: this.loyalty().phone,
            useReward: this.loyalty().useReward,
          },
          { headers: new HttpHeaders({ 'If-Match': `"${this.data.order.etag}"` }) },
        ),
      );
      this.done.set(sale);
      this.tenderedAtPayment = this.isCash() ? Number(v.tendered) : null;
      void this.printReceipt(false);
    } catch (e) {
      // Back to the form: the reason (e.g. reward not ready) usually needs a change there.
      this.reviewing.set(false);
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
