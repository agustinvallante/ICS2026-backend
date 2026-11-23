namespace Dsw2025Tpi.Domain.Interfaces;

public interface IUnitOfWork
{
    // Ejecuta la operación dentro de una única transacción: commit completo o rollback completo.
    // Ante un conflicto de concurrencia la operación se vuelve a ejecutar desde el principio,
    // por lo que no debe tener efectos fuera de la base de datos.
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation);
}
