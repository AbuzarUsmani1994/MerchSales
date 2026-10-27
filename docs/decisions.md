# Decisions log

Short, ADR-style notes on choices that change or add to the Build Spec (CLAUDE.md). Newest first.

## 2026-10-27 — Visit rewards (restaurants first)

**Context.** The owner wants repeat customers rewarded without SMS (no SMS provider). A points
wallet with a PIN was considered and dropped as harder for staff and customers; the owner chose
a visit count instead. Loyalty is not in the Build Spec's v1 scope.

**Decision.**
- A member is a customer record plus a `sales.LoyaltyMember` row, found by mobile number
  (normalised: 0300-1234567 = +92 300 1234567). Members are company-wide.
- Rules (company setting, owner only; defaults agreed 2026-10-27): a paid bill of Rs 2,000 or
  more counts as one visit, at most one per member per business day; after 5 visits the member
  gets Rs 500 off a bill of Rs 2,500 or more; the bill that uses the reward still counts as a
  visit; visits lapse after 6 months without one.
- Two switches per branch, both off by default: **visit rewards on** (owner) and **cashiers can
  add members** (owner or that branch's manager). Owners and managers can always add, pause
  and rename members (new permission `Sales.Loyalty.Manage`); cashiers only where switched on.
- The reward is a discount spread over the order lines (so invoices and reports show real
  revenue), not a payment. It is a fixed company rule, so it doesn't go through the manual
  discount limit.
- `sales.LoyaltyVisit` is append-only (UPDATE/DELETE denied). The member row is locked during
  payment, so two tills can't both count today's visit or both spend one reward.
- Paid orders can't be cancelled today; when sales returns are built, a return must add
  `VisitReversed` / `RewardReturned` rows (the kinds already exist in the table).
- Waiters may note the customer's phone on an order (only at branches where the programme
  runs); the cashier's screen picks it up.
- No receipts are printed yet, so progress shows on the cashier's screen only.
- Pharma distributors: not used (schemes and rebates fit B2B better); a pharma tenant with a
  retail counter can switch it on like a restaurant.

**Approved by** the owner (TRS), 2026-10-27. Schema: `database/scripts/015_Loyalty.sql`.

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
