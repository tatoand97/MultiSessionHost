# MultiSessionHost

## Resumen

`MultiSessionHost` sigue siendo una base **Worker-first** sobre `Generic Host` en .NET 10 para orquestar múltiples sesiones lógicas concurrentes. El `Worker` es el runtime real: ejecuta scheduler, lifecycle, health y, cuando se habilita, expone la Admin API HTTP **en el mismo proceso** y sobre el **mismo contenedor DI**.

La integración de escritorio ya no está acoplada a `MultiSessionHost.TestDesktopApp`. Ahora el runtime usa:

- `DesktopTargetProfile`
- `SessionTargetBinding`
- `IDesktopTargetAdapter`
- `IDesktopTargetAdapterRegistry`
- `UiCommand`
- `IUiActionResolver`
- `IUiInteractionAdapter`

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
- `IUiCommandExecutor`
  - ejecuta comandos semánticos sobre una sesión activa
  - valida sesión, attachment y estado UI
  - auto-refresca la UI cuando todavía no existe `UiTree`
  - refresca la UI después de un comando exitoso
- `IUiActionResolver`
  - interpreta `UiTree` + `UiCommand`
  - valida `nodeId`, visibilidad, habilitación y compatibilidad semántica
- `IUiInteractionAdapter`
  - ejecuta acciones cooperativas por `nodeId`
  - sin coordenadas ni input simulation
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

## Capa de comandos semánticos

La vista `UiTree` no ejecuta nada directamente. El flujo nuevo queda así:

```text
Session
  -> target resolved
  -> ui state available
  -> node selected by id
  -> UiCommand
  -> IUiActionResolver
  -> IUiInteractionAdapter
  -> cooperative target
  -> UiCommandResult
```

### Reglas base del resolver por defecto

- `ClickNode`
  - button-like o nodos marcados como `clickable`
- `InvokeNodeAction`
  - nodos con acciones expuestas
- `SetText`
  - textbox/input-like
- `ToggleNode`
  - checkbox/toggle-like
- `SelectItem`
  - list/listbox/selector-like

La especialización del test app no vive en el executor. Vive en:

- metadata del nodo
- `DefaultUiActionResolver`
- `TestDesktopAppUiInteractionAdapter`
- endpoints cooperativos del test app por `nodeId`

## Target kinds soportados

- `DriverMode=NoOp`
- `DriverMode=DesktopTargetAdapter`

Dentro de `DesktopTargetAdapter` hoy existen:

- `DesktopTargetKind=SelfHostedHttpDesktop`
- `DesktopTargetKind=DesktopTestApp`

`DesktopTestApp` reutiliza la ruta self-hosted HTTP y agrega validación específica mínima.

Además expone endpoints cooperativos por `nodeId`:

- `POST /ui/nodes/{nodeId}/click`
- `POST /ui/nodes/{nodeId}/invoke`
- `POST /ui/nodes/{nodeId}/text`
- `POST /ui/nodes/{nodeId}/toggle`
- `POST /ui/nodes/{nodeId}/select`

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

Endpoints nuevos de comandos semánticos:

- `POST /sessions/{id}/commands`
- `POST /sessions/{id}/nodes/{nodeId}/click`
- `POST /sessions/{id}/nodes/{nodeId}/invoke`
- `POST /sessions/{id}/nodes/{nodeId}/text`
- `POST /sessions/{id}/nodes/{nodeId}/toggle`
- `POST /sessions/{id}/nodes/{nodeId}/select`

`/sessions/{id}/target` expone:

- profile resuelto
- binding aplicado
- target renderizado
- attachment actual si existe
- adapter seleccionado

### Decisión sobre UI state faltante

Si una sesión todavía no tiene `UiTree` proyectado, el executor hace auto-refresh antes de resolver el comando. Después de un comando exitoso dispara un refresh posterior para dejar el árbol actualizado. Las fallas semánticas devuelven `409 Conflict` con `UiCommandResultDto`.

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

Ejemplos de comandos semánticos:

```powershell
Invoke-RestMethod -Method Post http://localhost:5088/sessions/alpha/nodes/startButton/click

Invoke-RestMethod -Method Post `
  -Uri http://localhost:5088/sessions/alpha/nodes/notesTextBox/text `
  -ContentType 'application/json' `
  -Body '{"textValue":"Updated notes for alpha"}'

Invoke-RestMethod -Method Post `
  -Uri http://localhost:5088/sessions/alpha/nodes/enabledCheckBox/toggle `
  -ContentType 'application/json' `
  -Body '{"boolValue":false}'

Invoke-RestMethod -Method Post `
  -Uri http://localhost:5088/sessions/alpha/nodes/itemsListBox/select `
  -ContentType 'application/json' `
  -Body '{"selectedValue":"alpha-item-2"}'

Invoke-RestMethod -Method Post `
  -Uri http://localhost:5088/sessions/alpha/commands `
  -ContentType 'application/json' `
  -Body '{"nodeId":"tickButton","kind":"InvokeNodeAction","actionName":"Tick"}'
```

`UiCommandResultDto` devuelve:

- `Succeeded`
- `SessionId`
- `NodeId`
- `Kind`
- `Message`
- `ExecutedAtUtc`
- `UpdatedUiStateAvailable`
- `FailureCode`

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
- unit tests de `DefaultUiActionResolver`
- `POST /sessions/{id}/commands`
- click semántico sobre botones cooperativos
- `SetText`, `ToggleNode` y `SelectItem`
- refresh UI posterior al comando
- aislamiento de comandos entre `alpha` y `beta`
