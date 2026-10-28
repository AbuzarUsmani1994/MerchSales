import { DecimalPipe } from '@angular/common';
import { HttpClient, HttpContext, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from '../../core/auth/auth.service';
import { ConfigService } from '../../core/config.service';
import { SILENT_ERRORS } from '../../core/http/error-toast.interceptor';
import { KitchenBoard, KitchenTicket } from './restaurant.models';

type Action = 'start' | 'ready' | 'served' | 'back';

/** Cards turn amber, then red, when an order has waited this long (minutes). */
const LATE_MINUTES = 10;
const VERY_LATE_MINUTES = 15;

/** A safety net: the board also reloads on its own, in case a live message was missed. */
const POLL_MS = 20_000;

/** Seconds since epoch in a JWT's "exp", or 0. */
function tokenExpiry(token: string | null): number {
  try {
    return token ? JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/'))).exp ?? 0 : 0;
  } catch {
    return 0;
  }
}

/**
 * Restaurant → Kitchen: the kitchen screen (docs/decisions.md "Tables and kitchen screen").
 * New, Preparing and Ready columns with the order number, table or "Takeaway", a waiting timer
 * and the items. Kitchen staff tap Start and Ready; whoever hands the food over taps Served.
 * Changes arrive live (SignalR) and the board also reloads every 20 seconds.
 */
@Component({
  selector: 'app-kitchen',
  standalone: true,
  imports: [DecimalPipe, MatButtonModule, MatFormFieldModule, MatIconModule, MatSelectModule],
  template: `
    <div class="kitchen" [class.full]="fullScreen()">
      <div class="bar">
        <h1>Kitchen</h1>
        @if (branches().length > 1) {
          <mat-form-field appearance="outline" class="branch-field" subscriptSizing="dynamic">
            <mat-label>Branch</mat-label>
            <mat-select [value]="branchId()" (selectionChange)="selectBranch($event.value)">
              @for (b of branches(); track b.id) {
                <mat-option [value]="b.id">{{ b.name }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        }
        <span class="live" [class.on]="live()"><span class="dot"></span>{{ live() ? 'Live' : 'Reconnecting…' }}</span>
        <span class="spacer"></span>
        <button mat-stroked-button (click)="toggleSound()"><mat-icon>{{ sound() ? 'volume_up' : 'volume_off' }}</mat-icon> {{ sound() ? 'Sound on' : 'Sound off' }}</button>
        <button mat-stroked-button (click)="toggleFullScreen()"><mat-icon>{{ fullScreen() ? 'fullscreen_exit' : 'fullscreen' }}</mat-icon> {{ fullScreen() ? 'Exit full screen' : 'Full screen' }}</button>
      </div>

      @if (error()) {
        <div class="callout callout-danger">{{ error() }}</div>
      }

      <div class="columns">
        @for (col of columns; track col.status) {
          <section class="column" [class]="col.css">
            <h2>{{ col.title }} <span class="count">{{ byStatus()[col.status].length }}</span></h2>
            <div class="cards">
              @for (t of byStatus()[col.status]; track t.id) {
                <article class="ticket" [class.cancelled]="t.status === 'Cancelled'" [class.late]="isLate(t, LATE)" [class.very-late]="isLate(t, VERY_LATE)"
                         [class.fresh]="fresh().has(t.id)">
                  <header>
                    <span class="no">#{{ t.orderNo }}</span>
                    <span class="where">{{ t.tableName ? 'Table ' + t.tableName : channelLabel(t.channel) }}</span>
                    @if (t.roundNo > 1) { <span class="round">Round {{ t.roundNo }}</span> }
                    <span class="spacer"></span>
                    <span class="timer"><mat-icon>schedule</mat-icon>{{ minutes(t) }} min</span>
                  </header>
                  @if (t.status === 'Cancelled') {
                    <div class="cancel-note">CANCELLED: stop this order</div>
                  } @else {
                    <ul>
                      @for (l of t.lines; track $index) {
                        <li><span class="qty">{{ l.quantity | number: '1.0-3' }}×</span> <span>{{ l.name }}@if (l.notes) { <em>{{ l.notes }}</em> }</span></li>
                      }
                    </ul>
                    @if (t.notes) { <div class="note"><mat-icon>sticky_note_2</mat-icon>{{ t.notes }}</div> }
                    <footer>
                      @switch (t.status) {
                        @case ('Queued') {
                          <button mat-button [disabled]="busy().has(t.id)" (click)="act(t, 'ready')">Ready</button>
                          <button mat-flat-button color="primary" [disabled]="busy().has(t.id)" (click)="act(t, 'start')"><mat-icon>local_fire_department</mat-icon> Start</button>
                        }
                        @case ('Preparing') {
                          <button mat-button [disabled]="busy().has(t.id)" (click)="act(t, 'back')">Back</button>
                          <button mat-flat-button color="primary" [disabled]="busy().has(t.id)" (click)="act(t, 'ready')"><mat-icon>check</mat-icon> Ready</button>
                        }
                        @case ('Ready') {
                          <button mat-button [disabled]="busy().has(t.id)" (click)="act(t, 'back')">Back</button>
                          <button mat-flat-button color="primary" [disabled]="busy().has(t.id)" (click)="act(t, 'served')"><mat-icon>room_service</mat-icon> Served</button>
                        }
                      }
                    </footer>
                  }
                </article>
              } @empty {
                <div class="empty">{{ col.empty }}</div>
              }
            </div>
          </section>
        }
      </div>
    </div>
  `,
  styles: `
    .kitchen { display: flex; flex-direction: column; gap: 0.75rem; min-height: calc(100vh - 6rem); padding: 1rem; }
    .kitchen.full { position: fixed; inset: 0; z-index: 1000; background: var(--so-bg, #f4f5f7); min-height: 100vh; overflow: auto; }
    .bar { display: flex; align-items: center; gap: 0.75rem; flex-wrap: wrap; }
    .bar h1 { margin: 0; font-size: 1.4rem; }
    .branch-field { width: 200px; }
    .spacer { flex: 1; }
    .live { display: inline-flex; align-items: center; gap: 0.35rem; font-size: 0.85rem; color: var(--so-text-muted); }
    .live .dot { width: 9px; height: 9px; border-radius: 50%; background: var(--so-warning); }
    .live.on .dot { background: var(--so-success); }
    .columns { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 0.75rem; flex: 1; }
    @media (max-width: 900px) { .columns { grid-template-columns: 1fr; } }
    .column { background: var(--so-surface-muted); border-radius: var(--so-radius); padding: 0.6rem; display: flex; flex-direction: column; min-height: 50vh; }
    .column h2 { margin: 0.2rem 0.3rem 0.6rem; font-size: 1rem; text-transform: uppercase; letter-spacing: 0.05em; display: flex; gap: 0.4rem; align-items: center; }
    .column.new h2 { color: var(--so-accent); }
    .column.preparing h2 { color: var(--so-warning); }
    .column.ready h2 { color: var(--so-success); }
    .count { background: var(--so-surface); border-radius: 999px; padding: 0 0.5rem; font-size: 0.85rem; color: var(--so-text); }
    .cards { display: flex; flex-direction: column; gap: 0.6rem; }
    .empty { color: var(--so-text-muted); text-align: center; padding: 2rem 0.5rem; }
    .ticket { background: var(--so-surface); border: 2px solid var(--so-border); border-radius: var(--so-radius-sm); padding: 0.6rem 0.75rem; }
    .ticket.fresh { animation: flash 1s ease-in-out 3; }
    @keyframes flash { 50% { box-shadow: 0 0 0 4px var(--so-accent-ring); } }
    .ticket.late { border-color: var(--so-warning); }
    .ticket.very-late { border-color: var(--so-danger); background: var(--so-danger-soft); }
    .ticket.cancelled { opacity: 0.8; border-style: dashed; border-color: var(--so-danger); padding: 0.35rem 0.75rem; }
    .ticket.cancelled .no { font-size: 1.2rem; text-decoration: line-through; }
    .ticket header { display: flex; align-items: baseline; gap: 0.5rem; flex-wrap: wrap; }
    .no { font-size: 1.6rem; font-weight: 800; font-variant-numeric: tabular-nums; }
    .where { font-weight: 600; }
    .round { font-size: 0.75rem; background: var(--so-accent-soft); color: var(--so-accent); border-radius: 6px; padding: 0.05rem 0.4rem; }
    .timer { display: inline-flex; align-items: center; gap: 0.15rem; font-size: 0.85rem; color: var(--so-text-muted); font-variant-numeric: tabular-nums; }
    .timer .mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .very-late .timer { color: var(--so-danger); font-weight: 700; }
    .cancel-note { color: var(--so-danger); font-weight: 700; margin-top: 0.25rem; }
    ul { list-style: none; margin: 0.5rem 0; padding: 0; display: grid; gap: 0.25rem; font-size: 1.05rem; }
    .qty { font-weight: 700; min-width: 2rem; display: inline-block; }
    em { display: block; margin-left: 2rem; color: var(--so-warning); font-size: 0.9rem; font-style: normal; }
    .note { display: flex; gap: 0.3rem; align-items: flex-start; background: var(--so-warning-soft, #fff4d6); border-radius: 6px; padding: 0.3rem 0.5rem; font-size: 0.9rem; }
    .note .mat-icon { font-size: 16px; width: 16px; height: 16px; }
    footer { display: flex; justify-content: flex-end; gap: 0.4rem; margin-top: 0.5rem; }
    .callout-danger { background: var(--so-danger-soft); color: var(--so-danger); }
  `,
})
export class KitchenComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly auth = inject(AuthService);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly LATE = LATE_MINUTES;
  protected readonly VERY_LATE = VERY_LATE_MINUTES;
  readonly columns = [
    { status: 'Queued', title: 'New', css: 'new', empty: 'No new orders.' },
    { status: 'Preparing', title: 'Preparing', css: 'preparing', empty: 'Nothing on the stove.' },
    { status: 'Ready', title: 'Ready', css: 'ready', empty: 'Nothing waiting to go out.' },
  ] as const;

  readonly branches = this.auth.branches;
  readonly branchId = signal('');
  readonly tickets = signal<KitchenTicket[]>([]);
  readonly live = signal(false);
  readonly sound = signal(false);
  readonly fullScreen = signal(false);
  readonly error = signal<string | null>(null);
  readonly busy = signal<Set<string>>(new Set());
  readonly fresh = signal<Set<string>>(new Set());
  /** Server time minus local time, so the timers are right on a PC with a wrong clock. */
  private clockOffsetMs = 0;
  readonly now = signal(Date.now());

  /** Cancelled tickets show at the top of New, so the cook sees the order was stopped. */
  readonly byStatus = computed<Record<string, KitchenTicket[]>>(() => {
    const t = this.tickets();
    return {
      Queued: [...t.filter((x) => x.status === 'Cancelled'), ...t.filter((x) => x.status === 'Queued')],
      Preparing: t.filter((x) => x.status === 'Preparing'),
      Ready: t.filter((x) => x.status === 'Ready'),
    };
  });

  private hub?: HubConnection;
  private poll?: ReturnType<typeof setInterval>;
  private tick?: ReturnType<typeof setInterval>;
  private reloadTimer?: ReturnType<typeof setTimeout>;
  private audio?: AudioContext;
  private known = new Set<string>();
  private firstLoad = true;

  private get api(): string {
    return `${this.config.apiBaseUrl}/api/v1/restaurant/kitchen`;
  }

  async ngOnInit(): Promise<void> {
    this.branchId.set(this.branches()[0]?.id ?? '');
    await this.load();
    await this.connect();
    this.poll = setInterval(() => void this.load(), POLL_MS);
    this.tick = setInterval(() => this.now.set(Date.now()), 15_000);
  }

  ngOnDestroy(): void {
    clearInterval(this.poll);
    clearInterval(this.tick);
    clearTimeout(this.reloadTimer);
    void this.hub?.stop();
    void this.audio?.close();
    if (document.fullscreenElement) {
      void document.exitFullscreen();
    }
  }

  async selectBranch(id: string): Promise<void> {
    const previous = this.branchId();
    this.branchId.set(id);
    this.firstLoad = true;
    await this.load();
    if (this.hub?.state === HubConnectionState.Connected && previous !== id) {
      await this.hub.invoke('Watch', id);
    }
  }

  channelLabel(channel: string): string {
    return channel === 'DineIn' ? 'Dine-in' : channel === 'BackOffice' ? 'Counter' : channel;
  }

  minutes(t: KitchenTicket): number {
    return Math.max(0, Math.floor((this.now() + this.clockOffsetMs - Date.parse(t.createdAtUtc)) / 60_000));
  }

  isLate(t: KitchenTicket, limit: number): boolean {
    return (t.status === 'Queued' || t.status === 'Preparing') && this.minutes(t) >= limit;
  }

  toggleSound(): void {
    if (!this.sound()) {
      try {
        // Browsers only allow sound after a tap on the page: this is that tap.
        this.audio ??= new AudioContext();
        void this.audio.resume();
        this.sound.set(true);
        this.chime();
      } catch {
        this.sound.set(false);
      }
    } else {
      this.sound.set(false);
    }
  }

  async toggleFullScreen(): Promise<void> {
    if (!this.fullScreen()) {
      this.fullScreen.set(true);
      await document.documentElement.requestFullscreen?.().catch(() => undefined);
    } else {
      this.fullScreen.set(false);
      if (document.fullscreenElement) {
        await document.exitFullscreen();
      }
    }
  }

  async act(ticket: KitchenTicket, action: Action): Promise<void> {
    this.busy.update((s) => new Set(s).add(ticket.id));
    try {
      const updated = await firstValueFrom(this.http.post<KitchenTicket>(`${this.api}/tickets/${ticket.id}/${action}`, null, {
        headers: new HttpHeaders({ 'If-Match': `"${ticket.etag}"` }),
        context: new HttpContext().set(SILENT_ERRORS, true),
      }));
      this.tickets.update((list) =>
        updated.status === 'Served' ? list.filter((t) => t.id !== updated.id) : list.map((t) => (t.id === updated.id ? updated : t)));
    } catch (e) {
      // Someone else tapped first (412/409): show the board as it is now.
      const status = e instanceof HttpErrorResponse ? e.status : 0;
      if (status !== 412 && status !== 409) {
        this.snackBar.open(e instanceof HttpErrorResponse ? (e.error?.detail ?? "That didn't work.") : "That didn't work.", 'Dismiss', { duration: 5000 });
      }
      await this.load();
    } finally {
      this.busy.update((s) => {
        const next = new Set(s);
        next.delete(ticket.id);
        return next;
      });
    }
  }

  private async load(): Promise<void> {
    if (!this.branchId()) {
      return;
    }
    try {
      const board = await firstValueFrom(this.http.get<KitchenBoard>(`${this.api}/board`, {
        params: { branchId: this.branchId() },
        context: new HttpContext().set(SILENT_ERRORS, true),
      }));
      this.clockOffsetMs = Date.parse(board.serverTimeUtc) - Date.now();
      this.now.set(Date.now());
      const arrived = board.tickets.filter((t) => t.status === 'Queued' && !this.known.has(t.id)).map((t) => t.id);
      this.known = new Set(board.tickets.map((t) => t.id));
      this.fresh.set(this.firstLoad ? new Set() : new Set(arrived));
      if (!this.firstLoad && arrived.length > 0) {
        this.chime();
      }
      this.firstLoad = false;
      this.tickets.set(board.tickets);
      this.error.set(null);
    } catch (e) {
      this.error.set(
        e instanceof HttpErrorResponse && e.status === 404
          ? "You can't open this branch's kitchen screen."
          : "Can't reach the server. The board will update again when it's back.",
      );
    }
  }

  /** Live updates: a message only says "this branch changed"; the board then reloads through the API. */
  private async connect(): Promise<void> {
    this.hub = new HubConnectionBuilder()
      .withUrl(`${this.config.apiBaseUrl}/hubs/kitchen`, {
        withCredentials: false,
        accessTokenFactory: async () => {
          // Access tokens last 15 minutes; a reconnect after that needs a fresh one.
          const token = this.auth.accessToken;
          return tokenExpiry(token) * 1000 - Date.now() > 60_000 ? token! : ((await this.auth.refresh()) ?? '');
        },
      })
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (ctx) => Math.min(30_000, 1_000 * 2 ** Math.min(ctx.previousRetryCount, 5)) })
      .configureLogging(LogLevel.Warning)
      .build();

    this.hub.on('boardChanged', () => {
      clearTimeout(this.reloadTimer);
      this.reloadTimer = setTimeout(() => void this.load(), 250);
    });
    this.hub.onreconnecting(() => this.live.set(false));
    this.hub.onreconnected(async () => {
      await this.hub!.invoke('Watch', this.branchId());
      this.live.set(true);
      await this.load();
    });
    this.hub.onclose(() => this.live.set(false));

    try {
      await this.hub.start();
      await this.hub.invoke('Watch', this.branchId());
      this.live.set(true);
    } catch {
      // No live connection (e.g. WebSockets blocked): the 20-second reload keeps the board current.
      this.live.set(false);
      setTimeout(() => void this.connectAgain(), 30_000);
    }
  }

  private async connectAgain(): Promise<void> {
    if (!this.hub || this.hub.state !== HubConnectionState.Disconnected) {
      return;
    }
    try {
      await this.hub.start();
      await this.hub.invoke('Watch', this.branchId());
      this.live.set(true);
    } catch {
      setTimeout(() => void this.connectAgain(), 30_000);
    }
  }

  private chime(): void {
    if (!this.sound() || !this.audio) {
      return;
    }
    const at = this.audio.currentTime;
    [0, 0.18].forEach((offset, i) => {
      const osc = this.audio!.createOscillator();
      const gain = this.audio!.createGain();
      osc.frequency.value = i ? 1318 : 988;
      osc.connect(gain);
      gain.connect(this.audio!.destination);
      gain.gain.setValueAtTime(0.3, at + offset);
      gain.gain.exponentialRampToValueAtTime(0.001, at + offset + 0.5);
      osc.start(at + offset);
      osc.stop(at + offset + 0.5);
    });
  }
}
