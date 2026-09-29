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

# Add an EF Core migration
dotnet ef migrations add <Name> --project ./InventoryApp.Api
```

### Integration test database

Integration tests run against a real SQL Server, not EF Core In-Memory. The README's mention of In-Memory testing is out of date. `DatabaseFixture` reads the connection string from the `ConnectionStrings__DefaultConnection` environment variable and throws if it isn't set. On startup it runs `MigrateAsync()`, and at teardown it calls `EnsureDeletedAsync()`, which **drops the database**. Only point it at a throwaway DB.

- **CI:** the variable is set in the workflow and comes from GitHub.
- **Locally:** run the tests against a SQL Server Docker container on `localhost,1433` (the container runs SQL Server 2019, CI uses 2022), so you can test without pushing. `InventoryApp.Tests/local.runsettings` is gitignored and sets the variable. The test `.csproj` loads that file automatically when it exists, so plain `dotnet test` and IDE test runners pick it up. To create it, copy `local.runsettings.example` and fill in the SA password. A variable already set in the shell also works.
- Local runs must not change how CI gets its connection string. Keep `DatabaseFixture` reading only the environment variable, and never commit `local.runsettings`, because it holds the password.

All integration test classes share one database through the `"Integration Tests"` collection (`IntegrationTestCollection`, an `ICollectionFixture<DatabaseFixture>`). Classes in the collection don't run in parallel.

Integration test classes inherit from `IntegrationTestBase`. Before each test it opens a context, exposed as `Context`, and starts a transaction; after the test it rolls the transaction back, so nothing a test writes stays in the database. Test data that several tests need, such as the employee in `ProductsControllerTests`, is created by overriding `InitializeAsync()` (call `base.InitializeAsync()` first so the data lands inside the transaction).

Rules for integration tests:
- Use only `Context`. Don't create another context through `DatabaseFixture.CreateContext()` inside a test: it runs on a different connection, can't see the uncommitted data and isn't rolled back. Don't commit the transaction.
- Put `[Trait("Category", "Integration")]` and `[Collection("Integration Tests")]` on each test class, not only on the base class. Don't add `IClassFixture<DatabaseFixture>`; the collection already provides the fixture.
- `DatabaseFixture` must not use `EnableRetryOnFailure()`. The retrying execution strategy rejects the user-initiated transaction in `IntegrationTestBase`.
- Because of the rollback, keys only need to be unique within a single test. Reusing a barcode across tests is fine.
- Never set `Id` on an `Employee` (or any other entity with an identity column). SQL Server assigns it and rejects explicit values. After `SaveChangesAsync()`, EF fills the generated ID into the entity, so use `employee.Id` directly instead of querying the row back.
- The rollback only covers work on the test's own connection. It breaks as soon as a second connection is involved: tests through `WebApplicationFactory`/HTTP (the API gets its own `DbContext` from DI, can't see the uncommitted test data, isn't rolled back and can block on the test's locks), real concurrency tests with two contexts (the second blocks on the first transaction's locks until the command times out), API code that calls `BeginTransactionAsync()` itself (EF throws because a transaction is already open), or anything that opens its own connection. For such tests, don't use `IntegrationTestBase`; reset the database between tests instead, e.g. with Respawn. Ask the user before adding that.
- A test that calls `UpdateProduct` must pass the product's current `RowVersion`. A new `Product` has an empty `RowVersion`, so the update matches no row and returns 409. `UpdateProduct_ChangesData_AndReturnsNoContent` shows how: set it through `Context.Entry(...).Property(p => p.RowVersion).CurrentValue`, because the setter is private.

## Architecture

### API

- Controllers take `InventoryDbContext` directly. There's no service or repository layer, and tests construct controllers with `new ProductsController(context)`.
- `Program.cs` calls `db.Database.Migrate()` on startup. The CD pipeline also runs `dotnet ef database update` against Azure before it deploys.
- The domain models `Product` and `ProductStock` are rich entities. They have private setters, a private parameterless constructor for EF, a validating public constructor, and mutation methods (`UpdatePrice`, `AddStock`, `RemoveStock`, …) that enforce invariants and record `LastUpdatedByEmployeeId`. Change state through those methods rather than adding public setters. `Employee` and `StorageLocation` are plain POCOs.
- `Product` is keyed by the string `InternalBarcode`, not an int `Id`. Stock quantities live in `ProductStock` rows, one per product and `StorageLocation` pair, with a unique index on that pair. `Product.TotalQuantity` is computed from `Stocks`.
- `Product.RowVersion` is a `[Timestamp]` concurrency token. `UpdateProduct` maps `DbUpdateConcurrencyException` to 409 Conflict, or to 404 if the product no longer exists.
- Configuration: `appsettings.json` is gitignored. Copy it from `appsettings.example.json` (`ConnectionStrings:DefaultConnection`).

### MAUI client (legacy, being replaced by React)

The notes below describe the existing MAUI code for reference only. The React frontend will need to cover the same features: barcode login, camera barcode scanning, creating a product when a scanned barcode is unknown, stock +/- and local search. It doesn't exist in the repo yet, so ask before assuming its stack or location. The API has no CORS configured, and a browser-based React app on another origin will need a CORS policy in `Program.cs`.

- `MauiProgram.cs` loads an **embedded resource** `appsettings.json`, copied from `InventoryApp.Maui/appsettings.example.json`, which holds `ApiSettings:ProductsEndpointUrl` and `ApiSettings:EmployeesEndpointsUrl`. `ApiService` (singleton) throws on startup if either one is missing.
- Views and ViewModels are registered as transient pairs (`ScannerPage`/`ScannerViewModel`, `InventoryListPage`/`InventoryListViewModel`, `LoginPage`/`LoginViewModel`). Shell navigation is a two-tab `TabBar` (Scanner, Lager).
- The client has drifted from the API contract. `Maui/Models/Product.cs` still uses an int `Id`, `Barcode` and `Quantity`. `ApiService` calls `GET products/barcode/{barcode}`, `PUT products/{Id}` and `POST employees/change-pin`, but the API exposes `GET/PUT products/{internalBarcode}` and has no change-pin endpoint. Treat the API as the source of truth, not the MAUI client.

## CI/CD

`.github/workflows/backend-pipeline.yml` runs on pushes to `main`, `ft/*` and `refactor/*`, and on PRs to `main`. It builds the API and runs all tests against a SQL Server 2022 service container. On `main` only, it applies EF migrations to Azure SQL and deploys to the Azure Web App `InventoryApi-MarvinFranke`. The MAUI app isn't built in CI.
