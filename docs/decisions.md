# Decisions log

Short, ADR-style notes on choices that change or add to the Build Spec (CLAUDE.md). Newest first.

## 2026-10-27 — Urdu in the mobile app

**Context.** Section 3 listed Urdu translations as out of scope for v1, while section 2 required
every string to go through i18n so Urdu could be added. The owner asked for the app in Urdu,
for restaurant and pharma staff alike.

**Decision.**
- Mobile app only: `mobile/lib/l10n/app_ur.arb` translates every string (a test fails if a key
  or placeholder is missing). The back office and the customer menu page stay English.
- Everyday Urdu with the English words staff actually use (آرڈر، کیشیئر، سنک، مینیو), not
  formal Urdu. A native speaker from the business should still read it through once.
- Language switch on the sign-in screen and in the home menu, remembered on the phone; with no
  choice made, the phone's own language decides. Right-to-left layout comes from Flutter.
- Font: Noto Nastaliq Urdu (SIL OFL 1.1, about 0.5 MB, bundled in `mobile/assets/fonts`) as a
  fallback font, so Urdu is set in Nastaliq and Latin text keeps the platform font. Urdu gets
  taller line spacing, because Nastaliq letters overlap at English line heights.
- Data stays as entered: item, category and branch names show in whatever language they were
  typed in. Numbers and prices stay in Western digits (1,850). Common sign-in refusals are shown
  in the app's language; other server messages are still English.

**Approved by** the owner (TRS), 2026-10-27.

## 2026-10-27 — Public customer menu (restaurants)

**Context.** The owner wants guests to see a branch's menu on their own phone instead of a
printed menu: a waiter shows a QR on the app, the guest scans it with the camera and the
browser opens the menu with prices and pictures. View only, no ordering (QR table ordering
stays out of scope, section 3).

**Decision.**
- One link per branch. Its random code (16 characters, about 80 bits) is stored as tenant
  setting `PublicMenu.Code.{branchId}`, so no schema script was needed. An empty value means
  switched off; "new link" replaces the code, and the old QR stops working at once.
- The page `GET /m/{code}` is the first endpoint that works **without a login**. Section 6
  says the tenant comes only from the access token; this is an approved exception: the code
  resolves to its tenant and branch through `PublicMenuLinkLookup`, which is on the bypass
  allow-list next to the login's tenant-code lookup and returns only the matching row. Every
  menu read then runs on a normal tenant connection, so RLS still applies.
- The page shows exactly what the waiter's phone menu shows for that branch (same filters,
  same branch-over-default price rule, items without a price hidden) and nothing else: no
  costs, stock, customers or orders. Pictures are served only for items on that menu.
- Plain server-rendered HTML from the API's own site: no script at all, strict CSP, rate
  limited per IP (1,200 requests/min, because guests share the restaurant's Wi-Fi).
- Restaurants only: the API refuses links for other industries, and the app and back office
  only offer it to restaurants.
- Plain HTTP for now (no HTTPS on the server yet). Acceptable for a page without passwords
  or personal data; browsers show "Not secure". Don't print QR stickers until the server has
  a domain name, or they'll break when the address changes.

**Approved by** the owner (TRS), 2026-10-27.
