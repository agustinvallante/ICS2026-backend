namespace Dsw2025Tpi.Application.Exceptions
{
    // El checkout falló por un error inesperado (ej. al persistir la orden) y la transacción se revirtió:
    // no se registró la orden ni se descontó stock. La causa original queda en InnerException para el log.
    public class CheckoutFailedException : Exception
    {
        public CheckoutFailedException(Exception innerException)
            : base("No se pudo completar la compra. No se registró la orden ni se descontó stock; podés intentarlo nuevamente.", innerException) { }
    }
}
