using Dsw2025Tpi.Api.Contract;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;

namespace Dsw2025Tpi.Api.NewFolder
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                
                _logger.LogError(ex, "Se capturó una excepción no controlada: {ExceptionType}", ex.GetType().Name);
                await HandleExceptionAsync(context, ex);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            HttpStatusCode status;
            string message;

            switch (exception)
            {
                // 400 Bad Request
                case InvalidOrderDataException or
                     InvalidDataException or
                     InvalidOrderStatusException or
                     ArgumentException or
                     InsufficientStockException or
                     ArgumentNullException: 
                    status = HttpStatusCode.BadRequest;
                    message = exception.Message;
                    break;

                // 404 Not Found
                case CustomerNotFoundException or
                     OrderNotFoundException or
                     KeyNotFoundException:
                    status = HttpStatusCode.NotFound;
                    message = exception.Message;
                    break;

                // 409 Conflict: duplicado detectado por la validación de Application
                case DuplicatedEntityException:
                    status = HttpStatusCode.Conflict;
                    message = exception.Message;
                    break;

                // 409 Conflict: duplicado rechazado por un índice único de SQL Server (ej. altas concurrentes)
                case DbUpdateException dbUpdateException when TryGetProductUniqueIndexMessage(dbUpdateException, out var conflictMessage):
                    status = HttpStatusCode.Conflict;
                    message = conflictMessage;
                    break;

                // errores de autenticación/autorización
                case UnauthorizedAccessException:
                    status = HttpStatusCode.Forbidden; // 403 Forbidden
                    message = "No tienes permiso para acceder a este recurso.";
                    break;

               

                // Manejador por defecto 
                default:
                    status = HttpStatusCode.InternalServerError;
                    message = "Ocurrió un error inesperado en el servidor.";
                    break;
            }

            var result = JsonSerializer.Serialize(new ApiError("" ,message));
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)status;
            return context.Response.WriteAsync(result);
        }

        // Error 2601 de SQL Server: fila duplicada en un índice único
        private const int SqlUniqueIndexViolation = 2601;

        // Solo traduce a 409 las violaciones de los índices únicos de Products (AT-B02);
        // cualquier otro error de base sigue al manejo por defecto (500)
        private static bool TryGetProductUniqueIndexMessage(DbUpdateException exception, out string message)
        {
            message = string.Empty;

            if (exception.InnerException is not SqlException sqlException || sqlException.Number != SqlUniqueIndexViolation)
                return false;

            // SQL Server informa el nombre del índice entre comillas simples: ... unique index 'UX_Products_Sku'
            if (sqlException.Message.Contains($"'{Dsw2025TpiContext.ProductSkuUniqueIndex}'", StringComparison.Ordinal))
            {
                message = "Ya existe un producto con el mismo SKU.";
                return true;
            }

            if (sqlException.Message.Contains($"'{Dsw2025TpiContext.ProductInternalCodeUniqueIndex}'", StringComparison.Ordinal))
            {
                message = "Ya existe un producto con el mismo código interno.";
                return true;
            }

            return false;
        }
    }
}