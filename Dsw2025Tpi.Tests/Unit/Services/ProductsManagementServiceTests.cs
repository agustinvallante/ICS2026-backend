using System.Linq.Expressions;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using Dsw2025Tpi.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Dsw2025Tpi.Tests.Unit.Services;

public class ProductsManagementServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Category", "Unit")]
    public async Task AddProduct_WithBlankSku_ThrowsInvalidDataException(string? sku)
    {
        var repository = CreateRepository();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.AddProduct(ProductRequestFactory.Create(sku: sku)));

        repository.Verify(x => x.Add(It.IsAny<Product>()), Times.Never());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Category", "Unit")]
    public async Task AddProduct_WithBlankName_ThrowsInvalidDataException(string? name)
    {
        var repository = CreateRepository();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.AddProduct(ProductRequestFactory.Create(name: name)));

        repository.Verify(x => x.Add(It.IsAny<Product>()), Times.Never());
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    [Trait("Category", "Unit")]
    public async Task AddProduct_WithNegativePrice_ThrowsInvalidDataException(decimal price)
    {
        var repository = CreateRepository();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.AddProduct(ProductRequestFactory.Create(currentUnitPrice: price)));

        repository.Verify(x => x.Add(It.IsAny<Product>()), Times.Never());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddProduct_WithZeroPrice_AddsProductAndReturnsZeroPrice()
    {
        var repository = CreateRepository();
        Product? addedProduct = null;
        repository.Setup(x => x.Add(It.IsAny<Product>()))
            .Callback<Product>(product => addedProduct = product)
            .Returns((Product product) => Task.FromResult(product));
        var service = CreateService(repository);

        var response = await service.AddProduct(ProductRequestFactory.Create(currentUnitPrice: 0m));

        var savedProduct = Assert.IsType<Product>(addedProduct);
        Assert.Equal(0m, savedProduct.CurrentUnitPrice);
        Assert.Equal(0m, response.CurrentUnitPrice);
        repository.Verify(x => x.Add(It.IsAny<Product>()), Times.Once());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    [Trait("Category", "Unit")]
    public async Task AddProduct_WithNegativeStock_ThrowsInvalidDataException(int stock)
    {
        var repository = CreateRepository();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.AddProduct(ProductRequestFactory.Create(stockQuantity: stock)));

        repository.Verify(x => x.Add(It.IsAny<Product>()), Times.Never());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddProduct_WithZeroStock_ThrowsInvalidDataExceptionWithoutWriting()
    {
        var repository = CreateRepository();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.AddProduct(ProductRequestFactory.Create(stockQuantity: 0)));

        repository.Verify(x => x.Add(It.IsAny<Product>()), Times.Never());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddProduct_WithDuplicateSku_ThrowsDuplicatedEntityExceptionWithoutWriting()
    {
        var existing = CreateProduct(sku: "SKU-001");
        var repository = CreateRepository(existing);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<Dsw2025Tpi.Application.Exceptions.DuplicatedEntityException>(() =>
            service.AddProduct(ProductRequestFactory.Create()));

        repository.Verify(x => x.Add(It.IsAny<Product>()), Times.Never());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddProduct_WithValidRequest_AddsProductAndReturnsItsValues()
    {
        var repository = CreateRepository();
        var request = ProductRequestFactory.Create(
            sku: "SKU-NEW",
            internalCode: "PRD-NEW",
            name: "New product",
            description: "Description",
            currentUnitPrice: 21.75m,
            stockQuantity: 8);
        Product? addedProduct = null;
        repository.Setup(x => x.Add(It.IsAny<Product>()))
            .Callback<Product>(product => addedProduct = product)
            .Returns((Product product) => Task.FromResult(product));
        var service = CreateService(repository);

        var response = await service.AddProduct(request);

        var savedProduct = Assert.IsType<Product>(addedProduct);
        Assert.Equal(savedProduct.Id, response.Id);
        Assert.Equal(request.Sku, savedProduct.Sku);
        Assert.Equal(request.InternalCode, savedProduct.InternalCode);
        Assert.Equal(request.Name, savedProduct.Name);
        Assert.Equal(request.Description, savedProduct.Description);
        Assert.Equal(request.CurrentUnitPrice, savedProduct.CurrentUnitPrice);
        Assert.Equal(request.StockQuantity, savedProduct.StockQuantity);
        Assert.True(savedProduct.IsActive);
        Assert.Equal(savedProduct.Sku, response.Sku);
        Assert.Equal(savedProduct.InternalCode, response.InternalCode);
        Assert.Equal(savedProduct.Name, response.Name);
        Assert.Equal(savedProduct.Description, response.Description);
        Assert.Equal(savedProduct.CurrentUnitPrice, response.CurrentUnitPrice);
        Assert.Equal(savedProduct.StockQuantity, response.StockQuantity);
        Assert.True(response.IsActive);
        repository.Verify(x => x.Add(It.IsAny<Product>()), Times.Once());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateProduct_WithMissingId_ThrowsKeyNotFoundExceptionWithoutWriting()
    {
        var repository = CreateRepository();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateProduct(Guid.NewGuid(), ProductRequestFactory.Create()));

        repository.Verify(x => x.First<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()), Times.Never());
        repository.Verify(x => x.Update(It.IsAny<Product>()), Times.Never());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateProduct_WithInactiveProduct_ThrowsKeyNotFoundExceptionWithoutWriting()
    {
        var inactiveProduct = CreateProduct(isActive: false);
        var repository = CreateRepository(inactiveProduct);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateProduct(inactiveProduct.Id, ProductRequestFactory.Create()));

        repository.Verify(x => x.First<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()), Times.Never());
        repository.Verify(x => x.Update(It.IsAny<Product>()), Times.Never());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Category", "Unit")]
    public async Task UpdateProduct_WithBlankSku_ThrowsInvalidDataExceptionWithoutMutation(string? sku)
    {
        var product = CreateProduct();
        var originalSku = product.Sku;
        var repository = CreateRepository(product);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.UpdateProduct(product.Id, ProductRequestFactory.Create(sku: sku)));

        Assert.Equal(originalSku, product.Sku);
        repository.Verify(x => x.Update(It.IsAny<Product>()), Times.Never());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Category", "Unit")]
    public async Task UpdateProduct_WithBlankName_ThrowsInvalidDataExceptionWithoutMutation(string? name)
    {
        var product = CreateProduct();
        var originalName = product.Name;
        var repository = CreateRepository(product);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.UpdateProduct(product.Id, ProductRequestFactory.Create(name: name)));

        Assert.Equal(originalName, product.Name);
        repository.Verify(x => x.Update(It.IsAny<Product>()), Times.Never());
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    [Trait("Category", "Unit")]
    public async Task UpdateProduct_WithNegativePrice_ThrowsInvalidDataExceptionWithoutMutation(decimal price)
    {
        var product = CreateProduct();
        var originalPrice = product.CurrentUnitPrice;
        var repository = CreateRepository(product);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.UpdateProduct(product.Id, ProductRequestFactory.Create(currentUnitPrice: price)));

        Assert.Equal(originalPrice, product.CurrentUnitPrice);
        repository.Verify(x => x.Update(It.IsAny<Product>()), Times.Never());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateProduct_WithZeroPrice_UpdatesProductAndReturnsZeroPrice()
    {
        var product = CreateProduct();
        var repository = CreateRepository(product);
        var service = CreateService(repository);

        var response = await service.UpdateProduct(
            product.Id,
            ProductRequestFactory.Create(currentUnitPrice: 0m));

        Assert.Equal(0m, product.CurrentUnitPrice);
        Assert.Equal(0m, response.CurrentUnitPrice);
        repository.Verify(x => x.Update(product), Times.Once());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    [Trait("Category", "Unit")]
    public async Task UpdateProduct_WithNegativeStock_ThrowsInvalidDataExceptionWithoutMutation(int stock)
    {
        var product = CreateProduct();
        var originalStock = product.StockQuantity;
        var repository = CreateRepository(product);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.UpdateProduct(product.Id, ProductRequestFactory.Create(stockQuantity: stock)));

        Assert.Equal(originalStock, product.StockQuantity);
        repository.Verify(x => x.Update(It.IsAny<Product>()), Times.Never());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateProduct_WithZeroStock_ThrowsInvalidDataExceptionWithoutMutation()
    {
        var product = CreateProduct();
        var originalSku = product.Sku;
        var originalInternalCode = product.InternalCode;
        var originalName = product.Name;
        var originalDescription = product.Description;
        var originalPrice = product.CurrentUnitPrice;
        var originalStock = product.StockQuantity;
        var repository = CreateRepository(product);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.UpdateProduct(
                product.Id,
                ProductRequestFactory.Create(
                    sku: "SKU-UPDATED",
                    internalCode: "PRD-UPDATED",
                    name: "Updated product",
                    description: "Updated description",
                    currentUnitPrice: 12.50m,
                    stockQuantity: 0)));

        Assert.Equal(originalSku, product.Sku);
        Assert.Equal(originalInternalCode, product.InternalCode);
        Assert.Equal(originalName, product.Name);
        Assert.Equal(originalDescription, product.Description);
        Assert.Equal(originalPrice, product.CurrentUnitPrice);
        Assert.Equal(originalStock, product.StockQuantity);
        repository.Verify(x => x.Update(It.IsAny<Product>()), Times.Never());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateProduct_WithDuplicateSkuOwnedByAnotherProduct_ThrowsWithoutMutation()
    {
        var product = CreateProduct(sku: "SKU-OWN");
        var otherProduct = CreateProduct(sku: "SKU-OTHER");
        var originalName = product.Name;
        var repository = CreateRepository(product, otherProduct);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<Dsw2025Tpi.Application.Exceptions.DuplicatedEntityException>(() =>
            service.UpdateProduct(
                product.Id,
                ProductRequestFactory.Create(sku: otherProduct.Sku)));

        Assert.Equal("SKU-OWN", product.Sku);
        Assert.Equal(originalName, product.Name);
        repository.Verify(x => x.Update(It.IsAny<Product>()), Times.Never());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateProduct_WithItsOwnSku_UpdatesProduct()
    {
        var product = CreateProduct(sku: "SKU-OWN");
        var repository = CreateRepository(product);
        var request = ProductRequestFactory.Create(
            sku: product.Sku,
            internalCode: "PRD-UPDATED",
            name: "Updated product",
            description: "Updated description",
            currentUnitPrice: 18.25m,
            stockQuantity: 11);
        var service = CreateService(repository);

        var response = await service.UpdateProduct(product.Id, request);

        Assert.Equal(product.Id, response.Id);
        Assert.Equal("SKU-OWN", product.Sku);
        Assert.Equal(request.InternalCode, product.InternalCode);
        Assert.Equal(request.Name, product.Name);
        Assert.Equal(request.Description, product.Description);
        Assert.Equal(request.CurrentUnitPrice, product.CurrentUnitPrice);
        Assert.Equal(request.StockQuantity, product.StockQuantity);
        Assert.Equal(request.Sku, response.Sku);
        Assert.Equal(request.InternalCode, response.InternalCode);
        Assert.Equal(request.Name, response.Name);
        Assert.Equal(request.Description, response.Description);
        Assert.Equal(request.CurrentUnitPrice, response.CurrentUnitPrice);
        Assert.Equal(request.StockQuantity, response.StockQuantity);
        Assert.Equal(product.IsActive, response.IsActive);
        repository.Verify(x => x.Update(product), Times.Once());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateProduct_WithAvailableSku_UpdatesProduct()
    {
        var product = CreateProduct(sku: "SKU-OWN");
        var otherProduct = CreateProduct(sku: "SKU-OTHER");
        var repository = CreateRepository(product, otherProduct);
        var request = ProductRequestFactory.Create(sku: "SKU-FREE", name: "Renamed product");
        var service = CreateService(repository);

        var response = await service.UpdateProduct(product.Id, request);

        Assert.Equal("SKU-FREE", product.Sku);
        Assert.Equal("Renamed product", product.Name);
        Assert.Equal("SKU-FREE", response.Sku);
        Assert.Equal("Renamed product", response.Name);
        repository.Verify(x => x.Update(product), Times.Once());
    }

    private static ProductsManagementService CreateService(Mock<IRepository> repository)
    {
        return new ProductsManagementService(
            repository.Object,
            NullLogger<ProductsManagementService>.Instance);
    }

    private static Mock<IRepository> CreateRepository(params Product[] products)
    {
        var repository = new Mock<IRepository>();
        repository.Setup(x => x.GetById<Product>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .Returns((Guid id, string[] _) => Task.FromResult<Product?>(products.FirstOrDefault(product => product.Id == id)));
        repository.Setup(x => x.First<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()))
            .Returns((Expression<Func<Product, bool>> predicate, string[] _) =>
                Task.FromResult(products.AsQueryable().FirstOrDefault(predicate)));
        repository.Setup(x => x.Add(It.IsAny<Product>()))
            .Returns((Product product) => Task.FromResult(product));
        repository.Setup(x => x.Update(It.IsAny<Product>()))
            .Returns((Product product) => Task.FromResult(product));
        return repository;
    }

    private static Product CreateProduct(
        string sku = "SKU-EXISTING",
        bool isActive = true)
    {
        return new Product(
            sku,
            "Existing product",
            9.99m,
            4,
            "Existing description",
            "PRD-EXISTING")
        {
            IsActive = isActive
        };
    }
}
