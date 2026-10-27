import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../config.service';
import { BranchSummary, LoginRequest, LoginResponse, MeResponse, RefreshResponse } from './auth.models';

const ACCESS_TOKEN_KEY = 'stockorder.accessToken';
const REFRESH_TOKEN_KEY = 'stockorder.refreshToken';
const TENANT_KEY = 'stockorder.tenant';

/** Display-only tenant info kept from the login response (not used for any security decision). */
export interface TenantInfo {
  code: string;
  name: string;
  /** 'Restaurant', 'Pharma', ...; missing in a session signed in before it was stored. */
  industry?: string;
}

/**
 * Login (section 10), silent refresh, and the signals the permission/feature-driven menu
 * and route guards read (section 13: "Navigation built from permissions and features;
 * route guards check both. Hidden != secure — the API always re-checks.").
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private readonly accessTokenSignal = signal<string | null>(sessionStorage.getItem(ACCESS_TOKEN_KEY));
  private readonly meSignal = signal<MeResponse | null>(null);
  private readonly tenantSignal = signal<TenantInfo | null>(readTenant());
  private refreshInFlight: Promise<string | null> | null = null;

  readonly isAuthenticated = computed(() => this.accessTokenSignal() !== null);
  readonly me = this.meSignal.asReadonly();
  readonly permissions = computed<string[]>(() => this.meSignal()?.permissions ?? []);
  readonly tenantWidePermissions = computed<string[]>(() => this.meSignal()?.tenantWidePermissions ?? []);
  readonly features = computed<string[]>(() => this.meSignal()?.features ?? []);
  readonly branches = computed<BranchSummary[]>(() => this.meSignal()?.branches ?? []);
  readonly tenant = this.tenantSignal.asReadonly();

  get accessToken(): string | null {
    return this.accessTokenSignal();
  }

  hasPermission(code: string): boolean {
    return this.permissions().includes(code);
  }

  /** True only when the permission comes from an all-branches role (branch managers get false). */
  hasTenantWidePermission(code: string): boolean {
    return this.tenantWidePermissions().includes(code);
  }

  hasFeature(key: string): boolean {
    return this.features().includes(key);
  }

  async login(request: LoginRequest): Promise<LoginResponse> {
    const response = await firstValueFrom(
      this.http.post<LoginResponse>(`${this.config.apiBaseUrl}/api/v1/auth/login`, request),
    );
    this.storeTokens(response.accessToken, response.refreshToken);
    const tenant: TenantInfo = { code: response.tenantCode, name: response.tenantName, industry: response.tenantIndustry };
    sessionStorage.setItem(TENANT_KEY, JSON.stringify(tenant));
    this.tenantSignal.set(tenant);
    await this.loadMe();
    return response;
  }

  async loadMe(): Promise<void> {
    const me = await firstValueFrom(this.http.get<MeResponse>(`${this.config.apiBaseUrl}/api/v1/auth/me`));
    this.meSignal.set(me);
  }

  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    await firstValueFrom(
      this.http.post(`${this.config.apiBaseUrl}/api/v1/auth/change-password`, { currentPassword, newPassword }),
    );
    await this.loadMe();
  }

  /** Single in-flight refresh shared across concurrent 401s (section 13). */
  async refresh(): Promise<string | null> {
    if (this.refreshInFlight) {
      return this.refreshInFlight;
    }

    this.refreshInFlight = this.doRefresh();
    try {
      return await this.refreshInFlight;
    } finally {
      this.refreshInFlight = null;
    }
  }

  private async doRefresh(): Promise<string | null> {
    const refreshToken = sessionStorage.getItem(REFRESH_TOKEN_KEY);
    if (!refreshToken) {
      return null;
    }

    try {
      const response = await firstValueFrom(
        this.http.post<RefreshResponse>(`${this.config.apiBaseUrl}/api/v1/auth/refresh`, { refreshToken }),
      );
      this.storeTokens(response.accessToken, response.refreshToken);
      return response.accessToken;
    } catch {
      this.logout();
      return null;
    }
  }

  async logout(): Promise<void> {
    const refreshToken = sessionStorage.getItem(REFRESH_TOKEN_KEY);
    const accessToken = this.accessToken;
    this.clearTokens();
    this.meSignal.set(null);
    if (refreshToken && accessToken) {
      try {
        // The endpoint needs the caller's bearer token, but the auth interceptor no longer has
        // it (cleared above, so an expired token can't trigger a refresh loop) — send it here.
        await firstValueFrom(
          this.http.post(
            `${this.config.apiBaseUrl}/api/v1/auth/logout`,
            { refreshToken },
            { headers: { Authorization: `Bearer ${accessToken}` } },
          ),
        );
      } catch {
        // Already logged out locally; a failed server-side revoke isn't fatal here.
      }
    }
  }

  private storeTokens(accessToken: string, refreshToken: string): void {
    sessionStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
    sessionStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
    this.accessTokenSignal.set(accessToken);
  }

  private clearTokens(): void {
    sessionStorage.removeItem(ACCESS_TOKEN_KEY);
    sessionStorage.removeItem(REFRESH_TOKEN_KEY);
    sessionStorage.removeItem(TENANT_KEY);
    this.accessTokenSignal.set(null);
    this.tenantSignal.set(null);
  }
}

function readTenant(): TenantInfo | null {
  try {
    const raw = sessionStorage.getItem(TENANT_KEY);
    return raw ? (JSON.parse(raw) as TenantInfo) : null;
  } catch {
    return null;
  }
}
