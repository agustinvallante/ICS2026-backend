# Dsw2025Tpi backend tests

This project contains the shared automated tests for AT-B03. Unit tests are isolated from the API host, EF Core, Azure, and production data. B1–B4 integration tests use the real API host with isolated SQLite databases in memory.

## Prerequisites

- .NET 8 SDK.
- Restore access to the pinned NuGet packages.
- No database or network service is required for `Category=Unit`.

## Run

From the repository root:

```bash
dotnet restore Dsw2025Tpi.sln
dotnet test Dsw2025Tpi.Tests/Dsw2025Tpi.Tests.csproj --list-tests
dotnet test Dsw2025Tpi.Tests/Dsw2025Tpi.Tests.csproj --filter Category=Unit
```

A successful exit code is not sufficient if test discovery reports zero tests; inspect the discovered and executed test count. Run integration tests or the complete suite with:

```bash
dotnet test Dsw2025Tpi.sln --filter Category=Integration
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

## EV-B03 boundary characterization (temporary)

EV-B03's acceptance criteria are confirmed: price must be greater than zero, and stock may be zero, for both add and update. The current `ProductsManagementService` on `development` behaves differently at those boundaries: it accepts price zero and rejects stock zero. To keep this A1–A4 PR test-only and passing, the four boundary tests currently characterize that existing behavior for add/update; they do not claim it meets the EV-B03 acceptance criteria. When EV-B03 changes the production service, update these four tests to the required behavior and rerun the suite. No production logic is changed in this PR.

Package versions are explicit in `Dsw2025Tpi.Tests.csproj`; do not upgrade production dependencies as part of this test setup.

## Integration tests (Axel, B1–B4)

- `Integration/ApiFactory.cs` uses `WebApplicationFactory<Program>` and the `Testing` environment. The API skips startup migrations and JSON seeding in this environment. The factory replaces both EF providers with SQLite and creates the schemas using `EnsureCreated`.
- Each test owns a host, two in-memory connections (business data and Identity), and its seed data. Disposing the factory closes the connections and deletes the databases. No SQL Server, Azure, credentials, environment variables, manual cleanup, or external service is required after package restore.
- JWT configuration is test-only. Requests go through the real authentication and authorization middleware; authentication is not mocked. Registration and login also use the real Identity store.
- `ApiAccessTests` covers public/protected endpoints, Admin/User/anonymous access, malformed/expired tokens and wrong signature/issuer/audience, registration/login, and HTTP validation/error responses.
- `OrdersApiTests` covers creation, persisted item associations/totals/stock, invalid inputs, ownership for creation/read/list, Admin status updates, missing orders, and rollback after a later item fails or order persistence throws. The latter uses a test-only EF interceptor; persistence is verified in a fresh scope.
- The ownership tests required enforcing the token's `customerId` in the order endpoints. Customers create and access their own orders; Admin can read orders and update status, but cannot create orders. Both existing customer role spellings (`User` and `USER`) are supported.
- The rollback tests required a transaction around the complete order creation, including all stock writes and order insertion. Disposal rolls back an uncommitted transaction after an exception.
- SQL Server migrations are intentionally not executed against SQLite: provider-specific migrations, collation, SQL Server error codes, and concurrent stock updates are not validated by this suite. Migration verification needs an isolated SQL Server environment; B4 makes that coverage conditional. The SQLite tests verify relational persistence and transactions, not SQL Server equivalence.

Restore requires NuGet access. Once restored, `dotnet test Dsw2025Tpi.sln --no-restore` runs both suites without downloading packages. Use the `Category=Unit` / `Category=Integration` filters shown above to run either suite separately.
