import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
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
  imports: [TableModule, ButtonModule, TagModule, DatePipe],
  template: `
    <h2>Devices</h2>
    <p-table [value]="devices()" [loading]="loading()">
      <ng-template pTemplate="header">
        <tr><th>Platform</th><th>Device</th><th>App version</th><th>Last seen</th><th>Status</th><th></th></tr>
      </ng-template>
      <ng-template pTemplate="body" let-device>
        <tr>
          <td>{{ device.platform }}</td>
          <td>{{ device.deviceName }}</td>
          <td>{{ device.appVersion }}</td>
          <td>{{ device.lastSeenAtUtc | date: 'short' }}</td>
          <td><p-tag [severity]="device.isRevoked ? 'danger' : 'success'" [value]="device.isRevoked ? 'Revoked' : 'Active'" /></td>
          <td>
            @if (!device.isRevoked) {
              <p-button label="Revoke" severity="danger" size="small" (onClick)="revoke(device, false)" />
              <p-button label="Revoke + wipe" severity="danger" size="small" (onClick)="revoke(device, true)" />
            }
          </td>
        </tr>
      </ng-template>
    </p-table>
  `,
})
export class DevicesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  readonly devices = signal<DeviceSummary[]>([]);
  readonly loading = signal(true);

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
