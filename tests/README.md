# Dsw2025Tpi backend tests

This folder contains the automated backend tests (originally AT-B03), split into two projects as required by the TP4 guide (CARD-TP4-01). Unit tests are isolated from the API host, EF Core, Azure, and production data. Integration tests use the real API host with isolated persistence. The fast tests use SQLite in memory; B4 migration and production-provider rollback tests use SQL Server.

## Layout

```text
tests/
├── Dsw2025Tpi.UnitTests/          xUnit + Moq. References Application and Domain only.
│   └── Services/                  JwtTokenServiceTests, ProductsManagementServiceTests
├── Dsw2025Tpi.IntegrationTests/   xUnit + WebApplicationFactory + SQLite/SQL Server. References Api, Application, Data and Domain.
│   ├── ApiFactory.cs              Test host (SQLite by default, ApiFactory.ForSqlServer() for Provider=SqlServer)
│   └── B4-QA.md                   Historical AT-B03 execution evidence
└── Shared/
    └── ProductRequestFactory.cs   Request builder used by both projects
```

`Shared/ProductRequestFactory.cs` is not a project. Both `.csproj` files include it as a linked file (`<Compile Include="..\Shared\ProductRequestFactory.cs" Link="Support\ProductRequestFactory.cs" />`), so each project compiles its own copy from a single source file. The two test projects never reference each other. If a shared helper needs a new dependency, both projects must reference it.

Production projects stay at the repository root (grouped in the `src` solution folder); only test projects live under `tests/`.

## Prerequisites

- .NET 8 SDK.
- Restore access to the pinned NuGet packages.
- No database or network service is required for `Category=Unit`.
- The complete suite, including `Provider=SqlServer`, requires an accessible SQL Server and permission to create/drop test databases. The default is `.\SQLEXPRESS` with Windows integrated authentication. Configure another server through `QA_SQLSERVER_CONNECTION_STRING` in the environment (never commit credentials). Each SQL Server factory overrides the database name with a generated `ATB03_QA_<guid>` and deletes only that database when disposed.

## Run

From the repository root:

```bash
dotnet restore Dsw2025Tpi.sln
dotnet test Dsw2025Tpi.sln --list-tests
dotnet test tests/Dsw2025Tpi.UnitTests/Dsw2025Tpi.UnitTests.csproj
```

A successful exit code is not sufficient if test discovery reports zero tests; inspect the discovered and executed test count for each project. Run integration tests or the complete suite with:

```bash
# Fast integration tests (SQLite in memory).
dotnet test tests/Dsw2025Tpi.IntegrationTests/Dsw2025Tpi.IntegrationTests.csproj --filter "Provider!=SqlServer"

# The same filters also work over the whole solution.
dotnet test Dsw2025Tpi.sln --filter Category=Unit
dotnet test Dsw2025Tpi.sln --filter "Category=Integration&Provider!=SqlServer"

# Requires SQL Server; no silent skips when the server is unavailable.
dotnet test Dsw2025Tpi.sln --filter Provider=SqlServer

# Includes SQL Server tests.
dotnet test Dsw2025Tpi.sln
```

## Unit-test conventions

- Follow Arrange–Act–Assert and name tests `Method_Scenario_ExpectedBehavior`; keep one behavior per test.
- Use `[Fact]` for one case and `[Theory]` for equivalent input sets. Async tests return `Task` and await the operation/assertion; do not use `async void`.
- Mark every test with `[Trait("Category", "Unit")]` or `[Trait("Category", "Integration")]`. Categories let either suite run independently.
- Build each test's inputs and doubles locally. Tests must not depend on ordering, shared mutable state, production configuration, or data created by another test.
- Use `NullLogger<T>.Instance` where a service requires a logger. Do not assert log text or call counts as business behavior.
- Prefer assertions on returned values and meaningful writes. Avoid coupling tests to irrelevant internal calls.
- Keep reusable request builders valid by default; each validation case should change only the field under test.
- For `IRepository` interactions use Moq 4. Specify `params string[] include` in setups/verifications when needed, and return entities consistent with the queried data.
- When a repository method receives an expression predicate, evaluate that predicate against representative in-memory entities (or otherwise verify its semantics). Returning a fixed entity for `It.IsAny<Expression<...>>()` does not prove the query is correct. In-memory LINQ checks service intent only; it does not prove SQL translation or SQL Server collation.
- Do not use `Thread.Sleep`/`Task.Delay` for token-expiration tests, or compare complete JWT strings: timestamps and `jti` legitimately vary.
- Do not load appsettings or real credentials. JWT tests use an in-memory configuration and a test-only signing key.

