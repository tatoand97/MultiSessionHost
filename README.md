# MultiSessionHost

## Resumen

`MultiSessionHost` sigue siendo una base **Worker-first** sobre `Generic Host` en .NET 10 para orquestar múltiples sesiones lógicas concurrentes. El `Worker` es el runtime real: ejecuta scheduler, lifecycle, health y, cuando se habilita, expone la Admin API HTTP **en el mismo proceso** y sobre el **mismo contenedor DI**.

La integración de escritorio ya no está acoplada a `MultiSessionHost.TestDesktopApp`. Ahora el runtime usa:

- `DesktopTargetProfile`
- `SessionTargetBinding`
- `IDesktopTargetAdapter`
- `IDesktopTargetAdapterRegistry`

`MultiSessionHost.TestDesktopApp` queda como una implementación de ejemplo sobre esta arquitectura.

## Alcance y restricciones

Diseñado solo para apps de escritorio locales controladas por nosotros.

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

### Principios

- `MultiSessionHost.Worker` sigue siendo el único proceso principal del runtime.
- `MultiSessionHost.AdminApi` no crea un host ni runtime aparte.
- Scheduler y coordinator no conocen adapters concretos de escritorio.
- La selección de target ocurre por configuración.

### Componentes nuevos

- `DesktopTargetProfile`
  - describe qué target es
  - `Kind`, `ProcessName`, fragments/templates, metadata y capacidades
- `SessionTargetBinding`
  - vincula una `SessionId` con un profile
  - aporta variables y overrides opcionales por sesión
- `ConfiguredDesktopTargetProfileResolver`
  - resuelve binding + profile
  - aplica overrides
  - renderiza templates con variables de sesión
- `DefaultSessionAttachmentResolver`
  - usa profile/binding resueltos
  - enumera procesos/ventanas
  - aplica el `MatchingMode`
- `DesktopTargetSessionDriver`
  - driver real configurable
  - delega attach/detach/work items/snapshots al adapter correcto
- `IDesktopTargetAdapterRegistry`
  - resuelve un adapter por `DesktopTargetKind`
- `IUiTreeNormalizerResolver` y `IWorkItemPlannerResolver`
  - permiten seleccionar pipeline UI por kind/profile

## Flujo runtime

```text
Worker session
  -> DesktopTargetSessionDriver
  -> SessionTargetBinding
  -> DesktopTargetProfile
  -> IDesktopTargetAdapterRegistry
  -> IDesktopTargetAdapter
  -> attachment
  -> optional raw snapshot
  -> normalize
  -> project
  -> planned work items
```

## Target kinds soportados

- `DriverMode=NoOp`
- `DriverMode=DesktopTargetAdapter`

Dentro de `DesktopTargetAdapter` hoy existen:

- `DesktopTargetKind=SelfHostedHttpDesktop`
- `DesktopTargetKind=DesktopTestApp`

`DesktopTestApp` reutiliza la ruta self-hosted HTTP y agrega validación específica mínima.

## Configuración

La sección sigue siendo `MultiSessionHost`.

```json
{
  "MultiSessionHost": {
    "MaxGlobalParallelSessions": 2,
    "SchedulerIntervalMs": 100,
    "HealthLogIntervalMs": 2000,
    "EnableAdminApi": true,
    "AdminApiUrl": "http://localhost:5088",
    "DriverMode": "DesktopTargetAdapter",
    "EnableUiSnapshots": true,
    "DesktopTargets": [
      {
        "ProfileName": "test-app",
        "Kind": "DesktopTestApp",
        "ProcessName": "MultiSessionHost.TestDesktopApp",
        "WindowTitleFragment": "[SessionId: {SessionId}]",
        "CommandLineFragmentTemplate": "--session-id {SessionId}",
        "BaseAddressTemplate": "http://127.0.0.1:{Port}/",
        "MatchingMode": "WindowTitleAndCommandLine",
        "SupportsUiSnapshots": true,
        "SupportsStateEndpoint": true,
        "Metadata": {
          "UiSource": "DesktopTestApp"
        }
      }
    ],
    "SessionTargetBindings": [
      {
        "SessionId": "alpha",
        "TargetProfileName": "test-app",
        "Variables": {
          "Port": "7100"
        }
      },
      {
        "SessionId": "beta",
        "TargetProfileName": "test-app",
        "Variables": {
          "Port": "7101"
        }
      }
    ],
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
      }
    ]
  }
}
```

