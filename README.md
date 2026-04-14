# MultiSessionHost

## Resumen

`MultiSessionHost` es una base **Worker-first** sobre `Generic Host` en .NET 10 para orquestar múltiples sesiones lógicas concurrentes. El `Worker` sigue siendo el proceso principal: ejecuta scheduler, health, lifecycle y, cuando se habilita, también expone la Admin API HTTP **en el mismo proceso** y sobre el **mismo contenedor DI**.

Este repositorio ahora agrega una capa segura y reutilizable para escritorio local controlado:

- attach lógico de una sesión a un proceso/ventana real
- captura de snapshot estructurado de UI
- normalización a un árbol UI propio del host
- planeación de work items desde ese árbol
- ejecución de work items sobre un driver abstracto

## Alcance y restricciones

Esto está diseñado **solo para una app local de prueba controlada por nosotros**.

No incluye ni pretende incluir:

- OCR
- computer vision
- lectura de memoria
- input simulation
- hooks
- DLL injection
- bypass de anticheat
- integración con juegos
- integración con clientes de terceros

## Arquitectura actual

### Principio clave

- `MultiSessionHost.Worker` sigue siendo el único proceso principal del runtime.
- `MultiSessionHost.AdminApi` no crea host propio ni runtime propio.
- La API y el Worker comparten las mismas instancias singleton de runtime.

### Nuevas capas

- `MultiSessionHost.UiModel`
  - modelo canónico y serializable de árbol UI
  - selector de nodos
  - diff básico entre árboles
  - planeación de work items desde UI
- `MultiSessionHost.Desktop`
  - localización de procesos/ventanas
  - attach lógico por `SessionId`
  - provider de snapshots estructurados
  - normalizador hacia `UiTree`
  - driver real `DesktopTestAppSessionDriver`
- `MultiSessionHost.TestDesktopApp`
  - WinForms local, multi-instancia
  - expone `/state`, `/ui-snapshot`, `/start`, `/pause`, `/resume`, `/stop`, `/tick`
  - introspección de su propia UI sin OCR ni automatización externa

## Flujo attach -> snapshot -> normalize -> plan

```text
Worker session
  -> ISessionDriver.AttachAsync()
  -> ISessionAttachmentResolver
  -> process/window binding por SessionId
  -> IUiSnapshotProvider
  -> raw UiSnapshotEnvelope
  -> IUiTreeNormalizer
  -> UiTree
  -> IUiStateProjector
  -> diff básico vs árbol previo
  -> IWorkItemPlanner
  -> planned work items
  -> ISessionUiStateStore
```

## Driver modes

La configuración ahora permite cambiar entre:

- `DriverMode=NoOp`
- `DriverMode=DesktopTestApp`

`DesktopTestAppSessionDriver` soporta por ahora:

- `Heartbeat`: valida conectividad con `/state`
- `Tick`: ejecuta `POST /tick`
- `FetchUiSnapshot`: captura snapshot raw
- `ProjectUiState`: normaliza a `UiTree` y recalcula planeación

## Estructura principal

```text
MultiSessionHost.AdminApi/
MultiSessionHost.Contracts/
MultiSessionHost.Core/
MultiSessionHost.Desktop/
MultiSessionHost.Infrastructure/
MultiSessionHost.TestDesktopApp/
MultiSessionHost.Tests/
MultiSessionHost.UiModel/
MultiSessionHost.Worker/
```

## Configuración

La sección sigue siendo `MultiSessionHost`.

```json
{
  "MultiSessionHost": {
    "MaxGlobalParallelSessions": 3,
    "SchedulerIntervalMs": 100,
    "HealthLogIntervalMs": 2000,
    "EnableAdminApi": true,
    "AdminApiUrl": "http://localhost:5088",
    "DriverMode": "DesktopTestApp",
    "DesktopSessionMatchingMode": "WindowTitleAndCommandLine",
    "TestAppBasePort": 7100,
    "EnableUiSnapshots": true,
    "Sessions": [
      {
        "SessionId": "alpha",
        "DisplayName": "Alpha Session",
        "Enabled": true,
        "TickIntervalMs": 1000,
        "StartupDelayMs": 0,
        "MaxParallelWorkItems": 1,
        "MaxRetryCount": 3,
        "InitialBackoffMs": 1000,
        "Tags": [ "desktop-test" ]
      },
      {
        "SessionId": "beta",
        "DisplayName": "Beta Session",
        "Enabled": true,
        "TickIntervalMs": 1000,
        "StartupDelayMs": 0,
        "MaxParallelWorkItems": 1,
        "MaxRetryCount": 3,
        "InitialBackoffMs": 1000,
        "Tags": [ "desktop-test" ]
      },
      {
        "SessionId": "gamma",
        "DisplayName": "Gamma Session",
        "Enabled": true,
        "TickIntervalMs": 1000,
        "StartupDelayMs": 0,
        "MaxParallelWorkItems": 1,
        "MaxRetryCount": 3,
        "InitialBackoffMs": 1000,
        "Tags": [ "desktop-test" ]
      }
    ]
  }
}
```

