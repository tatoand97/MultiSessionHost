# MultiSessionHost

## Resumen

`MultiSessionHost` es una base **Worker-first** sobre `Generic Host` en .NET 10 para orquestar multiples sesiones logicas concurrentes. El `Worker` es el proceso principal: ejecuta el loop del scheduler, el loop de health, el runtime de sesiones y, cuando se habilita, tambien hospeda la API HTTP de administracion en el **mismo proceso** y sobre el **mismo contenedor DI**.

Este repositorio no incluye logica de bot, OCR, input simulation, hooks, lectura de memoria, integracion con juegos ni UI visual. El foco esta en runtime, coordinacion, lifecycle, colas, health y extensibilidad.

## Arquitectura actual

### Principio clave

Ya no existe el problema de "runtime separado in-memory".

- `MultiSessionHost.Worker` es el unico proceso principal.
- `MultiSessionHost.AdminApi` ya no crea host propio ni runtime propio.
- `MultiSessionHost.AdminApi` ahora es una libreria compartida que expone:
  - `AddAdminApiServices()`
  - `MapAdminApiEndpoints()`
- Cuando `EnableAdminApi=true`, el Worker agrega Kestrel y mapea esos endpoints dentro del mismo proceso.
- Cuando `EnableAdminApi=false`, el Worker no registra servidor HTTP ni expone endpoints.

### Fuente unica de verdad

El Worker y la API comparten exactamente las mismas instancias singleton para:

- `ISessionCoordinator`
- `ISessionRegistry`
- `ISessionStateStore`
- `IWorkQueue`
- health y metricas

Eso significa que un `POST /sessions/{id}/start` impacta el runtime real del Worker y no un segundo runtime aislado.

## Estructura de la solucion

```text
MultiSessionHost.sln
README.md
MultiSessionHost.AdminApi/
  AdminApiEndpointRouteBuilderExtensions.cs
  AdminApiServiceCollectionExtensions.cs
  Mapping/
    DtoMappingExtensions.cs
  MultiSessionHost.AdminApi.csproj
  Security/
    AllowAllAdminAuthorizationPolicy.cs
    IAdminAuthorizationPolicy.cs
MultiSessionHost.Contracts/
  MultiSessionHost.Contracts.csproj
  Sessions/
    PauseSessionRequest.cs
    ProcessHealthDto.cs
    ResumeSessionRequest.cs
    SessionHealthDto.cs
    SessionInfoDto.cs
    SessionMetricsDto.cs
    SessionStateDto.cs
    StartSessionRequest.cs
    StopSessionRequest.cs
MultiSessionHost.Core/
  Configuration/
    SessionHostOptions.cs
    SessionHostOptionsExtensions.cs
  Enums/
    SchedulerDecisionType.cs
    SessionStatus.cs
    SessionWorkItemKind.cs
  Interfaces/
    IClock.cs
    IHealthReporter.cs
    ISessionCoordinator.cs
    ISessionDriver.cs
    ISessionLifecycleManager.cs
    ISessionRegistry.cs
    ISessionScheduler.cs
    ISessionStateStore.cs
    IWorkQueue.cs
  Models/
    ProcessHealthSnapshot.cs
    RetryPolicyState.cs
    SchedulerDecision.cs
    SessionDefinition.cs
    SessionHeartbeat.cs
    SessionHealthSnapshot.cs
    SessionId.cs
    SessionMetricsSnapshot.cs
    SessionRuntimeState.cs
    SessionSnapshot.cs
    SessionWorkItem.cs
  MultiSessionHost.Core.csproj
  State/
    SessionStateMachine.cs
MultiSessionHost.Infrastructure/
  Coordination/
    DefaultSessionCoordinator.cs
  DependencyInjection/
    ServiceCollectionExtensions.cs
  Drivers/
    MockDesktopSessionAdapter.cs
    NoOpSessionDriver.cs
  Health/
    DefaultHealthReporter.cs
  Lifecycle/
    DefaultSessionLifecycleManager.cs
  MultiSessionHost.Infrastructure.csproj
  Queues/
    ChannelBasedWorkQueue.cs
  Registry/
    InMemorySessionRegistry.cs
  Scheduling/
    RoundRobinSessionScheduler.cs
  State/
    InMemorySessionStateStore.cs
  Time/
    SystemClock.cs
MultiSessionHost.Tests/
  Common/
    FakeClock.cs
    TestOptionsFactory.cs
    TestRuntimeContext.cs
    TestWait.cs
    WorkerHostHarness.cs
  Coordination/
    SessionCoordinatorTests.cs
  Hosting/
    WorkerAdminApiIntegrationTests.cs
  Lifecycle/
    GracefulShutdownTests.cs
    SessionIsolationTests.cs
  MultiSessionHost.Tests.csproj
  Queues/
    ChannelBasedWorkQueueTests.cs
  Registry/
    InMemorySessionRegistryTests.cs
  Scheduling/
    RoundRobinSessionSchedulerTests.cs
  State/
    RetryPolicyStateTests.cs
    SessionStateMachineTests.cs
MultiSessionHost.Worker/
  appsettings.Development.json
  appsettings.json
  MultiSessionHost.Worker.csproj
  Program.cs
  WorkerHostService.cs
```

## Flujo de hosting

### `EnableAdminApi=false`

```text
Worker process
  -> Generic Host
  -> WorkerHostService
  -> scheduler loop
  -> health loop
  -> session runtime
  -> sin servidor HTTP
```

### `EnableAdminApi=true`

```text
Worker process
  -> Generic Host
  -> WorkerHostService
  -> scheduler loop
  -> health loop
  -> session runtime
  -> Kestrel en el mismo proceso
  -> endpoints Admin API usando el mismo ISessionCoordinator
```

