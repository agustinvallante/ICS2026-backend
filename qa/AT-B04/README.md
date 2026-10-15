# QA de AT-B04 — control de concurrencia de stock

Fecha: 2026-10-15. Rama revisada: `feature/at-b04-stock-concurrency`.
Commit revisado: `913c2ea640cc11decf5495917396d07ed2a38afd`.

## Dictamen

**QA funcional favorable para CA1–CA4 en los escenarios ejecutados. La DoD completa todavía no está acreditada.**

No se detectaron fallas en las pruebas realizadas. Esto no demuestra ausencia de errores bajo cualquier carga posible. El commit original no incluye un proyecto de pruebas ni casos automatizados; la solución tiene únicamente los cuatro proyectos de producción. El directorio local `Dsw2025Tpi.Tests` solo contiene restos de compilación, que no son evidencia ejecutable de esta rama.

El verificador de esta carpeta fue agregado durante esta revisión de QA. No modifica código de producción, no está integrado a la solución ni es descubierto por `dotnet test`. Debe incorporarse cobertura mantenible a la suite/CI del equipo para cerrar ese pendiente de calidad.

## Evidencia ejecutada

- Recompilación completa: `dotnet build Dsw2025Tpi.sln --no-restore --no-incremental --verbosity minimal`: 0 errores y 0 advertencias.
- `dotnet test Dsw2025Tpi.sln --no-build --no-restore --verbosity normal`: finaliza correctamente, pero no descubre ni ejecuta pruebas. No cuenta como suite aprobada.
- `git diff HEAD^ HEAD --check`: sin errores de whitespace.
- `dotnet format Dsw2025Tpi.sln --verify-no-changes --no-restore --include Dsw2025Tpi.Data/Repositories/EfUnitOfWork.cs Dsw2025Tpi.Domain/Interfaces/IUnitOfWork.cs Dsw2025Tpi.Domain/Exceptions/ConcurrencyConflictException.cs --verbosity minimal`: código de salida 0. Alcance: los tres archivos nuevos de código no generado; no certifica el formato de todo el repositorio.
- Verificador `StockQa.csproj`: todas las comprobaciones pasan. Salida completa en `results.txt`.

Se utilizó SQL Server real (`.\SQLEXPRESS`), no EF InMemory ni SQLite. Cada ejecución crea una base `ATB04_QA_<guid>`, aplica las migraciones y elimina esa base al terminar. No se usó la base de negocio. La restauración inicial de NuGet y la conexión integrada a SQL requirieron ejecutar fuera del entorno restringido.

| Criterio | Prueba y resultado |
| --- | --- |
| CA1 | 20 rondas de dos operaciones con contextos independientes. Una barrera obliga a ambas a leer la misma última unidad antes de actualizar. En cada ronda: una creación, un rechazo por stock, una orden persistida y stock final 0. |
| CA2 | 5 rondas de 40 solicitudes sobre stock inicial 10. Stock final nunca negativo y conservación `stock final + órdenes exitosas = 10`. Cantidad de órdenes persistidas coincide con las respuestas exitosas. |
| CA3 | Una conexión externa actualiza el producto después de su lectura. Se comprobó que la operación vuelve a leerlo. Con stock externo 0, rechaza sin crear orden; con stock externo 2, confirma y deja stock 1. |
| CA4 | Orden con dos productos: el primero tiene stock 5 y solicita 2; el segundo tiene stock 0. La operación rechaza y el primer producto conserva stock 5; no se registra orden. |

Pruebas adicionales aprobadas: producto repetido en dos líneas sin sobreventa; agotamiento de los tres intentos sin descontar stock ni registrar orden; traducción de falta de stock a HTTP 400 y de conflictos a HTTP 409, sin detalles internos en la respuesta.

Se levantó un host HTTP de QA con el controlador real de órdenes, repositorio y unidad de trabajo reales, el middleware de errores y JWT. Se verificaron POST sin token (401), POST exitoso (201 y Location), GET de esa orden (200), segundo POST sin stock (400), stock final 0 y exactamente una orden persistida. Este host usa configuración aislada: no ejecuta el arranque completo de `Program.Main`, su carga de administradores ni la configuración de despliegue.

### Observación de carga

