import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { PaymentMethod, PaymentPart, SalesOrderLine } from './sales.models';

interface Part {
  methodId: string;
  amount: number;
  tendered: number | null;
  reference: string;
  /** Lines this guest is paying for, when split by items. */
  lineIds: string[];
  picking: boolean;
}

export interface SplitChoice {
  parts: PaymentPart[];
  /** True when every part is complete and they add up to the total exactly. */
  valid: boolean;
}

function money(n: number): number {
  return Math.round(n * 100) / 100;
}

/**
 * Split the bill between guests (docs/decisions.md "Tables and kitchen screen"): equally, by the
 * items each guest had, or by typed amounts. Each part has its own method (cash with change,
 * card, wallet). One invoice, several payments; the server checks they add up to the total.
 */
@Component({
  selector: 'app-split-bill',
  standalone: true,
  imports: [FormsModule, DecimalPipe, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule],
  template: `
    <div class="equal">
      <span class="muted">Split equally:</span>
      @for (n of [2, 3, 4, 5, 6]; track n) {
        <button mat-stroked-button type="button" (click)="splitEqually(n)">{{ n }}</button>
      }
    </div>

    @for (p of parts(); track $index; let i = $index) {
      <div class="part">
        <div class="part-head">
          <strong>Guest {{ i + 1 }}</strong>
          <span class="spacer"></span>
          @if (parts().length > 2) {
            <button mat-icon-button type="button" (click)="remove(i)" aria-label="Remove this part"><mat-icon>close</mat-icon></button>
          }
        </div>
        <div class="row">
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="method">
            <mat-label>Method</mat-label>
            <mat-select [ngModel]="p.methodId" (ngModelChange)="update(i, { methodId: $event })">
              @for (m of methods(); track m.id) {
                <mat-option [value]="m.id">{{ m.name }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="amount">
            <mat-label>Amount</mat-label>
            <input matInput type="number" min="0" [ngModel]="p.amount" (ngModelChange)="update(i, { amount: money($event), lineIds: [] })" />
          </mat-form-field>
          @if (isCash(p)) {
            <mat-form-field appearance="outline" subscriptSizing="dynamic" class="amount">
              <mat-label>Cash received</mat-label>
              <input matInput type="number" min="0" [ngModel]="p.tendered" (ngModelChange)="update(i, { tendered: $event === null || $event === '' ? null : Number($event) })" />
            </mat-form-field>
          } @else {
            <mat-form-field appearance="outline" subscriptSizing="dynamic" class="amount">
              <mat-label>Reference</mat-label>
              <input matInput [ngModel]="p.reference" (ngModelChange)="update(i, { reference: $event })" />
            </mat-form-field>
          }
        </div>
        <div class="tools">
          <button mat-button type="button" (click)="update(i, { picking: !p.picking })">
            <mat-icon>checklist</mat-icon> {{ p.picking ? 'Done picking' : 'By items' }}
          </button>
          @if (remaining() !== 0) {
            <button mat-button type="button" (click)="update(i, { amount: money(p.amount + remaining()), lineIds: [] })">
              <mat-icon>add</mat-icon> Rest ({{ remaining() | number: '1.0-2' }})
            </button>
          }
          @if (isCash(p) && changeFor(p) > 0) { <span class="give">Change {{ changeFor(p) | number: '1.0-2' }}</span> }
          @if (isCash(p) && changeFor(p) < 0) { <span class="short">Short {{ -changeFor(p) | number: '1.0-2' }}</span> }
        </div>
        @if (p.picking) {
          <div class="items">
            @for (l of lines(); track l.id) {
              <mat-checkbox [checked]="p.lineIds.includes(l.id)" [disabled]="takenByOther(i, l.id)" (change)="toggleLine(i, l, $event.checked)">
                {{ l.quantity | number: '1.0-3' }}× {{ l.productName }} <span class="muted">{{ l.lineTotal | number: '1.0-2' }}</span>
              </mat-checkbox>
            }
            @if (reward() > 0) { <div class="muted small">The visit reward comes off the last guest's part.</div> }
          </div>
        }
      </div>
    }

    <div class="foot">
      <button mat-stroked-button type="button" (click)="add()" [disabled]="parts().length >= 10"><mat-icon>person_add</mat-icon> Add guest</button>
      <span class="spacer"></span>
      @if (remaining() === 0) {
        <span class="ok"><mat-icon>check</mat-icon> Adds up to {{ total() | number: '1.0-2' }}</span>
      } @else if (remaining() > 0) {
        <span class="short">{{ remaining() | number: '1.0-2' }} still to pay</span>
      } @else {
        <span class="short">{{ -remaining() | number: '1.0-2' }} too much</span>
      }
    </div>
  `,
  styles: `
    .equal { display: flex; align-items: center; gap: 0.4rem; flex-wrap: wrap; margin-bottom: 0.75rem; }
    .equal button { min-width: 2.75rem; }
    .part { border: 1px solid var(--so-border); border-radius: var(--so-radius-sm); padding: 0.5rem 0.75rem 0.25rem; margin-bottom: 0.6rem; }
    .part-head { display: flex; align-items: center; min-height: 2rem; }
    .spacer { flex: 1; }
    .row { display: flex; gap: 0.5rem; flex-wrap: wrap; }
    .method { flex: 1 1 8rem; }
    .amount { flex: 1 1 7rem; }
    .tools { display: flex; align-items: center; gap: 0.25rem; flex-wrap: wrap; font-size: 0.9rem; }
    .items { display: flex; flex-direction: column; padding: 0 0 0.5rem 0.25rem; }
    .give { color: var(--so-success); margin-left: auto; }
    .short { color: var(--so-danger); }
    .ok { color: var(--so-success); display: inline-flex; align-items: center; gap: 0.25rem; }
    .foot { display: flex; align-items: center; gap: 0.5rem; margin-top: 0.25rem; }
    .small { font-size: 0.8rem; }
  `,
})
export class SplitBillComponent {
  readonly total = input.required<number>();
  readonly reward = input<number>(0);
  readonly lines = input.required<SalesOrderLine[]>();
  readonly methods = input.required<PaymentMethod[]>();
  readonly changed = output<SplitChoice>();

