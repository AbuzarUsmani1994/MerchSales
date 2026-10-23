# Build Spec v1 — Generic Order & Stock Platform

> **For Claude Code.** This file is the single source of truth for building the system.
> If anything else (older architecture notes, comments, your own preferences) conflicts with
> this file, **this file wins**. If this file is silent or ambiguous on something that changes
> behaviour, data or security, **stop and ask** instead of guessing.
>
> Work **one phase at a time** (section 18). At the end of each phase, run the acceptance
> checks, report results, and wait for the user's go-ahead before starting the next phase.
> Do not build anything listed as out of scope (section 3).

Spec date: 2026-10-23. Owner: TRS.

---

## Contents

1. Product in one paragraph
2. Final decisions
3. Scope: v1 in / out
4. Repository layout
5. Backend architecture rules
6. Multi-tenancy and row-level security
7. Database rules (manual SQL scripts)
8. Domain rules (stock, orders, money)
9. API conventions
10. Authentication and authorization
11. Tenant provisioning and industry templates
12. Mobile sync protocol
13. Back-office web app (Angular)
14. Mobile app (Flutter)
15. Background jobs
16. Reports, imports, printing
17. Hosting, configuration, logging
18. Phases and acceptance criteria
19. Testing requirements
20. Coding conventions
21. Glossary

---

## 1. Product in one paragraph

A multi-tenant SaaS for businesses that buy, stock and sell physical goods. Launch verticals:
**restaurants** and **pharma distributors**. One shared core (catalog, inventory ledger,
purchasing, sales orders, payments) plus vertical packs switched on per tenant. Staff use a
**Flutter app (Android + iOS)** for daily operations (taking orders, receiving stock, counts,
transfers, collections), and an **Angular back-office web portal** for master data, stock
entry, configuration, approvals and reports. Both clients call the same **ASP.NET Core Web API
(.NET 10)**, hosted on the operator's **Windows Server + IIS**, with **SQL Server** as the only
database. All customers share one installation and one database, isolated by tenant.

---

## 2. Final decisions

These are decided. Do not substitute alternatives.

| Area | Decision |
|---|---|
| Backend | ASP.NET Core Web API on **.NET 10 (LTS)**, controllers, C# latest, nullable enabled |
| Architecture | **Modular monolith**: one API host + one Worker (Windows Service), strict module boundaries |
| Database | **SQL Server 2019 or later** (version on the server is unknown: write SQL that runs on 2019). JSON stored in `nvarchar(max)` with `ISJSON` checks — do **not** use the 2025 `json` type |
| Schema changes | **Manual numbered SQL scripts** in `database/scripts/`. The user runs them by hand. **No EF Core migrations, no `EnsureCreated`, no auto-created tables of any kind** |
| Data access | EF Core 10 (SqlServer provider) mapping existing tables; Dapper for reports and hot paths |
| CQRS / mediator | `Mediator` (martinothamar, source-generated, MIT). **Do not use MediatR, AutoMapper, MassTransit or FluentAssertions** (commercial licences) |
| Validation / mapping | FluentValidation; Riok.Mapperly |
| Background jobs | Hangfire with SQL Server storage, `PrepareSchemaIfNecessary = false`, running **only in the Worker** |
| Cache | `HybridCache` (in-memory). No Redis/Garnet in v1 (single server) |
| Messaging | Transactional outbox table + dispatcher in the Worker. No RabbitMQ in v1 |
| Real-time | SignalR (IIS WebSockets) — Phase 2 kitchen display and approvals |
| Push | Firebase Cloud Messaging (Android + iOS) via `FirebaseAdmin` |
| Auth | **Built-in JWT bearer** access tokens (15 min) + **rotating refresh tokens** stored hashed in `identity.RefreshToken`. *(Deviation from the architecture notes, which said OpenIddict: v1 has only first-party clients, so a full OAuth server is unnecessary. Revisit if SSO or third-party API clients are needed.)* |
| Passwords | ASP.NET Core `PasswordHasher<T>` (PBKDF2). TOTP MFA for admin-level users (section 10) |
| Files | NTFS folder outside the web root, path from config, served through the API |
| Excel | ClosedXML |
| PDF | QuestPDF (Community licence is free below USD 1M annual revenue — note in README) |
| Logging | Serilog → Seq + rolling files; OpenTelemetry traces/metrics |
| OpenAPI | `Microsoft.AspNetCore.OpenApi`, document emitted as **OpenAPI 3.0** (for client generators); Scalar UI in Development only |
| API clients | Generated with **openapi-generator**: `typescript-angular` for web, `dart-dio` for Flutter. Never hand-edit generated code |
| Back office | **Angular** (latest stable major), standalone components + signals, **PrimeNG**, **AG Grid Community** for large grids, **Transloco** for i18n |
| Mobile | **Flutter** (latest stable), Dart 3, **Riverpod** (with generator), **go_router**, **Drift** + SQLCipher, **dio**, **flutter_secure_storage**, **mobile_scanner**, **local_auth**, `firebase_messaging`, **Firebase Crashlytics** |
| Hosting | Windows Server + IIS (ASP.NET Core Module V2, in-process). Cloudflare in front. Currently IIS and SQL Server share one machine |
| Language | **English UI only in v1**, but every user-facing string goes through i18n (Transloco / Flutter `intl` ARB) so Urdu (RTL) can be added later |
| Currency | **PKR only in v1**. Currency columns exist and are always `'PKR'`; no conversion logic |
| Tax | **Off at launch.** Tax codes exist with 0 %; every document line stores `TaxCodeId`, `TaxRatePercent`, `TaxAmount`. Calculation code exists and runs with 0 % |
| Time | Store UTC (`datetime2(3)`). Business dates are `date` in the tenant's time zone (default `Asia/Karachi`) |
| Certificate pinning | **Not in v1** (Cloudflare rotates edge certificates; pinning would break the app). Revisit later |

---

## 3. Scope: v1 in / out

### In scope (Phases 0–2)

- Platform: operator tenant, tenant provisioning from industry templates, suspend/activate tenants.
- Identity: users, roles, permissions, branch-scoped roles, login, refresh, logout, change password, lockout, MFA (TOTP) for admin users, device registration/revocation/remote wipe, manager PIN.
- Settings: branches, warehouses, locations, feature flags, number series, stock document types, reason codes, custom field definitions, terminology, payment methods, tax codes (0 %).
- Catalog: units, product types, categories (+ attribute sets), brands, products, variants (parent + variant products), unit conversions, barcodes, price lists with tiers and dates, components (kits/recipes — used in Phase 2).
- Inventory: batches (default batch for untracked products), stock ledger, balances, moving-average cost, reservations, stock documents (opening stock, adjustments, wastage, transfers in two steps), stock counts (full/cycle, blind), reorder rules, batch block/unblock.
- Purchasing: suppliers, purchase orders (with approval threshold), goods receipts (with or without PO; batch/expiry capture; accepted/rejected/free qty), purchase returns.
- Sales: customers, customer groups, walk-in customer, sales orders (single state machine, all channels), counter sale in one step, partial fulfilment/back-orders, cancellation, invoices, sales returns.
- Finance: payment methods, payments in/out with allocation, customer/supplier ledger and balances, cash sessions (shift open/close, Z report), accounting periods (close/lock).
- Platform services: audit log, approvals, notifications (in-app + push), attachments, idempotency, outbox.
- Mobile sync: delta pull, command push, offline limits, device number ranges.
- Reports and Excel import/export; PDF for invoice, receipt, PO, GRN.
- **Phase 2 vertical packs:** restaurant (tables, modifiers, kitchen tickets/display, recipe back-flush, shifts); pharma distributor (FEFO enforcement, min shelf life, schemes, credit control, routes, expiry alerts, recall).

