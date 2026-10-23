import { HttpInterceptorFn } from '@angular/common/http';

/** Section 9: "accept/emit X-Correlation-Id". */
export const correlationIdInterceptor: HttpInterceptorFn = (req, next) =>
  next(req.clone({ setHeaders: { 'X-Correlation-Id': crypto.randomUUID() } }));
