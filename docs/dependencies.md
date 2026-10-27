# Third-party dependencies

Section 20: every dependency is pinned to an exact version and checked for a commercial-
friendly licence before being added. Update this file whenever a package is added, removed,
or its major version changes.

## Backend (NuGet — see `backend/Directory.Packages.props` for exact pins)

| Package | Licence | Notes |
|---|---|---|
| Microsoft.EntityFrameworkCore.* | MIT | Data access |
| Microsoft.Data.SqlClient | MIT | |
| Dapper | Apache-2.0 | Reports, hot paths |
| Mediator.Abstractions / .SourceGenerator | MIT | CQRS mediator (martinothamar) — not MediatR |
| FluentValidation(.DependencyInjectionExtensions) | Apache-2.0 | |
| Riok.Mapperly | Apache-2.0 | Source-generated mapping |
| Hangfire.Core / .SqlServer / .AspNetCore | LGPL-3.0-or-later | Explicitly named in the spec; free/open source |
| Microsoft.Extensions.Caching.Hybrid | MIT | In-memory `HybridCache` |
| Microsoft.AspNetCore.Authentication.JwtBearer | MIT | |
| Microsoft.IdentityModel.Tokens / System.IdentityModel.Tokens.Jwt | MIT | |
| Otp.NET | MIT | TOTP MFA |
| FirebaseAdmin | Apache-2.0 | Push (Phase 1+) |
| ClosedXML | MIT | Excel import/export |
| QuestPDF | QuestPDF Community licence: free for companies below USD 1M annual revenue (above that a paid licence is needed) | PDF generation: the 80 mm counter receipt |
| Roboto, Noto Nastaliq Urdu (font files, embedded in the Sales module) | Apache-2.0, SIL OFL 1.1 | Receipt text, Latin and Urdu |
| Microsoft.AspNetCore.OpenApi | MIT | |
| Scalar.AspNetCore | MIT | Dev-only API explorer |
| Serilog.AspNetCore / .Sinks.Seq / .Sinks.File | Apache-2.0 | |
| OpenTelemetry.* | Apache-2.0 | |
| AspNetCore.HealthChecks.SqlServer | Apache-2.0 | |
| Roslynator.Analyzers | Apache-2.0 | Build-time analyzer only |
| xunit, xunit.runner.visualstudio | Apache-2.0 | |
| Microsoft.NET.Test.Sdk | MIT | |
| Shouldly | BSD-3-Clause | |
| NSubstitute | BSD-3-Clause | |
| NetArchTest.Rules | MIT | |
| Testcontainers.MsSql | MIT | |
| Respawn | MIT | |
| Microsoft.AspNetCore.Mvc.Testing | MIT | |
| coverlet.collector | MIT | |
| Microsoft.Extensions.Hosting.WindowsServices | MIT | Worker host |

**Explicitly not used** (per section 2): MediatR, AutoMapper, MassTransit, FluentAssertions
(all commercial-licence-restricted for this kind of use).

## Back office (npm — see `web-admin/package.json`)

| Package | Licence |
|---|---|
| @angular/* | MIT |
| @jsverse/transloco | MIT |
| @angular/material, @angular/cdk | MIT |
| ag-grid-angular, ag-grid-community | MIT (Community edition) |
| rxjs | Apache-2.0 |
| eslint, @eslint/js, angular-eslint, typescript-eslint | MIT |
| Inter, JetBrains Mono fonts (Google Fonts, linked from `index.html`, not an npm package) | SIL Open Font License 1.1 |

**Deviation from CLAUDE.md section 2**: PrimeNG was named in the spec, but its licensing
model changed since the spec was written — even the "Community" tier now requires
registering for a license key (eligibility caps on revenue/team size, annual renewal) or
it shows an "Invalid License" watermark. Swapped for Angular Material (MIT, no key, no
caps, maintained by the Angular team) after confirming with the user. AG Grid Community,
the other UI dependency named in the spec, is still genuinely free MIT — kept as-is.

## Mobile (pub.dev — see `mobile/pubspec.yaml`)

| Package | Licence |
|---|---|
| flutter_riverpod, riverpod_annotation/_generator | MIT |
| go_router | BSD-3-Clause |
| dio | MIT |
| drift, drift_dev, sqlite3 | MIT |
| flutter_secure_storage | BSD-3-Clause |
| mobile_scanner | Apache-2.0 |
| qr_flutter, qr (transitive) | BSD-3-Clause (customer menu QR; approved 2026-10-27) |
| Noto Nastaliq Urdu (font file, not a package) | SIL OFL 1.1 (`mobile/assets/fonts/OFL.txt`; Urdu UI) |
| local_auth | BSD-3-Clause |
| firebase_core/_messaging/_crashlytics | BSD-3-Clause |
| path_provider, path | BSD-3-Clause |
| uuid | MIT |

## Before adding a new dependency

1. Check the licence is MIT / Apache-2.0 / BSD (or otherwise explicitly free for
   commercial use without per-seat/per-revenue fees, like QuestPDF's Community tier).
2. Pin the exact version in the relevant central file (`Directory.Packages.props`,
   `package.json`, `pubspec.yaml`) — never a floating range.
3. Add a row to this file.
4. If it's not already named in `CLAUDE.md` section 2, ask before adding it (section 20).
