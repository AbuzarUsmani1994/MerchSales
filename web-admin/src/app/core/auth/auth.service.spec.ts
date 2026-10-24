import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { ConfigService } from '../config.service';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiBaseUrl: 'http://localhost:5280' } },
      ],
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('starts unauthenticated when sessionStorage has no token', () => {
    expect(service.isAuthenticated()).toBeFalse();
  });

  it('stores tokens and loads the current user on successful login', async () => {
    const loginPromise = service.login({ tenantCode: 'ACME', username: 'owner1', password: 'pw' });

    const loginReq = httpMock.expectOne('http://localhost:5280/api/v1/auth/login');
    loginReq.flush({
      accessToken: 'access-token',
      refreshToken: 'refresh-token',
      expiresInSeconds: 900,
      mustChangePassword: false,
      mfaRequired: false,
      userId: 'u1',
      username: 'owner1',
      displayName: 'Owner',
      tenantId: 't1',
      tenantCode: 'ACME',
      tenantName: 'Acme',
    });

    // Let the `await firstValueFrom(...)` continuation inside AuthService.login run
    // before asserting on the next HTTP call it makes.
    await Promise.resolve();
    await Promise.resolve();

    const meReq = httpMock.expectOne('http://localhost:5280/api/v1/auth/me');
    meReq.flush({
      userId: 'u1',
      username: 'owner1',
      displayName: 'Owner',
      email: null,
      mustChangePassword: false,
      mfaEnabled: false,
      permissions: ['Users.View', 'Users.Manage'],
      tenantWidePermissions: ['Users.View'],
      features: [],
      branches: [],
    });

    await loginPromise;

    expect(service.isAuthenticated()).toBeTrue();
    expect(service.hasPermission('Users.View')).toBeTrue();
    expect(service.hasPermission('Roles.Manage')).toBeFalse();
    // Users.Manage came from a branch-level role only.
    expect(service.hasPermission('Users.Manage')).toBeTrue();
    expect(service.hasTenantWidePermission('Users.Manage')).toBeFalse();
    expect(service.hasTenantWidePermission('Users.View')).toBeTrue();
  });
});