### Reglas de validación

- `DriverMode` debe ser válido.
- `DesktopSessionMatchingMode` debe ser válido.
- `TestAppBasePort` debe estar entre `1` y `65535`.
- `EnableUiSnapshots=true` requiere `DriverMode=DesktopTestApp`.
- `TestAppBasePort + cantidad de sesiones` no puede desbordar el rango de puertos.

## Endpoints Admin API

Cuando `EnableAdminApi=true`, el Worker expone:

- `GET /health`
- `GET /sessions`
- `GET /sessions/{id}`
- `POST /sessions/{id}/start`
- `POST /sessions/{id}/stop`
- `POST /sessions/{id}/pause`
- `POST /sessions/{id}/resume`
- `GET /metrics`
- `GET /sessions/{id}/ui`
- `GET /sessions/{id}/ui/raw`
- `POST /sessions/{id}/ui/refresh`

Los endpoints de UI operan contra el runtime real del Worker y usan el mismo `ISessionCoordinator`.

## Cómo correr 3 instancias de MultiSessionHost.TestDesktopApp

Compila primero:

```powershell
dotnet build .\MultiSessionHost.sln
```

Lanza tres instancias, una por sesión:

```powershell
dotnet run --project .\MultiSessionHost.TestDesktopApp\MultiSessionHost.TestDesktopApp.csproj -- --session-id alpha --port 7100
dotnet run --project .\MultiSessionHost.TestDesktopApp\MultiSessionHost.TestDesktopApp.csproj -- --session-id beta --port 7101
dotnet run --project .\MultiSessionHost.TestDesktopApp\MultiSessionHost.TestDesktopApp.csproj -- --session-id gamma --port 7102
```

Cada ventana incluirá `SessionId` en el título y expondrá su propio endpoint local.

## Cómo iniciar el Worker con DesktopTestApp

1. Ajusta `MultiSessionHost.Worker/appsettings.json` o `appsettings.Development.json`:

```json
{
  "MultiSessionHost": {
    "EnableAdminApi": true,
    "AdminApiUrl": "http://localhost:5088",
    "DriverMode": "DesktopTestApp",
    "DesktopSessionMatchingMode": "WindowTitleAndCommandLine",
    "TestAppBasePort": 7100,
    "EnableUiSnapshots": true
  }
}
```

2. Inicia el Worker:

```powershell
dotnet run --project .\MultiSessionHost.Worker\MultiSessionHost.Worker.csproj
```

## Ejemplos de llamadas HTTP

### Listar sesiones

```powershell
Invoke-RestMethod http://localhost:5088/sessions
```

### Refrescar UI de una sesión

```powershell
Invoke-RestMethod -Method Post http://localhost:5088/sessions/alpha/ui/refresh
```

### Obtener árbol UI proyectado

```powershell
Invoke-RestMethod http://localhost:5088/sessions/alpha/ui
```

### Obtener snapshot raw

```powershell
Invoke-RestMethod http://localhost:5088/sessions/alpha/ui/raw
```

### Health y métricas

```powershell
Invoke-RestMethod http://localhost:5088/health
Invoke-RestMethod http://localhost:5088/metrics
```

## Cómo probar

```powershell
dotnet build .\MultiSessionHost.sln
dotnet test .\MultiSessionHost.Tests\MultiSessionHost.Tests.csproj
```

La suite cubre, entre otras cosas:

- localización de instancias por `SessionId`
- attach a la instancia correcta
- varias instancias simultáneas
- refresh de snapshots UI
- normalización a `UiTree`
- endpoints `/sessions/{id}/ui`
- endpoints `/sessions/{id}/ui/raw`
- endpoints `/sessions/{id}/ui/refresh`
- aislamiento entre sesiones

