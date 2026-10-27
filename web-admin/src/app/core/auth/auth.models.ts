export interface LoginRequest {
  tenantCode: string;
  username: string;
  password: string;
  totpCode?: string | null;
}

export interface LoginResponse {
  accessToken: string;
  expiresInSeconds: number;
  refreshToken: string;
  mustChangePassword: boolean;
  mfaRequired: boolean;
  userId: string;
  username: string;
  displayName: string;
  tenantId: string;
  tenantCode: string;
  tenantName: string;
  tenantIndustry: string;
}

export interface RefreshResponse {
  accessToken: string;
  expiresInSeconds: number;
  refreshToken: string;
}

export interface BranchSummary {
  id: string;
  code: string;
  name: string;
  address: string | null;
  phone: string | null;
  isActive: boolean;
}

export interface MeResponse {
  userId: string;
  username: string;
  displayName: string;
  email: string | null;
  mustChangePassword: boolean;
  mfaEnabled: boolean;
  /** Held at any branch. */
  permissions: string[];
  /** Held through an all-branches role; tenant-wide screens (roles, settings, new branches) need these. */
  tenantWidePermissions: string[];
  features: string[];
  branches: BranchSummary[];
}

/** RFC 9457 ProblemDetails (section 9). */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
  [key: string]: unknown;
}