### Validaciones de arranque

- `DriverMode` debe ser válido.
- `EnableUiSnapshots=true` requiere `DriverMode=DesktopTargetAdapter`.
- cada `DesktopTargetProfile` debe tener `ProfileName` único y `Kind` válido.
- cada `SessionTargetBinding` debe apuntar a una sesión configurada.
- cada binding debe apuntar a un profile existente.
- cada sesión requiere binding cuando `DriverMode=DesktopTargetAdapter`.
- si un template usa variables como `{Port}`, cada binding debe proveerlas.
- `BaseAddressTemplate` debe renderizar una URL absoluta válida para los targets HTTP.

## Admin API

Endpoints existentes mantenidos:

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

Endpoints nuevos de inspección:

- `GET /targets`
- `GET /targets/{profileName}`
- `GET /sessions/{id}/target`

`/sessions/{id}/target` expone:

- profile resuelto
- binding aplicado
- target renderizado
- attachment actual si existe
- adapter seleccionado

## Cómo agregar un nuevo target kind

1. agrega un valor nuevo a `DesktopTargetKind`
2. implementa `IDesktopTargetAdapter`
3. registra el adapter en `DesktopServiceCollectionExtensions`
4. extiende `IUiTreeNormalizerResolver` y `IWorkItemPlannerResolver` si ese kind necesita pipeline distinto
5. crea uno o más `DesktopTargetProfile` en configuración

Scheduler y coordinator no necesitan cambios.

## Cómo agregar un nuevo profile

1. agrega una entrada a `DesktopTargets`
2. define `Kind`, `ProcessName`, templates y metadata
3. decide si soporta `/state` y snapshots UI
4. crea bindings por sesión en `SessionTargetBindings`

## Cómo bindear una sesión a un profile

1. crea o reutiliza un `DesktopTargetProfile`
2. agrega un `SessionTargetBinding`
3. define `SessionId`
4. define `TargetProfileName`
5. agrega variables como `Port`
6. si hace falta, usa `Overrides` para esa sesión

## Cómo probar con MultiSessionHost.TestDesktopApp

Compila primero:

```powershell
dotnet build .\MultiSessionHost.sln
```

Lanza una instancia por sesión:

```powershell
dotnet run --project .\MultiSessionHost.TestDesktopApp\MultiSessionHost.TestDesktopApp.csproj -- --session-id alpha --port 7100
dotnet run --project .\MultiSessionHost.TestDesktopApp\MultiSessionHost.TestDesktopApp.csproj -- --session-id beta --port 7101
```

Configura `MultiSessionHost.Worker/appsettings.Development.json` con `DriverMode=DesktopTargetAdapter` y el bloque `DesktopTargets` + `SessionTargetBindings` mostrado arriba.

Inicia el Worker:

```powershell
dotnet run --project .\MultiSessionHost.Worker\MultiSessionHost.Worker.csproj
```

Pruebas HTTP rápidas:

```powershell
Invoke-RestMethod http://localhost:5088/targets
Invoke-RestMethod http://localhost:5088/sessions/alpha/target
Invoke-RestMethod -Method Post http://localhost:5088/sessions/alpha/ui/refresh
Invoke-RestMethod http://localhost:5088/sessions/alpha/ui
Invoke-RestMethod http://localhost:5088/sessions/alpha/ui/raw
```

## Cómo probar la solución

```powershell
dotnet build .\MultiSessionHost.sln
dotnet test .\MultiSessionHost.Tests\MultiSessionHost.Tests.csproj
```

La suite cubre ahora:

- parse y validación de `DesktopTargets`
- parse y validación de `SessionTargetBindings`
- errores por binding faltante, profile inexistente o variables faltantes
- render de templates por binding
- aislamiento entre sesiones
- registry de adapters
- selección de adapter por el driver real
- integración end-to-end con `DesktopTestApp`
- endpoints `/sessions/{id}/ui`
- endpoints `/sessions/{id}/ui/raw`
- endpoints `/sessions/{id}/ui/refresh`
- endpoints `/targets`
- endpoint `/sessions/{id}/target`
