import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
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
  imports: [TableModule, ButtonModule, TagModule],
  template: `
    <h2>Users</h2>
    <p-table [value]="users()" [loading]="loading()">
      <ng-template pTemplate="header">
        <tr>
          <th>Username</th>
          <th>Display name</th>
          <th>Email</th>
          <th>Status</th>
          <th>MFA</th>
        </tr>
      </ng-template>
      <ng-template pTemplate="body" let-user>
        <tr>
          <td>{{ user.username }}</td>
          <td>{{ user.displayName }}</td>
          <td>{{ user.email }}</td>
          <td><p-tag [severity]="user.isActive ? 'success' : 'danger'" [value]="user.isActive ? 'Active' : 'Inactive'" /></td>
          <td>{{ user.mfaEnabled ? 'Enabled' : '—' }}</td>
        </tr>
      </ng-template>
    </p-table>
  `,
})
export class UsersComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  readonly users = signal<UserSummary[]>([]);
  readonly loading = signal(true);

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
