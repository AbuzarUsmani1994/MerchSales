import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MatTableModule } from '@angular/material/table';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ConfigService } from '../../core/config.service';

interface FeatureFlag {
  featureKey: string;
  isEnabled: boolean;
}

@Component({
  selector: 'app-feature-toggles',
  standalone: true,
  imports: [MatTableModule, MatSlideToggleModule, MatProgressBarModule],
  template: `
    <h2>Feature toggles</h2>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" />
    }
    <table mat-table [dataSource]="features()" class="mat-elevation-z1">
      <ng-container matColumnDef="featureKey">
        <th mat-header-cell *matHeaderCellDef>Feature key</th>
        <td mat-cell *matCellDef="let feature">{{ feature.featureKey }}</td>
      </ng-container>
      <ng-container matColumnDef="enabled">
        <th mat-header-cell *matHeaderCellDef>Enabled</th>
        <td mat-cell *matCellDef="let feature">
          <mat-slide-toggle [checked]="feature.isEnabled" (change)="toggle(feature, $event.checked)" />
        </td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns"></tr>
    </table>
  `,
  styles: `table { width: 100%; }`,
})
export class FeatureTogglesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  readonly features = signal<FeatureFlag[]>([]);
  readonly loading = signal(true);
  readonly columns = ['featureKey', 'enabled'];

  async ngOnInit(): Promise<void> {
    try {
      this.features.set(
        await firstValueFrom(this.http.get<FeatureFlag[]>(`${this.config.apiBaseUrl}/api/v1/tenancy/features`)),
      );
    } finally {
      this.loading.set(false);
    }
  }

  async toggle(feature: FeatureFlag, enabled: boolean): Promise<void> {
    feature.isEnabled = enabled;
    await firstValueFrom(
      this.http.put(`${this.config.apiBaseUrl}/api/v1/tenancy/features/${feature.featureKey}`, enabled),
    );
  }
}
