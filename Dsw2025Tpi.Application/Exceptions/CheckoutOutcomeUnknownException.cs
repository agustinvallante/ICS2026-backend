namespace Dsw2025Tpi.Application.Exceptions
{
    // Falló la confirmación (COMMIT) del checkout: la orden pudo haberse registrado aunque el cliente
    // reciba un error. El mensaje no afirma un rollback y pide revisar las órdenes antes de repetir la compra.
    public class CheckoutOutcomeUnknownException : Exception
    {
        public CheckoutOutcomeUnknownException(Exception innerException)
            : base("No pudimos confirmar si tu compra se registró. Revisá tus órdenes antes de volver a intentarlo.", innerException) { }
    }
}
