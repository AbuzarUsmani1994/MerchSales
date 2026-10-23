import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TableModule } from 'primeng/table';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { FormsModule } from '@angular/forms';
import { ConfigService } from '../../core/config.service';

interface FeatureFlag {
  featureKey: string;
  isEnabled: boolean;
}

@Component({
  selector: 'app-feature-toggles',
  standalone: true,
  imports: [TableModule, ToggleSwitchModule, FormsModule],
  template: `
    <h2>Feature toggles</h2>
    <p-table [value]="features()" [loading]="loading()">
      <ng-template pTemplate="header"><tr><th>Feature key</th><th>Enabled</th></tr></ng-template>
      <ng-template pTemplate="body" let-feature>
        <tr>
          <td>{{ feature.featureKey }}</td>
          <td>
            <p-toggleswitch [ngModel]="feature.isEnabled" (onChange)="toggle(feature, $event.checked)" />
          </td>
        </tr>
      </ng-template>
    </p-table>
  `,
})
export class FeatureTogglesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  readonly features = signal<FeatureFlag[]>([]);
  readonly loading = signal(true);

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