## Ownership and current scope

Agustín owns the shared project conventions and A1–A4 unit tests for product service behavior and JWT generation. Axel owns B1–B4 integration host, isolated persistence, HTTP authentication/authorization/ownership, orders/stock, migrations, and exception-path coverage. Do not add integration dependencies or fixtures without coordinating ownership and setup with Axel.

## EV-B03 boundary validation

For both product creation and update, the service requires `CurrentUnitPrice > 0` and `StockQuantity >= 0`. Unit tests verify zero/negative price and negative/zero stock boundaries; SQLite API integration tests verify the corresponding HTTP status, error message, persistence, and absence of mutation. Invalid values return `400 Bad Request`; zero stock is accepted. These tests enforce the EV-B03 acceptance criteria.

Package versions are explicit in `Dsw2025Tpi.UnitTests.csproj` and `Dsw2025Tpi.IntegrationTests.csproj`; do not upgrade production dependencies as part of this test setup.

## Integration tests (Axel, B1–B4)

- `Dsw2025Tpi.IntegrationTests/ApiFactory.cs` uses `WebApplicationFactory<Program>` and the `Testing` environment. The API skips startup migrations and JSON seeding in this environment. The default factory replaces both EF providers with SQLite and creates their schemas using `EnsureCreated`. `ApiFactory.ForSqlServer()` instead configures both contexts against the same generated SQL Server database and explicitly runs Identity migrations followed by business migrations, matching the production order. SQL Server tests never call `EnsureCreated`.
- Each fast test owns a host, two in-memory connections (business data and Identity), and seed data. Disposing the factory closes the connections and deletes the databases. These fast tests need no external service after restore. Each SQL Server test owns a separate generated database; disposal deletes it after shutting down the host. An interrupted process may leave its generated test database behind.
- JWT configuration is test-only. Requests go through the real authentication and authorization middleware; authentication is not mocked. Registration and login also use the real Identity store.
- `ApiAccessTests` covers public/protected endpoints, Admin/User/anonymous access, malformed/expired tokens and wrong signature/issuer/audience, registration/login, and HTTP validation/error responses.
- `OrdersApiTests` covers creation, persisted item associations/totals/stock, invalid inputs, ownership for creation/read/list, Admin status updates, missing orders, and rollback after a later item fails or order persistence throws. The latter uses a test-only EF interceptor; persistence is verified in a fresh scope. Invalid status inputs include `NOT_A_STATUS`, `999`, `-1`, and `0`: each must return HTTP 400 and preserve the previously valid persisted status. The service combines `Enum.TryParse` with `Enum.IsDefined`.
- Order reads (EV-B01) require `Admin`, `User` or `USER`. Customers read their own orders using the authenticated JWT's `customerId` claim. An anonymous read returns `401`. An unsupported role, an invalid customer claim or a read of another customer's order returns `403`. The tests create orders for two customers and verify the scopes for both customer roles and for Admin. Admin can read orders globally and update their status. Order creation requires a customer role and a matching claim.
- The rollback tests required a transaction around the complete order creation, including all stock writes and order insertion. Disposal rolls back an uncommitted transaction after an exception.
- `SqlServerPersistenceTests` executes B4 against SQL Server: it verifies both contexts' complete applied migration history and lack of pending migrations, reapplies both contexts' migrations in fresh scopes, and verifies unchanged history plus preserved Identity/business data. Both contexts share the same database/history table, as in production.
- The SQL Server rollback theory covers insufficient stock on a later item (HTTP 400) and a test-only exception when saving the order after both product stock updates (HTTP 500 with sanitized error). Fresh contexts verify that neither orders nor order items persisted and that both original stock quantities were restored. It uses the real SQL Server provider and real API/repository transactions; only the failure is injected.
- SQL Server tests are part of the complete suite and fail if SQL Server is unavailable. SQLite-only runs do not constitute B4 evidence. SQL Server collation, specific SQL error-code mappings and AT-B04 concurrency retries are outside these B4 tests' scope.

