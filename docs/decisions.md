# Decisions log

Short, ADR-style notes on choices that change or add to the Build Spec (CLAUDE.md). Newest first.

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
