# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Proof-of-concept inventory system: an ASP.NET Core Web API backed by SQL Server (Azure SQL in production), used by warehouse staff on shared PDA browsers (badge login, barcode scanning, stock check-in/out). The client will be a React app that doesn't exist yet. The connection string placeholders in `appsettings.example.json` are in German.

Projects:
- `InventoryApp.Api` — ASP.NET Core Web API, EF Core (SQL Server), `net10.0`
- `InventoryApp.Tests` — xUnit tests for the API, `net10.0`

**Multi-tenancy and security refactor in progress** (branch `ft/multi-tenancy`): read `docs/PROJECT_CONTEXT.md` first. It records the confirmed decisions, the assumptions made, the open issues, the implementation status and the remaining phases.

## Conventions

- Comment as little as possible. Code and test names carry the meaning; write a comment only when the reason isn't visible in the code (a security invariant, a database constraint, a workaround). No XML docs that restate a name, no section separators, no step numbers, no trailing comments.

## Commands

```bash
# Build the API
dotnet build ./InventoryApp.Api/InventoryApp.Api.csproj

# Run the API (Swagger UI is only served in the Development environment)
dotnet run --project ./InventoryApp.Api

# Unit tests only (no database needed)
dotnet test ./InventoryApp.Tests --filter "Category=Unit"

# Integration tests (need SQL Server, see below)
dotnet test ./InventoryApp.Tests --filter "Category=Integration"

# Single test
dotnet test ./InventoryApp.Tests --filter "FullyQualifiedName~ProductsControllerTests.GetProductByBarcode_ReturnsOk_WhenProductExists"

# Add an EF Core migration (never runs Program.cs, see InventoryDbContextFactory)
dotnet ef migrations add <Name> --project ./InventoryApp.Api

# Apply migrations (always pass --connection; CD and the CI "Verify EF Core migrations" step do the same)
dotnet ef database update --project ./InventoryApp.Api --connection "<connection string>"
```

### Integration test database

Integration tests run against a real SQL Server, not EF Core In-Memory. `DatabaseFixture` reads the connection string from the `ConnectionStrings__DefaultConnection` environment variable and throws if it isn't set. On startup it runs `MigrateAsync()`, and at teardown it calls `EnsureDeletedAsync()`, which **drops the database**. Only point it at a throwaway DB.

- **CI:** the variable is set in the workflow and comes from GitHub.
- **Locally:** run the tests against a SQL Server Docker container on `localhost,1433` (the container runs SQL Server 2019, CI uses 2022), so you can test without pushing. `InventoryApp.Tests/local.runsettings` is gitignored and sets the variable. The test `.csproj` loads that file automatically when it exists, so plain `dotnet test` and IDE test runners pick it up. To create it, copy `local.runsettings.example` and fill in the SA password. A variable already set in the shell also works.
- Local runs must not change how CI gets its connection string. Keep `DatabaseFixture` reading only the environment variable, and never commit `local.runsettings`, because it holds the password.

All integration test classes share one database through the `"Integration Tests"` collection (`IntegrationTestCollection`, an `ICollectionFixture<DatabaseFixture>`). Classes in the collection don't run in parallel.

Integration test classes inherit from `IntegrationTestBase`. Before each test it resets the database with Respawn (`DatabaseFixture.ResetDatabaseAsync()`, which deletes all rows except `__EFMigrationsHistory`) and then opens a context, exposed as `Context`. Nothing a test writes survives into the next test. Test data that several tests need, such as the employee in `ProductsControllerTests`, is created by overriding `InitializeAsync()` (call `base.InitializeAsync()` first so the reset runs before the data is created).