### Out of scope (do not build)

Customer-facing ordering app, QR table ordering, delivery-aggregator integrations, B2B portal,
WhatsApp ordering, all AI features, supplier invoices with three-way match, tax calculation rules
and tax-authority e-invoicing (FBR/PRA), multi-currency, Urdu translations, serial-number tracking
(the `Serial` tracking mode exists in the schema but is rejected by the API), manufacturing,
general ledger / accounting, payroll, database-per-tenant, Redis/Garnet, RabbitMQ, Kubernetes,
microservices, certificate pinning, Bluetooth printing until Phase 2.

---

## 4. Repository layout

```
/CLAUDE.md                               this spec
/database/scripts/000_…sql … 011_…sql     provided; run by the user; never edit applied scripts
/database/scripts/README.md
/backend/
  StockOrder.sln
  Directory.Build.props                   nullable, warnings as errors, analyzers, LangVersion latest
  Directory.Packages.props                central package versions (pin every version)
  src/
    Hosts/StockOrder.Api/                 composition root, controllers wiring, middleware
    Hosts/StockOrder.Worker/              Windows Service: outbox dispatcher + Hangfire server
    BuildingBlocks/StockOrder.SharedKernel/       Result/Error types, base entities, clock, ids, money/qty helpers
    BuildingBlocks/StockOrder.Infrastructure/     DB connection + RLS session, unit of work, outbox, idempotency,
                                                  audit, number series, files, FCM, Excel, PDF, caching
    Modules/<Module>/StockOrder.Modules.<Module>/            Domain, Application, Infrastructure, Api folders
    Modules/<Module>/StockOrder.Modules.<Module>.Contracts/  public interfaces, DTOs, integration events
      Modules: Tenancy, Identity, Platform, Catalog, Inventory, Purchasing, Sales, Finance, Sync, Reporting,
               Restaurant (Phase 2), Pharma (Phase 2)
    Templates/                            industry template JSON files (embedded resources)
  tests/
    StockOrder.UnitTests/
    StockOrder.IntegrationTests/
    StockOrder.ArchitectureTests/
/web-admin/                               Angular workspace
/mobile/                                  Flutter app
/docs/                                    operations runbook, decisions log (ADR-style, short)
```

---

## 5. Backend architecture rules

1. **Module boundaries.** A module references only `SharedKernel`, `Infrastructure` and other modules' `*.Contracts` projects. Never another module's implementation project. Enforced by architecture tests.
2. **Each module owns its schema** (`catalog`, `inventory`, …) and maps only its own tables in its own `DbContext`. Exceptions: `Reporting` (read-only Dapper over all schemas) and `Sync` (calls `ISyncSource` implementations that each module provides).
3. **Inside a module:** `Domain` (entities, value objects, domain rules — no EF or HTTP types), `Application` (commands/queries + handlers + validators), `Infrastructure` (EF configuration, repositories, SQL), `Api` (controllers). Controllers are thin: map request → command → `Result` → HTTP.
4. **One unit of work per request.** A scoped `IUnitOfWork` owns one `SqlConnection` and one transaction. All module `DbContext`s in the request use that connection and enlist in that transaction (`Database.UseTransaction`). Commands that must be atomic across modules (confirming an order reserves stock; posting a GRN writes the ledger and the supplier ledger) call other modules **synchronously through their Contracts interfaces inside the same transaction**.
5. **Side effects go through the outbox.** Notifications, push messages, reorder suggestions, emails: write an outbox message in the same transaction; the Worker dispatches it. Consumers are idempotent via `platform.InboxMessage`.
6. **Errors are values.** Handlers return `Result<T>` with a typed `Error(code, message, details)`. Exceptions are for bugs and infrastructure failures only.
7. **No business logic in clients.** Angular and Flutter validate input for usability, but the API re-validates everything and recomputes all money and stock.
8. **Time and ids** come from injectable `IClock` and `IIdGenerator` (sequential GUIDs) so tests are deterministic.

---

## 6. Multi-tenancy and row-level security

- Every tenant-owned table has `TenantId`. SQL Server row-level security (script 009) filters and blocks rows by `SESSION_CONTEXT('TenantId')`.
- **On every connection open** (a `DbConnectionInterceptor` / connection factory), before any query:
  `EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId, @read_only=1;`
  The tenant id comes **only from the validated access token** (`tid` claim), never from a header, route or body.
- **Bypass** (`sp_set_session_context N'RlsBypass', 1, @read_only=1`) is allowed only in: the login/refresh tenant lookup, the Worker's cross-tenant jobs (which then set `TenantId` per tenant when acting for one), and platform-admin tenant provisioning. Implement it as a separate `IBypassConnectionFactory` that only those classes can resolve (constructor-injected, registered as internal). Log every bypass use.
- EF Core global query filters on `TenantId` are kept as the first guard; RLS is the second.
- Inserts: the infrastructure sets `TenantId` on new entities from the current tenant; RLS block predicates reject any mismatch.
- Branch scoping: users get roles per branch (`identity.UserRole.BranchId`, NULL = all branches). Queries for branch data filter to the user's allowed branches in the application layer.

---

## 7. Database rules (manual SQL scripts)

- Scripts `000`–`011` are provided and are the schema. Read `database/scripts/README.md`. Map entities to these tables exactly (names, types, lengths, nullability).
- Any schema change = a **new numbered script** following the README rules (guard, single transaction, `EXEC` for function/trigger/view, add new tenant tables to the RLS policy). Hand the script to the user to run; **never** change the database from code.
- The API at startup reads `dbo.SchemaVersion` and **refuses to start** (clear log message) if any script the code expects is missing. Keep the expected list in one constant.
- Phase 0 must produce `012_Hangfire.sql` (Hangfire schema for the pinned Hangfire.SqlServer version, plus `GRANT` to `app_runtime`).
- Column conventions: quantities `decimal(18,6)`, money `decimal(19,4)`, unit cost `decimal(19,6)`, percentages `decimal(9,4)`. Map `rowversion` as EF concurrency token (`IsRowVersion()`).
- `ClusterKey` is database-generated; never set it in code. `Id` is generated in code (sequential GUID) or by the client (offline documents).
- Append-only tables (`inventory.StockLedgerEntry`, `finance.PartyLedgerEntry`, `platform.AuditLog`) are insert-only; the DB denies UPDATE/DELETE.
- Never hard-delete rows referenced by documents; set `IsActive = 0`. If a synced row must be physically deleted, write `sync.Tombstone`.

---

## 8. Domain rules

