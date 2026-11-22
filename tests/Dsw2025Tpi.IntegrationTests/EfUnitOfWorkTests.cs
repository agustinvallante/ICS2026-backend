using Dsw2025Tpi.Data;
using Dsw2025Tpi.Data.Repositories;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Exceptions;
using Dsw2025Tpi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dsw2025Tpi.IntegrationTests;

// EV-B02: la transacción del checkout confirma todo o revierte todo, incluso los SaveChanges
// que ya se ejecutaron dentro de ella. Usa el EfUnitOfWork real registrado en la API sobre SQLite.
[Trait("Category", "Integration")]
public class EfUnitOfWorkTests
{
    private static ApiFactory SeededFactory()
    {
        var factory = new ApiFactory();
        factory.CreateSeededClient(role: null).Dispose();
        return factory;
    }

    // Descuenta stock y lo guarda dentro de la transacción en curso, como hace el checkout
    private static async Task DecrementStockAndSave(Dsw2025TpiContext db, Guid productId, int quantity)
    {
        var product = await db.Set<Product>().SingleAsync(p => p.Id == productId);
        product.StockQuantity -= quantity;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_WhenOperationSucceeds_CommitsEveryWrite()
    {
        using var factory = SeededFactory();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var result = await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await DecrementStockAndSave(db, factory.ProductId, 2);
                await DecrementStockAndSave(db, factory.OtherProductId, 1);
                return "ok";
            });

            Assert.Equal("ok", result);
        }

        factory.Inspect(db =>
        {
            Assert.Equal(3, db.Set<Product>().Single(p => p.Id == factory.ProductId).StockQuantity);
            Assert.Equal(1, db.Set<Product>().Single(p => p.Id == factory.OtherProductId).StockQuantity);
        });
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_WhenOperationFailsAfterSaving_RollsBackWithoutRetrying()
    {
        using var factory = SeededFactory();
        var failure = new InvalidOperationException("Simulated failure after stock was saved");
        var attempts = 0;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                unitOfWork.ExecuteInTransactionAsync<string>(async () =>
                {
                    attempts++;
                    await DecrementStockAndSave(db, factory.ProductId, 2);
                    await DecrementStockAndSave(db, factory.OtherProductId, 1);
                    throw failure;
                }));

            Assert.Same(failure, exception);
            // Las entidades del intento fallido se descartan para no reutilizar valores revertidos
            Assert.Empty(db.ChangeTracker.Entries());
        }

        Assert.Equal(1, attempts);
        factory.Inspect(db =>
        {
            Assert.Equal(5, db.Set<Product>().Single(p => p.Id == factory.ProductId).StockQuantity);
            Assert.Equal(2, db.Set<Product>().Single(p => p.Id == factory.OtherProductId).StockQuantity);
        });
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_WhenEveryAttemptConflicts_RollsBackEachAttemptAndReportsConflict()
    {
        using var factory = SeededFactory();
        var attempts = 0;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                unitOfWork.ExecuteInTransactionAsync<string>(async () =>
                {
                    attempts++;
                    await DecrementStockAndSave(db, factory.ProductId, 1);
                    throw new DbUpdateConcurrencyException("Simulated RowVersion conflict");
                }));
        }

        Assert.Equal(EfUnitOfWork.MaxAttempts, attempts);
        factory.Inspect(db =>
            Assert.Equal(5, db.Set<Product>().Single(p => p.Id == factory.ProductId).StockQuantity));
    }
}
