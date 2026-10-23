import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { PasswordModule } from 'primeng/password';
import { CardModule } from 'primeng/card';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-change-password',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonModule, PasswordModule, CardModule],
  template: `
    <div class="shell">
      <p-card header="Change your password">
        <form [formGroup]="form" (ngSubmit)="submit()">
          <div class="field">
            <label for="currentPassword">Current password</label>
            <p-password id="currentPassword" formControlName="currentPassword" [feedback]="false" [toggleMask]="true" />
          </div>
          <div class="field">
            <label for="newPassword">New password</label>
            <p-password id="newPassword" formControlName="newPassword" [toggleMask]="true" />
          </div>
          <p-button type="submit" label="Change password" [loading]="submitting()" [disabled]="form.invalid" />
        </form>
      </p-card>
    </div>
  `,
  styles: `
    .shell { display: flex; justify-content: center; align-items: center; height: 100vh; background: #f4f6f8; }
    .field { display: flex; flex-direction: column; gap: 0.25rem; margin-bottom: 1rem; }
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
