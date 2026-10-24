import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-change-password',
  standalone: true,
  imports: [ReactiveFormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule],
  template: `
    <div class="shell">
      <div class="panel card">
        <span class="mark"><mat-icon>lock_reset</mat-icon></span>
        <h1 class="title">Set a new password</h1>
        <p class="subtitle">Choose a password only you know — at least 8 characters, not all digits.</p>
        <form [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field appearance="outline" class="full-width">
            <mat-label>Current password</mat-label>
            <input matInput type="password" formControlName="currentPassword" autocomplete="current-password" />
          </mat-form-field>
          <mat-form-field appearance="outline" class="full-width">
            <mat-label>New password</mat-label>
            <input matInput type="password" formControlName="newPassword" autocomplete="new-password" />
          </mat-form-field>
          <button mat-flat-button color="primary" type="submit" class="full-width submit" [disabled]="form.invalid || submitting()">
            Change password
          </button>
        </form>
      </div>
    </div>
  `,
  styles: `
    .shell {
      display: flex; justify-content: center; align-items: center; min-height: 100vh; padding: 1rem; box-sizing: border-box;
      background: radial-gradient(circle at 20% 10%, #e0e7ff 0%, transparent 45%), var(--so-bg);
    }
    .panel { width: 100%; max-width: 400px; padding: 2rem; box-shadow: var(--so-shadow-lg); }
    .mark {
      width: 44px; height: 44px; border-radius: 12px; display: flex; align-items: center; justify-content: center;
      background: var(--so-accent-soft); color: var(--so-accent); margin-bottom: 1rem;
    }
    .title { margin: 0 0 0.35rem; font-size: 1.4rem; font-weight: 700; letter-spacing: -0.02em; }
    .subtitle { margin: 0 0 1.5rem; color: var(--so-text-muted); font-size: 0.9rem; }
    .submit { height: 46px; margin-top: 0.25rem; }
  `,
})
export class ChangePasswordComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly submitting = signal(false);

  readonly form = this.fb.nonNullable.group({
    currentPassword: ['', Validators.required],
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
  });

  async submit(): Promise<void> {
    if (this.form.invalid) {
      return;
    }

    this.submitting.set(true);
    try {
      const { currentPassword, newPassword } = this.form.getRawValue();
      await this.auth.changePassword(currentPassword, newPassword);
      await this.router.navigate(['/']);
    } finally {
      this.submitting.set(false);
    }
  }
}
