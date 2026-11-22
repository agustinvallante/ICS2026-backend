
namespace Dsw2025Tpi.Application.Exceptions
{
    public class InsufficientStockException : Exception
    {
        public InsufficientStockException(string productName, int available, long requested)
            : base($"El producto '{productName}' no tiene suficiente stock. Stock actual: {available}, solicitado: {requested}.") { }
    }
}
