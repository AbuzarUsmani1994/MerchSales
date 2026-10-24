import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
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
  imports: [MatSlideToggleModule, MatProgressBarModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1 class="page-title">Feature toggles</h1>
          <p class="page-subtitle">Switch modules and packs on or off for the whole company. Menus follow these switches.</p>
        </div>
      </div>
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @for (group of groups(); track group.area) {
        <section class="area">
          <h2 class="area-title">{{ group.area }}</h2>
          <div class="grid">
            @for (feature of group.features; track feature.featureKey) {
              <label class="feature card" [class.on]="feature.isEnabled">
                <div class="feature-text">
                  <span class="feature-name">{{ label(feature.featureKey) }}</span>
                  <span class="feature-key mono">{{ feature.featureKey }}</span>
                </div>
                <mat-slide-toggle [checked]="feature.isEnabled" (change)="toggle(feature, $event.checked)"
                  [attr.aria-label]="feature.featureKey" />
              </label>
            }
          </div>
        </section>
      }
    </div>
  `,
  styles: `
    .area { margin-bottom: 1.75rem; }
    .area-title {
      margin: 0 0 0.75rem; font-size: 0.78rem; font-weight: 700; text-transform: uppercase;
      letter-spacing: 0.06em; color: var(--so-text-muted);
    }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 0.75rem; }
    .feature {
      display: flex; align-items: center; justify-content: space-between; gap: 1rem;
      padding: 1rem 1.1rem; cursor: pointer; transition: border-color 120ms ease, box-shadow 120ms ease;
    }
    .feature:hover { border-color: var(--so-border-strong); box-shadow: var(--so-shadow); }
    .feature.on { border-color: #c7d2fe; background: linear-gradient(180deg, #fff 0%, var(--so-accent-soft) 100%); }
    .feature-text { display: flex; flex-direction: column; min-width: 0; }
    .feature-name { font-weight: 600; }
    .feature-key { color: var(--so-text-faint); font-size: 0.72rem; margin-top: 2px; }
  `,
})
export class FeatureTogglesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  readonly features = signal<FeatureFlag[]>([]);
  readonly loading = signal(true);

  /** Grouped by the key's prefix: "Inventory.FEFO" goes under Inventory. */
  readonly groups = computed(() => {
    const byArea = new Map<string, FeatureFlag[]>();
    for (const feature of [...this.features()].sort((a, b) => a.featureKey.localeCompare(b.featureKey))) {
      const area = feature.featureKey.split('.')[0];
      byArea.set(area, [...(byArea.get(area) ?? []), feature]);
    }
    return [...byArea.entries()].map(([area, features]) => ({ area, features }));
  });

  /** "Inventory.BatchTracking" -> "Batch tracking"; "Inventory.FEFO" -> "FEFO". */
  label(featureKey: string): string {
    const name = featureKey.split('.').slice(1).join(' ') || featureKey;
    const spaced = name.replace(/([a-z])([A-Z])/g, '$1 $2');
    return spaced.charAt(0) + spaced.slice(1).replace(/ ([A-Z])(?=[a-z])/g, (_, c: string) => ' ' + c.toLowerCase());
  }

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
    this.features.update((all) => all.map((f) => (f.featureKey === feature.featureKey ? { ...f, isEnabled: enabled } : f)));
    await firstValueFrom(
      this.http.put(`${this.config.apiBaseUrl}/api/v1/tenancy/features/${feature.featureKey}`, enabled),
    );
  }
}
