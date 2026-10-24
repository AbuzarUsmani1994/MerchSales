import { HttpInterceptorFn } from '@angular/common/http';
import { randomUuid } from './uuid';

/** Section 9: "accept/emit X-Correlation-Id". */
export const correlationIdInterceptor: HttpInterceptorFn = (req, next) =>
  next(req.clone({ setHeaders: { 'X-Correlation-Id': randomUuid() } }));
