# Trabajo Práctico Integrador

**Ingeniería y Calidad de Software — Backend**

## Introducción

El proyecto consiste en el desarrollo de una plataforma de comercio electrónico (*e-commerce*).

En su primera etapa, el objetivo fue implementar el módulo de órdenes, permitiendo su creación, consulta y gestión, junto con las operaciones necesarias sobre productos, clientes y stock.

## Visión general del producto

Del relevamiento preliminar se identificaron los siguientes requisitos:

- Los visitantes pueden consultar productos sin necesidad de registrarse o iniciar sesión.
- Para realizar un pedido, el usuario debe haber iniciado sesión.
- Una orden debe incluir la información correspondiente al cliente, envío y facturación.
- Antes de registrar una orden, se debe verificar la disponibilidad de stock de los productos solicitados.
- Si la creación de la orden es exitosa, debe actualizarse el stock de los productos involucrados.
- Se deben poder consultar órdenes individuales y listar órdenes aplicando filtros.
- Se debe permitir la actualización del estado de una orden según su ciclo de vida.
- Los administradores pueden gestionar productos (alta, modificación y baja) y actualizar el estado de las órdenes.
- Los clientes pueden crear y consultar sus órdenes.

[Consultar documento completo de requisitos](https://frtutneduar.sharepoint.com/:b:/s/DSW2025/ETueAd4rTe1Gilj_Yfi64RYB5oz9s2dOamxKSfMFPREbiA?e=azZcwg)

## Alcance para el primer parcial

> [!IMPORTANT]
> Del apartado **IMPLEMENTACIÓN** (página 7 del documento de requisitos), se contempla el desarrollo hasta el punto **6**, inclusive.

## Tecnologías utilizadas

- **Lenguaje:** C# 12.0.
- **Plataforma:** .NET 8.
- **Framework web:** ASP.NET Core Web API.
- **Persistencia:** Entity Framework Core.
- **Base de datos:** SQL Server.
- **Autenticación:** ASP.NET Core Identity y JWT.

## Requisitos previos

Antes de ejecutar el proyecto, es necesario contar con:

- SDK de .NET 8.
- Una instancia compatible de SQL Server.
- PowerShell, para ejecutar el script de configuración inicial en Windows.
- El repositorio del Backend clonado localmente.

## Configuración inicial

### Configuración del secreto JWT

La API utiliza tokens JWT firmados mediante HMAC-SHA256 para autenticar a los usuarios.

Por motivos de seguridad, la clave utilizada para firmar y validar los tokens **no debe almacenarse en `appsettings.json` ni en ningún archivo versionado del repositorio**.

Cada desarrollador debe configurar una clave propia en su entorno local antes de ejecutar la aplicación.

#### Configuración automática en desarrollo

El repositorio incluye dos scripts en el directorio `scripts/`:

- `setup-jwt-secret.ps1`: genera una clave criptográficamente aleatoria de 256 bits y la configura mediante .NET User Secrets.
- `setup-jwt-secret.cmd`: permite ejecutar el script anterior mediante doble clic en Windows.

**Procedimiento:**

1. Clonar el repositorio y comprobar que el SDK de .NET esté instalado.
2. Acceder al directorio `scripts/`.
3. Ejecutar `setup-jwt-secret.cmd`.
4. Esperar el mensaje de confirmación de configuración exitosa.

También puede ejecutarse desde PowerShell, ubicado en la raíz del repositorio:

```powershell
.\scripts\setup-jwt-secret.cmd
```

El script genera una clave y la almacena mediante **.NET User Secrets**, utilizando el identificador configurado en `Dsw2025Tpi.Api.csproj`.

El valor generado no se muestra por consola ni se almacena dentro del repositorio.

La configuración se carga automáticamente cuando la aplicación se ejecuta en el entorno `Development`.

> [!NOTE]
> User Secrets es una herramienta para desarrollo local. Su almacenamiento no está cifrado y no debe utilizarse como gestor de secretos de producción.

### Configuración de la base de datos

La aplicación utiliza SQL Server mediante Entity Framework Core.

Antes de iniciar la API, se debe comprobar que la conexión a la base de datos esté correctamente configurada y que el esquema requerido se encuentre disponible.

Si se trabaja con una base de datos nueva, será necesario aplicar las migraciones correspondientes antes de utilizar los endpoints que dependen de la persistencia.

## Ejecución de la API

Una vez configurado el secreto JWT y preparada la base de datos, desde la raíz del repositorio se pueden ejecutar los siguientes comandos:

**Restaurar dependencias:**

```powershell
dotnet restore
```

**Iniciar la aplicación:**

```powershell
dotnet run --project .\Dsw2025Tpi.Api\Dsw2025Tpi.Api.csproj
```

La API también puede ejecutarse desde Visual Studio, seleccionando `Dsw2025Tpi.Api` como proyecto de inicio.

> [!IMPORTANT]
> La API requiere una clave JWT correctamente configurada. Si la clave falta o no cumple la longitud mínima exigida, el inicio de la aplicación se interrumpe con un mensaje de error.

## Gestión y rotación del secreto JWT

### Rotación en desarrollo

El script `setup-jwt-secret.cmd` puede ejecutarse nuevamente para reemplazar el secreto JWT configurado localmente.

Cada ejecución genera una clave diferente y reemplaza el valor anterior en User Secrets.

**Importante:** después de rotar la clave y reiniciar la API, los tokens firmados con el secreto anterior dejarán de ser válidos. Como consecuencia, los usuarios deberán iniciar sesión nuevamente.

Por este motivo, el script debe ejecutarse durante la configuración inicial o cuando se necesite realizar una rotación.

**No debe ejecutarse automáticamente cada vez que inicia la API.**

### Configuración en otros entornos

En entornos diferentes del desarrollo local, el secreto JWT debe proporcionarse mediante una fuente de configuración segura, como una variable de entorno o un gestor de secretos.

La variable de entorno correspondiente es:

```text
Jwt__Key
```

ASP.NET Core interpreta el doble guion bajo (`__`) como un separador jerárquico (`:`). Por lo tanto, esta variable representa la propiedad `Jwt:Key`.

En producción, la generación, distribución y rotación del secreto deben realizarse de manera controlada.

Todas las instancias de la API que deban validar los mismos tokens deben utilizar la clave vigente correspondiente.

### Consideraciones de seguridad

- No incorporar claves JWT reales a `appsettings.json`, commits ni Pull Requests.
- No compartir secretos mediante capturas de pantalla, documentación o mensajes.
- No reutilizar claves de desarrollo en producción.
- No ejecutar automáticamente el generador de claves en cada inicio de la aplicación.
- Si una clave estuvo expuesta, debe reemplazarse en todos los entornos afectados.
- Retirar una clave del archivo actual no elimina sus versiones anteriores del historial de Git. Por eso, una clave expuesta debe considerarse comprometida y rotarse.

Esta configuración permite mantener el mecanismo de autenticación existente sin exponer el material criptográfico utilizado para firmar y validar los tokens.

## Gestión de configuración y trabajo en equipo

El equipo acuerda el siguiente flujo de trabajo:

- `main` contiene la versión estable.
- `development` integra el trabajo del equipo.
- Las ramas `feature/nombre-funcionalidad` nacen desde `development`
  y se integran mediante Pull Request hacia `development`.
- La promoción estable se realiza mediante Pull Request
  de `development` hacia `main`.
- Las ramas `hotfix/descripcion` nacen desde `main`.
  Se integran allí mediante Pull Request y después se reintegran
  en `development` mediante otra Pull Request.
- No se realizan commits ni pushes directos sobre `main`
  o `development`.
- Toda Pull Request hacia las ramas principales requiere
  la aprobación de otro integrante.


## Configuración externa de la API — TP3 / HU-T03

La API utiliza el sistema de configuración de ASP.NET Core mediante
WebApplication.CreateBuilder(args).

Variables utilizadas:

- ASPNETCORE_URLS: dirección y puerto de escucha.
- ASPNETCORE_ENVIRONMENT: ambiente de ejecución.
- DOTNET_ENVIRONMENT: variable alternativa de ambiente. Si se configuran
  ambas variables de ambiente, se utilizan valores coincidentes.
- DB_CONNECTION_STRING: cadena de conexión para los contextos de
  autenticación y datos del sistema.
- Jwt__Key: clave JWT externa, requerida para iniciar la aplicación.

La conexión se resuelve en este orden:

1. DB_CONNECTION_STRING.
2. ConnectionStrings:Dsw2025TpiEntities.
3. ConnectionStrings:DB_CONNECTION_STRING.

Las claves jerárquicas pueden suministrarse mediante variables de
entorno utilizando doble guion bajo; por ejemplo,
ConnectionStrings__Dsw2025TpiEntities y Jwt__Key.

Para verificar la configuración externa con dotnet run se utiliza
--no-launch-profile, evitando que launchSettings.json imponga
el puerto o el ambiente del perfil local.

La configuración habitual de desarrollo se conserva en los archivos
y perfiles existentes. Las credenciales reales se suministran
externamente y no se incorporan al repositorio.


[Documentación del Sprint 2 y evidencias](https://docs.google.com/document/d/1TBX0RXLDeNRAczGH_ERpyUQbhTneHeIfGx-c1b1B8Rw/edit?usp=drivesdk)
