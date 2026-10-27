import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTableModule } from '@angular/material/table';
import { ConfigService } from '../../core/config.service';
import { LoyaltyBranchSwitches, LoyaltyMemberRow, LoyaltyRules, LoyaltySettings, PagedList } from './sales.models';

/** Add a member from the back office (owners and managers): no till, so no branch. */
@Component({
  selector: 'app-add-member-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data ? 'Edit member' : 'Add member' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" (ngSubmit)="submit()">
        @if (!data) {
          <mat-form-field appearance="outline" class="full-width">
            <mat-label>Mobile number</mat-label>
            <input matInput formControlName="phone" placeholder="0300 1234567" inputmode="tel" />
          </mat-form-field>
        }
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" />
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button mat-flat-button color="primary" [disabled]="form.invalid" (click)="submit()">{{ data ? 'Save' : 'Add' }}</button>
    </mat-dialog-actions>
  `,
})
export class MemberDialogComponent {
  readonly dialogRef = inject(MatDialogRef<MemberDialogComponent>);
  readonly data = inject<LoyaltyMemberRow | null>(MAT_DIALOG_DATA);
  readonly form = inject(FormBuilder).nonNullable.group({
    phone: [this.data?.phone ?? '', this.data ? [] : [Validators.required, Validators.maxLength(30)]],
    name: [this.data?.name ?? '', [Validators.maxLength(200)]],
  });

  submit(): void {
    if (this.form.valid) {
      const v = this.form.getRawValue();
      this.dialogRef.close({ phone: v.phone.trim(), name: v.name.trim() || null });
    }
  }
}

/**
 * Visit rewards (docs/decisions.md): the members list for owners and managers, each branch's
 * switches (the programme: owner; "cashiers can add members": owner or that branch's manager),
 * and the company's numbers (owner).
 */
@Component({
  selector: 'app-rewards',
  standalone: true,
  imports: [FormsModule, ReactiveFormsModule, DatePipe, DecimalPipe, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatProgressBarModule, MatSlideToggleModule, MatTableModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1 class="page-title">Visit rewards</h1>
          @if (settings(); as s) {
            <p class="page-subtitle">
              Every {{ s.rules.visitsForReward }} visits of Rs {{ s.rules.minVisitBill | number: '1.0-0' }} or more (one a day):
              Rs {{ s.rules.rewardAmount | number: '1.0-0' }} off a bill of Rs {{ s.rules.minRewardBill | number: '1.0-0' }} or more.
              Visits lapse after {{ s.rules.expiryMonths }} months without one.
            </p>
          }
        </div>
        <div class="page-actions">
          <button mat-flat-button color="primary" (click)="addMember()"><mat-icon>person_add</mat-icon> Add member</button>
        </div>
      </div>

      @if (settings(); as s) {
        <div class="cards">
          <div class="table-card switches">
            <h2 class="card-title">Branches</h2>
            @for (b of s.branches; track b.branchId) {
              <div class="switch-row">
                <span class="branch">{{ b.branchName }}</span>
                <mat-slide-toggle [checked]="b.isEnabled" [disabled]="!s.canEditRules" (change)="setBranch(b, { isEnabled: $event.checked })">
                  Visit rewards on
                </mat-slide-toggle>
                <mat-slide-toggle [checked]="b.cashierCanAddMembers" [disabled]="!b.isEnabled" (change)="setBranch(b, { cashierCanAddMembers: $event.checked })">
                  Cashiers can add members
                </mat-slide-toggle>
              </div>
            } @empty {
              <div class="muted">No branches.</div>
            }
            @if (!s.canEditRules) {
              <p class="hint">Only the owner can switch visit rewards on or off at a branch.</p>
            }
          </div>

          @if (s.canEditRules) {
            <form class="table-card rules" [formGroup]="rules" (ngSubmit)="saveRules()">
              <h2 class="card-title">Rules</h2>
              <div class="rule-grid">
                <mat-form-field appearance="outline"><mat-label>Visits for a reward</mat-label><input matInput type="number" formControlName="visitsForReward" /></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>A visit counts from (Rs)</mat-label><input matInput type="number" formControlName="minVisitBill" /></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Reward (Rs off)</mat-label><input matInput type="number" formControlName="rewardAmount" /></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Reward needs a bill of (Rs)</mat-label><input matInput type="number" formControlName="minRewardBill" /></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Visits lapse after (months)</mat-label><input matInput type="number" formControlName="expiryMonths" /></mat-form-field>
              </div>
              <button mat-stroked-button color="primary" [disabled]="rules.invalid || rules.pristine">Save rules</button>
            </form>
          }
        </div>
      }

      <div class="table-card">
        <div class="toolbar">
          <mat-form-field appearance="outline" class="search" subscriptSizing="dynamic">
            <mat-icon matPrefix>search</mat-icon>
            <mat-label>Search by name or phone</mat-label>
            <input matInput [(ngModel)]="search" (keydown.enter)="load()" />
          </mat-form-field>
          <span class="muted">{{ total() }} members</span>
        </div>
        @if (loading()) {
          <mat-progress-bar mode="indeterminate" />
        }
        <table mat-table [dataSource]="members()">
          <ng-container matColumnDef="member">
            <th mat-header-cell *matHeaderCellDef>Member</th>
            <td mat-cell *matCellDef="let m">
              <span class="cell-primary">{{ m.name }}</span>
              <span class="cell-secondary mono">{{ m.phone }}</span>
            </td>
          </ng-container>
          <ng-container matColumnDef="visits">
            <th mat-header-cell *matHeaderCellDef>Visits</th>
            <td mat-cell *matCellDef="let m">
              {{ m.visits }} of {{ m.visitsForReward }}
              @if (m.rewardAvailable) { <span class="pill pill-success">Reward ready</span> }
            </td>
          </ng-container>
          <ng-container matColumnDef="rewards">
            <th mat-header-cell *matHeaderCellDef>Rewards used</th>
            <td mat-cell *matCellDef="let m">{{ m.rewardsUsed }}</td>
          </ng-container>
          <ng-container matColumnDef="last">
            <th mat-header-cell *matHeaderCellDef>Last visit</th>
            <td mat-cell *matCellDef="let m">{{ m.lastVisit ? (m.lastVisit | date: 'd MMM y') : '—' }}</td>
          </ng-container>
          <ng-container matColumnDef="joined">
            <th mat-header-cell *matHeaderCellDef>Added</th>
            <td mat-cell *matCellDef="let m">
              <span class="cell-primary">{{ m.joinedAtUtc | date: 'd MMM y, h:mm a' }}</span>
              <span class="cell-secondary">{{ m.joinedBy ?? '' }}@if (m.joinedBranch) { · {{ m.joinedBranch }} }</span>
            </td>
          </ng-container>
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>Status</th>
            <td mat-cell *matCellDef="let m"><span class="pill" [class.pill-success]="m.status === 'Active'" [class.pill-warning]="m.status !== 'Active'">{{ m.status }}</span></td>
          </ng-container>
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef></th>
            <td mat-cell *matCellDef="let m" class="cell-actions">
              <button mat-button (click)="edit(m)">Edit</button>
              <button mat-button (click)="setStatus(m, m.status === 'Active' ? 'Paused' : 'Active')">{{ m.status === 'Active' ? 'Pause' : 'Resume' }}</button>
            </td>
          </ng-container>
          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>
        @if (!loading() && members().length === 0) {
          <div class="empty-state"><mat-icon>loyalty</mat-icon><div>No members yet.</div></div>
        }
      </div>
    </div>
  `,
  styles: `
    .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(22rem, 1fr)); gap: 1rem; margin-bottom: 1rem; }
    .card-title { font-size: 1rem; margin: 0 0 0.75rem; }
    .switches, .rules { padding: 1rem 1.25rem; }
    .switch-row { display: flex; flex-wrap: wrap; align-items: center; gap: 1rem; padding: 0.5rem 0; border-top: 1px solid var(--so-border); }
    .switch-row .branch { font-weight: 600; min-width: 8rem; }
    .rule-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr)); gap: 0 0.75rem; }
    .hint { font-size: 0.8rem; color: var(--so-text-muted); margin: 0.5rem 0 0; }
    .toolbar { display: flex; align-items: center; gap: 1rem; padding: 0.75rem 1rem; }
    .search { flex: 1; max-width: 26rem; }
    .pill { margin-left: 0.4rem; }
  `,
})
export class RewardsComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialog);
  private readonly api = `${inject(ConfigService).apiBaseUrl}/api/v1/sales/loyalty`;

  readonly columns = ['member', 'visits', 'rewards', 'last', 'joined', 'status', 'actions'];
  readonly settings = signal<LoyaltySettings | null>(null);
  readonly members = signal<LoyaltyMemberRow[]>([]);
  readonly total = signal(0);
  readonly loading = signal(true);
  search = '';

  readonly rules = inject(FormBuilder).nonNullable.group({
    visitsForReward: [5, [Validators.required, Validators.min(1), Validators.max(100)]],
    minVisitBill: [2000, [Validators.required, Validators.min(0)]],
    rewardAmount: [500, [Validators.required, Validators.min(1)]],
    minRewardBill: [2500, [Validators.required, Validators.min(1)]],
    expiryMonths: [6, [Validators.required, Validators.min(1), Validators.max(60)]],
  });

  async ngOnInit(): Promise<void> {
    await Promise.all([this.loadSettings(), this.load()]);
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      const params = new HttpParams().set('search', this.search.trim()).set('pageSize', 200);
      const page = await firstValueFrom(this.http.get<PagedList<LoyaltyMemberRow>>(`${this.api}/members`, { params }));
      this.members.set(page.items);
      this.total.set(page.totalCount);
    } finally {
      this.loading.set(false);
    }
  }

  async setBranch(branch: LoyaltyBranchSwitches, change: Partial<Pick<LoyaltyBranchSwitches, 'isEnabled' | 'cashierCanAddMembers'>>): Promise<void> {
    try {
      await firstValueFrom(this.http.put(`${this.api}/settings/branches/${branch.branchId}`, change));
    } finally {
      await this.loadSettings();
    }
  }

  async saveRules(): Promise<void> {
    await firstValueFrom(this.http.put<LoyaltyRules>(`${this.api}/settings/rules`, this.rules.getRawValue()));
    await Promise.all([this.loadSettings(), this.load()]);
  }

  addMember(): void {
    this.dialog.open(MemberDialogComponent, { width: '400px', data: null }).afterClosed().subscribe(async (result) => {
      if (result) {
        await firstValueFrom(this.http.post(`${this.api}/members`, { phone: result.phone, name: result.name, branchId: null }));
        await this.load();
      }
    });
  }

  edit(member: LoyaltyMemberRow): void {
    this.dialog.open(MemberDialogComponent, { width: '400px', data: member }).afterClosed().subscribe(async (result) => {
      if (result?.name) {
        await firstValueFrom(this.http.patch(`${this.api}/members/${member.id}`, { name: result.name, status: null }));
        await this.load();
      }
    });
  }

  async setStatus(member: LoyaltyMemberRow, status: 'Active' | 'Paused'): Promise<void> {
    await firstValueFrom(this.http.patch(`${this.api}/members/${member.id}`, { name: null, status }));
    await this.load();
  }

  private async loadSettings(): Promise<void> {
    const settings = await firstValueFrom(this.http.get<LoyaltySettings>(`${this.api}/settings`));
    this.settings.set(settings);
    this.rules.reset(settings.rules);
  }
}
