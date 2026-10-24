import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
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
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './login.component.html',
  styles: `
    .login-shell { display: flex; min-height: 100vh; background: var(--so-bg); }

    .login-form-panel { flex: 1; display: flex; align-items: center; justify-content: center; padding: 2rem; }
    .login-form-wrap { width: 100%; max-width: 380px; }

    .brand { display: flex; align-items: center; gap: 0.6rem; margin-bottom: 2.5rem; }
    .brand-mark {
      width: 36px; height: 36px; border-radius: 10px; color: #fff;
      background: linear-gradient(135deg, #6366f1 0%, var(--so-accent) 100%);
      box-shadow: 0 6px 16px -4px rgba(79, 70, 229, 0.55);
      display: flex; align-items: center; justify-content: center; font-weight: 700; font-size: 1.1rem;
      flex-shrink: 0;
    }
    .brand-name { font-weight: 700; font-size: 1.15rem; letter-spacing: -0.02em; color: #1a1f36; }

    .title { font-size: 1.9rem; font-weight: 700; margin: 0 0 0.4rem; color: #1a1f36; }
    .subtitle { color: #6b7280; margin: 0 0 2rem; font-size: 0.95rem; }

    .full-width { width: 100%; margin-bottom: 0.75rem; }
    .submit-btn { height: 48px; font-weight: 600; margin-top: 0.5rem; }
    .error { color: #c0392b; font-size: 0.9rem; margin: 0 0 0.75rem; }

    .login-brand-panel {
      flex: 1;
      background:
        radial-gradient(circle at 80% 15%, rgba(99, 102, 241, 0.35) 0%, transparent 45%),
        radial-gradient(circle at 10% 90%, rgba(34, 211, 238, 0.14) 0%, transparent 40%),
        linear-gradient(160deg, var(--so-navy-800) 0%, var(--so-navy-900) 100%);
      display: flex; align-items: center; justify-content: center;
      padding: 3rem;
    }
    .brand-panel-content { max-width: 420px; color: #fff; }
    .brand-mark-light { width: 48px; height: 48px; border-radius: 14px; font-size: 1.4rem; margin-bottom: 1.5rem; }
    .login-brand-panel h2 { font-size: 2.2rem; font-weight: 700; margin: 0 0 1rem; letter-spacing: -0.02em; }
    .login-brand-panel p { color: rgba(255, 255, 255, 0.72); line-height: 1.6; font-size: 1rem; margin: 0; }

    @media (max-width: 900px) {
      .login-brand-panel { display: none; }
    }
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
