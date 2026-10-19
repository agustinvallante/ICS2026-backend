using Dsw2025Tpi.Application.Dtos;

namespace Dsw2025Tpi.Tests.Support;

public static class ProductRequestFactory
{
    public static ProductModel.Request Create(
        string? sku = "SKU-001",
        string internalCode = "PRD-001",
        string? name = "Test product",
        string? description = "Test description",
        decimal currentUnitPrice = 12.50m,
        int stockQuantity = 5)
    {
        return new ProductModel.Request(
            sku!,
            internalCode,
            name!,
            description,
            currentUnitPrice,
            stockQuantity);
    }
}
