using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
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

    private static OrderRequest CreateRequest(Guid customerId, params OrderItemRequest[] items)
    {
        return new OrderRequest(customerId, "Test shipping address", "Test billing address", null, items.ToList());
    }

    private static OrdersManagementService CreateService(Mock<IRepository> repository)
    {
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task<OrderResponse>>>()))
            .Returns((Func<Task<OrderResponse>> operation) => operation());

        return new OrdersManagementService(
            repository.Object,
            unitOfWork.Object,
            NullLogger<OrdersManagementService>.Instance);
    }
}
