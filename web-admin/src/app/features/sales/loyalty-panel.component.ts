import { DecimalPipe } from '@angular/common';
import { HttpClient, HttpContext, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ConfigService } from '../../core/config.service';
import { SILENT_ERRORS } from '../../core/http/error-toast.interceptor';
import { LoyaltyBranchStatus, LoyaltyLookup } from './sales.models';

/** What the payment sends: the member's phone (only once it's a member) and whether to use their reward. */
export interface LoyaltyChoice {
  phone: string | null;
  useReward: boolean;
  rewardAmount: number;
}

/**
 * Visit rewards on the payment screen (docs/decisions.md "Visit rewards"). Shown only where the
 * owner switched the programme on. The cashier types the customer's phone: a member's progress
 * shows, a ready reward can be taken off this bill, and a new number can become a member where
 * the cashier is allowed to add members.
 */
@Component({
  selector: 'app-loyalty-panel',
  standalone: true,
  imports: [FormsModule, DecimalPipe, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule],
  template: `
    @if (status()?.programmeOn) {
      <section class="panel">
        <div class="head"><mat-icon>loyalty</mat-icon> Visit rewards</div>
        <div class="row">
          <mat-form-field appearance="outline" class="phone" subscriptSizing="dynamic">
            <mat-label>Customer phone</mat-label>
            <input matInput [(ngModel)]="phone" (keydown.enter)="lookup()" placeholder="0300 1234567" inputmode="tel" />
          </mat-form-field>
          <button mat-stroked-button type="button" (click)="lookup()" [disabled]="busy() || !phone.trim()">Check</button>
        </div>

        @if (error()) {
          <div class="note danger">{{ error() }}</div>
        }

        @if (found(); as m) {
          @if (m.isMember) {
            <div class="member">
              <div>
                <strong>{{ m.name }}</strong> <span class="muted">· {{ m.phone }}</span>
                @if (m.status === 'Paused') { <span class="pill pill-warning">Paused</span> }
              </div>
              <div class="dots" [attr.aria-label]="m.visits + ' of ' + m.visitsForReward + ' visits'">
                @for (d of dots(); track $index) { <span class="dot" [class.on]="d"></span> }
                <span class="muted">{{ m.visits }} of {{ m.visitsForReward }} visits</span>
              </div>
              @if (m.status === 'Paused') {
                <div class="note">This membership is paused: today's bill won't count.</div>
              } @else {
                @if (m.rewardAvailable) {
                  <mat-checkbox [ngModel]="useReward()" (ngModelChange)="setReward($event)" [disabled]="total() < m.minRewardBill">
                    Use reward: <strong>Rs {{ m.rewardAmount | number: '1.0-0' }} off</strong>
                  </mat-checkbox>
                  @if (total() < m.minRewardBill) {
                    <div class="note">The reward needs a bill of Rs {{ m.minRewardBill | number: '1.0-0' }} or more.</div>
                  }
                }
                @if (m.countedToday) {
                  <div class="note">Today's visit is already counted.</div>
                } @else if (total() < m.minVisitBill) {
                  <div class="note">This bill won't count as a visit: visits count from Rs {{ m.minVisitBill | number: '1.0-0' }}.</div>
                }
              }
            </div>
          } @else {
            <div class="member">
              <div class="muted">{{ m.phone }} isn't a member.</div>
              @if (m.canAddMember) {
                <div class="row">
                  <mat-form-field appearance="outline" class="phone" subscriptSizing="dynamic">
                    <mat-label>Name (optional)</mat-label>
                    <input matInput [(ngModel)]="name" />
                  </mat-form-field>
                  <button mat-flat-button color="primary" type="button" (click)="addMember()" [disabled]="busy()">
                    <mat-icon>person_add</mat-icon> Add member
                  </button>
                </div>
              } @else {
                <div class="note">Ask a manager to add members.</div>
              }
            </div>
          }
        }
      </section>
    }
  `,
  styles: `
    .panel { border: 1px solid var(--so-border); border-radius: var(--so-radius-sm); padding: 0.75rem; margin: 0.25rem 0 1rem; }
    .head { display: flex; align-items: center; gap: 0.4rem; font-weight: 600; margin-bottom: 0.5rem; color: var(--so-accent); }
    .row { display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap; }
    .phone { flex: 1; min-width: 12rem; }
    .member { margin-top: 0.6rem; display: flex; flex-direction: column; gap: 0.35rem; }
    .dots { display: flex; align-items: center; gap: 0.3rem; }
    .dot { width: 14px; height: 14px; border-radius: 50%; border: 2px solid var(--so-accent); }
    .dot.on { background: var(--so-accent); }
    .dots .muted { margin-left: 0.4rem; }
    .note { font-size: 0.85rem; color: var(--so-text-muted); }
    .note.danger { color: var(--so-danger); }
    .pill { margin-left: 0.4rem; }
  `,
})
export class LoyaltyPanelComponent implements OnInit {
  readonly branchId = input.required<string>();
  readonly total = input.required<number>();
  readonly initialPhone = input<string | null>(null);
  readonly changed = output<LoyaltyChoice>();

  private readonly http = inject(HttpClient);
  private readonly api = `${inject(ConfigService).apiBaseUrl}/api/v1/sales/loyalty`;

  phone = '';
  name = '';
  readonly status = signal<LoyaltyBranchStatus | null>(null);
  readonly found = signal<LoyaltyLookup | null>(null);
  readonly useReward = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly dots = computed(() => {
    const m = this.found();
    return m ? Array.from({ length: m.visitsForReward }, (_, i) => i < m.visits) : [];
  });

  async ngOnInit(): Promise<void> {
    try {
      this.status.set(await firstValueFrom(this.http.get<LoyaltyBranchStatus>(`${this.api}/branch-status`, {
        params: new HttpParams().set('branchId', this.branchId()),
        context: new HttpContext().set(SILENT_ERRORS, true),
      })));
    } catch {
      this.status.set(null);
      return;
    }

    // The waiter noted the phone on the order: look it up straight away.
    if (this.status()?.programmeOn && this.initialPhone()) {
      this.phone = this.initialPhone()!;
      await this.lookup();
    }
  }

  async lookup(): Promise<void> {
    if (!this.phone.trim()) {
      return;
    }
    await this.run(() => firstValueFrom(this.http.get<LoyaltyLookup>(`${this.api}/lookup`, {
      params: new HttpParams().set('phone', this.phone.trim()).set('branchId', this.branchId()),
      context: new HttpContext().set(SILENT_ERRORS, true),
    })));
  }

  async addMember(): Promise<void> {
    await this.run(() => firstValueFrom(this.http.post<LoyaltyLookup>(`${this.api}/members`,
      { phone: this.phone.trim(), name: this.name.trim() || null, branchId: this.branchId() },
      { context: new HttpContext().set(SILENT_ERRORS, true) })));
  }

  setReward(on: boolean): void {
    this.useReward.set(on);
    this.emit();
  }

  private async run(request: () => Promise<LoyaltyLookup>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const result = await request();
      this.found.set(result);
      this.useReward.set(false);
    } catch (e) {
      this.found.set(null);
      this.useReward.set(false);
      this.error.set(e instanceof HttpErrorResponse ? (e.error?.detail ?? 'Something went wrong.') : 'Something went wrong.');
    } finally {
      this.busy.set(false);
      this.emit();
    }
  }

  private emit(): void {
    const m = this.found();
    const member = !!m && m.isMember && m.status === 'Active';
    this.changed.emit({
      phone: member ? m!.phone : null,
      useReward: member && this.useReward(),
      rewardAmount: m?.rewardAmount ?? 0,
    });
  }
}