### 8.1 Units and quantities
- Every product has a base UoM. `catalog.ProductUomConversion.FactorToBase`: 1 unit of that UoM = factor base units. The base UoM itself has factor 1 (store a row for it).
- Every document line stores `Quantity` (in the chosen UoM) **and** `QtyBase`. All stock logic uses `QtyBase`.
- Round `QtyBase` to 6 decimals; round displayed quantities to the UoM's `DecimalPlaces`. Reject quantities with more decimals than the UoM allows.

### 8.2 Batches
- Every stock balance and ledger row has a `BatchId`. When a product is created, create its **default batch** (`BatchNo = 'DEFAULT'`, `IsDefault = 1`).
- Tracking `None`: always use the default batch. `Batch`: batch number required on receipts. `BatchExpiry`: batch number and expiry required. `Serial`: reject (out of scope).
- Receipts reject a batch whose expiry is closer than `Product.MinShelfLifeDays` (error `SHELF_LIFE_TOO_SHORT`).
- Batches with status `Blocked`, `Recalled` or `Expired` can never be reserved or issued. A daily job sets `Expired` when `ExpiryDate < today`.

### 8.3 Stock ledger and balances
- **All stock changes go through one service:** `IStockPostingService.Post(StockPosting posting)` in the Inventory module. It, in the current transaction: inserts ledger rows, updates `StockBalance` rows (insert-if-missing), updates `ProductCost`.
- Decrease on hand with **one atomic statement per balance row**; never read-then-write.
  Issue **without** a reservation (adjustments, wastage, transfers out, purchase returns, counter sales):
  ```sql
  UPDATE inventory.StockBalance
     SET QtyOnHand = QtyOnHand - @qty, LastMovementAtUtc = SYSUTCDATETIME()
   WHERE TenantId=@t AND ProductId=@p AND LocationId=@l AND BatchId=@b
     AND (@allowNegative = 1 OR QtyOnHand - QtyReserved >= @qty);
  ```
  Issue **consuming** a reservation (sales dispatch):
  ```sql
  UPDATE inventory.StockBalance
     SET QtyOnHand = QtyOnHand - @qty, QtyReserved = QtyReserved - @qty, LastMovementAtUtc = SYSUTCDATETIME()
   WHERE TenantId=@t AND ProductId=@p AND LocationId=@l AND BatchId=@b
     AND QtyReserved >= @qty;
  ```
  0 rows affected ⇒ `INSUFFICIENT_STOCK` (roll back the whole command). Increases use `UPDATE … SET QtyOnHand = QtyOnHand + @qty`, inserting the balance row first if it does not exist (`INSERT … WHERE NOT EXISTS` under `UPDLOCK, HOLDLOCK`).
- Negative stock is allowed only if `Product.AllowNegativeStock = 1`, or for **offline counter sales synced late** (they already happened physically): post them, let the balance go negative, and raise a `NegativeStock` notification for review.
- `StockLedgerEntry.UnitCost` = moving-average cost at posting time for issues; the receipt cost for receipts.
- **Rebuild check:** an admin endpoint and an integration test recompute balances from the ledger and compare. They must always match.

### 8.4 Costing (moving weighted average per product per warehouse)
- On receipt of `q` base units at unit cost `c`: `newAvg = (QtyBasis*Avg + q*c) / (QtyBasis + q)` when `QtyBasis + q > 0`; free units arrive at cost 0. `QtyBasis += q`.
- GRN unit cost per base unit = `UnitCost * (1 - DiscountPercent/100) / FactorToBase` (tax excluded).
- Issues and transfers out use the current average and reduce `QtyBasis`. Transfers in arrive at the source's cost.
- If `QtyBasis` would go ≤ 0, keep the last average (do not divide by zero).

### 8.5 Reservations and allocation
- Confirming an order reserves stock line by line in the order's warehouse, only from locations with `IsSellable = 1`.
- Candidate balances: batch `Active`, `ExpiryDate IS NULL OR ExpiryDate >= today` (pharma: `>= today + customer minimum expiry days`, Phase 2), `QtyOnHand - QtyReserved > 0`.
- Order candidates by the product's issue rule: **FEFO** = `ExpiryDate` ascending (NULLs last), then batch creation; **FIFO** = batch creation ascending; **Manual** = batch chosen on the line.
- Reserve with an atomic `UPDATE … SET QtyReserved = QtyReserved + @q WHERE … AND QtyOnHand - QtyReserved >= @q`; on 0 rows re-read and retry with the next candidate (max 3 passes).
- Shortfall handling by `SalesOrder.BackOrderPolicy`: `Reject` → error `INSUFFICIENT_STOCK`; `BackOrder` → reserve what exists, set `BackOrderedQtyBase`; `ShipAvailable` → reduce the line to what exists.
- Reservations have `ExpiresAtUtc` per channel (tenant setting `Sales.ReservationMinutes.<Channel>`; default: none for BackOffice/FieldBooking, 30 min for others). A job releases expired ones.
- Cancelling an order releases its reservations; dispatching consumes them.

### 8.6 Sales order state machine (all channels)

| From | Action (endpoint) | To | Effects |
|---|---|---|---|
| — | create | Draft | Lines priced (snapshot) |
| Draft | submit | Submitted | Validation; number assigned on confirm |
| Submitted | (automatic checks) | Confirmed or OnHold | Credit check, approvals; on Confirmed: reserve stock, assign `DocumentNo` |
| OnHold | release (`Sales.Credit.Override`) | Confirmed | Reserve stock |
| Confirmed | start-fulfilment | Fulfilling | — |
| Confirmed / Fulfilling | dispatch | Dispatched | Issue stock (ledger), consume reservations, `UnitCostAtSale` |
| Dispatched | deliver | Delivered | Optional proof (photo/signature attachment) |
| Dispatched / Delivered | invoice | Invoiced | Create `sales.Invoice` (gap-free number), customer ledger debit |
| Invoiced | (payments cover total) | Paid | Automatic when allocations ≥ GrandTotal |
| Paid + Delivered | (automatic) | Closed | — |
| Draft / Submitted / OnHold / Confirmed / Fulfilling | cancel (reason required) | Cancelled | Release reservations; paid amounts → refund workflow |

- **Counter sale** (`POST /sales/counter-sales`): one command creates the order and runs Confirmed → Dispatched → Delivered → Invoiced → Paid → Closed in **one transaction**, writing every status-history row.
- Every transition writes `sales.SalesOrderStatusHistory` and an outbox event (`SalesOrderConfirmed`, `SalesOrderDispatched`, …).
- Invalid transition ⇒ `INVALID_STATE_TRANSITION`. Amendments allowed only in Draft/Submitted/OnHold/Confirmed (Confirmed: release and re-reserve; `Version += 1`).
- After dispatch, corrections are only via **sales returns** (against an invoice) and credit notes.

### 8.7 Pricing, discounts, totals (tax-ready)
- Price list resolution: `Customer.PriceListId` → `CustomerGroup.PriceListId` → tenant default price list. Choose the item with matching UoM, highest `MinQuantity ≤ quantity`, valid on the business date. If only a base-UoM price exists, multiply by the factor. No price ⇒ `PRICE_NOT_FOUND`, unless the user has `Sales.Price.Override` and supplies a price.
- Line maths (tax-exclusive; round each money result to 2 decimals, `MidpointRounding.AwayFromZero`):
  `gross = Quantity × UnitPrice`; `DiscountAmount = round(gross × DiscountPercent/100)` (or an entered amount);
  `TaxAmount = round((gross − DiscountAmount) × TaxRatePercent/100)`; `LineTotal = gross − DiscountAmount + TaxAmount`.
  Header totals are sums of line values; `GrandTotal = SubTotal − DiscountTotal + TaxTotal + ChargesTotal`.
