import { HttpInterceptorFn } from '@angular/common/http';

/** Every creating/posting POST needs a client-generated Idempotency-Key (section 9). */
export const idempotencyInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'POST' || req.headers.has('Idempotency-Key')) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { 'Idempotency-Key': crypto.randomUUID() } }));
};
