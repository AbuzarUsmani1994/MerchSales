import { HttpClient, HttpContext } from '@angular/common/http';
import { Component, computed, effect, inject, Injectable, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ConfigService } from '../../core/config.service';
import { SILENT_ERRORS } from '../../core/http/error-toast.interceptor';

/** Longest side of a stored picture; plenty for a phone menu tile, about 50-200 KB as JPEG. */
const MAX_SIDE = 800;

/**
 * Shrinks a photo in the browser before upload (server limit 2 MB), so a 5 MB phone photo goes
 * up as a small JPEG. Rejects files the browser can't read as an image.
 */
export async function shrinkPicture(file: File): Promise<Blob> {
  const bitmap = await createImageBitmap(file);
  const scale = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  const context = canvas.getContext('2d')!;
  // JPEG has no transparency: put PNG cut-outs on white rather than black.
  context.fillStyle = '#fff';
  context.fillRect(0, 0, canvas.width, canvas.height);
  context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  return new Promise((resolve, reject) =>
    canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('Could not read this picture.'))), 'image/jpeg', 0.82),
  );
}

/**
 * Pictures are served by the API to signed-in users only, so <img src> can't load them directly:
 * this fetches each one with the user's token once and hands out an object URL. A picture's id
 * changes whenever it's replaced, so caching by id never shows a stale picture.
 */
@Injectable({ providedIn: 'root' })
export class ProductImageService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly cache = new Map<string, Promise<string | null>>();

  objectUrl(productId: string, attachmentId: string): Promise<string | null> {
    let url = this.cache.get(attachmentId);
    if (!url) {
      url = firstValueFrom(
        this.http.get(`${this.base(productId)}?v=${attachmentId}`, { responseType: 'blob', context: new HttpContext().set(SILENT_ERRORS, true) }),
      ).then(
        (blob) => URL.createObjectURL(blob),
        () => null,
      );
      this.cache.set(attachmentId, url);
    }
    return url;
  }

  async upload(productId: string, picture: Blob): Promise<void> {
    const form = new FormData();
    form.append('file', picture, 'picture.jpg');
    await firstValueFrom(this.http.put(this.base(productId), form));
  }

  async remove(productId: string): Promise<void> {
    await firstValueFrom(this.http.delete(this.base(productId)));
  }

  private base(productId: string): string {
    return `${this.config.apiBaseUrl}/api/v1/catalog/products/${productId}/image`;
  }
}

/** An item's picture, or a coloured tile with its initial when it has none. */
@Component({
  selector: 'app-product-image',
  standalone: true,
  template: `
    @if (src(); as url) {
      <img [src]="url" [alt]="name()" [style.width.px]="size()" [style.height.px]="size()" (error)="src.set(null)" />
    } @else {
      <span class="placeholder" [style.width.px]="size()" [style.height.px]="size()" [style.font-size.px]="size() * 0.42"
            [style.background]="tint()" aria-hidden="true">{{ initial() }}</span>
    }
  `,
  styles: `
    :host { display: inline-flex; flex: none; }
    img, .placeholder { border-radius: var(--so-radius-sm); object-fit: cover; }
    .placeholder { display: inline-flex; align-items: center; justify-content: center; color: #fff; font-weight: 700; }
  `,
})
export class ProductImageComponent {
  readonly productId = input.required<string>();
  readonly attachmentId = input<string | null>(null);
  readonly name = input('');
  readonly size = input(44);

  private readonly images = inject(ProductImageService);
  readonly src = signal<string | null>(null);
  readonly initial = computed(() => (this.name().trim()[0] ?? '?').toUpperCase());

  /** A stable colour per item name, so placeholders are easy to tell apart. */
  readonly tint = computed(() => {
    let hash = 0;
    for (const ch of this.name()) hash = (hash * 31 + ch.charCodeAt(0)) | 0;
    return `hsl(${Math.abs(hash) % 360} 55% 52%)`;
  });

  constructor() {
    effect(() => {
      const id = this.attachmentId();
      const productId = this.productId();
      this.src.set(null);
      if (id) {
        void this.images.objectUrl(productId, id).then((url) => {
          if (this.attachmentId() === id) this.src.set(url);
        });
      }
    });
  }
}

/** What the item dialogs return for the picture: undefined = unchanged, null = remove, a Blob = new. */
export type PictureChange = Blob | null | undefined;

/** Picture section of the item dialogs: preview, choose, remove. Shrinks the file as soon as it's chosen. */
@Component({
  selector: 'app-picture-picker',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, ProductImageComponent],
  template: `
    <div class="picker">
      @if (previewUrl(); as url) {
        <img [src]="url" alt="" class="preview" />
      } @else if (!removed() && currentAttachmentId()) {
        <app-product-image [productId]="productId()!" [attachmentId]="currentAttachmentId()" [name]="name()" [size]="96" />
      } @else {
        <div class="empty"><mat-icon>add_photo_alternate</mat-icon></div>
      }
      <div class="actions">
        <button mat-stroked-button type="button" (click)="fileInput.click()">
          <mat-icon>photo_camera</mat-icon>
          {{ hasPicture() ? 'Change picture' : 'Add picture' }}
        </button>
        @if (hasPicture()) {
          <button mat-button type="button" color="warn" (click)="remove()">Remove</button>
        }
        <span class="hint">{{ error() ?? 'JPEG, PNG or WebP. Shrunk automatically.' }}</span>
      </div>
      <input #fileInput type="file" accept="image/jpeg,image/png,image/webp" hidden (change)="choose(fileInput)" />
    </div>
  `,
  styles: `
    .picker { display: flex; gap: 1rem; align-items: center; margin-bottom: 1rem; }
    .preview, .empty { width: 96px; height: 96px; border-radius: var(--so-radius-sm); object-fit: cover; flex: none; }
    .empty { display: flex; align-items: center; justify-content: center; background: var(--so-surface-muted);
      border: 1px dashed var(--so-border-strong); color: var(--so-text-faint); }
    .actions { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: center; }
    .hint { flex-basis: 100%; font-size: 0.8rem; color: var(--so-text-muted); }
  `,
})
export class PicturePickerComponent {
  readonly productId = input<string | null>(null);
  readonly currentAttachmentId = input<string | null>(null);
  readonly name = input('');
  readonly changed = output<PictureChange>();

  readonly previewUrl = signal<string | null>(null);
  readonly removed = signal(false);
  readonly error = signal<string | null>(null);
  readonly hasPicture = computed(() => !!this.previewUrl() || (!this.removed() && !!this.currentAttachmentId()));

  async choose(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    try {
      const picture = await shrinkPicture(file);
      this.setPreview(URL.createObjectURL(picture));
      this.removed.set(false);
      this.error.set(null);
      this.changed.emit(picture);
    } catch {
      this.error.set("That file isn't a picture this browser can read.");
    }
  }

  remove(): void {
    this.setPreview(null);
    this.removed.set(true);
    // Nothing stored yet (new item) means nothing to remove on the server either.
    this.changed.emit(this.currentAttachmentId() ? null : undefined);
  }

  private setPreview(url: string | null): void {
    const old = this.previewUrl();
    if (old) URL.revokeObjectURL(old);
    this.previewUrl.set(url);
  }
}
