import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MatTableModule } from '@angular/material/table';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ConfigService } from '../../core/config.service';

interface UserSummary {
  id: string;
  username: string;
  displayName: string;
  email: string | null;
  isActive: boolean;
  mfaEnabled: boolean;
}

@Component({
  selector: 'app-users',
  standalone: true,
  imports: [MatTableModule, MatChipsModule, MatProgressBarModule],
  template: `
    <h2>Users</h2>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" />
    }
    <table mat-table [dataSource]="users()" class="mat-elevation-z1">
      <ng-container matColumnDef="username">
        <th mat-header-cell *matHeaderCellDef>Username</th>
        <td mat-cell *matCellDef="let user">{{ user.username }}</td>
      </ng-container>
      <ng-container matColumnDef="displayName">
        <th mat-header-cell *matHeaderCellDef>Display name</th>
        <td mat-cell *matCellDef="let user">{{ user.displayName }}</td>
      </ng-container>
      <ng-container matColumnDef="email">
        <th mat-header-cell *matHeaderCellDef>Email</th>
        <td mat-cell *matCellDef="let user">{{ user.email }}</td>
      </ng-container>
      <ng-container matColumnDef="status">
        <th mat-header-cell *matHeaderCellDef>Status</th>
        <td mat-cell *matCellDef="let user">
          <mat-chip [color]="user.isActive ? 'primary' : 'warn'" highlighted>
            {{ user.isActive ? 'Active' : 'Inactive' }}
          </mat-chip>
        </td>
      </ng-container>
      <ng-container matColumnDef="mfa">
        <th mat-header-cell *matHeaderCellDef>MFA</th>
        <td mat-cell *matCellDef="let user">{{ user.mfaEnabled ? 'Enabled' : '—' }}</td>
      </ng-container>

      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns"></tr>
    </table>
  `,
  styles: `table { width: 100%; }`,
})
export class UsersComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  readonly users = signal<UserSummary[]>([]);
  readonly loading = signal(true);
  readonly columns = ['username', 'displayName', 'email', 'status', 'mfa'];

  async ngOnInit(): Promise<void> {
    try {
      this.users.set(
        await firstValueFrom(this.http.get<UserSummary[]>(`${this.config.apiBaseUrl}/api/v1/identity/users`)),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
