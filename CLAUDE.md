# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Proof-of-concept inventory app: a mobile client (barcode scanning, stock check-in/out, badge login) talking to an ASP.NET Core Web API backed by SQL Server (Azure SQL in production). Some comments, UI strings and config placeholders are in German.

Projects:
- `InventoryApp.Api` — ASP.NET Core Web API, EF Core (SQL Server), `net10.0`
- `InventoryApp.Tests` — xUnit tests for the API, `net10.0`
- `InventoryApp.Maui` — **legacy client that will be replaced by a React frontend** because of unresolved MAUI/Android compatibility issues. Don't invest in new MAUI features; keep the API frontend-agnostic (plain JSON over HTTP). It uses MVVM via CommunityToolkit.Mvvm and ZXing.Net.Maui for scanning, targets `net8.0-android/ios/maccatalyst`, and isn't in `InventoryApp.sln`.

## Commands

```bash
# Build the API
dotnet build ./InventoryApp.Api/InventoryApp.Api.csproj

# Run the API (Swagger UI is enabled in all environments)
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

Integration tests run against a real SQL Server, not EF Core In-Memory. The README's mention of In-Memory testing is out of date. `DatabaseFixture` reads the connection string from the `ConnectionStrings__DefaultConnection` environment variable and throws if it isn't set. On startup it runs `MigrateAsync()`, and at teardown it calls `EnsureDeletedAsync()`, which **drops the database**. Only point it at a throwaway DB.

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
- **Multi-tenancy.** `Tenant` is the root. `Employee`, `Product`, `ProductStock` and `StorageLocation` implement `ITenantOwned` and carry a `TenantId`. Isolation is enforced twice: `InventoryDbContext` applies a query filter on the current tenant (no tenant set means no rows) and a `SaveChanges` guard that refuses writes for any other tenant; and the database rejects cross-tenant references because every key and foreign key includes `TenantId` (`Employee` and `StorageLocation` expose `(TenantId, Id)` as alternate key for this). The tenant comes from the scoped `TenantContext`, never from client input; use `_context.RequireTenantId()` when creating entities. Employee FKs use `NoAction`, so employees are deactivated, never deleted. Until Phase 3 of the multi-tenancy work nothing fills `TenantContext` in the running API, so tenant endpoints can't serve requests yet.
- **Roles and permissions.** Permissions are a fixed catalog in code (`Security/Permissions.cs`), each with a scope: `Inventory` (badge employees) or `Admin` (Entra identities only, so privileged operations never rest on a badge and PIN). `Role` is per tenant, has an immutable scope and may only hold permissions of that scope; system roles can't be edited. `DefaultRoles.CreateFor(tenantId)` gives every new tenant `TenantAdmin` (admin scope, system role), `WarehouseManager` and `WarehouseEmployee`. `Employee.AssignRole` accepts only Inventory roles of the same tenant, and the database backs that up: `EmployeeRole` references `(TenantId, RoleId, RoleScope)` and a check constraint fixes `RoleScope` to Inventory. A new tenant is created without a tenant context; bind it with `TenantContext.Set(tenant.Id)` afterwards and add the default roles (see `ANewTenant_CanBeCreatedAndSeededInOneScope`).
- **PINs.** Exactly six digits, stored as BCrypt hashes (`PinHasher`). `Employee` holds `PinHash`, `MustChangePin` (an admin-set PIN must be replaced), and the lockout state: 5 failed attempts lock the employee for 15 minutes (`RegisterFailedPin(now)`; the login must not check the PIN while locked out). `Tenant.RequirePin` makes the PIN mandatory for the tenant. Never log a PIN or its hash.
- `Product` is keyed by `(TenantId, InternalBarcode)`; the same barcode may exist in several tenants. Routes still use only the barcode. Stock quantities live in `ProductStock` rows, one per product and `StorageLocation` pair, with a unique index on that pair. `Product.TotalQuantity` is computed from `Stocks`.
- `Product.RowVersion` is a `[Timestamp]` concurrency token. `UpdateProduct` maps `DbUpdateConcurrencyException` to 409 Conflict, or to 404 if the product no longer exists.
- Configuration: `appsettings.json` is gitignored. Copy it from `appsettings.example.json` (`ConnectionStrings:DefaultConnection`). `Auth:SigningKey` (at least 32 characters) is required and the app refuses to start without it. Keep it out of the repo: locally `dotnet user-secrets set "Auth:SigningKey" "<random>" --project ./InventoryApp.Api`, in Azure an app setting or Key Vault reference. Changing it ends every session. `Auth:IdleTimeout` (30 min), `Auth:AbsoluteLifetime` (12 h) and the rate limits are optional.

### Authentication (warehouse employees)

Warehouse PDAs are browsers. Device and employee are authenticated separately, and neither secret is readable by client-side code.

1. **Device enrollment.** An admin creates a `Device` and gets a one-time enrollment code (24 h, hashed in the database). `POST /api/auth/enroll` redeems it and sets the device credential as an `inv_device` cookie (HttpOnly, Secure, SameSite=Strict, Path=/api/auth). Only the SHA-256 of the credential is stored. The `DeviceCode` is public (printed as the barcode) and only identifies the device. Re-issuing a code cuts off the old cookie. The cookie only works if the React app and the API are served from the same site.
2. **Login.** `POST /api/auth/login` with `{deviceCode, badgeBarcode, pin?}` plus the cookie. The device resolves the tenant (never the request); then the employee is found by badge; if `Tenant.RequirePin` the PIN is checked (5 wrong attempts lock the employee for 15 minutes, answered with 423; the failure count is concurrency-safe via `Employee.RowVersion`). Every refusal is an identical 401, except `pin_required` and the lockout. A new login on a device revokes its previous session (a unique filtered index enforces one active session per device).
3. **Session and token.** The login creates an `EmployeeSession` and returns a signed JWT (claims `sub`, `inv_tenant`, `sid`, `did`). The token is only a pointer: on every request `EmployeeAuthentication.ValidateSessionAsync` loads the session and checks revocation, idle (30 min) and absolute (12 h) expiry, and that employee, device and tenant are still active and match the token. It then sets `TenantContext` from the stored session and adds the employee's permissions as claims, read from the database, so a revocation, deactivation or role change applies on the next request. The client keeps the token in memory only.
4. **Authorization.** Every endpoint names a policy: `[Authorize(Policy = Permissions.X)]` (inventory permissions), `Policies.EmployeeSession` (any logged-in employee) or `[AllowAnonymous]` (only `enroll` and `login`). A fallback policy denies anything unmarked. `Policies.AdminOnly` denies everyone until the Entra admin scheme exists. An employee with `MustChangePin` can only call `logout` and `change-pin`. `EndpointGuardrailTests` fails if an action isn't marked or a new anonymous endpoint appears.
5. **Rate limits** on login, change-pin (30/min/IP) and enroll (5/min/IP) are flood control only; the PIN lockout is what stops guessing. The client address comes from `X-Forwarded-For` (App Service sits behind a proxy).

HTTP-level tests derive from `ApiTestBase` (real pipeline via `ApiFactory`, fake clock `Time`, helpers such as `EnrollPdaAsync`, `CreateEmployeeAsync`, `LoginTokenAsync`). Direct controller tests call `.AsEmployee(id)` to give the controller an authenticated user.

### MAUI client (legacy, being replaced by React)

The notes below describe the existing MAUI code for reference only. The React frontend will need to cover the same features: barcode login, camera barcode scanning, creating a product when a scanned barcode is unknown, stock +/- and local search. It doesn't exist in the repo yet, so ask before assuming its stack or location. The API has no CORS configured, and a browser-based React app on another origin will need a CORS policy in `Program.cs`.

- `MauiProgram.cs` loads an **embedded resource** `appsettings.json`, copied from `InventoryApp.Maui/appsettings.example.json`, which holds `ApiSettings:ProductsEndpointUrl` and `ApiSettings:EmployeesEndpointsUrl`. `ApiService` (singleton) throws on startup if either one is missing.
- Views and ViewModels are registered as transient pairs (`ScannerPage`/`ScannerViewModel`, `InventoryListPage`/`InventoryListViewModel`, `LoginPage`/`LoginViewModel`). Shell navigation is a two-tab `TabBar` (Scanner, Lager).
- The client has drifted from the API contract. `Maui/Models/Product.cs` still uses an int `Id`, `Barcode` and `Quantity`. `ApiService` calls `GET products/barcode/{barcode}`, `PUT products/{Id}` and `POST employees/change-pin`, but the API exposes `GET/PUT products/{internalBarcode}` and has no change-pin endpoint. Treat the API as the source of truth, not the MAUI client.

## CI/CD

`.github/workflows/backend-pipeline.yml` runs on pushes to `main`, `ft/*` and `refactor/*`, and on PRs to `main`. It builds the API and runs all tests against a SQL Server 2022 service container. On `main` only, it applies EF migrations to Azure SQL and deploys to the Azure Web App `InventoryApi-MarvinFranke`. The MAUI app isn't built in CI.