Rules for integration tests:
- Put `[Trait("Category", "Integration")]` and `[Collection("Integration Tests")]` on each test class, not only on the base class. Don't add `IClassFixture<DatabaseFixture>`; the collection already provides the fixture.
- Data is committed for real, so a second context, an HTTP call through `WebApplicationFactory`, or API code that opens its own transaction all see it. Classes in the collection still must not run in parallel, because the reset wipes the shared database.
- Because of the reset, keys only need to be unique within a single test. Reusing a barcode across tests is fine.
- `IntegrationTestBase` creates a tenant after each reset and exposes it as `Tenant`/`TenantId`; `Context` works for that tenant. For a second tenant use `CreateTenantAsync(name)` (also seeds the default roles) and `CreateContextFor(tenant)` (dispose the context), and `CreateContextWithoutTenant()` for a context that has none. Tenant-owned entities take the tenant id as their first constructor argument.
- Never set `Id` on an `Employee` (or any other entity with an identity column). SQL Server assigns it and rejects explicit values. After `SaveChangesAsync()`, EF fills the generated ID into the entity, so use `employee.Id` directly instead of querying the row back.
- A test that calls `UpdateProduct` must put the product's current `RowVersion` into the `UpdateProductRequest` (e.g. `originalProduct.RowVersion` after `SaveChangesAsync()`). Any other value makes the update match no row and return 409, which `UpdateProduct_ReturnsConflict_WhenRowVersionIsStale` relies on.

## Architecture

### API

- Controllers take `InventoryDbContext` directly. There's no service or repository layer, and tests construct controllers with `new ProductsController(context)`.
- Controllers never bind or return entities. Request and response types are records in `Dtos/` (`CreateProductRequest`, `UpdateProductRequest`, `ProductDto`, `CreateEmployeeRequest`, `EmployeeDto`, and in `AuthDtos.cs` `EnrollRequest`, `LoginRequest`, `LoginResponse`, `ChangePinRequest`), each response DTO with a static `FromEntity`. Controllers build entities through their constructors and mutation methods and turn the `ArgumentException`s they throw into 400. Requests never carry an `EmployeeId` or `TenantId`: the employee comes from the validated session (`User.GetEmployeeId()`), the tenant from `TenantContext`.
- `Program.cs` calls `db.Database.Migrate()` on startup. The CD pipeline also runs `dotnet ef database update` against Azure before it deploys.
- `MultiTenantSchema` is a reset migration: it drops the single-tenant tables and creates the multi-tenant schema, with no `Down`. It was written for an Azure database without data worth keeping. `MigrationTests` apply it from an empty database and from both older schema states (with rows), and fail on model changes without a migration.
- The domain models `Product` and `ProductStock` are rich entities. They have private setters, a private parameterless constructor for EF, a validating public constructor, and mutation methods (`UpdatePrice`, `AddStock`, `RemoveStock`, …) that enforce invariants and record `LastUpdatedByEmployeeId`. Change state through those methods rather than adding public setters. `Employee`, `Role` and `Tenant` follow the same pattern; only `StorageLocation` is a plain POCO.
- **Multi-tenancy.** `Tenant` is the root. `Employee`, `Product`, `ProductStock` and `StorageLocation` implement `ITenantOwned` and carry a `TenantId`; the three inventory ones implement `IInventoryData`, which administrator requests can't see or write. Isolation is enforced twice: `InventoryDbContext` applies a query filter on the current tenant (no tenant set means no rows) and a `SaveChanges` guard that refuses writes for any other tenant; and the database rejects cross-tenant references because every key and foreign key includes `TenantId` (`Employee` and `StorageLocation` expose `(TenantId, Id)` as alternate key for this). The tenant comes from the scoped `TenantContext`, never from client input; use `_context.RequireTenantId()` when creating entities. Employee FKs use `NoAction`, so employees are deactivated, never deleted. `TenantContext` is filled by the employee token validation (session) and by the Entra administrator resolution (mapping); both are server-side.
- **Roles and permissions.** Permissions are a fixed catalog in code (`Security/Permissions.cs`), each with a scope: `Inventory` (badge employees) or `Admin` (Entra identities only, so privileged operations never rest on a badge and PIN). `Role` is per tenant, has an immutable scope and may only hold permissions of that scope; system roles can't be edited. `DefaultRoles.CreateFor(tenantId)` gives every new tenant `TenantAdmin` (admin scope, system role), `WarehouseManager` and `WarehouseEmployee`. `TenantAdmin` links an Entra identity `(tid, oid)` to one tenant and to an Admin-scope role (unique per identity; the database also refuses Inventory roles and roles of other tenants). `Employee.AssignRole` accepts only Inventory roles of the same tenant, and the database backs that up: `EmployeeRole` references `(TenantId, RoleId, RoleScope)` and a check constraint fixes `RoleScope` to Inventory. A new tenant is created without a tenant context; bind it with `TenantContext.Set(tenant.Id)` afterwards and add the default roles (see `ANewTenant_CanBeCreatedAndSeededInOneScope`).
- **PINs.** Exactly six digits, stored as BCrypt hashes (`PinHasher`). `Employee` holds `PinHash`, `MustChangePin` (an admin-set PIN must be replaced), and the lockout state: 5 failed attempts lock the employee for 15 minutes (`RegisterFailedPin(now)`; the login must not check the PIN while locked out). `Tenant.RequirePin` makes the PIN mandatory for the tenant. Never log a PIN or its hash.
- `Product` is keyed by `(TenantId, InternalBarcode)`; the same barcode may exist in several tenants. Routes still use only the barcode. Stock quantities live in `ProductStock` rows, one per product and `StorageLocation` pair, with a unique index on that pair. `Product.TotalQuantity` is computed from `Stocks`.
- `Product.RowVersion` is a `[Timestamp]` concurrency token. `UpdateProduct` maps `DbUpdateConcurrencyException` to 409 Conflict, or to 404 if the product no longer exists.
- Configuration: `appsettings.json` is gitignored. Copy it from `appsettings.example.json` (`ConnectionStrings:DefaultConnection`). `Entra:TenantId` / `Entra:ClientId` / `Entra:RequiredScope` / `Entra:PlatformAdminRole` configure administrator sign-in (see below). `Auth:SigningKey` (at least 32 characters) is required and the app refuses to start without it. Keep it out of the repo: locally `dotnet user-secrets set "Auth:SigningKey" "<random>" --project ./InventoryApp.Api`, in Azure an app setting or Key Vault reference. Changing it ends every session. `Auth:IdleTimeout` (30 min), `Auth:AbsoluteLifetime` (12 h) and the rate limits are optional.

