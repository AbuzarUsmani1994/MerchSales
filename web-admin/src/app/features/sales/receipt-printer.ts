import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../../core/config.service';

/**
 * Prints a paid order's 80 mm receipt (a PDF from the API) on the till's printer: loads it into
 * a hidden frame and opens the browser's print dialog. Browsers always show that dialog; a till
 * that should print straight away runs Chrome with --kiosk-printing (docs/operations.md).
 */
@Injectable({ providedIn: 'root' })
export class ReceiptPrinter {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  /** <paramref name="tendered"/> prints cash received and change (first print only); copies say "COPY". */
  async print(orderId: string, options: { tendered?: number | null; copy?: boolean } = {}): Promise<void> {
    let params = new HttpParams().set('copy', !!options.copy);
    if (options.tendered != null) {
      params = params.set('tendered', options.tendered);
    }

    const pdf = await firstValueFrom(
      this.http.get(`${this.config.apiBaseUrl}/api/v1/sales/orders/${orderId}/receipt`, { params, responseType: 'blob' }),
    );
    const url = URL.createObjectURL(pdf);
    const frame = document.createElement('iframe');
    frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;';
    frame.src = url;
    frame.onload = () => {
      try {
        frame.contentWindow?.focus();
        frame.contentWindow?.print();
      } catch {
        // Some browsers won't print a PDF inside a frame: open it so the cashier can print from there.
        window.open(url, '_blank');
      }
      setTimeout(() => {
        frame.remove();
        URL.revokeObjectURL(url);
      }, 60_000);
    };
    document.body.appendChild(frame);
  }
}
