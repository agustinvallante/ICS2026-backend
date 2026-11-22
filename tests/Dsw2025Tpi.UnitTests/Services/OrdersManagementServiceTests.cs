using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Exceptions;
using Dsw2025Tpi.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Dsw2025Tpi.UnitTests.Services;

[Trait("Category", "Unit")]
public class OrdersManagementServiceTests
{
    [Fact]
    public async Task CreateOrder_WithValidItemsAndStock_CalculatesTotalAndDecrementsProductStock()
    {
        // Arrange
        var customer = new Customer { Name = "Test customer" };
        var firstProduct = new Product("SKU-01", "First product", 11.25m, 10, null, "CODE-01");
        var secondProduct = new Product("SKU-02", "Second product", 7.50m, 6, null, "CODE-02");
        var request = CreateRequest(
            customer.Id,
            new OrderItemRequest(firstProduct.Id, 3),
            new OrderItemRequest(secondProduct.Id, 2));
        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetById<Customer>(customer.Id, It.IsAny<string[]>()))
            .ReturnsAsync(customer);
        repository.Setup(x => x.GetById<Product>(firstProduct.Id, It.IsAny<string[]>()))
            .ReturnsAsync(firstProduct);
        repository.Setup(x => x.GetById<Product>(secondProduct.Id, It.IsAny<string[]>()))
            .ReturnsAsync(secondProduct);
        repository.Setup(x => x.Update(It.Is<Product>(p => p.Id == firstProduct.Id && p.StockQuantity == 7)))
            .ReturnsAsync(firstProduct);
        repository.Setup(x => x.Update(It.Is<Product>(p => p.Id == secondProduct.Id && p.StockQuantity == 4)))
            .ReturnsAsync(secondProduct);

        Order? addedOrder = null;
        repository.Setup(x => x.Add(It.IsAny<Order>()))
            .Callback<Order>(order => addedOrder = order)
            .Returns((Order order) => Task.FromResult(order));
        var service = CreateService(repository);

        // Act
        var response = await service.CreateOrder(request);

        // Assert
        Assert.Equal(7, firstProduct.StockQuantity);
        Assert.Equal(4, secondProduct.StockQuantity);
        Assert.Equal(48.75m, response.TotalAmount);
        Assert.Equal(
            new[] { (firstProduct.Id, 3, 11.25m, 33.75m), (secondProduct.Id, 2, 7.50m, 15m) },
            response.Items.Select(item => (item.ProductId, item.Quantity, item.UnitPrice, item.Subtotal)));