- Discounts above tenant setting `Sales.MaxDiscountPercent` (default 10) need `Sales.Discount.Apply` or a manager PIN.
- Purchase documents use the same maths with `UnitCost`.

### 8.8 Credit control (feature `Sales.CreditControl`)
- If the customer has `CreditLimit`: exposure = ledger balance + confirmed-but-not-invoiced orders + this order. Exposure > limit, or any invoice overdue by more than `CreditDays` ⇒ order goes `OnHold` (reason stored). Released only with `Sales.Credit.Override`.

### 8.9 Document numbers
- `INumberSeriesService.Next(documentType, branchId)` runs `UPDATE platform.NumberSeries SET NextNumber = NextNumber + 1 OUTPUT deleted.NextNumber …` **inside the posting transaction**, so a rolled-back post never consumes a number (invoices are gap-free).
- Numbers are assigned at posting/confirmation, never at draft. Offline documents show `DeviceDocumentNo` until synced, except counter-sale receipts, which use a number from the device's `sync.DeviceNumberRange` (server issues blocks of 200; unused numbers are logged when a range is closed).

### 8.10 Approvals
- Stock documents whose type has `RequiresApproval = 1` and `TotalCostValue > ApprovalThresholdValue` → `PendingApproval`, create `platform.ApprovalRequest` (permission `Inventory.Document.Approve`), notify holders.
- Purchase orders above tenant setting `Purchasing.ApprovalThreshold` → `PendingApproval` (`Purchasing.Order.Approve`).
- The creator can never approve their own document.

### 8.11 Posting rules (what each post writes)

| Document | Stock ledger | Party ledger | Other |
|---|---|---|---|
| Goods receipt | `Receipt` +AcceptedQtyBase(+Free) into line location; create/resolve batch | Supplier **credit** GrandTotal | Update cost, PO received qty and PO status |
| Purchase return | `Issue` − from location/batch | Supplier **debit** TotalAmount | — |
| Stock document, Direction `In` (e.g. OPENING) | entries of the type's `MovementKind`, + at line UnitCost | — | Unit cost required |
| Stock document, Direction `Out` (e.g. WASTAGE, STAFF_MEAL) | entries of the type's `MovementKind`, − at average cost | — | Reason required if the type says so |
| Stock document, Direction `Both` (ADJUST, COUNT_ADJ) | entries of the type's `MovementKind`, ± per line | — | Positive lines at line UnitCost if given, else current average cost |
| Transfer dispatch | `Transfer` − source, + destination warehouse's `TRANSIT` location | — | Status `InTransit` |
| Transfer receive | `Transfer` − TRANSIT, + destination location | — | Status `Posted`; destination cost updated |
| Stock count post | via a system `COUNT_ADJ` stock document with the variances | — | Link `AdjustmentDocumentId` |
| Sales dispatch | `Issue` − per reserved batch at average cost | — | Consume reservations |
| Invoice | — | Customer **debit** GrandTotal | — |
| Sales return | `Receipt` + to Sellable/Quarantine location at original issue cost; none for WriteOff | Customer **credit** CreditAmount | Refund payment if RefundMethod ≠ Credit |
| Payment In | — | Customer **credit** | Allocate to invoices, update `AmountPaid` |
| Payment Out | — | Supplier **debit** (or customer debit for refunds) | — |

- Posting into a **closed accounting period** (by `BusinessDate`) ⇒ `PERIOD_CLOSED`.
- Business date = tenant-local today; for counter and dine-in sales, the open cash session's `BusinessDate`.

---

## 9. API conventions

- Base path `/api/v1/{module}/{resource}`; resources plural, kebab-case. Actions are `POST` sub-resources: `/api/v1/sales/orders/{id}/confirm`, `/cancel`, `/dispatch`; `/api/v1/inventory/stock-documents/{id}/post`.
- JSON camelCase. Ids are GUID strings. Timestamps ISO-8601 UTC (`…Z`). Business dates `yyyy-MM-dd`. Decimals as JSON numbers.
- **Lists:** `GET ?page=1&pageSize=50&sort=name,-createdAtUtc&search=…&<field>=…` (max pageSize 200) → `{ "items": [...], "page": 1, "pageSize": 50, "totalCount": 1234 }`.
- **Concurrency:** GET of a single resource returns `ETag` = base64 of `RowVer`. PUT/PATCH and state actions require `If-Match`; mismatch ⇒ `412` code `CONCURRENCY_CONFLICT`.
- **Idempotency:** every `POST` that creates or posts something requires header `Idempotency-Key` (client GUID). Same key + same request hash ⇒ replay the stored response; same key + different body ⇒ `422 IDEMPOTENCY_KEY_REUSED`. Records kept 7 days.
- **Errors:** RFC 9457 ProblemDetails: `type` = `https://errors.stockorder.app/<code>`, `title`, `status`, `detail`, extensions `code`, `traceId`, and for validation `errors: { field: [messages] }`.

