using Dsw2025Tpi.Domain.Exceptions;
using Dsw2025Tpi.Domain.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Dsw2025Tpi.Data.Repositories;

public class EfUnitOfWork : IUnitOfWork
{
    public const int MaxAttempts = 3;

    // Error 1205 de SQL Server: la transacción fue elegida como víctima de un deadlock
    private const int SqlDeadlockVictim = 1205;

    private readonly Dsw2025TpiContext _context;
    private readonly ILogger<EfUnitOfWork> _logger;

    public EfUnitOfWork(Dsw2025TpiContext context, ILogger<EfUnitOfWork> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation)
    {
        for (var attempt = 1; ; attempt++)
        {
            // Cada intento usa una transacción nueva; la del intento fallido se descarta al salir del bloque
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var result = await operation();
                await transaction.CommitAsync();
                return result;
            }
            catch (Exception ex)
            {
                await RollbackAsync(transaction);

                // Descarta las entidades del intento fallido para que el próximo intento relea datos frescos
                _context.ChangeTracker.Clear();

                if (!IsConcurrencyConflict(ex))
                    throw;

                // Se agotaron los intentos: se informa el conflicto sin exponer detalles de EF/SQL
                if (attempt >= MaxAttempts)
                    throw new ConcurrencyConflictException(ex);

                _logger.LogWarning("Conflicto de concurrencia en el intento {Attempt} de {MaxAttempts}; se reintenta la operación completa.", attempt, MaxAttempts);
            }
        }
    }

    // Conflicto de concurrencia: RowVersion desactualizado o víctima de deadlock entre transacciones
    private static bool IsConcurrencyConflict(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException)
                return true;

            if (current is SqlException sqlException && sqlException.Number == SqlDeadlockVictim)
                return true;
        }

        return false;
    }

    private async Task RollbackAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch (Exception rollbackException)
        {
            // SQL Server ya pudo haber abortado la transacción (ej. deadlock); no se oculta la excepción original
            _logger.LogWarning(rollbackException, "No se pudo hacer rollback explícito de la transacción.");
        }
    }
}
