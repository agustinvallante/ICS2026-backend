# Dsw2025Tpi backend tests

This project contains the shared automated tests for AT-B03. Unit tests are isolated from the API host, EF Core, Azure, and production data. Integration-test host and database setup belong to the B1–B4 workstream and must be documented when available.

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

A successful exit code is not sufficient if test discovery reports zero tests; inspect the discovered and executed test count. After B1–B4 add integration tests, run them using the isolated environment and cleanup steps documented by that workstream:

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
