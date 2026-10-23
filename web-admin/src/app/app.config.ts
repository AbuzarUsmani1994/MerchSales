import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { providePrimeNG } from 'primeng/config';
import { MessageService } from 'primeng/api';
import Lara from '@primeng/themes/lara';
import { routes } from './app.routes';
import { ConfigService } from './core/config.service';
import { authInterceptor } from './core/auth/auth.interceptor';
import { idempotencyInterceptor } from './core/http/idempotency.interceptor';
import { correlationIdInterceptor } from './core/http/correlation-id.interceptor';
import { errorToastInterceptor } from './core/http/error-toast.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([correlationIdInterceptor, authInterceptor, idempotencyInterceptor, errorToastInterceptor]),
    ),
    providePrimeNG({ theme: { preset: Lara } }),
    MessageService,
    provideAppInitializer(() => inject(ConfigService).load()),
  ],
};
