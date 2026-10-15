using System.Net.Http.Headers;
using Dsw2025Tpi.Api;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dsw2025Tpi.Tests.Integration;

// Each test owns its host and two databases. Closing the connections deletes all data.
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _data = new("Data Source=:memory:");
    private readonly SqliteConnection _auth = new("Data Source=:memory:");
    public const string Key = "TEST_ONLY_INTEGRATION_KEY_0123456789_ABCDEF";
    public const string Issuer = "integration-tests";
    public const string Audience = "integration-client";
    public Guid CustomerId { get; } = Guid.NewGuid();
    public Guid OtherCustomerId { get; } = Guid.NewGuid();
    public Guid ProductId { get; } = Guid.NewGuid();
    public Guid OtherProductId { get; } = Guid.NewGuid();
    public bool FailOrderSave { get; set; }

    public ApiFactory()
    {
        _data.Open();
        _auth.Open();
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
            services.AddDbContext<Dsw2025TpiContext>(o => o.UseSqlite(_data)
                .AddInterceptors(new OrderFailureInterceptor(this)));
            services.AddDbContext<AuthenticateContext>(o => o.UseSqlite(_auth));
        });
    }

    public HttpClient CreateSeededClient(string? role = "User", Guid? customerId = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        db.Database.EnsureCreated();
        scope.ServiceProvider.GetRequiredService<AuthenticateContext>().Database.EnsureCreated();
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
}
