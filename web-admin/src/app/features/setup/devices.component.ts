import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ConfigService } from '../../core/config.service';
import { AuthService } from '../../core/auth/auth.service';

interface DeviceSummary {
  id: string;
  platform: string;
  deviceName: string | null;
  appVersion: string | null;
  lastSeenAtUtc: string | null;
  isRevoked: boolean;
  wipeRequested: boolean;
  userId: string;
  userDisplayName: string;
  username: string;
}

@Component({
  selector: 'app-devices',
  standalone: true,
  imports: [MatTableModule, MatButtonModule, MatIconModule, MatProgressBarModule, DatePipe],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1 class="page-title">Devices</h1>
          <p class="page-subtitle">
            Phones and tablets signed in to the mobile app. Revoke a lost device; wipe erases its local data.
            @if (!seesAllDevices()) {
              Showing your branch staff's devices.
            }
          </p>
        </div>
      </div>
      <div class="table-card">
        @if (loading()) {
          <mat-progress-bar mode="indeterminate" />
        }
        <table mat-table [dataSource]="devices()">
          <ng-container matColumnDef="device">
            <th mat-header-cell *matHeaderCellDef>Device</th>
            <td mat-cell *matCellDef="let device">
              <div class="device-cell">
                <span class="device-icon"><mat-icon>{{ device.platform === 'iOS' ? 'phone_iphone' : 'phone_android' }}</mat-icon></span>
                <div>
                  <span class="cell-primary">{{ device.deviceName ?? 'Unnamed device' }}</span>
                  <span class="cell-secondary">{{ device.platform }}</span>
                </div>
              </div>
            </td>
          </ng-container>
          <ng-container matColumnDef="user">
            <th mat-header-cell *matHeaderCellDef>Used by</th>
            <td mat-cell *matCellDef="let device">
              <span class="cell-primary">{{ device.userDisplayName }}</span>
              <span class="cell-secondary">{{ device.username }}</span>
            </td>
          </ng-container>
          <ng-container matColumnDef="appVersion">
            <th mat-header-cell *matHeaderCellDef>App version</th>
            <td mat-cell *matCellDef="let device"><span class="mono">{{ device.appVersion ?? '—' }}</span></td>
          </ng-container>
          <ng-container matColumnDef="lastSeen">
            <th mat-header-cell *matHeaderCellDef>Last seen</th>
            <td mat-cell *matCellDef="let device" class="muted">{{ device.lastSeenAtUtc ? (device.lastSeenAtUtc | date: 'medium') : '—' }}</td>
          </ng-container>
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>Status</th>
            <td mat-cell *matCellDef="let device">
              @if (device.isRevoked) {
                <span class="pill pill-danger">{{ device.wipeRequested ? 'Revoked · wipe' : 'Revoked' }}</span>
              } @else {
                <span class="pill pill-success">Active</span>
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef></th>
            <td mat-cell *matCellDef="let device" class="cell-actions">
              @if (!device.isRevoked) {
                <button mat-button color="warn" (click)="revoke(device, false)">Revoke</button>
                <button mat-button color="warn" (click)="revoke(device, true)">
                  <mat-icon>delete_forever</mat-icon>
                  Revoke + wipe
                </button>
              }
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>
        @if (!loading() && devices().length === 0) {
          <div class="empty-state">
            <mat-icon>devices_other</mat-icon>
            <div>No devices have signed in yet.</div>
          </div>
        }
      </div>
    </div>
  `,
  styles: `
    .device-cell { display: flex; align-items: center; gap: 0.75rem; padding: 0.5rem 0; }
    .device-icon {
      width: 36px; height: 36px; border-radius: 10px; flex-shrink: 0;
      display: flex; align-items: center; justify-content: center;
      background: var(--so-surface-muted); color: var(--so-text-muted); border: 1px solid var(--so-border);
    }
    .device-icon .mat-icon { width: 20px; height: 20px; font-size: 20px; }
  `,
})
export class DevicesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly auth = inject(AuthService);

  readonly devices = signal<DeviceSummary[]>([]);
  readonly loading = signal(true);
  readonly columns = ['device', 'user', 'appVersion', 'lastSeen', 'status', 'actions'];
  readonly seesAllDevices = computed(() => this.auth.hasTenantWidePermission('Devices.Manage'));

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.devices.set(
        await firstValueFrom(this.http.get<DeviceSummary[]>(`${this.config.apiBaseUrl}/api/v1/identity/devices`)),
      );
    } finally {
      this.loading.set(false);
    }
  }

  async revoke(device: DeviceSummary, requestWipe: boolean): Promise<void> {
    await firstValueFrom(
      this.http.post(`${this.config.apiBaseUrl}/api/v1/identity/devices/${device.id}/revoke`, { requestWipe }),
    );
    await this.load();
  }
}