  protected readonly money = money;
  protected readonly Number = Number;
  readonly parts = signal<Part[]>([]);
  readonly remaining = computed(() => money(this.total() - this.parts().reduce((sum, p) => sum + (Number(p.amount) || 0), 0)));

  constructor() {
    effect(() => {
      // A new total (a reward switched on or off) re-splits evenly between the same guests.
      const total = this.total();
      const count = Math.max(2, untracked(this.parts).length);
      if (this.lastTotal !== total) {
        this.lastTotal = total;
        queueMicrotask(() => this.splitEqually(count));
      }
    });
  }

  private lastTotal: number | null = null;

  isCash(part: Part): boolean {
    return this.methods().find((m) => m.id === part.methodId)?.kind === 'Cash';
  }

  changeFor(part: Part): number {
    return part.tendered === null ? 0 : money(part.tendered - part.amount);
  }

  splitEqually(count: number): void {
    const total = this.total();
    const share = Math.floor((total / count) * 100) / 100;
    const cash = this.methods().find((m) => m.kind === 'Cash')?.id ?? this.methods()[0]?.id ?? '';
    const old = this.parts();
    this.parts.set(Array.from({ length: count }, (_, i) => {
      const amount = i === count - 1 ? money(total - share * (count - 1)) : share;
      const methodId = old[i]?.methodId ?? cash;
      return { methodId, amount, tendered: null, reference: '', lineIds: [], picking: false };
    }));
    this.emit();
  }

  add(): void {
    const cash = this.methods().find((m) => m.kind === 'Cash')?.id ?? this.methods()[0]?.id ?? '';
    this.parts.update((ps) => [...ps, { methodId: cash, amount: Math.max(0, this.remaining()), tendered: null, reference: '', lineIds: [], picking: false }]);
    this.emit();
  }

  remove(index: number): void {
    this.parts.update((ps) => ps.filter((_, i) => i !== index));
    this.emit();
  }

  update(index: number, change: Partial<Part>): void {
    this.parts.update((ps) => ps.map((p, i) => (i === index ? { ...p, ...change } : p)));
    this.emit();
  }

  takenByOther(index: number, lineId: string): boolean {
    return this.parts().some((p, i) => i !== index && p.lineIds.includes(lineId));
  }

  /** By items: the guest's amount is the sum of the lines they had; the reward comes off the last guest. */
  toggleLine(index: number, line: SalesOrderLine, checked: boolean): void {
    const part = this.parts()[index];
    const lineIds = checked ? [...part.lineIds, line.id] : part.lineIds.filter((id) => id !== line.id);
    const itemsTotal = this.lines().filter((l) => lineIds.includes(l.id)).reduce((sum, l) => sum + l.lineTotal, 0);
    const isLast = index === this.parts().length - 1;
    this.update(index, { lineIds, amount: money(Math.max(0, itemsTotal - (isLast ? this.reward() : 0))) });
  }

  private emit(): void {
    const parts = this.parts();
    const valid =
      parts.length >= 2 &&
      this.remaining() === 0 &&
      parts.every((p) => p.methodId && p.amount > 0 && (!this.isCash(p) || p.tendered === null || p.tendered >= p.amount));
    this.changed.emit({
      valid,
      parts: parts.map((p) => ({
        paymentMethodId: p.methodId,
        amount: money(p.amount),
        tendered: this.isCash(p) ? (p.tendered ?? p.amount) : null,
        reference: this.isCash(p) ? null : p.reference.trim() || null,
      })),
    });
  }
}
