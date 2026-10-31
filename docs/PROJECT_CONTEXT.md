# InventoryApp: multi-tenancy and security refactor, project context

Last updated: 2026-10-31, after Phase 3 (commit `f0d071d`).
Purpose: a standalone starting context for a new Claude Code session. Read this first, then `CLAUDE.md` (conventions, commands, test rules). `CLAUDE.md` describes how the code works today; this file records **why**, what was **decided by whom**, and **what is left**.

## How to read this document

Every statement about a decision carries one of three labels. Do not promote a lower label to a higher one without asking the user.

| Label | Meaning |
|---|---|
| **CONFIRMED** | The user stated it explicitly. Change it only if the user says so. |
| **ASSUMPTION** | Claude proposed or chose it. The user told Claude to use best practices and reasonable assumptions, and to proceed, but did not confirm the item individually. Implemented where noted. Each can be revisited. |
| **OPEN** | Unresolved, unverified or not yet decided. Do not resolve silently; raise it when it becomes relevant. |

## 1. Project in one paragraph

Proof-of-concept inventory system: ASP.NET Core Web API (`net10.0`, EF Core, SQL Server, Azure SQL in production) used by warehouse staff on shared PDA devices. The MAUI client (`InventoryApp.Maui`) is legacy and **will be replaced by a React frontend** (CONFIRMED earlier; no React code exists yet; do not invest in MAUI). The API must stay frontend-agnostic (plain JSON over HTTP). The ongoing work turns the single-tenant, unauthenticated API into a secure, multi-tenant system. Projects: `InventoryApp.Api`, `InventoryApp.Tests`, `InventoryApp.Maui` (not in the solution).

## 2. Working agreements with the user (CONFIRMED)