## Diagrama ASCII

```text
MultiSessionHost.Worker (single process)
  |
  +-- Generic Host
      |
      +-- WorkerHostService
      |    |
      |    +-- scheduler loop
      |    +-- health loop
      |    +-- ISessionCoordinator (DefaultSessionCoordinator)
      |
      +-- Optional in-process Kestrel (only when EnableAdminApi=true)
           |
           +-- Admin API endpoints
                |
                +-- IAdminAuthorizationPolicy
                +-- ISessionCoordinator  -----+
                +-- ISessionRegistry      --- |
                +-- ISessionStateStore    --- | shared singleton runtime
                +-- IWorkQueue            --- |
                +-- health / metrics      ---+
```

## Configuracion

La seccion de configuracion sigue siendo `MultiSessionHost`.

```json
{
  "MultiSessionHost": {
    "MaxGlobalParallelSessions": 2,
    "SchedulerIntervalMs": 250,
    "HealthLogIntervalMs": 5000,
    "EnableAdminApi": true,
    "AdminApiUrl": "http://localhost:5088",
    "Sessions": [
      {
        "SessionId": "alpha",
        "DisplayName": "Alpha Session",
        "Enabled": true,
        "TickIntervalMs": 1000,
        "StartupDelayMs": 250,
        "MaxParallelWorkItems": 1,
        "MaxRetryCount": 3,
        "InitialBackoffMs": 1000,
        "Tags": [ "primary", "mock" ]
      }
    ]
  }
}
```

### Reglas de Admin API

- `EnableAdminApi=false`: el Worker no expone HTTP.
- `EnableAdminApi=true`: el Worker expone la API local en `AdminApiUrl`.
- `AdminApiUrl` debe ser URL absoluta valida cuando la API esta habilitada.

## Endpoints preservados

Cuando la API esta habilitada, el Worker expone:

- `GET /health`
- `GET /sessions`
- `GET /sessions/{id}`
- `POST /sessions/{id}/start`
- `POST /sessions/{id}/stop`
- `POST /sessions/{id}/pause`
- `POST /sessions/{id}/resume`
- `GET /metrics`

Todos operan contra el mismo `ISessionCoordinator` del Worker.

## Seguridad

La politica por defecto sigue siendo simple:

- `AllowAllAdminAuthorizationPolicy`

Se mantiene desacoplada por `IAdminAuthorizationPolicy` para poder reemplazarla despues sin tocar el wiring del Worker ni los endpoints.

## Como correr en consola

```powershell
dotnet build .\MultiSessionHost.sln
dotnet run --project .\MultiSessionHost.Worker\MultiSessionHost.Worker.csproj
```

Si `EnableAdminApi=true`, la API queda disponible en `AdminApiUrl` dentro del mismo proceso del Worker.

## Como correr como Windows Service

Publica primero el Worker:

```powershell
dotnet publish .\MultiSessionHost.Worker\MultiSessionHost.Worker.csproj -c Release -r win-x64 --self-contained false -o .\artifacts\worker
```

Instalacion con PowerShell:

```powershell
$exe = "C:\path\to\artifacts\worker\MultiSessionHost.Worker.exe"
New-Service -Name "MultiSessionHost" -BinaryPathName $exe -DisplayName "MultiSessionHost Worker" -StartupType Automatic
Start-Service -Name "MultiSessionHost"
```

Alternativa con `sc.exe`:

```powershell
sc.exe create MultiSessionHost binPath= "C:\path\to\artifacts\worker\MultiSessionHost.Worker.exe" start= auto
sc.exe start MultiSessionHost
```

Para actualizar el servicio:

1. `Stop-Service MultiSessionHost`
2. reemplazar binarios publicados
3. `Start-Service MultiSessionHost`

Para desinstalar:

```powershell
Stop-Service -Name "MultiSessionHost"
sc.exe delete MultiSessionHost
```

## Como probar localmente

### Verificar build y tests

```powershell
dotnet build .\MultiSessionHost.sln
dotnet test .\MultiSessionHost.Tests\MultiSessionHost.Tests.csproj
```

### Probar la API en el mismo proceso del Worker

1. En `MultiSessionHost.Worker/appsettings.json` o `appsettings.Development.json`, fija:

```json
{
  "MultiSessionHost": {
    "EnableAdminApi": true,
    "AdminApiUrl": "http://localhost:5088"
  }
}
```

2. Inicia el Worker:

```powershell
dotnet run --project .\MultiSessionHost.Worker\MultiSessionHost.Worker.csproj
```

3. Llama los endpoints:

```powershell
Invoke-RestMethod http://localhost:5088/health
Invoke-RestMethod http://localhost:5088/sessions
Invoke-RestMethod -Method Post http://localhost:5088/sessions/alpha/start
Invoke-RestMethod -Method Post http://localhost:5088/sessions/alpha/pause
Invoke-RestMethod -Method Post http://localhost:5088/sessions/alpha/resume
Invoke-RestMethod -Method Post http://localhost:5088/sessions/alpha/stop
Invoke-RestMethod http://localhost:5088/metrics
```

4. Para deshabilitar la API, cambia `EnableAdminApi` a `false` y reinicia el Worker.

## Estado actual de pruebas

La suite valida:

- scheduler round-robin
- transiciones de estado
- retry/backoff
- heartbeats
- registro y consulta de sesiones
- aislamiento entre sesiones
- graceful shutdown
- queue draining
- `EnableAdminApi=false` no expone servidor HTTP
- `EnableAdminApi=true` expone la API en el mismo proceso del Worker
- `start/stop/pause/resume` cambian el estado real del Worker
- `/health` y `/metrics` reflejan el mismo estado interno del Worker
