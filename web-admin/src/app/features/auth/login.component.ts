import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../core/auth/auth.service';
import { ProblemDetails } from '../../core/auth/auth.models';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  template: `
    <div class="login-shell">
      <mat-card class="login-card">
        <mat-card-header>
          <mat-card-title>StockOrder — Sign in</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="submit()">
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Tenant code</mat-label>
              <input matInput formControlName="tenantCode" autocomplete="organization" />
            </mat-form-field>
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Username</mat-label>
              <input matInput formControlName="username" autocomplete="username" />
            </mat-form-field>
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Password</mat-label>
              <input matInput type="password" formControlName="password" autocomplete="current-password" />
            </mat-form-field>
            @if (mfaRequired()) {
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>Authenticator code</mat-label>
                <input matInput formControlName="totpCode" autocomplete="one-time-code" />
              </mat-form-field>
            }
            @if (errorMessage()) {
              <p class="error">{{ errorMessage() }}</p>
            }
            <button mat-flat-button color="primary" type="submit" class="full-width" [disabled]="form.invalid || submitting()">
              @if (submitting()) {
                <mat-spinner diameter="20" />
              } @else {
                Sign in
              }
            </button>
          </form>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: `
    .login-shell { display: flex; justify-content: center; align-items: center; height: 100vh; background: #f4f6f8; }
    .login-card { width: 360px; }
    .full-width { width: 100%; margin-bottom: 0.5rem; }
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
