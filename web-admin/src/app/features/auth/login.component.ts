import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { CardModule } from 'primeng/card';
import { AuthService } from '../../core/auth/auth.service';
import { ProblemDetails } from '../../core/auth/auth.models';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonModule, InputTextModule, PasswordModule, CardModule],
  template: `
    <div class="login-shell">
      <p-card header="StockOrder — Sign in" class="login-card">
        <form [formGroup]="form" (ngSubmit)="submit()">
          <div class="field">
            <label for="tenantCode">Tenant code</label>
            <input id="tenantCode" pInputText formControlName="tenantCode" autocomplete="organization" />
          </div>
          <div class="field">
            <label for="username">Username</label>
            <input id="username" pInputText formControlName="username" autocomplete="username" />
          </div>
          <div class="field">
            <label for="password">Password</label>
            <p-password
              id="password"
              formControlName="password"
              [feedback]="false"
              [toggleMask]="true"
              autocomplete="current-password"
            />
          </div>
          @if (mfaRequired()) {
            <div class="field">
              <label for="totpCode">Authenticator code</label>
              <input id="totpCode" pInputText formControlName="totpCode" autocomplete="one-time-code" />
            </div>
          }
          @if (errorMessage()) {
            <p class="error">{{ errorMessage() }}</p>
          }
          <p-button type="submit" label="Sign in" [loading]="submitting()" [disabled]="form.invalid" />
        </form>
      </p-card>
    </div>
  `,
  styles: `
    .login-shell { display: flex; justify-content: center; align-items: center; height: 100vh; background: #f4f6f8; }
    .login-card { width: 360px; }
    .field { display: flex; flex-direction: column; gap: 0.25rem; margin-bottom: 1rem; }
    .error { color: #c0392b; font-size: 0.9rem; }
  `,
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly submitting = signal(false);
  readonly mfaRequired = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    tenantCode: ['', Validators.required],
    username: ['', Validators.required],
    password: ['', Validators.required],
    totpCode: [''],
  });

  async submit(): Promise<void> {
    if (this.form.invalid) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const { tenantCode, username, password, totpCode } = this.form.getRawValue();
      const result = await this.auth.login({ tenantCode, username, password, totpCode: totpCode || null });

      if (result.mustChangePassword) {
        await this.router.navigate(['/change-password']);
      } else {
        await this.router.navigate(['/']);
      }
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        const problem = error.error as ProblemDetails | undefined;
        if (problem?.code === 'MFA_REQUIRED') {
          this.mfaRequired.set(true);
          this.errorMessage.set('Enter your authenticator code to continue.');
        } else {
          this.errorMessage.set(problem?.detail ?? 'Sign in failed.');
        }
      } else {
        this.errorMessage.set('Sign in failed.');
      }
    } finally {
      this.submitting.set(false);
    }
  }
}
