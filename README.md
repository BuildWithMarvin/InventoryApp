# InventoryApp

Proof-of-concept inventory management system: an ASP.NET Core Web API backed by SQL Server (Azure SQL in production), used by warehouse staff on shared PDA devices. Employees scan barcodes, check stock and update inventory; administrators manage tenants, devices and employees through Microsoft Entra ID.

## Project status

The project is in the middle of a **multi-tenancy and security refactor** (branch `ft/multi-tenancy`, not yet merged into `main`). The API has been turned from a single-tenant, unauthenticated service into a multi-tenant, authenticated one.

| Area | Status |
|---|---|
| Multi-tenant data model with tenant isolation (query filters, write guard, composite keys/FKs) | Done |
| Warehouse employee authentication (device enrollment, badge + optional PIN login, server-side sessions, JWT) | Done |
| Roles and permissions (per tenant, fixed permission catalog) | Done |
| Administrator authentication (Microsoft Entra ID, Tenant Admin and Platform Admin) | Done in code, tested with locally signed tokens only |
| Administration endpoints (tenants, devices, employees, roles, sessions, tenant administrators) | Done |
| Entra app registration, MFA/Conditional Access, deployment hardening, merge to `main` | Not started (Phase 5) |
| Endpoints for storage locations and stock +/- | Not built yet |
| React frontend | Not started |
| MAUI client | Removed |

Not yet verified: GitHub Actions results for the refactor, the Azure database/deployment, and a real Entra directory. See [`docs/PROJECT_CONTEXT.md`](docs/PROJECT_CONTEXT.md) for decisions, assumptions, open issues and the remaining phases.

## What the API does

* **Products:** look up, create and update products by internal barcode, with optimistic concurrency (HTTP 409 on a stale `RowVersion`).
* **Warehouse employee login:** a PDA is enrolled once with a one-time code and receives an HttpOnly device cookie. Employees then log in with their badge barcode and, if the tenant requires it, a six-digit PIN. Five wrong PINs lock the employee for 15 minutes.
* **Sessions:** 30-minute idle timeout, 12-hour absolute lifetime, one active session per device, revocable immediately. Permissions are read from the database on every request.
* **Multi-tenancy:** every tenant-owned entity carries a `TenantId` that comes from the validated session or device, never from the client. The database rejects cross-tenant references.
* **Roles:** each tenant gets `TenantAdmin`, `WarehouseManager` and `WarehouseEmployee` by default.
* **Administration:** Tenant Admins (`/api/admin`) manage their own tenant; Platform Admins (`/api/platform`) manage tenants and support tasks but never see inventory data.

## Architecture

* **Backend:** ASP.NET Core Web API (`net10.0`), EF Core, SQL Server. Controllers use `InventoryDbContext` directly and exchange DTOs, never entities. Swagger is only served in Development.
* **Frontend:** to be a React app (browser-based PDA client). It does not exist yet; the API stays frontend-agnostic (plain JSON over HTTP). The React app and the API need to be served from the same site because of the SameSite=Strict device cookie.
* **Hosting:** Azure Web App and Azure SQL. The CD pipeline runs on `main` only.

## Why React instead of .NET MAUI

The first client was written with .NET MAUI. It has been dropped in favour of a browser-based React frontend. The reasons:

* **The MAUI app kept crashing on Android.** The app crashed repeatedly at startup on the target Android devices. The cause was traced to Android's move from 4 KB to 16 KB memory page sizes (Android 15): native libraries (.so files) that are not aligned for 16 KB page sizes make the app fail on such devices. Microsoft documents this as warning XA0141, and it can only be fixed by updating or replacing the affected native dependencies. A client whose stability depends on the alignment of third-party native binaries is a risk for devices that warehouse staff rely on every day.
* **A web client has no native layer.** A React app runs in the browser, so there are no native libraries to align and no Android-version-specific build problems on our side.
* **It fits how the API is already designed.** The device enrollment uses an HttpOnly, SameSite=Strict cookie, which requires the client and the API to be served from the same site. A browser client served together with the API satisfies this naturally. The API stays frontend-agnostic (plain JSON over HTTP).
* **The MAUI client no longer matched the API.** The multi-tenancy and authentication refactor changed the API fundamentally (device enrollment, badge/PIN login, sessions, tenant isolation). The MAUI client would have needed a rewrite anyway, so little was lost by switching.
* **Simpler rollout on shared devices.** Updates ship by deploying the web app; there is no app package to build, sign and install on every PDA.

The decision does not change the backend: it stays ASP.NET Core on .NET 10 with EF Core and SQL Server.

## Testing

The backend has an xUnit suite of 411 tests (unit tests plus integration tests against a real SQL Server, not EF Core In-Memory). They cover:

* Product retrieval, updates, validation and optimistic concurrency
* Tenant isolation at query, write and database-constraint level
* Device enrollment, login, PIN lockout, sessions and token forgery
* Authorization of every endpoint (a guardrail test fails on unmarked or newly anonymous endpoints)
* Entra administrator authentication and the rule that administrators never reach inventory data
* The EF Core migrations, from an empty database and from older schema states

All 411 tests passed locally on 2026-11-12 against SQL Server 2019 in Docker. CI runs them against SQL Server 2022; those runs have not been verified for the refactor.

## Next steps

* **Phase 5:** Entra app registration and app roles, MFA via Conditional Access, signing key management, same-site hosting decision for the React app, CI verification, then merge to `main` (with approval; this applies the reset migration to Azure).
* **Storage locations and stock endpoints:** needed for the "stock +/-" workflow.
* **React frontend:** barcode login, camera scanning, product creation for unknown barcodes, stock changes, local search.
* **Larger datasets:** API-side pagination so clients don't have to load thousands of items.
* **Dashboard & analytics:** total stock value and a log of recent stock movements for managers.
* **Offline resilience:** basic caching/syncing for unreliable warehouse Wi-Fi.

## Running it locally

You need the .NET 10 SDK and a SQL Server instance.

1. Copy `InventoryApp.Api/appsettings.example.json` to `appsettings.json` and set `ConnectionStrings:DefaultConnection`.
2. Set the session signing key (at least 32 characters; the app refuses to start without it):
   `dotnet user-secrets set "Auth:SigningKey" "<random>" --project ./InventoryApp.Api`
3. Optionally fill the `Entra` section to enable administrator sign-in. Without it, every Entra token is rejected.
4. Run `dotnet run --project ./InventoryApp.Api`. Migrations are applied on startup; Swagger is available in the Development environment.

Tests: `dotnet test ./InventoryApp.Tests --filter "Category=Unit"` needs no database. Integration tests need SQL Server and drop the database they use, so point them at a throwaway one (see `CLAUDE.md`).

---

*Built by Marvin*
