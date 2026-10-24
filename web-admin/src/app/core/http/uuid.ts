/**
 * `crypto.randomUUID()` only works in a browser "secure context" (HTTPS, or localhost) --
 * it throws `TypeError: crypto.randomUUID is not a function` anywhere else, including the
 * plain-HTTP topology this app is deployed behind for now (section 17: "fine for testing,
 * not for real users (no HTTPS)"). That broke every POST silently: the correlation-id and
 * idempotency interceptors run before the request is dispatched, so the thrown error never
 * reaches the network layer or the console -- it just looks like the request vanished.
 *
 * `crypto.getRandomValues()` has no such restriction and is available everywhere `crypto`
 * itself exists, so build a UUID v4 from that instead. Falls back to `Math.random()` only
 * if `crypto` is entirely unavailable (shouldn't happen in any supported browser).
 */
export function randomUuid(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    try {
      return crypto.randomUUID();
    } catch {
      // Fall through to the getRandomValues()-based implementation below.
    }
  }

  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    const bytes = crypto.getRandomValues(new Uint8Array(16));
    bytes[6] = (bytes[6] & 0x0f) | 0x40; // version 4
    bytes[8] = (bytes[8] & 0x3f) | 0x80; // variant 10
    const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  }

  // Last-resort fallback: not cryptographically strong, but every supported browser has
  // at least one of the two branches above, so this should be unreachable in practice.
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}
