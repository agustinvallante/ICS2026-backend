namespace Dsw2025Tpi.Api.Contract
{
    // Valores de ApiError.Code: estables para que el frontend reaccione al tipo de error sin parsear el mensaje
    public static class ErrorCodes
    {
        public const string InsufficientStock = "INSUFFICIENT_STOCK";
        public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
        public const string CheckoutFailed = "CHECKOUT_FAILED";
    }
}
