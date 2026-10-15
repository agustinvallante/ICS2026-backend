namespace Dsw2025Tpi.Domain.Exceptions
{
    // Una operación agotó sus intentos por conflictos de concurrencia con otras operaciones.
    // Vive en Domain para que la capa Data pueda lanzarla sin depender de Application.
    public class ConcurrencyConflictException : Exception
    {
        public ConcurrencyConflictException(Exception innerException)
            : base("Los datos fueron modificados por otra operación. Intentá nuevamente.", innerException) { }
    }
}