- `ProductsApiTests` covers the product catalog scenarios of TP4: product creation persisted in SQLite, the public listing contract, query-string filters/pagination, and the Admin listing with inactive products.

Restore requires NuGet access. Once restored, `dotnet test Dsw2025Tpi.sln --no-restore` runs all suites without downloading packages, and requires SQL Server. Use the filters above to run the unit, fast integration, or SQL Server tests separately. Executed evidence for the original AT-B03 change is recorded in [B4-QA.md](Dsw2025Tpi.IntegrationTests/B4-QA.md).

## TP4 integration scenarios (CARD-TP4-03)

Each integration scenario required by the TP4 statement has a test with the exact name from the statement. All of them run through the real HTTP pipeline (routing, JWT authentication/authorization, controllers, exception middleware) against isolated SQLite.

| ID | Test | Class |
|---|---|---|
| I-01 | `AddProduct_WhenUnauthenticatedOrNotAdmin_ReturnsUnauthorizedOrForbidden` (anonymous → 401, User → 403, nothing persisted) | `ApiAccessTests` |
| I-02 | `AddProduct_WhenAuthenticatedAsAdmin_PersistsInSqliteAndReturnsCreated` (201, `Location`, every persisted column) | `ProductsApiTests` |
| I-03 | `AddProduct_WhenSkuAlreadyExistsInDatabase_ReturnsConflictStatusCode` (409 with JSON error, no second row) | `ApiAccessTests` |
| I-04 | `CreateOrder_WhenUnauthenticated_ReturnsUnauthorized` (401, no order, stock unchanged) | `OrdersApiTests` |
| I-05 | `CreateOrder_WhenAuthenticatedWithValidData_ReturnsCreatedAndPersistsOrderAndStockInSqlite` | `OrdersApiTests` |
| I-06 | `CreateOrder_WhenStockIsInsufficient_ReturnsBadRequestWithErrorMessage` (400, message names the available and requested stock) | `OrdersApiTests` |
| I-07 | `GetProducts_PublicEndpoint_ReturnsOkWithPaginatedJsonStructure` and `GetProducts_PublicEndpoint_WhenCatalogIsEmpty_ReturnsNoContent` | `ProductsApiTests` |
| I-08 | `GetProducts_WithQueryStringFilters_ReturnsFilteredResultsFromSqlite` (search by name/SKU/internal code, case variants, pagination) | `ProductsApiTests` |
| I-09 | `GetAuthProducts_AdminEndpoint_WhenCalledByAdmin_IncludesInactiveProducts` (also checks the public listing excludes it) | `ProductsApiTests` |

Notes on the API contract these tests verify:

- The paginated product response is `ProductModel.ResponsePagination(ProductItems, Total)`, serialized as `{ "productItems": [...], "total": N }`. The TP4 statement calls the total `totalItems`; the API field is `total`.
- `GET /api/products` (anonymous) returns only active products; `GET /api/products/admin` (Admin only) includes inactive ones. Both return `204 No Content` when there are no results.
- Duplicate SKUs return `409 Conflict`.
- Search ignores case. In SQLite `upper()` only converts ASCII letters, so the search tests use ASCII terms. Searching accented terms with different casing is not covered by this suite.

### E2E API flow

The TP4 E2E scenario is covered at API level (no UI), as the TP4 guide requires. The chosen flow is order creation, implemented by I-05 `CreateOrder_WhenAuthenticatedWithValidData_ReturnsCreatedAndPersistsOrderAndStockInSqlite`:

1. A customer authenticates with a real JWT (`User` role, its own `customerId`).
2. `POST /api/orders` buys two products ("Product one" ×2 at 12.50 and "Product two" ×1 at 3.00).
3. The API returns `201 Created`, the `Location` header, total `28.00` and status `PENDING`.
4. SQLite contains the order with both items, addresses and total, and the stock dropped from 5 to 3 and from 2 to 1.
5. `GET` on the returned `Location` returns `200` with the same order.

`RegisterAndLogin_WithValidCredentials_ReturnUsableCustomerTokens` complements it with the real register → login → authenticated request flow.