| Code | HTTP | Meaning |
|---|---|---|
| `VALIDATION_FAILED` | 400 | Input invalid |
| `UNAUTHENTICATED` | 401 | Missing/expired token |
| `DEVICE_REVOKED` | 401 | Device revoked; app must sign out (and wipe if told) |
| `PERMISSION_DENIED` | 403 | Lacks permission or branch access |
| `TENANT_SUSPENDED` | 403 | Tenant not active |
| `NOT_FOUND` | 404 | (also returned for other tenants' ids) |
| `CONCURRENCY_CONFLICT` | 412 | ETag mismatch |
| `DUPLICATE` | 409 | Unique value exists (SKU, code, barcode) |
| `INVALID_STATE_TRANSITION` | 409 | Action not allowed in current status |
| `INSUFFICIENT_STOCK` | 409 | Not enough available stock (details list per line) |
| `BATCH_NOT_AVAILABLE` | 409 | Batch blocked, recalled or expired |
| `SHELF_LIFE_TOO_SHORT` | 409 | Receipt batch expires too soon |
| `CREDIT_LIMIT_EXCEEDED` | 409 | Order put on hold (returned as info on confirm) |
| `PRICE_NOT_FOUND` | 409 | No price for product/UoM |
| `PERIOD_CLOSED` | 409 | Business date in a closed period |
| `IDEMPOTENCY_KEY_REUSED` | 422 | Key reused with a different request |
| `APP_VERSION_UNSUPPORTED` | 426 | Mobile app below minimum version |

- Every endpoint has an explicit `[Authorize(Policy = "<PermissionCode>")]` or `[AllowAnonymous]`. An architecture test fails the build if any endpoint has neither.
- Mobile requests send `X-App-Version` and `X-Device-Id`; middleware enforces the minimum version and device revocation.
- Correlation: accept/emit `X-Correlation-Id`; include it in logs and ProblemDetails `traceId`.
- Health: `/health/live` (process up), `/health/ready` (DB reachable + schema version OK). Not authenticated, no details in body.

---

## 10. Authentication and authorization

- **Login** `POST /api/v1/auth/login` `{ tenantCode, username, password, totpCode?, device?: { id, platform, name, appVersion, osVersion, pushToken } }`
  → `{ accessToken, expiresInSeconds, refreshToken, mustChangePassword, mfaRequired?, user, tenant, permissions[], features[], branches[] }`.
  Tenant lookup by code uses the bypass connection; everything after uses the tenant context. When `device` is sent, the device row (`sync.Device`) is upserted in the same transaction before the refresh token is issued (the token references it). Requires `Mobile.Use` for mobile logins.
- **Access token:** JWT RS256, 15 minutes. Claims: `sub` (user id), `tid` (tenant id), `tcode`, `sst` (security stamp), `did` (device id, mobile only), `jti`. **Permissions are not in the token**; load them per request from `HybridCache` keyed by `(tenant, user, securityStamp)`. Changing roles or password rotates `SecurityStamp`, which invalidates cached permissions and tokens.
- Signing key: RSA key from a PFX file (path + password from environment variables) or the Windows certificate store by thumbprint. Publish the public key only internally; tokens are validated by the API itself.
- **Refresh token:** format `{tenantId:N}.{32 random bytes base64url}`; store SHA-256 hash only. Rotation on every use; reuse of an already-rotated token revokes the whole `FamilyId`. Lifetimes: web 12 h, mobile 30 days.
- **Lockout:** 5 failed logins ⇒ locked 15 minutes. Passwords: min 8 chars, not all digits, not equal to username; new users and resets set `MustChangePassword = 1`.
- **MFA:** TOTP required at web login for users who hold `Users.Manage`, `Roles.Manage` or `Settings.Manage`. Secret stored encrypted with ASP.NET Data Protection (key ring persisted to a protected folder).
- **Manager PIN:** 4–6 digits, hashed; used for voids, discounts above limit, price overrides when the cashier lacks permission. Each use is audited with both users.
- **Authorization:** permission policies (codes in `011_SeedReferenceData.sql`). Branch-scoped roles restrict which branches' data a user may read or write.
- **Platform operator:** a tenant with `IsPlatform = 1` (code `PLATFORM`). Its `PlatformAdmin` role holds `Platform.*` permissions. Bootstrap with a CLI mode of the API: `StockOrder.Api.exe bootstrap-platform --admin <username>`, which creates the platform tenant and admin and prints a one-time password.
- Audit every login, failed login, lockout, token family revocation, permission change, device revocation.

---

## 11. Tenant provisioning and industry templates

`POST /api/v1/platform/tenants` (permission `Platform.Tenants.Manage`) `{ code, name, industry, owner: { username, displayName, email, phone }, branch: { code, name }, warehouse: { code, name } }`
creates in **one transaction**: tenant, branch, warehouse with locations `MAIN` (Storage), `TRANSIT` (InTransit, not sellable), `QUARANTINE` (Quarantine, not sellable), the owner user (role `Owner`, one-time password), and everything in the industry template.

Every warehouse created later through the API gets the same three locations automatically (transfers rely on each warehouse having a `TRANSIT` location). Every product created gets its base-unit conversion row (factor 1) and its default batch automatically.

Templates live in `backend/src/Templates/{Restaurant|Pharma|Retail}.json` and contain:

| Section | Restaurant | Pharma (distributor) |
|---|---|---|
| Units | kg, g, l, ml, pcs, portion | carton, box, strip, tab, cap, bottle, ml, pcs |
| Product types | Ingredient (Stocked, Batch none, FIFO), Dish (Recipe), Beverage (Stocked), Packaging (Stocked) | Medicine (Stocked, BatchExpiry, FEFO), Surgical (Stocked, Batch, FEFO), Cosmetic (Stocked, BatchExpiry, FEFO) |
| Attribute definitions | spiceLevel (List), isVeg (Boolean) | genericName (Text, searchable), strength (Text), dosageForm (List), drapRegNo (Text), isControlled (Boolean), storage (List); Customer: drugLicenseNo (Text), drugLicenseExpiry (Date) |
| Tax codes | NONE 0 % | NONE 0 % |
| Payment methods | CASH, CARD, WALLET | CASH, BANK, CHEQUE, CREDIT |
| Reason codes | WASTAGE, SPOILED, STAFF_MEAL, COMPLIMENTARY, VOID, DAMAGE | DAMAGE, EXPIRED, NEAR_EXPIRY_RETURN, RECALL, SHORT_SUPPLY, CANCELLED_BY_CUSTOMER |
| Stock document types | OPENING (Receipt/In), ADJUST (Adjustment/Both, reason, approval > 50,000), WASTAGE (Adjustment/Out, reason), STAFF_MEAL (Issue/Out), TRANSFER (Transfer/Move), COUNT_ADJ (Count/Both, system) | OPENING, ADJUST (approval > 25,000), TRANSFER, COUNT_ADJ, EXPIRED_WRITEOFF (Adjustment/Out, reason) |
| Number series | SO-, INV-, RCPT-, GRN-, PO-, PR-, SR-, PAY-, ADJ-, TRF-, CNT-, OPN- (padding 6) | same |
| Roles | Owner (all), Manager, Cashier, Waiter, Kitchen, StoreKeeper, Purchaser | Owner (all), Manager, SalesRep (Booker), StoreKeeper, Purchaser, Accountant, Rider |
| Features on | Inventory.Approvals, Finance.CashSessions, Restaurant.Tables, Restaurant.Kitchen, Restaurant.Recipes, Restaurant.Modifiers, Mobile.Offline | Inventory.BatchTracking, Inventory.ExpiryTracking, Inventory.FEFO, Inventory.Approvals, Purchasing.Approvals, Sales.CreditControl, Sales.BackOrders, Pharma.Schemes, Pharma.Routes, Pharma.Recall, Mobile.Offline |
| Settings | `Mobile.MaxOfflineHours` = 24, `Sales.MaxDiscountPercent` = 10, `Purchasing.ApprovalThreshold` = 100,000, `Money.RoundingDecimals` = 2, terminology defaults | `Mobile.MaxOfflineHours` = 4, `Sales.MaxDiscountPercent` = 5, `Purchasing.ApprovalThreshold` = 250,000, `Money.RoundingDecimals` = 2, terminology: Customer → "Pharmacy", SalesRep → "Booker" |
| Records | Walk-in customer, default price list "Menu", accounting period for current month | Walk-in customer, default price list "Trade price", current period |

Write the exact role → permission mapping into each template file (list every permission code per role) and show it in the Phase 0 report for the user to confirm. Feature keys must come from one constants class; unknown keys in a template fail provisioning.

---

## 12. Mobile sync protocol

### Pull — `GET /api/v1/sync/pull?tokens=<base64 json>&limit=1000`
- `tokens` maps entity → last `RowVer` (hex) received, e.g. `{"products":"00000000000A1F2C","barcodes":"…"}`; empty on first sync.
- Server per entity: capture `upper = MIN_ACTIVE_ROWVERSION()` first, then select rows with `RowVer > token AND RowVer < upper` ordered by `RowVer`, top `limit`. New token = last row's `RowVer` if the page is full (`hasMore = true`), else `upper − 1`.
- Response: `{ "tokens": {…}, "hasMore": { "products": false, … }, "changes": { "products": [...], … }, "tombstones": [ { "entity": "barcodes", "id": "…" } ], "serverTimeUtc": "…" }`. The app repeats pulls until no entity has `hasMore`.
- Entities v1: settings, features, permissions (for this user), branches (user's), warehouses, locations, uoms, productTypes, categories, attributeDefinitions, products, conversions, barcodes, priceLists, priceListItems, taxCodes, paymentMethods, reasonCodes, customers (all for back-office roles; assigned only for reps in Phase 2), batches (active, with stock in user's warehouses), stockBalances (user's warehouses), openOrders (user's branch, last 7 days).
- Each module implements `ISyncSource` for its entities; the Sync module only orchestrates.

### Push — `POST /api/v1/sync/push`
- Body `{ "deviceId": "…", "commands": [ { "id": "<guid>", "type": "CreateSalesOrder", "clientSequence": 42, "createdOnDeviceAtUtc": "…", "payload": {…} } ] }` (max 100 commands).
- Processed in `clientSequence` order, **each in its own transaction**. Before applying, check `sync.CommandLog`: if the id exists, return the stored result (idempotent).
- Result per command: `{ "id", "status": "Applied" | "AppliedWithChanges" | "Rejected" | "Failed", "errorCode"?, "message"?, "result": { serverIds, documentNo, adjustedLines? } }`. `AppliedWithChanges` = applied but the server changed something (e.g. reduced quantity, back-ordered).
- A `Rejected` command does not block later commands unless a later command depends on it (payload references its client id) — then that one is `Rejected` with `DEPENDENCY_REJECTED`.
- Command types v1: `CreateSalesOrder`, `CompleteCounterSale`, `CancelSalesOrder`, `RecordPayment`, `CreateGoodsReceipt`, `PostGoodsReceipt`, `CreateStockDocument`, `PostStockDocument`, `DispatchTransfer`, `ReceiveTransfer`, `SubmitCountLines`, `OpenCashSession`, `CloseCashSession`, `UpdatePushToken`. Each maps to the same application command the REST endpoint uses (no duplicate logic).

### Other sync endpoints
- `GET /api/v1/sync/status` → `{ minVersion, latestVersion, storeUrl, deviceRevoked, wipeRequested, maxOfflineHours, serverTimeUtc }`.
- `POST /api/v1/sync/number-range` `{ documentType }` → a new block for the device.

### Offline rules
- The app blocks **new** transactions when the last successful sync is older than `Mobile.MaxOfflineHours`; it shows why and keeps trying to sync.
- Recall/block changes to batches are pulled first on every sync.
- Controlled drugs (Phase 2 pharma) are online-only.

---

## 13. Back-office web app (Angular) — `/web-admin`

- Angular latest stable, standalone components, signals, strict TypeScript, ESLint. PrimeNG for UI, AG Grid Community for big grids (products, stock, ledger). Transloco i18n (`en` only, all text via keys).
- Generated API client in `src/app/api/generated` (openapi-generator `typescript-angular`), regenerated by an npm script from the API's OpenAPI document.
- HTTP interceptors: bearer token + silent refresh (single in-flight refresh), `Idempotency-Key` on POST, `If-Match` from stored ETags, ProblemDetails → toast/inline field errors, correlation id.
- Auth: login (tenant code, username, password, TOTP when required), change password on first login, idle timeout 30 min.
- Navigation built from permissions and features; route guards check both. Hidden ≠ secure — the API always re-checks.
- Forms: reactive forms; a **dynamic custom-fields component** renders `AttributeDefinition`s for the entity (product by category, customer, supplier, orders).
- Lazy-loaded feature areas and v1 screens:
  - **Setup:** company, branches, warehouses/locations, users, roles & permission matrix, devices, feature toggles, number series, stock document types, reason codes, payment methods, tax codes, custom fields, terminology.
  - **Catalog:** products grid (filters, search by name/SKU/barcode/searchable attributes), product form (type, category attributes, units & conversions, barcodes, prices, reorder rules, image), variants matrix, categories tree, brands, units, price lists (grid editing).
  - **Inventory:** stock on hand (by warehouse/location/batch), product stock card (ledger), batches (block/unblock), stock documents (create/post/approve), transfers (dispatch/receive), stock counts (create, review variances, post), reorder suggestions.
  - **Purchasing:** suppliers, purchase orders (approve, print/email PDF), goods receipts (with batch/expiry grid), purchase returns.
  - **Sales:** customers, customer groups, sales orders (all states, actions per state), invoices (print PDF), sales returns.
  - **Finance:** receive payment (allocate to invoices), pay supplier, customer/supplier statements, cash sessions, accounting periods.
  - **Approvals inbox, notifications, audit log viewer.**
  - **Import centre** and **Reports** (section 16).
  - **Platform** (operator tenant only): tenants list, create tenant, suspend, app version policy.
- Deployed as static files to an IIS site with a URL Rewrite rule sending unknown routes to `index.html`. Environment config (`apiBaseUrl`) via a `config.json` loaded at startup, not compiled in.

---

## 14. Mobile app (Flutter) — `/mobile`

- Structure: `lib/core/` (config, api client, auth, local db, sync engine, printing later), `lib/features/<feature>/` (data, application, presentation), `lib/shared/` (widgets, theme, i18n). Phone and tablet layouts (breakpoint 600 dp).
- Generated Dart client (openapi-generator `dart-dio`) in `lib/core/api/generated`, never edited.
- **Local database (Drift + SQLCipher):** tables mirroring the pulled entities (only needed columns), plus `outbox_commands` (id, type, payload, clientSequence, status, lastError) and local document tables. Encryption key generated per install, stored in secure storage.
- **Every screen reads the local database.** Every write creates the local record and an outbox command in one local transaction; the sync engine pushes in order, then pulls. Sync runs on app start, on connectivity regained, after each new command (debounced 5 s), every 5 minutes while open, and on pull-to-refresh.
- Auth: login (tenant code, username, password), tokens in secure storage, biometric unlock after first login, device registration, handle `DEVICE_REVOKED` (sign out; wipe local DB if `wipeRequested`), `APP_VERSION_UNSUPPORTED` (blocking update screen with store link).
- Barcode scanning (mobile_scanner) everywhere a product is chosen.
- Push notifications (FCM): approvals waiting, low stock, near expiry, order status.
- v1 screens by role (menus from permissions + features):
  - **All:** home dashboard (today's sales/receipts/alerts), product lookup (scan → stock by location/batch, price), notifications, sync status (pending commands, last sync, errors with retry).
  - **Store keeper:** goods receipt (from PO or direct; batch/expiry capture), stock document (adjustment, wastage, opening), transfer dispatch/receive, stock count (blind entry by scan), batch view.
  - **Sales staff / rep:** customer list and detail (balance), new sales order (scan/search, UoM, price from local price list, discount within limit), order list and status, record payment.
  - **Cashier / counter:** open/close cash session, counter sale (one screen: cart, payment, receipt number from device range), Z report.
  - **Manager/owner:** approvals, key reports (sales today, stock alerts).
- Phase 2 adds: waiter table map and dine-in orders, kitchen display (tablet), Bluetooth ESC/POS receipt printing (choose a maintained package that supports Android and iOS; prove it on one real printer before building on it), rep route plan and schemes.

---

## 15. Background jobs (Worker — Windows Service)

| Job | Schedule | What it does |
|---|---|---|
| Outbox dispatcher | continuous (poll 2 s) | Dispatch pending outbox messages to in-process handlers (notifications, FCM, etc.); retry with backoff; dead-letter after 10 attempts |
| Reservation expiry | every minute | Release expired reservations |
| Low stock check | hourly | Compare available vs reorder level; notify; create reorder suggestions |
| Expiry alerts | daily 06:00 tenant time | Batches expiring in 180/90/30 days → notifications |
| Batch auto-expire | daily 00:05 tenant time | Set `Expired` status on batches past expiry |
| Cleanup | daily | Idempotency records past expiry; revoked/expired refresh tokens > 30 days; read notifications > 90 days |
| Report exports | on demand (enqueued by API) | Large Excel exports to the file store; notify when ready |

The Worker sets the RLS bypass only to list tenants, then runs each job **per tenant** with that tenant's context.

---

## 16. Reports, imports, printing

- **Reports v1** (Reporting module, Dapper, read-only, permission per report, Excel export with `Reports.Export`): stock on hand (+ value with `Inventory.Cost.View`), stock movement/stock card, near-expiry, low stock vs reorder level, dead stock (no movement N days), sales by day/product/customer/branch, purchases by supplier/product, customer aging (0–30/31–60/61–90/90+), supplier balances, cash session (Z) report, margin by product (with cost permission).
- **Import centre** (`Catalog.Import` and module permissions): downloadable Excel templates for products (with units, conversions, barcodes, prices), opening stock (warehouse, SKU, batch, expiry, qty, unit cost), customers, suppliers, price list items. Flow: upload → validate every row → preview with row-level errors → commit all-or-nothing (≤ 20,000 rows per file; larger ⇒ background job). Opening stock import creates and posts one `OPENING` stock document.
- **PDF (QuestPDF):** invoice, counter receipt (80 mm), purchase order, goods receipt, sales return, customer statement. Branch name/address and a tenant logo (attachment) in the header.

---

## 17. Hosting, configuration, logging

- **IIS:** API site (in-process, app pool "No Managed Code", AlwaysRunning, preload, idle timeout 0, WebSockets on); admin site (static Angular). Worker installed as a Windows Service (`UseWindowsService()`), never inside IIS.
- **Configuration:** `appsettings.json` (no secrets) + environment variables for secrets: `ConnectionStrings__Main`, `Auth__SigningCertificatePath`, `Auth__SigningCertificatePassword` (or `Auth__SigningCertificateThumbprint`), `Files__RootPath`, `Firebase__CredentialsPath`, `Seq__Url`, `DataProtection__KeysPath`. Startup validates all required settings and fails fast with a clear message.
- **Connection string** uses the `app_runtime` login (script 010), `Encrypt=True;TrustServerCertificate=False` (install a proper certificate on SQL Server, or use `TrustServerCertificate=True` only while SQL Server is on the same machine and listening locally), `Application Name=StockOrder.Api`.
- **Logging:** Serilog → Seq and rolling files (14 days). Never log passwords, tokens, PINs, full request bodies of auth endpoints, or personal data beyond ids. Request logging with correlation id, tenant id, user id, elapsed ms.
- **Security headers** on the API and admin site: HSTS, `X-Content-Type-Options`, `Referrer-Policy`, CSP for the admin site; CORS allows only the admin site origin.
- **Rate limiting** (ASP.NET Core rate limiter): login 10/min per IP + per username; general 300/min per user.
### 17.1 GitHub repository and CI/CD

The code lives in **one private GitHub repository** (this layout). The owner has no Android Studio or Android SDK locally, so **all Android builds happen in GitHub Actions**.

- **Never commit secrets:** `.gitignore` covers `appsettings.*.local.json`, `.env*`, `*.pfx`, `*.jks`, `*.keystore`, `key.properties`, `google-services.json`, `GoogleService-Info.plist`, `firebase-*.json`, `build/`, `bin/`, `obj/`, `node_modules/`. Add a secret-scanning step (gitleaks) to CI.
- **Workflows** (`.github/workflows/`), each triggered only when its folder changes, plus manual `workflow_dispatch`:
  - `backend.yml`: restore, build, unit + architecture tests, integration tests against SQL Server 2019 via Testcontainers, `dotnet publish` (win-x64, framework-dependent) for Api and Worker uploaded as artifacts.
  - `web-admin.yml`: install, lint, unit tests, production build uploaded as an artifact.
  - `mobile.yml`: Flutter (version pinned in the workflow), `flutter analyze`, `flutter test`, then **`flutter build apk`** uploaded as an artifact named `app-<flavor>-<version>.apk`.
- **Mobile build inputs** come from GitHub settings, never from the repo:
  - Repository **variable** `API_BASE_URL` → passed with `--dart-define=API_BASE_URL=...`.
  - Secret `GOOGLE_SERVICES_JSON_BASE64` → decoded to `android/app/google-services.json` at build time. If absent, the build still succeeds with Firebase disabled (push off); the app must not crash without it.
  - Secrets `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD` → release signing. If absent, build a **debug-signed** APK instead of failing.
- Two flavors: `dev` (debug-signed, app name suffix "Dev", may allow HTTP to a LAN address for testing) and `prod` (release-signed, HTTPS only). Both can be installed side by side (different application ids).
- `/docs/mobile-build.md` explains for a non-developer: where to set the variables/secrets, how to create the signing keystore once (a one-off workflow `create-keystore.yml` that generates it and shows how to store it as secrets), how to download the APK from a workflow run, and how to install it on an Android phone.
- Keep CI within GitHub's free minutes: cache Gradle, pub, NuGet and npm; skip workflows whose folders did not change.

- **Deployment docs** in `/docs/operations.md`: install steps (Hosting Bundle, IIS features, sites, service), environment variables, running SQL scripts, backup plan (full daily, differential hourly, log every 15 min, copied off the machine), restore drill, upgrade procedure (backup → run new scripts → deploy API/Worker → deploy admin site → smoke test).

---

## 18. Phases and acceptance criteria

Build in this order. **Stop after each phase**, report what was built, test results, deviations and open questions, and wait for approval.

### Phase 0 — Foundations
Build:
- Repo layout, `.gitignore`, solution, central package management, analyzers, GitHub Actions workflows (section 17.1).
- SharedKernel; Infrastructure: connection factory with RLS session context (+ bypass factory), unit of work, EF base configuration, audit interceptor (writes `platform.AuditLog`), outbox writer + Worker dispatcher, idempotency filter, number series service, ProblemDetails mapping, correlation id, Serilog, health checks, schema-version check, HybridCache, file store.
- `012_Hangfire.sql`.
- Tenancy + Identity + Platform modules: bootstrap CLI, tenant provisioning with Restaurant and Pharma templates, login/refresh/logout/change-password/me, lockout, MFA (TOTP), permission policies, users/roles/permission matrix endpoints, devices, feature flags, settings, number series endpoints.
- Angular shell: login (+MFA), change password, layout with permission/feature-driven menu, users, roles & permission matrix, branches/warehouses, feature toggles, platform tenants screen.
- Flutter shell: login, secure storage, biometric unlock, device registration, Drift database with encryption, sync engine skeleton (pull settings/permissions/features; push queue with one sample command), version gate, revoke/wipe handling.
- Architecture tests and the mandatory security tests (section 19).

Done when:
- [ ] Scripts 000–012 run cleanly on an empty database; API starts and `/health/ready` is healthy.
- [ ] Bootstrap creates the platform admin; platform admin creates one Restaurant and one Pharma tenant; owners can log in on web (MFA enrolment) and mobile.
- [ ] Cross-tenant tests pass (EF **and** raw Dapper see zero rows of another tenant; inserting another tenant's id fails).
- [ ] Every endpoint has an authorization policy (test passes).
- [ ] Refresh-token reuse revokes the family; lockout works; changing roles invalidates permissions immediately.
- [ ] Role → permission mapping for both templates is shown to the user for confirmation.
- [ ] All three GitHub Actions workflows pass; the `mobile` workflow produces a downloadable APK that installs on an Android phone and can log in to the user's server (URL from the `API_BASE_URL` variable).
- [ ] `/docs/mobile-build.md` exists and was followed successfully by the user.

### Phase 1 — Core MVP
Build: Catalog, Inventory, Purchasing, Sales (incl. counter sale), Finance, Sync (all v1 entities and command types), Reporting v1, Import centre, PDFs, all Angular v1 screens, all Flutter v1 screens (except Phase 2 items), background jobs (section 15).

Done when:
- [ ] Ledger rebuild equals balances after a scripted scenario of receipts, transfers, counts, sales, returns.
- [ ] 20 parallel confirmations for the last 5 units ⇒ exactly 5 succeed, no negative available stock.
- [ ] FEFO allocation picks the earliest-expiring active batch; blocked/recalled/expired batches are never reserved.
- [ ] Same `Idempotency-Key` twice ⇒ one document; same sync command id twice ⇒ one document.
- [ ] Offline on the phone: create a sales order and a GRN in airplane mode, reconnect, both appear on the server with correct numbers; local and server stock agree after pull.
- [ ] Moving-average cost matches hand-calculated values in tests (incl. free units and discounts).
- [ ] Invoice numbers are gap-free under concurrent posting with forced rollbacks.
- [ ] Posting into a closed period fails with `PERIOD_CLOSED`.
- [ ] Import of 5,000 products and opening stock completes and is all-or-nothing on error.
- [ ] Playwright e2e (web): login → create product → GRN → sales order → dispatch → invoice → payment.
- [ ] One pilot tenant per vertical can run daily operations end-to-end (user acceptance by the owner).

### Phase 2 — Vertical packs
- **Restaurant** (new script `013_RestaurantPack.sql`): dining areas/tables (status), modifier groups/modifiers (price delta, ingredient add/remove), product–modifier links, kitchen stations and product–station mapping, kitchen tickets (+lines, status Queued/Preparing/Ready/Served), `sales.SalesOrder` gains `TableId`, `Covers`, `ServerUserId`. Dine-in flow (open order on a table, add rounds, each round fires tickets via SignalR to the kitchen tablet and/or network printer), bill/split bill/service charge, recipe back-flush on sale (`Consumption` entries for components × (1 + wastage %)), void/refire with reason and PIN, theoretical vs actual consumption report, Bluetooth receipt printing.
- **Pharma** (new script `014_PharmaPack.sql`): schemes (bonus qty e.g. 10+1, slab discounts) with validity and product/category scope, scheme application in pricing, customer minimum expiry days on delivery (real column), routes and route customers (visit days, sequence), rep's route plan on mobile, recall workflow (mark batch Recalled → block, reallocate reservations, list customers who received it from the ledger, recall return document), expiry alerts and near-expiry return to supplier, controlled-drug flag enforcement (online-only, restricted roles).

Done when each pack's flows run end-to-end on web and mobile for its pilot tenant, and enabling/disabling the pack's features changes menus and behaviour without errors.

---

## 19. Testing requirements

- **Unit tests** (xUnit + Shouldly + NSubstitute): state machine transitions, UoM conversion and rounding, pricing resolution and line maths, moving-average cost, FEFO/FIFO allocation ordering, credit check, scheme calculation (Phase 2).
- **Integration tests** against **real SQL Server**: CI uses Testcontainers (`mcr.microsoft.com/mssql/server:2019-latest`); locally an instance from `TEST_SQL_CONNECTION`. The fixture creates a fresh database, runs scripts `000`–latest exactly as the user would (same files), creates the `app_runtime` login/user, and runs tests as that login (so RLS and DENY permissions apply). Respawn resets data between tests.
- **Mandatory tests:** cross-tenant isolation (EF + Dapper + insert), ledger rebuild, concurrency on last units, idempotency (REST + sync), gap-free numbering, authorization policy present on every endpoint, module dependency rules (NetArchTest), append-only tables reject UPDATE/DELETE.
- **Angular:** unit tests for services/interceptors; Playwright e2e for the Phase 1 flow.
- **Flutter:** unit tests for the sync engine (ordering, retries, idempotency, dependency rejection), outbox, offline limit; widget tests for login, sales order, GRN.
- Coverage target: domain + application layers ≥ 80 % lines.

---

## 20. Coding conventions

- C#: file-scoped namespaces, `sealed` by default, records for DTOs/commands, `CancellationToken` everywhere, async all the way, no `static` mutable state, no service locator. Warnings as errors.
- One command/query + handler + validator per use case, named `<Verb><Noun>Command` (e.g. `ConfirmSalesOrderCommand`).
- Money and quantities are `decimal`, never `double`. Use helper methods in SharedKernel for rounding.
- No raw string SQL concatenation with values — parameters only. Dapper SQL in `.sql`-like constants per module.
- Every write handler: authorize → validate → load (tenant-scoped) → domain operation → persist → outbox → audit.
- Commit messages: conventional commits. Small PR-sized commits per feature.
- Pin every NuGet, npm and pub package version. Before adding a new dependency, check its licence is MIT/Apache/BSD (or otherwise free for commercial use) and record it in `/docs/dependencies.md`.
- Ask before: adding a new package not named in this spec, changing a decision in section 2, or creating a schema change script.

---

## 21. Glossary

| Term | Meaning |
|---|---|
| Tenant | One customer company (a restaurant group or a pharma distributor) |
| Branch | A physical outlet/office of a tenant |
| Warehouse / Location | A stock-holding site of a branch / a place inside it (Storage, Quarantine, InTransit, Kitchen…) |
| Base unit | The smallest unit the ledger counts in (g, tablet, piece) |
| Batch | A lot of a product with optional expiry; untracked products use a default batch |
| Ledger | Append-only record of every stock (or party money) movement |
| Reservation | Stock promised to a confirmed order, not yet issued |
| FEFO / FIFO | First-expiry-first-out / first-in-first-out batch selection |
| GRN | Goods receipt note — stock entry from a supplier |
| Counter sale | A sale completed and paid immediately (restaurant counter, pharmacy counter) |
| Business date | The trading day a document belongs to (tenant time zone or cash session) |
| Industry template | JSON seed applied when a tenant is created |
| Pack | A vertical module (Restaurant, Pharma) enabled by feature flags |