### Authentication (warehouse employees)

Warehouse PDAs are browsers. Device and employee are authenticated separately, and neither secret is readable by client-side code.

1. **Device enrollment.** An admin creates a `Device` and gets a one-time enrollment code (24 h, hashed in the database). `POST /api/auth/enroll` redeems it and sets the device credential as an `inv_device` cookie (HttpOnly, Secure, SameSite=Strict, Path=/api/auth). Only the SHA-256 of the credential is stored. The `DeviceCode` is public (printed as the barcode) and only identifies the device. Re-issuing a code cuts off the old cookie. The cookie only works if the React app and the API are served from the same site.
2. **Login.** `POST /api/auth/login` with `{deviceCode, badgeBarcode, pin?}` plus the cookie. The device resolves the tenant (never the request); then the employee is found by badge; if `Tenant.RequirePin` the PIN is checked (5 wrong attempts lock the employee for 15 minutes, answered with 423; the failure count is concurrency-safe via `Employee.RowVersion`). Every refusal is an identical 401, except `pin_required` and the lockout. A new login on a device revokes its previous session (a unique filtered index enforces one active session per device).
3. **Session and token.** The login creates an `EmployeeSession` and returns a signed JWT (claims `sub`, `inv_tenant`, `sid`, `did`). The token is only a pointer: on every request `EmployeeAuthentication.ValidateSessionAsync` loads the session and checks revocation, idle (30 min) and absolute (12 h) expiry, and that employee, device and tenant are still active and match the token. It then sets `TenantContext` from the stored session and adds the employee's permissions as claims, read from the database, so a revocation, deactivation or role change applies on the next request. The client keeps the token in memory only.
4. **Authorization.** Every endpoint names a policy: `[Authorize(Policy = Permissions.X)]` (inventory permissions), `Policies.EmployeeSession` (any logged-in employee) or `[AllowAnonymous]` (only `enroll` and `login`). A fallback policy denies anything unmarked. Administrators use the Entra policies below; no employee token is valid for them. An employee with `MustChangePin` can only call `logout` and `change-pin`. `EndpointGuardrailTests` fails if an action isn't marked or a new anonymous endpoint appears.
5. **Rate limits** on login, change-pin (30/min/IP) and enroll (5/min/IP) are flood control only; the PIN lockout is what stops guessing. The client address comes from `X-Forwarded-For` (App Service sits behind a proxy).

HTTP-level tests derive from `ApiTestBase` (real pipeline via `ApiFactory`, fake clock `Time`, helpers such as `EnrollPdaAsync`, `CreateEmployeeAsync`, `LoginTokenAsync`). Direct controller tests call `.AsEmployee(id)` to give the controller an authenticated user.