- Work in **small, independently testable phases**. Before each phase, briefly state its scope and intended outcome.
- Ask questions **only** when a decision fundamentally changes the architecture, has significant security implications, is hard to reverse, or needs a business decision that cannot reasonably be inferred. Otherwise decide on best practice, document the assumption briefly, and continue. No long lists of open questions.
- **Commit locally at the end of each phase** (explicitly confirmed). **Never push or merge without the user's approval.** The user asked for pushes explicitly after Phases 1, 2 and 3; do not assume standing permission.
- Do not invent decisions or silently resolve open questions (this document's own rule).
- Preserve existing functionality where possible; avoid unnecessary abstractions and overengineering.
- Commit messages end with the attribution line `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` (from the session's configuration).

## 3. Original requirements (CONFIRMED)

**Two authentication mechanisms.**
- *Warehouse employees:* shared PDAs; device identified by barcode; employee identified by personal badge barcode; optional employee PIN; backend-managed sessions; signed JWT access tokens; server-side session validation and revocation.
- *Administrators and IT:* Microsoft Entra ID; role-based authorization; Entra for privileged administrative operations.
- Employees do not necessarily have Entra accounts.

**Multi-tenancy from the start.** `Tenant` entity; employees, devices, products, roles and sessions belong to a tenant; strict isolation at database and API level; **never trust a TenantId (or EmployeeId) from the client**; tenant-specific role assignments and permissions; database constraints that prevent cross-tenant references.

**Separation of concerns:** authentication, tenant resolution, employee identification, role-based authorization and device authentication are separate. Employee roles live in the InventoryApp database; Entra manages privileged admin identities, not warehouse employees.

**Security:** derive employee identity from the validated session token; validate device identity; session expiry and revocation; brute-force protection for PINs; PINs hashed with a suitable algorithm; every protected endpoint enforces authorization; no insecure direct object references or cross-tenant access.

## 4. Confirmed decisions (CONFIRMED)

| # | Decision |
|---|---|
| D1 | `Product` key is the composite `(TenantId, InternalBarcode)`. Preserve the existing URL structure (routes still use only the barcode). |
| D2 | A device has a public `DeviceCode` and a separate high-entropy secret. Only a secure hash of the secret is stored. The device barcode is **never** authentication by itself. |
| D3 | PIN policy is a per-tenant `RequirePin` setting. If on, every employee must use a PIN. PIN verification is enforced server-side. |
| D4 | Sessions: 30-minute idle timeout, 12-hour absolute lifetime, revocable immediately. |
| D5 | The Azure SQL database holds no relevant application data (the user checked it). The old schema is disposable; no backfill/data-preservation logic; the database is initialised by the new EF Core migrations; the existing integration test setup is kept; the complete migration process must be tested against SQL Server before deployment. |
| D6 | Separate **Platform Admin** and **Tenant Admin** roles, both authenticating through Microsoft Entra ID. Platform Admins manage the platform and tenants; Tenant Admins manage only their own tenant. |
| D7 | Entra: a single directory for the platform; external Tenant Admins are invited as B2B guest users. A multi-tenant app registration may be considered later. |
| D8 | Platform Admins may manage devices, employees and sessions inside tenants (support). They must **never** access inventory data (products, stock). |
| D9 | One active employee session per device; a new badge login revokes the previous session on that device. |
| D10 | The PDA client is a browser React app. A DeviceSecret must **not** be stored in browser-accessible storage or exposed in client-side code. Device identification and employee authentication stay separate concerns. (Claude was asked to propose the mechanism; see A3.) |
| D11 | PINs are six digits. A Tenant Admin sets the initial PIN; employees can change their own later. Only a secure hash is stored; protect against repeated failed attempts. |
| D12 | Every new tenant gets three default roles: **TenantAdmin** (full administrative control of the tenant: employee management, role assignments, permissions, tenant settings), **WarehouseManager** (manages products, storage locations, inventory, stock adjustments; cannot manage employees, roles or permissions), **WarehouseEmployee** (day-to-day warehouse operations according to assigned permissions). **Only TenantAdmin may manage employees, roles and permissions.** PlatformAdmin is a separate platform-wide role. |
| D13 | Do not manually delete or reset the Azure database. Assess whether the migration history needs re-baselining; prefer a controlled, migration-based approach. If a manual Azure operation ever becomes absolutely necessary, explain why and wait for approval. |
| D14 | Branching: `refactor/use-dtos` was to be inspected and verified (done: DTOs complete, tests pass), then **the user** merges it into `main`, then a new feature branch is created from the updated `main`. Nothing is merged or pushed without approval. (See A12 and O1 for what actually happened.) |
| D15 | Swagger is disabled in Production and enabled in Development. It never replaces authentication and authorization. **Not yet implemented** (planned for Phase 4). |
| D16 | The MAUI client is legacy; keep the API frontend-agnostic; ask before assuming the React stack or location. |

## 5. Assumptions adopted under delegation (ASSUMPTION)

Claude made or proposed these; the user said to use best practices and proceed. "Impl." says whether the code reflects it today.

| # | Assumption | Impl. |
|---|---|---|
| A1 | The `TenantAdmin` role has **admin scope** and is held by Entra identities only, never by badge employees. Admin-scope permissions are never usable with a badge login. It holds all admin permissions and no inventory permissions. | Role and DB constraints: yes. Entra side: no |
| A2 | Reading of D8 against D12: a Platform Admin may manage employees (including role assignment and PIN reset) but may **not** edit role definitions or permissions (only a TenantAdmin may). | No (Phase 4) |
| A3 | Device authentication for a browser PDA: the device credential is a server-issued random 256-bit token delivered **only as an HttpOnly, Secure, SameSite=Strict cookie** (`inv_device`, Path `/api/auth`, 400 days). Only its SHA-256 is stored. Enrollment is a one-time 130-bit code (24 h) redeemed at `POST /api/auth/enroll`. | Yes |
| A4 | **The React app and the API are served from the same site** (preferably same origin), because A3 needs it; this also removes the need for CORS. | Not decided. See O2 |
| A5 | The employee access token is kept **in memory only** by the client (a page reload means scanning the badge again). | Client does not exist |
| A6 | Default permission sets: WarehouseManager has all inventory permissions; WarehouseEmployee has `Products.Read`, `Locations.Read`, `Stock.Read`, `Stock.Adjust`. Consequence: a WarehouseEmployee **cannot create products**, so the old MAUI flow "create a product when a scanned barcode is unknown" is manager-only. | Yes |
| A7 | An admin-set PIN is flagged `MustChangePin`; until replaced, an employee can only call `logout` and `change-pin`. Changing the PIN ends the employee's other sessions. | Yes |
| A8 | PIN rules: 5 wrong attempts lock the employee for 15 minutes; a locked-out employee gets HTTP 423 with `Retry-After` (disclosed on purpose, since the caller already holds an enrolled device); BCrypt work factor 12; a supplied PIN is ignored if the tenant does not require one; an employee without a PIN cannot log in where a PIN is required; `change-pin` needs the current PIN only if one exists and counts wrong attempts like a login. | Yes |
| A9 | All refusals at login look identical (401), except `pin_required` and the lockout. A dummy BCrypt check equalises timing for unknown badges. | Yes |
| A10 | Rate limits per client IP (fixed window): login and change-pin 30/min, enroll 5/min. They are flood control; the lockout is the real PIN defence. The IP comes from `X-Forwarded-For` (all proxies trusted, as on App Service). | Yes |
| A11 | Token design: HS256 with a symmetric key from `Auth:SigningKey` (min. 32 characters, app refuses to start otherwise); claims `sub`, `inv_tenant`, `sid`, `did`; `exp` equals the session's absolute end; no refresh tokens; permissions are read from the database on every request, never stored in the token; `LastSeenAt` is updated at most once a minute. The tenant claim is named `inv_tenant` to avoid confusion with Entra's `tid`. | Yes |
| A12 | **Deviation from D14:** `ft/multi-tenancy` was created from the **tip of `refactor/use-dtos`**, not from an updated `main`, because the merge had not happened. Content is identical to what `main` will be after a fast-forward. | Done; see O1 |
| A13 | One feature branch (`ft/multi-tenancy`) with one commit per phase; merged to `main` only at the end. | Yes |
| A14 | Migration strategy: keep the existing migration history (no re-baseline, no squash) and add one explicit **reset migration** (`MultiTenantSchema`) that drops the four old tables and creates the new schema; `Down` is deliberately unsupported; no row-count guard. Later phases add normal additive migrations. No manual Azure operation is needed. | Yes |
| A15 | Employees are deactivated, never deleted (composite FKs to `Employee` use `NoAction`, because SQL Server cannot null one column of a composite FK). A role that is in use cannot be deleted. | Yes |
| A16 | Planned Entra design (Phase 4/5): Entra app roles `Platform.Admin` and `Tenant.Admin`; a `TenantAdmin` table `(TenantId, EntraObjectId, ...)` links a Tenant Admin to a tenant; admin endpoints under `/api/admin`; for tenant-scoped admin routes the route's tenant id is a *selector* that is authorized first and only then bound to `TenantContext`; enabling `RequirePin` is rejected while active employees lack a PIN; the first Platform Admin is assigned in the Entra portal (no database seed). | No |
| A17 | `POST /api/employees` is guarded by the placeholder policy `AdminOnly` (denies everyone) until the Entra scheme exists, so employee creation is currently unreachable over HTTP. | Yes (temporary) |

## 6. Open issues (OPEN)

| # | Issue |
|---|---|
| O1 | `refactor/use-dtos` (29 commits ahead of `main`, tip `486bb14`) is **not merged into `main`**; `main` is still `1ed7503`. It contains much more than DTOs (CI/test infrastructure, two additive migrations, removal of MAUI from the `.sln`, `CLAUDE.md`). Whether to merge it by PR or directly is the user's call. Merging to `main` triggers the CD pipeline (migrate Azure, then deploy). `ft/multi-tenancy` contains it as ancestor. |
| O2 | **Same-site hosting for the React app (A4) is not decided.** If React must live on another site, SameSite=Strict cookies will not be sent and the device-authentication design (A3) has to change. Custom domain, same App Service, or a proxy are the options. |
| O3 | CI results for the three pushes (Phases 1 to 3) were never verified: `gh` is not installed locally. The new "Verify EF Core migrations" CI step has never run on GitHub. |
| O4 | The real state of the Azure database is unverified by Claude (no access): its migration history (probably only `InitialCreate`) and contents are known only from the user's statement (D5). The reset migration irreversibly drops the old tables when applied. Between the migration and the deployment, CD leaves the old API running against the new schema for a few minutes (accepted for a PoC with no clients; not explicitly confirmed). |
| O5 | Entra specifics are undecided: app registration, audience and scopes, how B2B guests get the app roles, Conditional Access, how the `TenantAdmin` mapping rows are created, token validation settings. |
| O6 | Signing-key management in Azure (Key Vault reference vs app setting) and rotation (a key change ends all sessions). |
| O7 | `Program.cs` runs `Migrate()` at startup, so the app's database login needs DDL rights. Whether to separate a migration identity is undecided. |
| O8 | A1, A2, A6 (the exact meaning of the TenantAdmin role, Platform Admin's reach over role definitions, and the default permission sets) have not been confirmed individually. |
| O9 | The API still lacks endpoints for storage locations, stock, roles, devices and sessions. The permissions exist in the catalog; the endpoints do not. The old "stock +/-" feature has no endpoint. |
| O10 | Residual risks: a burst of parallel PIN guesses before the lockout lands is bounded only by the rate limit; the in-process rate limiter is per instance; `EmployeeSession` rows are never purged. |
| O11 | The React frontend does not exist (stack and location unknown). The MAUI client no longer matches the API (login, `EmployeeId`, DTOs) and is intentionally not updated. |
| O12 | `README.md` is outdated (still mentions In-Memory testing). |
| O13 | Production role values were never inspected (no access); treated as moot under D5. If real roles existed, nothing carries over: the old `Employee.Role` column was dropped. |

## 7. Target architecture and what exists

### 7.1 Data model (implemented, migrations 4 to 6)

Every tenant-owned entity implements `ITenantOwned` (has `TenantId`). Every key and foreign key includes `TenantId`, so the **database itself** rejects cross-tenant references. `Employee`, `StorageLocation`, `Role` and `Device` expose `(TenantId, Id)` as alternate key for this.

| Entity | Notes |
|---|---|
| `Tenant` | Id, Name (unique), IsActive, RequirePin. Not tenant-filtered. |
| `Employee` | Unique `(TenantId, BadgeBarcode)`; IsActive; PinHash (BCrypt), MustChangePin, FailedPinAttempts, LockoutEnd; `RowVersion` (concurrency-safe PIN failure counting). |
| `Role`, `RolePermission`, `EmployeeRole` | Per tenant. A role has an immutable **scope** (Inventory or Admin) and may only hold permissions of that scope. `EmployeeRole` references `(TenantId, RoleId, RoleScope)` and a check constraint fixes `RoleScope = 0` (Inventory): the database refuses admin roles for employees. Unique role name per tenant. |
| `Device` | Public `DeviceCode` (globally unique, 10 chars), `CredentialHash`, enrollment code hash and expiry, IsActive, `RowVersion`. |
| `EmployeeSession` | Guid Id; employee, device, CreatedAt, LastSeenAt, ExpiresAt, RevokedAt/RevokedReason. **Unique filtered index** on `DeviceId` where `RevokedAt IS NULL`: one active session per device. |
| `Product` | Composite PK `(TenantId, InternalBarcode)`; `RowVersion` concurrency token (unchanged behaviour). |
| `StorageLocation`, `ProductStock` | Unique `(TenantId, Barcode)` and `(TenantId, ProductInternalBarcode, StorageLocationId)`. |

### 7.2 Tenant isolation at the API level

`TenantContext` (scoped) holds the request's tenant. It is set only from validated credentials: the device at login/enroll, or the stored session during token validation. `InventoryDbContext` applies a global query filter on every tenant-owned entity (no tenant set means no rows, fail closed) and a `SaveChanges` guard that refuses writes for any other tenant and any change of an entity's tenant. `IgnoreQueryFilters()` is used only on the pre-tenant lookups (device by code, enrollment code, session by id).

### 7.3 Warehouse employee authentication (implemented)

1. **Enroll.** An admin creates a `Device` and a one-time enrollment code. `POST /api/auth/enroll` redeems it and sets the credential cookie (A3). Issuing a new code immediately invalidates the old cookie.
2. **Login.** `POST /api/auth/login` `{deviceCode, badgeBarcode, pin?}` plus the cookie. Order: device (code plus cookie) then tenant (from the device) then employee (badge, PIN if required, lockout) then session (previous session on the device revoked, in a transaction) then JWT.
3. **Every request.** `EmployeeAuthentication.ValidateSessionAsync` loads the session and checks: not revoked, within idle and absolute limits, employee/device/tenant active, token claims match the stored session. It then sets `TenantContext` and adds the employee's permissions from the database as claims. A revocation, deactivation or role change applies on the next request.
4. **Authorization.** One policy per inventory permission (`[Authorize(Policy = Permissions.X)]`), `Policies.EmployeeSession`, `Policies.AdminOnly` (placeholder), `[AllowAnonymous]` only on `enroll` and `login`, plus a default-deny fallback policy. `EndpointGuardrailTests` fails on unmarked actions or new anonymous endpoints.
5. Other endpoints: `POST /api/auth/logout`, `POST /api/auth/change-pin`.

### 7.4 Administrator authentication (NOT implemented, planned for Phase 4)

Microsoft Entra ID via JWT bearer, app roles `Platform.Admin` and `Tenant.Admin` (A16), `/api/admin/...` endpoints, tenant-scoped policy. The tenant of an admin call is derived from an authorization check against the `TenantAdmin` mapping, never trusted from the route alone.

### 7.5 Other design facts

Controllers take `InventoryDbContext` directly (no service/repository layer); DTOs are records in `Dtos/`; responses never contain `BadgeBarcode`; requests never contain `EmployeeId` or `TenantId`; the employee of a write is `User.GetEmployeeId()`.

## 8. Role definitions (D12 plus A1/A6)

Permission catalog (`Security/Permissions.cs`). **Inventory scope** (badge employees): `Products.Read`, `Products.Write`, `Locations.Read`, `Locations.Manage`, `Stock.Read`, `Stock.Adjust`. **Admin scope** (Entra only): `Employees.Manage`, `Roles.Manage`, `Devices.Manage`, `Sessions.Revoke`, `Tenant.Settings`.

| Role (per tenant) | Scope | Permissions | Notes |
|---|---|---|---|
| `TenantAdmin` | Admin | all admin permissions | System role: not editable, not deletable (D12, A1). |
| `WarehouseManager` | Inventory | all inventory permissions | Editable. |
| `WarehouseEmployee` | Inventory | `Products.Read`, `Locations.Read`, `Stock.Read`, `Stock.Adjust` | Editable (A6). |
| Platform Admin | n/a | not a database role; Entra app role `Platform.Admin` | Reach per D8 and A2. |

## 9. Security requirements and where they are met

| Requirement | Status |
|---|---|
| Never trust client TenantId/EmployeeId | Implemented and tested (tenant from device/session; employee from session; extra fields in bodies ignored). |
| Derive identity from validated session token | Implemented. |
| Validate device identity | Implemented (cookie credential, hashed; public code only identifies). |
| Session expiry and immediate revocation | Implemented (per-request database check). |
| PIN brute-force protection | Implemented (lockout, concurrency-safe counter, rate limit); residual risk in O10. |
| PIN hashing | Implemented (BCrypt). |
| All protected endpoints enforce authorization | Implemented for the employee scheme (policies, fallback, guardrail test); admin scheme pending. |
| No IDOR / cross-tenant access | Implemented at query, write and database-constraint level; tests cover each. |
| Entra ID for privileged operations | **Pending (Phase 4).** |
| Swagger off in Production | **Pending (Phase 4).** |

## 10. Database and migration strategy

- Migrations in order: `InitialCreate`, `UpdateInventoryModel`, `AdjustProductPricePrecision` (all pre-existing), then this work's `MultiTenantSchema` (the **reset**: drops the old tables, creates the multi-tenant schema, no `Down`), `AddRolesAndPins`, `AddDevicesAndSessions` (additive).
- Production (Azure): `main` currently has only `InitialCreate` (O4). CD runs `dotnet ef database update --connection …` before deploying, which would apply all five newer migrations in order.
- A design-time factory (`InventoryDbContextFactory`) means the EF tools never execute `Program.cs` (which would run `Migrate()` against whatever connection the machine resolves). **Always pass `--connection` to `database update`.**
- Testing of the migration path (D5): `MigrationTests` migrate an empty database and each older schema state (with rows) to the latest and assert no pending model changes; the CI step "Verify EF Core migrations" runs the same command as CD on a scratch database; `dotnet ef migrations has-pending-model-changes` runs in CI. Each phase that changes the model must ship its migration.

## 11. Testing strategy and environment

- **223 tests** at `f0d071d` (70 unit, 153 integration), all passing. Mutation checks confirmed that tests fail when session revocation, the device-cookie check, the PIN-failure retry or an `[Authorize]` attribute is removed.
- Layers: unit tests (domain, hashing, credentials, guardrail); direct controller tests (`.AsEmployee(id)` supplies the user); tenant-isolation tests (queries, write guard, controllers, raw SQL against database constraints, each with a positive control); HTTP tests through the real pipeline (`ApiTestBase`, `ApiFactory`, `FakeTimeProvider`) for enrollment, login, PIN, sessions, forged tokens, authorization, rate limits.
- Integration tests need SQL Server: local Docker container `mssql_db` (SQL Server 2019, `localhost,1433`; start it with `docker start mssql_db`); connection comes from `InventoryApp.Tests/local.runsettings` (gitignored; **never print its password**). CI uses SQL Server 2022.
- `dotnet-ef`: the global tool on this machine is an 11.0 preview; CD pins 10.0.11. Use 10.0.11, e.g. `dotnet tool install dotnet-ef --version 10.0.11 --tool-path <dir>`.
- Test scratch databases (`MigrationTest_*`, `MigrationScratch`) must not be left behind; the migration tests delete theirs.
- To run the API itself: set `Auth:SigningKey` (`dotnet user-secrets set "Auth:SigningKey" "<32+ chars>" --project ./InventoryApp.Api`) and a connection string.

## 12. Git status (as of 2026-10-31)

- Current branch: `ft/multi-tenancy`, tracking `origin/ft/multi-tenancy`, **pushed**, tip `f0d071d`, working tree clean.
- `origin/main` = `1ed7503` (unchanged). `origin/refactor/use-dtos` = `486bb14` (unmerged; ancestor of `ft/multi-tenancy`).
- Commits on this work, in order: `6904479` Phase 0 (test infrastructure), `c22eaf4` (user's commit removing `backend-pipeline.yml.save`), `b6c4ca0` Phase 1 (tenant schema), `afc57f1` Phase 2 (roles, PINs), `f0d071d` Phase 3 (device enrollment, login, sessions).
- Nothing has been merged. The CD pipeline deploys only from `main`.
- The document you are reading is new and **not yet committed** (the user asked only for it to be written).

## 13. Implementation status

| Phase | Scope | Status |
|---|---|---|
| 0 | Branch, `Microsoft.AspNetCore.Mvc.Testing`, `public partial class Program`, `TimeProvider`, startup smoke test | Done (`6904479`) |
| 1 | Tenant schema, composite keys/FKs, query filters, write guard, design-time factory, reset migration, migration tests, CI migration step | Done (`b6c4ca0`) |
| 2 | Permission catalog, roles, default roles, `EmployeeRole` DB constraints, PIN hashing and lockout logic, `RequirePin`, `IsActive` | Done (`afc57f1`) |
| 3 | Devices, enrollment cookie, sessions, login/logout/change-pin, JWT with per-request validation, policies and fallback, rate limiting, DTO changes, HTTP tests, guardrail test | Done (`f0d071d`) |
| 4 | Entra administration (below) | **Not started** |
| 5 | Deployment hardening and merge (below) | **Not started** |

### Phase 4 scope (planned; details are assumptions A2, A16)

- Entra JWT scheme, app roles `Platform.Admin` / `Tenant.Admin`, `TenantAdmin` mapping table, tenant-scoped authorization handler.
- `/api/admin` endpoints: tenants (create with default roles, activate/deactivate, `RequirePin` with the "no active employee without PIN" guard); devices (create, issue new enrollment code, deactivate); employees (create, assign roles, set PIN with `MustChangePin`, deactivate); roles and permissions (TenantAdmin only); sessions (list, revoke). Platform Admin never reaches inventory data.
- Move `CreateEmployee` off the `AdminOnly` placeholder.
- Swagger only in Development (D15).
- Tests with a fake Entra scheme: tenant admin of A cannot touch B; employee tokens cannot reach admin endpoints and the reverse; platform admin cannot read products.

### Phase 5 scope (planned)

Entra app registration and app roles (done by the user in the Azure portal; Claude supplies exact steps), signing key in Key Vault or app settings, same-origin/same-site hosting decision for React (O2), cookie/CSP settings, CORS only if still needed, update `CLAUDE.md` and `README.md`, verify CI, final review, then merge `ft/multi-tenancy` to `main` **only with the user's approval**, which applies the reset migration to Azure automatically.

### Not in scope of this refactor

The React frontend, MAUI changes, new storage-location/stock endpoints (O9).

## 14. Where things are

- Entities: `InventoryApp.Api/Models/`. DbContext, mapping, filters, guard, `TenantContext`, design-time factory: `InventoryApp.Api/Data/`.
- Security: `InventoryApp.Api/Security/` (`Permissions`, `DefaultRoles`, `PinHasher`, `DeviceCredentials`, `EmployeeAuthentication` for scheme/policies/rate limits/session validation, `JwtTokenService`, `AuthOptions`, `InventoryClaims`, `Policies`).
- Controllers: `AuthController`, `ProductsController`, `EmployeesController`. DTOs: `Dtos/`.
- Tests: `ApiFactory`, `ApiTestBase`, `IntegrationTestBase`, `DatabaseFixture`; HTTP tests in `LoginTests`, `SessionTests`, `DeviceEnrollmentTests`, `ChangePinTests`, `AuthorizationHttpTests`, `RateLimitTests`; isolation and schema tests in `TenantIsolationTests`, `RolesAndPinsTests`, `MigrationTests`; `Unit/` for domain and guardrail tests.
- CI/CD: `.github/workflows/backend-pipeline.yml` (build, migration rehearsal, tests; deploy only on `main`).

## 15. Starting a new session

1. Read this file and `CLAUDE.md`. Run `git status -sb` and `git log --oneline -5`; confirm you are on `ft/multi-tenancy` at or after `f0d071d`.
2. Start SQL Server (`docker start mssql_db`) and run `dotnet test ./InventoryApp.Tests` to confirm the baseline (223 passing).
3. Ask the user which of these they want next: start Phase 4; verify the CI result of the pushes (O3); decide O1/O2. Do not start Phase 4 without the user's go-ahead, and state the phase scope first (section 2).
4. Do not merge or push without explicit approval. Commit locally at the end of a phase.
5. Raise unresolved items from section 6 only when they become relevant to the work in hand.
