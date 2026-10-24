import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ConfigService } from '../../core/config.service';

interface DeviceSummary {
  id: string;
  platform: string;
  deviceName: string | null;
  appVersion: string | null;
  lastSeenAtUtc: string | null;
  isRevoked: boolean;
  wipeRequested: boolean;
}

@Component({
  selector: 'app-devices',
  standalone: true,
  imports: [MatTableModule, MatButtonModule, MatChipsModule, MatProgressBarModule, DatePipe],
  template: `
    <h2>Devices</h2>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" />
    }
    <table mat-table [dataSource]="devices()" class="mat-elevation-z1">
      <ng-container matColumnDef="platform">
        <th mat-header-cell *matHeaderCellDef>Platform</th>
        <td mat-cell *matCellDef="let device">{{ device.platform }}</td>
      </ng-container>
      <ng-container matColumnDef="deviceName">
        <th mat-header-cell *matHeaderCellDef>Device</th>
        <td mat-cell *matCellDef="let device">{{ device.deviceName }}</td>
      </ng-container>
      <ng-container matColumnDef="appVersion">
        <th mat-header-cell *matHeaderCellDef>App version</th>
        <td mat-cell *matCellDef="let device">{{ device.appVersion }}</td>
      </ng-container>
      <ng-container matColumnDef="lastSeen">
        <th mat-header-cell *matHeaderCellDef>Last seen</th>
        <td mat-cell *matCellDef="let device">{{ device.lastSeenAtUtc | date: 'short' }}</td>
      </ng-container>
      <ng-container matColumnDef="status">
        <th mat-header-cell *matHeaderCellDef>Status</th>
        <td mat-cell *matCellDef="let device">
          <mat-chip [color]="device.isRevoked ? 'warn' : 'primary'" highlighted>
            {{ device.isRevoked ? 'Revoked' : 'Active' }}
          </mat-chip>
        </td>
      </ng-container>
      <ng-container matColumnDef="actions">
        <th mat-header-cell *matHeaderCellDef></th>
        <td mat-cell *matCellDef="let device">
          @if (!device.isRevoked) {
            <button mat-button color="warn" (click)="revoke(device, false)">Revoke</button>
            <button mat-button color="warn" (click)="revoke(device, true)">Revoke + wipe</button>
          }
        </td>
      </ng-container>

      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns"></tr>
    </table>
  `,
  styles: `table { width: 100%; }`,
})
export class DevicesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  readonly devices = signal<DeviceSummary[]>([]);
  readonly loading = signal(true);
  readonly columns = ['platform', 'deviceName', 'appVersion', 'lastSeen', 'status', 'actions'];

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
