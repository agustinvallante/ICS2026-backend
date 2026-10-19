using System.Net.Http.Headers;
using Dsw2025Tpi.Api;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dsw2025Tpi.Tests.Integration;

// Each test owns its host and isolated persistence; SQL Server uses a generated database.
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _data = new("Data Source=:memory:");
    private readonly SqliteConnection _auth = new("Data Source=:memory:");
    private readonly string? _sqlConnectionString;
    private readonly string? _sqlDatabaseName;
    public const string Key = "TEST_ONLY_INTEGRATION_KEY_0123456789_ABCDEF";
    public const string Issuer = "integration-tests";
    public const string Audience = "integration-client";
    public Guid CustomerId { get; } = Guid.NewGuid();
    public Guid OtherCustomerId { get; } = Guid.NewGuid();
    public Guid ProductId { get; } = Guid.NewGuid();
    public Guid OtherProductId { get; } = Guid.NewGuid();
    public bool FailOrderSave { get; set; }
    // SQL Server only: runs before each SaveChanges that updates a Product, with the 1-based count of such
    // saves, so a test can hold requests or change the row from another context after it was read.
    public Func<int, Task>? BeforeProductUpdate { get; set; }
    public int ProductUpdateAttempts => Volatile.Read(ref _productUpdateAttempts);
    public int ConcurrencyConflictsDetected => Volatile.Read(ref _concurrencyConflictsDetected);
    private int _productUpdateAttempts;
    private int _concurrencyConflictsDetected;

    public ApiFactory() : this(false) { }

    public static ApiFactory ForSqlServer() => new(true);

    private ApiFactory(bool useSqlServer)
    {
        if (useSqlServer)
        {
            _sqlDatabaseName = "ATB03_QA_" + Guid.NewGuid().ToString("N");
            var connection = new SqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("QA_SQLSERVER_CONNECTION_STRING")
                ?? "Server=.\\SQLEXPRESS;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=10");
            // Never connect to the database named by the caller: each test owns a fresh database.
            connection.InitialCatalog = _sqlDatabaseName;
            connection.AttachDBFilename = string.Empty;
            _sqlConnectionString = connection.ConnectionString;
        }
        else
        {
            _data.Open();
            _auth.Open();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DB_CONNECTION_STRING", "Server=unused;Database=tests");
        builder.UseSetting("Jwt:Key", Key);
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:ExpireInMinutes", "30");
        builder.UseSetting("LogPath", "");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<Dsw2025TpiContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<Dsw2025TpiContext>>();
            services.RemoveAll<DbContextOptions<AuthenticateContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuthenticateContext>>();
            if (_sqlConnectionString is not null)
            {
                services.AddDbContext<Dsw2025TpiContext>(o => o.UseSqlServer(_sqlConnectionString)
                    .AddInterceptors(new OrderFailureInterceptor(this), new ProductUpdateProbe(this)));
                services.AddDbContext<AuthenticateContext>(o => o.UseSqlServer(_sqlConnectionString));
            }
            else
            {
                // SQLite cannot generate rowversion values; simulate them on the client (test-only)
                services.AddDbContext<Dsw2025TpiContext>(o => o.UseSqlite(_data)
                    .ReplaceService<IModelCustomizer, SqliteRowVersionModelCustomizer>()
                    .AddInterceptors(new SqliteRowVersionInterceptor(), new OrderFailureInterceptor(this)));
                services.AddDbContext<AuthenticateContext>(o => o.UseSqlite(_auth));
            }
        });
    }

    public HttpClient CreateSeededClient(string? role = "User", Guid? customerId = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticateContext>();
        if (_sqlConnectionString is not null)
        {
            auth.Database.Migrate();
            db.Database.Migrate();
        }
        else
        {
            db.Database.EnsureCreated();
            auth.Database.EnsureCreated();
        }
        db.AddRange(
            new Customer { Id = CustomerId, Name = "Axel", Email = "axel@example.test" },
            new Customer { Id = OtherCustomerId, Name = "Other", Email = "other@example.test" },
            new Product("SKU-1", "Product one", 12.50m, 5, null, "CODE-1") { Id = ProductId },
            new Product("SKU-2", "Product two", 3m, 2, null, "CODE-2") { Id = OtherProductId });
        db.SaveChanges();
        if (role is not null)
        {
            var token = scope.ServiceProvider.GetRequiredService<JwtTokenService>()
                .GenerateToken("test-user", role, (customerId ?? CustomerId).ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    public void Inspect(Action<Dsw2025TpiContext> assertion)
    {
        using var scope = Services.CreateScope();
        assertion(scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _data.Dispose();
            _auth.Dispose();
            if (_sqlConnectionString is not null)
            {
                var connection = new SqlConnectionStringBuilder(_sqlConnectionString);
                if (connection.InitialCatalog != _sqlDatabaseName ||
                    !_sqlDatabaseName!.StartsWith("ATB03_QA_", StringComparison.Ordinal))
                    throw new InvalidOperationException("Refusing to delete a database not owned by this test.");
                using var db = new Dsw2025TpiContext(new DbContextOptionsBuilder<Dsw2025TpiContext>()
                    .UseSqlServer(_sqlConnectionString).Options);
                db.Database.EnsureDeleted();
            }
        }
    }

    private sealed class OrderFailureInterceptor(ApiFactory factory) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (factory.FailOrderSave && eventData.Context!.ChangeTracker.Entries<Order>()
                    .Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("Simulated order persistence failure; test-only detail.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    // Observes the real optimistic-concurrency flow: product updates attempted and conflicts raised by EF.
    // The hook must change rows without SaveChanges (e.g. ExecuteUpdate) to avoid re-entering the probe.
    private sealed class ProductUpdateProbe(ApiFactory factory) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Product>().Any(e => e.State == EntityState.Modified))
            {
                var attempt = Interlocked.Increment(ref factory._productUpdateAttempts);
                if (factory.BeforeProductUpdate is { } hook)
                    await hook(attempt);
            }
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        // Raised by EF right before it throws DbUpdateConcurrencyException (RowVersion no longer matches)
        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Entries.Any(e => e.Entity is Product))
                Interlocked.Increment(ref factory._concurrencyConflictsDetected);
            return base.ThrowingConcurrencyExceptionAsync(eventData, result, cancellationToken);
        }
    }

    // SQL Server generates RowVersion on every INSERT/UPDATE. On SQLite the token stays a concurrency
    // token but becomes client-generated, so EF sends the new value and keeps the original in the WHERE.
    private sealed class SqliteRowVersionModelCustomizer(ModelCustomizerDependencies dependencies)
        : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<Product>().Property<byte[]>(RowVersion)
                .ValueGeneratedNever()
                .IsConcurrencyToken();
        }
    }

    private sealed class SqliteRowVersionInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
        {
            RenewRowVersions(eventData.Context!);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            RenewRowVersions(eventData.Context!);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private static void RenewRowVersions(DbContext context)
        {
            // Products changed through property setters are only marked Modified after detection
            context.ChangeTracker.DetectChanges();
            foreach (var entry in context.ChangeTracker.Entries<Product>()
                         .Where(e => e.State is EntityState.Added or EntityState.Modified))
                entry.Property<byte[]>(RowVersion).CurrentValue = Guid.NewGuid().ToByteArray();
        }
    }

    private const string RowVersion = "RowVersion";
}
