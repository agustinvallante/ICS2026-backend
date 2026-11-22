namespace Dsw2025Tpi.Application.Exceptions
{
    // El checkout falló por un error inesperado ANTES del commit (ej. al persistir la orden), así que la
    // transacción nunca se confirmó: no se registró la orden ni se descontó stock. Un fallo durante el
    // commit se informa con CheckoutOutcomeUnknownException. La causa original queda en InnerException.
    public class CheckoutFailedException : Exception
    {
        public CheckoutFailedException(Exception innerException)
            : base("No se pudo completar la compra. No se registró la orden ni se descontó stock; podés intentarlo nuevamente.", innerException) { }
    }
}
