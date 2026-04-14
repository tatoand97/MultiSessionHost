# MultiSessionHost

## 1. Resumen del proyecto

`MultiSessionHost` es una base Worker-first para orquestar multiples sesiones logicas concurrentes sobre `Generic Host` en .NET 10. El enfoque esta centrado en el runtime del host, el scheduler, el registro de sesiones, el lifecycle, la observabilidad y la extensibilidad, sin incluir logica de bot, automatizacion real ni integraciones con clientes de terceros.

Caracteristicas principales:

- `Generic Host` como base comun.
- `Worker Service` como runtime principal.
- soporte para ejecucion como consola y como `Windows Service`.
- scheduler round-robin con fairness basica.
- colas por sesion con `System.Threading.Channels`.
- health y metricas simples en memoria.
- `AdminApi` opcional y separada del Worker.
- implementaciones `in-memory` y drivers mock/no-op listos para reemplazar.

## 2. Arquitectura

La solucion separa contratos, core, infraestructura y hosts. `MultiSessionHost.Worker` es el entrypoint principal y coordina el runtime del proceso. `MultiSessionHost.Core` modela sesiones, estados, snapshots, scheduler decisions e interfaces. `MultiSessionHost.Infrastructure` implementa registro, state store, colas, scheduler, lifecycle manager, coordinator y drivers. `MultiSessionHost.AdminApi` expone administracion local opcional sobre el mismo conjunto de servicios.

## 3. Arbol de archivos

```text
MultiSessionHost.sln
README.md
MultiSessionHost.AdminApi/
  AdminApiRuntimeService.cs
  appsettings.Development.json
  appsettings.json
  Mapping/
    DtoMappingExtensions.cs
  MultiSessionHost.AdminApi.csproj
  Program.cs
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
    TestSessionDriver.cs
    TestWait.cs
  Coordination/
    SessionCoordinatorTests.cs
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

## 4. Como ejecutar en consola

```powershell
dotnet build .\MultiSessionHost.sln
dotnet run --project .\MultiSessionHost.Worker\MultiSessionHost.Worker.csproj
```

## 5. Como instalar como Windows Service

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

## 6. Como ejecutar la AdminApi

```powershell
dotnet run --project .\MultiSessionHost.AdminApi\MultiSessionHost.AdminApi.csproj
```

Endpoints:

- `GET /health`
- `GET /sessions`
- `GET /sessions/{id}`
- `POST /sessions/{id}/start`
- `POST /sessions/{id}/stop`
- `POST /sessions/{id}/pause`
- `POST /sessions/{id}/resume`
- `GET /metrics`

Nota: la `AdminApi` es un host opcional separado. En esta base comparte la misma infraestructura `in-memory`, asi que cuando corre aparte administra su propio runtime local.

## 7. Como agregar una nueva implementacion de `ISessionDriver`

1. crear una clase en `MultiSessionHost.Infrastructure/Drivers/` que implemente `ISessionDriver`.
2. implementar `AttachAsync`, `DetachAsync` y `ExecuteWorkItemAsync`.
3. registrar la implementacion en `ServiceCollectionExtensions.cs`.
4. reemplazar el binding por defecto de `ISessionDriver` si quieres que sea la activa.
5. agregar pruebas usando `MultiSessionHost.Tests/Common/TestRuntimeContext.cs` como referencia.

## 8. Que partes son mock/stub

- `NoOpSessionDriver`: no hace trabajo real.
- `MockDesktopSessionAdapter`: solo simula attach/detach/ticks.
- `InMemorySessionRegistry`: no persiste fuera del proceso.
- `InMemorySessionStateStore`: no persiste fuera del proceso.
- `DefaultHealthReporter`: metricas simples solo en memoria.
- `AdminApi`: sin autenticacion real; usa `AllowAllAdminAuthorizationPolicy`.

## 9. Futuras extensiones

- store persistente para estado y metricas.
- scheduler con prioridades y quotas.
- policy engine por tags o grupos.
- autenticacion y autorizacion real en `AdminApi`.
- exportacion de metricas a OpenTelemetry/Prometheus.
- drivers especializados por dominio a traves de `ISessionDriver`.
- coordinacion distribuida y stores remotos.

## 10. Diagrama ASCII de arquitectura

```text
MultiSessionHost.Worker
  |
  +-- WorkerHostService
      |
      +-- ISessionCoordinator (DefaultSessionCoordinator)
          |
          +-- ISessionRegistry (InMemorySessionRegistry)
          +-- ISessionStateStore (InMemorySessionStateStore)
          +-- ISessionScheduler (RoundRobinSessionScheduler)
          +-- ISessionLifecycleManager (DefaultSessionLifecycleManager)
          |    |
          |    +-- IWorkQueue (ChannelBasedWorkQueue)
          |    +-- ISessionDriver (NoOpSessionDriver / MockDesktopSessionAdapter)
          |
          +-- IHealthReporter (DefaultHealthReporter)
          +-- IClock (SystemClock)

MultiSessionHost.AdminApi
  |
  +-- Minimal API endpoints
      |
      +-- IAdminAuthorizationPolicy
      +-- ISessionCoordinator
```

## Configuracion de ejemplo

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

## Estado actual

La solucion compila y la suite de pruebas cubre:

- scheduler round-robin
- transiciones de estado
- retry/backoff
- heartbeats
- registro y consulta de sesiones
- aislamiento entre sesiones
- graceful shutdown
- queue draining