        var savedOrder = Assert.IsType<Order>(addedOrder);
        Assert.Equal(response.OrderId, savedOrder.Id);
        Assert.Equal(customer.Id, savedOrder.CustomerId);
        Assert.Equal(48.75m, savedOrder.TotalAmount);
        Assert.Equal(
            new[] { ((Guid?)firstProduct.Id, 3, 11.25m), ((Guid?)secondProduct.Id, 2, 7.50m) },
            Assert.IsAssignableFrom<ICollection<OrderItem>>(savedOrder.OrderItems)
                .Select(item => (item.ProductId, item.Quantity, item.UnitPrice)));
        repository.Verify(x => x.Update(firstProduct), Times.Once());
        repository.Verify(x => x.Update(secondProduct), Times.Once());
        repository.Verify(x => x.Add(It.IsAny<Order>()), Times.Once());
    }

    [Fact]
    public async Task CreateOrder_WhenProductStockIsInsufficient_ThrowsInsufficientStockException()
    {
        // Arrange
        var customer = new Customer { Name = "Test customer" };
        var product = new Product("SKU-01", "Test product", 11.25m, 2, null, "CODE-01");
        var request = CreateRequest(customer.Id, new OrderItemRequest(product.Id, 5));
        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetById<Customer>(customer.Id, It.IsAny<string[]>()))
            .ReturnsAsync(customer);
        repository.Setup(x => x.GetById<Product>(product.Id, It.IsAny<string[]>()))
            .ReturnsAsync(product);
        var service = CreateService(repository);

        // Act and assert
        await Assert.ThrowsAsync<InsufficientStockException>(() => service.CreateOrder(request));
        Assert.Equal(2, product.StockQuantity);
        repository.Verify(x => x.Update(It.IsAny<Product>()), Times.Never());
        repository.Verify(x => x.Add(It.IsAny<Order>()), Times.Never());
    }

    [Fact]
    public async Task CreateOrder_WhenCustomerDoesNotExist_ThrowsCustomerNotFoundException()
    {
        // Arrange
        var request = CreateRequest(Guid.NewGuid(), new OrderItemRequest(Guid.NewGuid(), 1));
        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetById<Customer>(request.CustomerId, It.IsAny<string[]>()))
            .ReturnsAsync((Customer?)null);
        var service = CreateService(repository);

        // Act and assert
        await Assert.ThrowsAsync<CustomerNotFoundException>(() => service.CreateOrder(request));
        repository.Verify(x => x.GetById<Product>(It.IsAny<Guid>(), It.IsAny<string[]>()), Times.Never());
        repository.Verify(x => x.Update(It.IsAny<Product>()), Times.Never());
        repository.Verify(x => x.Add(It.IsAny<Order>()), Times.Never());
    }

    // EV-B02 CA1: lecturas, descuentos de stock y alta de la orden ocurren dentro de una única transacción
    [Fact]
    public async Task CreateOrder_WithValidOrder_RunsTheWholeCheckoutInsideOneTransaction()
    {
        // Arrange
        var customer = new Customer { Name = "Test customer" };
        var product = new Product("SKU-01", "Test product", 10m, 5, null, "CODE-01");
        var request = CreateRequest(customer.Id, new OrderItemRequest(product.Id, 2));
        var insideTransaction = false;
        var callsOutsideTransaction = new List<string>();
        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetById<Customer>(customer.Id, It.IsAny<string[]>()))
            .Callback(() => { if (!insideTransaction) callsOutsideTransaction.Add("GetById<Customer>"); })
            .ReturnsAsync(customer);
        repository.Setup(x => x.GetById<Product>(product.Id, It.IsAny<string[]>()))
            .Callback(() => { if (!insideTransaction) callsOutsideTransaction.Add("GetById<Product>"); })
            .ReturnsAsync(product);
        repository.Setup(x => x.Update(product))
            .Callback(() => { if (!insideTransaction) callsOutsideTransaction.Add("Update"); })
            .ReturnsAsync(product);
        repository.Setup(x => x.Add(It.IsAny<Order>()))
            .Callback(() => { if (!insideTransaction) callsOutsideTransaction.Add("Add"); })
            .Returns((Order order) => Task.FromResult(order));
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task<OrderResponse>>>()))
            .Returns(async (Func<Task<OrderResponse>> operation) =>
            {
                insideTransaction = true;
                try { return await operation(); }
                finally { insideTransaction = false; }
            });
        var service = CreateService(repository, unitOfWork);

        // Act
        await service.CreateOrder(request);

        // Assert
        unitOfWork.Verify(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task<OrderResponse>>>()), Times.Once());
        Assert.Empty(callsOutsideTransaction);
        repository.Verify(x => x.Update(product), Times.Once());
        repository.Verify(x => x.Add(It.IsAny<Order>()), Times.Once());
    }

    // EV-B02 CA2: la orden se valida completa antes de descontar stock, así que un ítem posterior
    // sin stock suficiente no deja escrito el descuento de los ítems anteriores
    [Fact]
    public async Task CreateOrder_WhenALaterProductHasInsufficientStock_DoesNotUpdateAnyStock()
    {
        // Arrange
        var customer = new Customer { Name = "Test customer" };
        var firstProduct = new Product("SKU-01", "First product", 10m, 5, null, "CODE-01");
        var secondProduct = new Product("SKU-02", "Second product", 4m, 1, null, "CODE-02");
        var request = CreateRequest(
            customer.Id,
            new OrderItemRequest(firstProduct.Id, 2),
            new OrderItemRequest(secondProduct.Id, 3));
        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetById<Customer>(customer.Id, It.IsAny<string[]>()))
            .ReturnsAsync(customer);
        repository.Setup(x => x.GetById<Product>(firstProduct.Id, It.IsAny<string[]>()))
            .ReturnsAsync(firstProduct);
        repository.Setup(x => x.GetById<Product>(secondProduct.Id, It.IsAny<string[]>()))
            .ReturnsAsync(secondProduct);
        var service = CreateService(repository);

        // Act and assert
        await Assert.ThrowsAsync<InsufficientStockException>(() => service.CreateOrder(request));
        Assert.Equal(5, firstProduct.StockQuantity);
        Assert.Equal(1, secondProduct.StockQuantity);
        repository.Verify(x => x.Update(It.IsAny<Product>()), Times.Never());
        repository.Verify(x => x.Add(It.IsAny<Order>()), Times.Never());
    }

    // Un producto repetido en la orden se valida contra la cantidad total pedida
    [Fact]
    public async Task CreateOrder_WhenRepeatedProductExceedsStockInTotal_ThrowsWithoutUpdatingStock()
    {
        // Arrange
        var customer = new Customer { Name = "Test customer" };
        var product = new Product("SKU-01", "Test product", 10m, 4, null, "CODE-01");
        var request = CreateRequest(
            customer.Id,
            new OrderItemRequest(product.Id, 3),
            new OrderItemRequest(product.Id, 2));
        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetById<Customer>(customer.Id, It.IsAny<string[]>()))
            .ReturnsAsync(customer);
        repository.Setup(x => x.GetById<Product>(product.Id, It.IsAny<string[]>()))
            .ReturnsAsync(product);
        var service = CreateService(repository);

        // Act
        var exception = await Assert.ThrowsAsync<InsufficientStockException>(() => service.CreateOrder(request));

        // Assert
        Assert.Contains("Stock actual: 4, solicitado: 5", exception.Message);
        Assert.Equal(4, product.StockQuantity);
        repository.Verify(x => x.Update(It.IsAny<Product>()), Times.Never());
        repository.Verify(x => x.Add(It.IsAny<Order>()), Times.Never());
    }

    // Caso límite (revisión PR #19): 3 + int.MaxValue desborda int y, sumado sin control, daría un
    // total negativo que pasaría la validación y aumentaría el stock. Debe rechazarse sin escribir nada.
    [Fact]
    public async Task CreateOrder_WhenRepeatedQuantitiesWouldOverflowInt_ThrowsWithoutUpdatingStock()
    {
        // Arrange
        var customer = new Customer { Name = "Test customer" };
        var product = new Product("SKU-01", "Test product", 10m, 5, null, "CODE-01");
        var request = CreateRequest(
            customer.Id,
            new OrderItemRequest(product.Id, 3),
            new OrderItemRequest(product.Id, int.MaxValue));
        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetById<Customer>(customer.Id, It.IsAny<string[]>()))
            .ReturnsAsync(customer);
        repository.Setup(x => x.GetById<Product>(product.Id, It.IsAny<string[]>()))
            .ReturnsAsync(product);
        var service = CreateService(repository);

        // Act
        var exception = await Assert.ThrowsAsync<InsufficientStockException>(() => service.CreateOrder(request));

        // Assert
        Assert.Contains($"Stock actual: 5, solicitado: {3L + int.MaxValue}", exception.Message);
        Assert.Equal(5, product.StockQuantity);
        repository.Verify(x => x.Update(It.IsAny<Product>()), Times.Never());
        repository.Verify(x => x.Add(It.IsAny<Order>()), Times.Never());
    }

    [Fact]
    public async Task CreateOrder_WhenRepeatedProductFitsStock_DecrementsTheTotalOnce()
    {
        // Arrange
        var customer = new Customer { Name = "Test customer" };
        var product = new Product("SKU-01", "Test product", 10m, 5, null, "CODE-01");
        var request = CreateRequest(
            customer.Id,
            new OrderItemRequest(product.Id, 1),
            new OrderItemRequest(product.Id, 2));
        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetById<Customer>(customer.Id, It.IsAny<string[]>()))
            .ReturnsAsync(customer);
        repository.Setup(x => x.GetById<Product>(product.Id, It.IsAny<string[]>()))
            .ReturnsAsync(product);
        repository.Setup(x => x.Update(It.Is<Product>(p => p.Id == product.Id && p.StockQuantity == 2)))
            .ReturnsAsync(product);
        repository.Setup(x => x.Add(It.IsAny<Order>()))
            .Returns((Order order) => Task.FromResult(order));
        var service = CreateService(repository);

        // Act
        var response = await service.CreateOrder(request);

        // Assert
        Assert.Equal(2, product.StockQuantity);
        Assert.Equal(30m, response.TotalAmount);
        Assert.Equal(new[] { 1, 2 }, response.Items.Select(item => item.Quantity));
        repository.Verify(x => x.GetById<Product>(product.Id, It.IsAny<string[]>()), Times.Once());
        repository.Verify(x => x.Update(product), Times.Once());
    }

    // EV-B02 CA3/CA4: un fallo al persistir se informa como error de checkout controlado,
    // conservando la causa original para el log
    [Fact]
    public async Task CreateOrder_WhenOrderPersistenceFails_ThrowsCheckoutFailedWithOriginalCause()
    {
        // Arrange
        var customer = new Customer { Name = "Test customer" };
        var product = new Product("SKU-01", "Test product", 10m, 5, null, "CODE-01");
        var request = CreateRequest(customer.Id, new OrderItemRequest(product.Id, 2));
        var persistenceFailure = new InvalidOperationException("Simulated persistence failure");
        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetById<Customer>(customer.Id, It.IsAny<string[]>()))
            .ReturnsAsync(customer);
        repository.Setup(x => x.GetById<Product>(product.Id, It.IsAny<string[]>()))
            .ReturnsAsync(product);
        repository.Setup(x => x.Update(product))
            .ReturnsAsync(product);
        repository.Setup(x => x.Add(It.IsAny<Order>()))
            .ThrowsAsync(persistenceFailure);
        var service = CreateService(repository);

        // Act
        var exception = await Assert.ThrowsAsync<CheckoutFailedException>(() => service.CreateOrder(request));

        // Assert
        Assert.Same(persistenceFailure, exception.InnerException);
        Assert.DoesNotContain("Simulated", exception.Message);
    }

    // Revisión PR #19: un fallo del commit no se informa como rollback confirmado
    [Fact]
    public async Task CreateOrder_WhenCommitOutcomeIsUnknown_ThrowsCheckoutOutcomeUnknownInsteadOfCheckoutFailed()
    {
        // Arrange
        var commitFailure = new TransactionOutcomeUnknownException(new Exception("Connection lost during commit"));
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task<OrderResponse>>>()))
            .ThrowsAsync(commitFailure);
        var service = CreateService(new Mock<IRepository>(MockBehavior.Strict), unitOfWork);

        // Act
        var exception = await Assert.ThrowsAsync<CheckoutOutcomeUnknownException>(
            () => service.CreateOrder(CreateRequest(Guid.NewGuid(), new OrderItemRequest(Guid.NewGuid(), 1))));

        // Assert
        Assert.Same(commitFailure, exception.InnerException);
        Assert.Contains("Revisá tus órdenes", exception.Message);
        Assert.DoesNotContain("No se registró", exception.Message);
    }

    // Los conflictos de concurrencia no se ocultan detrás del error genérico de checkout
    [Fact]
    public async Task CreateOrder_WhenConcurrencyConflictPersists_PropagatesConflictUnchanged()
    {
        // Arrange
        var conflict = new ConcurrencyConflictException(new Exception("RowVersion mismatch"));
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task<OrderResponse>>>()))
            .ThrowsAsync(conflict);
        var service = CreateService(new Mock<IRepository>(MockBehavior.Strict), unitOfWork);

        // Act
        var exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => service.CreateOrder(CreateRequest(Guid.NewGuid(), new OrderItemRequest(Guid.NewGuid(), 1))));

        // Assert
        Assert.Same(conflict, exception);
    }

    private static OrderRequest CreateRequest(Guid customerId, params OrderItemRequest[] items)
    {
        return new OrderRequest(customerId, "Test shipping address", "Test billing address", null, items.ToList());
    }

    private static OrdersManagementService CreateService(Mock<IRepository> repository, Mock<IUnitOfWork>? unitOfWork = null)
    {
        if (unitOfWork is null)
        {
            unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
            unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task<OrderResponse>>>()))
                .Returns((Func<Task<OrderResponse>> operation) => operation());
        }

        return new OrdersManagementService(
            repository.Object,
            unitOfWork.Object,
            NullLogger<OrdersManagementService>.Instance);
    }
}
