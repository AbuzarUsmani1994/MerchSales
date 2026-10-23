import { Injectable, signal } from '@angular/core';

export interface AppRuntimeConfig {
  apiBaseUrl: string;
}

/**
 * Loaded from public/config.json at startup (section 13: "via a config.json loaded at
 * startup, not compiled in") so the same build artifact can point at different API hosts
 * per environment without rebuilding.
 */
@Injectable({ providedIn: 'root' })
export class ConfigService {
  private readonly loadedConfig = signal<AppRuntimeConfig | null>(null);

  get apiBaseUrl(): string {
    const config = this.loadedConfig();
    if (!config) {
      throw new Error('ConfigService.load() must complete before the app bootstraps.');
    }
    return config.apiBaseUrl;
  }

  async load(): Promise<void> {
    const response = await fetch('/config.json');
    if (!response.ok) {
      throw new Error(`Failed to load /config.json: ${response.status}`);
    }
    this.loadedConfig.set((await response.json()) as AppRuntimeConfig);
  }
}