En la ejecución guardada, las cinco rondas crearon respectivamente 10, 9, 7, 7 y 7 órdenes. Los stocks finales fueron 0, 1, 3, 3 y 3. Hubo rechazos por conflicto al agotarse el límite de tres intentos, incluso cuando quedaba stock. No contradice CA2 ni implica sobreventa; es una limitación de disponibilidad bajo contención. Conviene documentar el HTTP 409 y el reintento para el consumidor. La traducción de conflictos a 409 se verificó directamente en el middleware, no mediante un conflicto forzado en HTTP.

## Evaluación de la DoD

| Apartado | Estado y evidencia |
| --- | --- |
| Cumplimiento funcional | Favorable en los escenarios ejecutados para CA1–CA4 y casos adicionales. |
| Backend: compilación | Cumple en este entorno: recompilación con 0 errores y 0 advertencias. |
| Backend: migraciones | Cumple sobre base nueva de QA: todas aplicadas, sin pendientes. Falta evidencia de aplicación en la base del entorno de integración/QA del equipo. No se ensayó migrar una base preexistente con datos de negocio. |
| Backend: endpoints y errores | Respuestas 201/200/400/401 verificadas por HTTP en host aislado. Conflictos 409 verificados en middleware. Arranque completo y configuración desplegada no verificados. |
| Autenticación/autorización/ownership | Se verificó la exigencia de JWT. La historia no modifica las reglas de roles ni ownership; no se hizo una auditoría completa de seguridad. |
| Frontend y UI | No aplica al cambio de backend. Este repositorio no aporta evidencia de compilación ni pruebas del frontend. Si el equipo exige regresión del checkout en navegador, queda pendiente. |
| Pruebas automatizadas y regresiones | Pendiente para la DoD: el commit original no incorpora pruebas. El verificador agregado aquí pasa, pero queda fuera de la solución y CI. No hay una suite existente en esta rama que permita acreditar ausencia de regresiones. |
| Validación funcional manual | Los flujos se comprobaron mediante un verificador automatizado y HTTP. No se acreditó un recorrido manual completo de la aplicación. |
| Secretos en el cambio | No se observaron nuevas credenciales en el diff de la implementación. El verificador genera una clave JWT efímera en memoria. Esto no constituye una auditoría de todo el historial. |
| Linter y formato | Compilación y comprobaciones de formato acotadas aprobadas. No se identificó configuración adicional de linter/formato específica del proyecto. |
| PR y revisión | El commit aparece en `origin/feature/at-b04-stock-concurrency` según las referencias locales. El usuario informó revisión estática favorable. No se verificaron PR, aprobación formal ni resolución de comentarios en el servicio remoto. |
| Integración | Pendiente: según las referencias locales, el commit solo está en la rama de la funcionalidad, no en `development` ni `main`. No se hizo fetch ni merge. Falta recompilar y probar después de integrar. |
| Documentación | El código documenta RowVersion, reintentos y deadlocks mediante comentarios. El commit original no actualiza documentación de uso o evidencias de QA. Este informe aporta evidencia local; queda vincularla a la tarjeta/PR y documentar la migración y el 409. |
| Estado de la tarjeta | No verificable desde este repositorio: transiciones In Progress → In Code Review → In QA, aprobación y evidencias adjuntas. |

No corresponde mover la tarjeta a Done solamente con la revisión estática y este resultado funcional. Deben cerrarse los pendientes de pruebas, revisión formal, integración, documentación y evidencias del proceso. La falta de evidencia de un punto no demuestra que el equipo no lo haya realizado.

## Reproducción

Requiere SDK compatible con net8.0, SQL Server local `.\SQLEXPRESS` y un usuario con autenticación integrada habilitada y permisos para crear/eliminar su base de QA.

```powershell
dotnet build qa/AT-B04/StockQa.csproj
dotnet run --project qa/AT-B04/StockQa.csproj --no-build --no-restore
```

Un resultado exitoso termina con `Failures: 0`. El verificador comprueba invariantes y propaga errores con código de salida distinto de cero. La base se elimina en `finally` cuando la migración inicial terminó correctamente. Si se interrumpe el proceso o falla una migración durante la creación, puede quedar una base con el prefijo indicado: identificar exactamente la ejecución antes de cualquier limpieza manual.