### Authentication (administrators, Microsoft Entra)

The `Entra` scheme (`Security/EntraAuthentication.cs`) is separate from `EmployeeJwt`: different issuer, key and audience, and every policy names exactly one scheme, so neither token type is valid for the other (an employee token on an admin endpoint, or an Entra token on an employee endpoint, is a 401).

1. **Token.** Validated against the single configured directory (`Entra:TenantId`, issuer `…/{tid}/v2.0`), audience `Entra:ClientId` or `api://{ClientId}`, RS256 only, expiry required, 1 minute skew, same `TimeProvider` as the rest. `tid` must equal the directory, `oid` must be one valid GUID, and the token must carry the delegated scope `Entra:RequiredScope` in `scp` (so app-only tokens fail). Settings come from the `Entra` section; none are secrets. With no settings the scheme rejects every token (the app still starts); half-filled or malformed settings stop startup.
2. **Resolution on every request** (`ResolveAdministratorAsync`). The request scope is first restricted with `TenantContext.RestrictToAdministration()`: from then on `Product`, `ProductStock` and `StorageLocation` (`IInventoryData`) are invisible and unwritable through `InventoryDbContext` (query filter plus write guard), whatever a controller does (decision D8). Then the active `TenantAdmin` row of `(tid, oid)` is looked up with the role's permissions in one query; if the row and its tenant are active, `TenantContext.Set(tenant)` is called and the admin-scope permissions become `permission` claims (claim `inv_admin_kind=tenant`). A Platform Admin is a token that carries the app role `Entra:PlatformAdminRole` (claim `inv_admin_kind=platform`, fixed `PlatformPermissions` as `platform_permission` claims, no tenant). Claims of those types in the token are stripped first. Unknown or inactive identities stay signed in without claims (403 at every policy); a database failure is a 503.
3. **Policies** (`AdminPolicies`): `AdminSession`, `TenantAdmin:<AdminPermission>` for the whole Admin catalog, `PlatformAdmin:<PlatformPermission>`. Platform permissions deliberately have no inventory and no role-definition permission (D8, A2).
4. **Administration endpoints.** Tenant Admin: `AdminController` (`api/admin`: require-pin, devices, employees, sessions, and roles for Inventory roles only; Admin roles are read-only so nobody can grant themselves more). Platform Admin: `PlatformController` (`api/platform/tenants`: create with default roles, rename, activate, deactivate) and `PlatformTenantController` (`api/platform/tenants/{tenantId}/…`: require-pin, devices, employees, sessions, tenant administrators; no roles). The shared logic for devices, employees, sessions and the PIN setting is in the abstract `TenantManagementController`, which only ever works on the tenant of the request scope. For a platform route the `{tenantId}` is just a selector: `[BindRouteTenant]` (an action filter, so it runs after the policy) checks the tenant exists and only then binds it; actions never take a tenant id parameter. An identity that is both Tenant and Platform Admin is bound to its mapped tenant and gets 409 elsewhere. Invariant: `RequirePin` means every active employee has a PIN (enforced on switch-on, creation and activation). An admin-set PIN is `MustChangePin` and ends the employee's sessions; deactivating an employee or device and re-issuing an enrollment code revoke sessions too. Only a Platform Admin can map an Entra object id to a tenant (`…/administrators`).

Tests: `EntraTestBase` makes tokens with `EntraTestTokens` (own RSA key; `ApiFactory` swaps only the key discovery) and seeds `TenantAdmin` rows. `ApiFactory` also has `DatabaseFailure` (makes every query throw) and registers the test-only `InventoryProbeController`, which goes for inventory data like a careless admin endpoint would.

### Frontend

The client will be a browser-based React app. It doesn't exist in the repo yet, so ask before assuming its stack or location. It will need barcode login, camera barcode scanning, product creation for unknown barcodes, stock +/- and local search. The API has no CORS configured, and a React app on another origin will need a CORS policy in `Program.cs`.

## CI/CD

`.github/workflows/backend-pipeline.yml` runs on pushes to `main`, `ft/*` and `refactor/*`, and on PRs to `main`. It builds the API and runs all tests against a SQL Server 2022 service container. On `main` only, it applies EF migrations to Azure SQL and deploys to the Azure Web App `InventoryApi-MarvinFranke`.
