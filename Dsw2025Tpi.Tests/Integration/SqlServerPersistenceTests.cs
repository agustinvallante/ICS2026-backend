using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dsw2025Tpi.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Provider", "SqlServer")]
public class SqlServerPersistenceTests
{
    [Fact]
    public async Task BothContexts_MigrateAndMigrateAgain_LeaveNoPendingMigrationsAndPreserveData()
    {
        using var factory = ApiFactory.ForSqlServer();
        using var client = factory.CreateSeededClient();
        string[] originalHistory;
        string[] expectedHistory;
        using (var scope = factory.Services.CreateScope())
        {
            var data = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            var auth = scope.ServiceProvider.GetRequiredService<AuthenticateContext>();
            Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", data.Database.ProviderName);
            Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", auth.Database.ProviderName);
            Assert.Equal(data.Database.GetDbConnection().Database, auth.Database.GetDbConnection().Database);
            expectedHistory = data.Database.GetMigrations().Concat(auth.Database.GetMigrations()).Order().ToArray();
            Assert.NotEmpty(data.Database.GetMigrations());
            Assert.NotEmpty(auth.Database.GetMigrations());
            originalHistory = (await data.Database.GetAppliedMigrationsAsync()).Order().ToArray();
            Assert.Equal(expectedHistory, originalHistory);
            Assert.Empty(await data.Database.GetPendingMigrationsAsync());
            Assert.Empty(await auth.Database.GetPendingMigrationsAsync());
            auth.Users.Add(new IdentityUser { UserName = "migration-marker", NormalizedUserName = "MIGRATION-MARKER" });
            await auth.SaveChangesAsync();
        }

        // Repeat the same order used by production, using new contexts and the existing data.
        using (var scope = factory.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<AuthenticateContext>();
            var data = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            await auth.Database.MigrateAsync();
            await data.Database.MigrateAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<AuthenticateContext>();
            var data = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            Assert.Empty(await auth.Database.GetPendingMigrationsAsync());
            Assert.Empty(await data.Database.GetPendingMigrationsAsync());
            Assert.Equal(originalHistory, (await data.Database.GetAppliedMigrationsAsync()).Order().ToArray());
            Assert.Equal(expectedHistory, (await auth.Database.GetAppliedMigrationsAsync()).Order().ToArray());
            Assert.True(await auth.Users.AnyAsync(u => u.UserName == "migration-marker"));
            Assert.Equal(2, await data.Set<Customer>().CountAsync());
            Assert.Equal(5, (await data.Set<Product>().SingleAsync(p => p.Id == factory.ProductId)).StockQuantity);
            Assert.Equal(2, (await data.Set<Product>().SingleAsync(p => p.Id == factory.OtherProductId)).StockQuantity);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateOrder_WhenLaterValidationOrSaveFails_RollsBackOnSqlServer(bool persistenceFailure)
    {
        using var factory = ApiFactory.ForSqlServer();
        using var client = factory.CreateSeededClient();
        var request = new OrderRequest(factory.CustomerId, "Shipping", "Billing", null,
            [new OrderItemRequest(factory.ProductId, 2), new OrderItemRequest(factory.OtherProductId, persistenceFailure ? 1 : 3)]);
        factory.FailOrderSave = persistenceFailure;

        using var response = await client.PostAsJsonAsync("/api/orders", request);

        Assert.Equal(persistenceFailure ? HttpStatusCode.InternalServerError : HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("Message").GetString()));
        if (persistenceFailure)
            Assert.Equal("Ocurrió un error inesperado en el servidor.", error.GetProperty("Message").GetString());
        factory.Inspect(db =>
        {
            Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
            Assert.Empty(db.Set<Order>());
            Assert.Empty(db.Set<OrderItem>());
            Assert.Equal(5, db.Set<Product>().Single(p => p.Id == factory.ProductId).StockQuantity);
            Assert.Equal(2, db.Set<Product>().Single(p => p.Id == factory.OtherProductId).StockQuantity);
        });
    }
}
