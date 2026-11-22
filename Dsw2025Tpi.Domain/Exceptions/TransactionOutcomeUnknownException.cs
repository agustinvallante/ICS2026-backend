namespace Dsw2025Tpi.Domain.Exceptions
{
    // Falló el COMMIT de una transacción: la base pudo haberla confirmado aunque la aplicación no recibió
    // la respuesta (ej. corte de conexión). No se puede afirmar que se revirtió ni reintentar a ciegas.
    // Vive en Domain para que la capa Data pueda lanzarla sin depender de Application.
    public class TransactionOutcomeUnknownException : Exception
    {
        public TransactionOutcomeUnknownException(Exception innerException)
            : base("No se pudo determinar si la transacción se confirmó.", innerException) { }
    }
}
