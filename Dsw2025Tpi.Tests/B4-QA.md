# Evidencia B4 y correcciones de revisión

Fecha: 2026-10-15. Rama: `feature/at-b03-tests-integracion`, sobre `84076c5` con los cambios locales de esta revisión.

## Resultado

Los tres bloqueantes señalados quedan cubiertos por código y pruebas ejecutadas: **76 casos aprobados, 0 fallos, 0 omitidos**, sumando las dos ejecuciones que se detallan abajo. SQL Server fue ejecutado efectivamente; no se usó un skip ni se sustituyó el proveedor por SQLite para B4.

1. **Migraciones de ambos contextos:** `SqlServerPersistenceTests.BothContexts_MigrateAndMigrateAgain_LeaveNoPendingMigrationsAndPreserveData` crea una base SQL Server nueva, aplica las migraciones de `AuthenticateContext` y `Dsw2025TpiContext`, compara el historial completo con la unión de las migraciones esperadas y comprueba que ninguno tiene pendientes. Luego vuelve a aplicar ambas en nuevos scopes y verifica historial sin cambios, ausencia de pendientes y conservación de datos de Identity, clientes y productos.
2. **Rollback en SQL Server:** `SqlServerPersistenceTests.CreateOrder_WhenLaterValidationOrSaveFails_RollsBackOnSqlServer` ejecuta dos casos mediante HTTP: stock insuficiente en el segundo producto y excepción de persistencia al guardar la orden después de actualizar el stock de ambos productos. Verifica HTTP 400/500 y, desde un scope nuevo, ausencia de órdenes/ítems y stock original de ambos productos.
3. **Estados fuera del enum:** el servicio ahora requiere tanto `Enum.TryParse` como `Enum.IsDefined`. La teoría HTTP prueba `NOT_A_STATUS`, `999`, `-1` y `0`, exige 400 y verifica que el estado previamente guardado (`PROCESSING`) no cambie.

## Ejecución

Entorno: Windows, runtime .NET 8 y SQL Server local `.\SQLEXPRESS` con autenticación integrada. Cada caso SQL usa una base nueva `ATB03_QA_<guid>` y la elimina al disponer su factory; no usa la base de negocio. Ambos contextos comparten esa base, igual que en producción.

```powershell
dotnet build Dsw2025Tpi.sln --no-restore --verbosity minimal
# Build succeeded: 0 warnings, 0 errors.

dotnet test Dsw2025Tpi.Tests/Dsw2025Tpi.Tests.csproj --no-restore --filter 'Provider!=SqlServer' --logger 'trx;LogFileName=b03-fast.trx' --results-directory TestResults/AT-B03
# Passed: 73, Failed: 0, Skipped: 0.

dotnet test Dsw2025Tpi.Tests/Dsw2025Tpi.Tests.csproj --no-build --no-restore --filter 'Provider=SqlServer' --logger 'trx;LogFileName=b03-sqlserver.trx' --results-directory TestResults/AT-B03
# Passed: 3, Failed: 0, Skipped: 0.
```

Los resultados detallados de esta ejecución están en `TestResults/AT-B03/b03-fast.trx` y `TestResults/AT-B03/b03-sqlserver.trx` (archivos locales ignorados por Git). Se pueden regenerar con los comandos anteriores y adjuntar a la PR/tarjeta. La ejecución SQL requirió salir del entorno restringido para usar la autenticación integrada del usuario.

`Testing` sigue omitiendo la inicialización y carga JSON de producción. Esto no omite B4: la factory SQL aplica explícitamente las migraciones reales; el caso dedicado prueba además la reaplicación. No se afirma haber probado el despliegue o la carga de administradores de `Program.Main`, ni las pruebas de concurrencia de AT-B04, que corresponde a otra rama.

Esta evidencia resuelve los tres puntos técnicos. La aprobación de PR, integración y estado de la tarjeta todavía deben registrarse en el proceso del equipo.
