import { Component } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/** Shown at "/" when none of the back-office screens fit the user's roles (e.g. a phone-only role). */
@Component({
  selector: 'app-no-access',
  standalone: true,
  imports: [MatIconModule],
  template: `
    <div class="page">
      <div class="empty-state">
        <mat-icon>lock</mat-icon>
        <div>Your role has no back-office screens. Use the mobile app, or ask your manager for access.</div>
      </div>
    </div>
  `,
})
export class NoAccessComponent {}
