# MultiSessionHost

## Resumen

`MultiSessionHost` sigue siendo una base **Worker-first** sobre `Generic Host` en .NET 10 para orquestar múltiples sesiones lógicas concurrentes. El `Worker` es el runtime real: ejecuta scheduler, lifecycle, health y, cuando se habilita, expone la Admin API HTTP **en el mismo proceso** y sobre el **mismo contenedor DI**.

La integración de escritorio ya no está acoplada a `MultiSessionHost.TestDesktopApp`. Ahora el runtime usa:

- `DesktopTargetProfile`
- `SessionTargetBinding`
- `ISessionTargetBindingStore`
- `ISessionTargetBindingPersistence`
- `IDesktopTargetAdapter`
- `IDesktopTargetAdapterRegistry`
- `UiCommand`
- `IUiActionResolver`
- `IUiInteractionAdapter`

`MultiSessionHost.TestDesktopApp` queda como una implementación de ejemplo sobre esta arquitectura.

## Arquitectura actual

### Consola de administración

`MultiSessionHost.AdminDesktop` es una consola WPF de operador sobre la Admin API HTTP existente. No llama al worker por internals ni reemplaza el runtime: consume el mismo surface administrativo que ya expone el proceso worker en el mismo contenedor DI.

Inicio rápido:

- `dotnet run --project .\MultiSessionHost.AdminDesktop\MultiSessionHost.AdminDesktop.csproj`
- Configura la base URL de la Admin API en la barra superior, por ejemplo `http://127.0.0.1:5000`

La consola permite inspeccionar sesiones, target/binding, UI, semántica, riesgo, dominio, actividad, decisión, ejecución, memoria, persistencia, coordinación y policy control. También expone acciones operativas como refresh, evaluate, execute, pause/resume de runtime, pause/resume de policy, comandos semánticos y edición de bindings.

Distinción importante:

- `pause/resume` afecta al runtime de la sesión.
- `pause-policy/resume-policy` afecta solo la policy por sesión y esa decisión se persiste.

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
- `ISessionTargetBindingStore`
  - mantiene bindings mutables en memoria
  - se inicializa desde `SessionTargetBindings`
  - pasa a ser la fuente runtime de verdad después del arranque
- `ISessionTargetBindingPersistence`
  - carga bindings persistidos al arrancar
  - guarda el snapshot runtime después de cada mutación
- `ConfiguredDesktopTargetProfileCatalog`
  - mantiene `DesktopTargetProfile` como configuración inmutable
- `ConfiguredDesktopTargetProfileResolver`
  - resuelve binding runtime + profile configurado
  - aplica overrides
  - renderiza templates con variables de sesión
- `ISessionTargetBindingManager`
  - valida create/update/delete
  - persiste cambios
  - invalida attachments obsoletos por sesión
- `DefaultSessionAttachmentResolver`
  - usa profile/binding resueltos
  - enumera procesos/ventanas
  - aplica el `MatchingMode`
- `DefaultSessionAttachmentRuntime`
  - garantiza attach lazy con el binding más reciente
  - invalida attachments obsoletos después de una mutación
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
- `IExecutionResourceResolver`
  - deriva keys de sesión, target efectivo y global opcional
  - centraliza el formato determinístico de identidad de target
- `IExecutionCoordinator`
  - concede leases async para ejecución target-facing
  - aplica exclusión por sesión, target y límite global opcional
  - aplica cooldown por target cuando está configurado
  - expone snapshot operativo de ejecuciones activas/en espera
- `SessionDomainState`
  - snapshot genérico, activity-oriented, por sesión
  - complementa `SessionUiState` sin reemplazarlo
  - se inicializa al arrancar y se actualiza después de proyectar UI
- `ISessionDomainStateStore`
  - mantiene un snapshot de dominio por `SessionId`
  - thread-safe, en memoria y estrictamente aislado por sesión
- `ISessionDomainStateProjectionService`
  - deriva estado de dominio desde metadata runtime, `SessionUiState` y resultados semánticos
  - mantiene heurísticas runtime como fallback cuando la extracción es ausente o débil
- `IUiSemanticExtractionPipeline`
  - ejecuta detectores composables sobre el `UiTree` proyectado
  - produce `UiSemanticExtractionResult` sin mutar stores directamente
- `ISessionSemanticExtractionStore`
  - mantiene el último resultado semántico por `SessionId`
  - permite inspección Admin API separada del estado de dominio final
- `IRiskClassificationPipeline`
  - capa dedicada encima de la extracción semántica
  - construye candidatos de riesgo, aplica reglas configuradas y persiste `RiskAssessmentResult`
- `IRiskCandidateBuilder`
  - convierte `DetectedTarget`, `DetectedPresenceEntity`, `DetectedAlert` y otras señales semánticas en `RiskCandidate`
- `IRiskRuleProvider`
  - carga reglas configurables por nombre, tipo y tags
  - valida nombres duplicados, prioridades y matchers vacíos
- `IRiskClassifier`
  - clasifica entidades como `Safe`, `Unknown` o `Threat`
  - asigna severidad, prioridad y política sugerida
- `ISessionRiskAssessmentStore`
  - mantiene la última evaluación de riesgo por `SessionId`
  - permite inspección Admin API separada de la extracción semántica raw
- `IPolicyEngine`
  - capa de comportamiento dedicada después de `SessionDomainState`
  - ejecuta políticas determinísticas y produce un `DecisionPlan`
  - planifica directivas, no ejecuta comandos ni simula input
- `IPolicy`
  - unidad testeable que recibe `PolicyEvaluationContext`
  - devuelve `PolicyEvaluationResult` con directivas, razones y warnings
- `IDecisionPlanAggregator`
  - resuelve conflictos entre políticas y aplica precedencia
  - conserva razones trazables para inspección
- `ISessionDecisionPlanStore`
  - mantiene el último `DecisionPlan` por `SessionId`
  - conserva historial acotado de planes para inspección y rehidratación
  - thread-safe, en memoria y aislado por sesión
- `ISessionOperationalMemoryStore`
  - mantiene memoria operacional por `SessionId`
  - conserva snapshot actual + historial normalizado acotado
  - expone lectura estrecha para futuras políticas sin acoplarlas al historial raw
- `ISessionOperationalMemoryUpdater`
  - proyecta señales runtime hacia memoria operacional
  - centraliza derivación de worksite, riesgo, presencia, timing y outcomes
  - no persiste en base de datos y no reemplaza `SessionDomainState` ni `DecisionPlan`
- `IRuntimePersistenceCoordinator`
  - construye y rehidrata envelopes durables por sesión
  - centraliza flush, errores, status y backend pluggable
  - usa backend local JSON primero, con escritura atómica

## Flujo runtime

```text
Worker session
  -> target resolution
  -> attachment ensure
  -> UI capture/project
  -> UiTree query helpers
  -> semantic classifier
  -> detector extractors
  -> UiSemanticExtractionResult
  -> semantic extraction store
  -> risk candidate builder
  -> risk rules
  -> risk classifier
  -> RiskAssessmentResult
  -> risk assessment store
  -> planned work items
  -> domain projection
  -> SessionDomainStateStore
  -> policy engine
  -> policy results
  -> decision plan aggregator
  -> DecisionPlan
  -> decision plan store
  -> activity state evaluation
  -> SessionActivityStateStore
  -> optional decision plan execution
  -> SessionDecisionPlanExecutionStore
  -> operational memory update
  -> SessionOperationalMemoryStore
  -> Admin API inspection
```

## DecisionPlan execution bridge (Fase 2.4)

La evaluación de políticas y la ejecución de directivas son ahora capas separadas:

- `DecisionPlan` sigue siendo la salida de planificación del policy engine.
- `IDecisionPlanExecutor` es el puente de ejecución determinística de directivas finales.
- `ISessionDecisionPlanExecutionStore` conserva snapshot actual + historial acotado por sesión.

Semántica principal de ejecución:

- orden determinístico: se respeta el orden final de directivas en el `DecisionPlan`.
- waits sin bloqueo: `WaitDirectiveHandler` devuelve `Deferred` con `DeferredUntilUtc`; no bloquea hilos.
- abort/block: una directiva abortiva o bloqueante detiene directivas posteriores.
- directivas no manejadas: se registran como `NotHandled`; no generan crash.
- supresión de repetición: planes idénticos dentro de `RepeatSuppressionWindowMs` se suprimen con resultado `Skipped` y traza explícita.

Handlers base incluidos (genéricos):

- `ObserveDirectiveHandler`
- `WaitDirectiveHandler`
- `PauseActivityDirectiveHandler` (usa `ISessionControlGateway` interno)
- `AbortDirectiveHandler` (usa `ISessionControlGateway` interno)

Directivas target-facing (ejemplo: `SelectSite`, `PrioritizeTarget`, `AvoidTarget`, `ConserveResource`, `Withdraw`) permanecen pluggables vía `IDecisionDirectiveHandler`. El core no impone comportamiento específico de juego/app.

### Configuración de DecisionExecution

`MultiSessionHost:DecisionExecution`:

```json
{
  "DecisionExecution": {
    "EnableDecisionExecution": true,
    "AutoExecuteAfterEvaluation": false,
    "MaxHistoryEntries": 50,
    "RepeatSuppressionWindowMs": 1000,
    "FailOnUnhandledBlockingDirective": false,
    "RecordNoOpExecutions": true
  }
}
```

Validaciones:

- `MaxHistoryEntries > 0`
- `RepeatSuppressionWindowMs >= 0`
- `AutoExecuteAfterEvaluation` requiere `EnableDecisionExecution=true`

### Admin API nueva

- `GET /decision-executions`
- `GET /sessions/{id}/decision-execution`
- `GET /sessions/{id}/decision-execution/history`
- `POST /sessions/{id}/decision-plan/execute`

`POST /sessions/{id}/decision-plan/execute` ejecuta el último `DecisionPlan` persistido para la sesión. Si no existe plan, responde `409 Conflict`.

## Operational memory (Fase 3.1)

La memoria operacional es una capa por sesión, thread-safe e inspeccionable que recuerda observaciones runtime a través del tiempo. Su objetivo es dar contexto histórico a futuras políticas sin obligarlas a leer stores raw ni a reconstruir historial desde `SessionDomainState`, `RiskAssessmentResult`, actividad o ejecución.

Desde Fase 3.2, su snapshot actual y su historial acotado pueden rehidratarse desde la persistencia runtime durable. La memoria sigue siendo un store en memoria durante la ejecución normal; la persistencia se aplica como snapshot resumido de arranque/flush, no como reemplazo DB-backed del store.

### Qué guarda

- `WorksiteObservation`: propiedades observadas de sitios de trabajo genéricos, selección, llegada, visitas, último outcome, severidad asociada, señales de presencia y confianza.
- `RiskObservation`: entidades o fuentes evaluadas por riesgo, severidad, policy sugerida, regla aplicada, conteo y frescura.
- `PresenceObservation`: presencia/ocupación genérica derivada de señales semánticas.
- `TimingObservation`: duraciones genéricas como `arrival-delay`, `wait-window`, `transition-duration` y retrasos tipo cooldown.
- `OutcomeObservation`: resultados de `DecisionPlanExecutionResult` o estados de plan cuando no hubo ejecución.
- `MemoryObservationRecord`: historial normalizado acotado de cambios observados.

`SessionOperationalMemorySnapshot` contiene el resumen actual, listas categorizadas, warnings y metadata. `SessionOperationalMemorySummary` expone conteos, `TopRememberedRiskSeverity`, `MostRecentOutcomeKind` y `LastUpdatedAtUtc`.

### Diferencia con otras capas

- `SessionDomainState` describe el **estado actual proyectado** de la sesión.
- `DecisionPlan` describe la **intención actual** producida por políticas.
- `DecisionPlanExecutionResult` describe la **ejecución de un plan**.
- `SessionOperationalMemorySnapshot` resume **lo recordado históricamente** para esa sesión, con frescura y conteos.

La memoria se actualiza después de que existen suficientes señales:

```text
ui refresh
  -> semantic extraction
  -> risk classification
  -> domain projection
  -> policy engine
  -> decision plan store
  -> activity state evaluation/store
  -> optional decision plan execution
  -> decision execution store
  -> operational memory update
  -> operational memory store
```

Si la ejecución automática está deshabilitada, la memoria igualmente se actualiza desde dominio, extracción semántica, riesgo, plan y actividad. Si se ejecuta manualmente `POST /sessions/{id}/decision-plan/execute`, el resultado también se proyecta a memoria.

### Configuración

`MultiSessionHost:OperationalMemory`:

```json
{
  "OperationalMemory": {
    "EnableOperationalMemory": true,
    "MaxHistoryEntries": 250,
    "MaxWorksitesPerSession": 100,
    "MaxRiskObservationsPerSession": 100,
    "MaxPresenceObservationsPerSession": 100,
    "MaxTimingObservationsPerSession": 100,
    "MaxOutcomeObservationsPerSession": 100,
    "StaleAfterMinutes": 60
  }
}
```

Validaciones:

- todos los límites máximos deben ser `> 0`
- `StaleAfterMinutes >= 0`
- si `EnableOperationalMemory=false`, el updater no falla y no produce nuevos snapshots

### Admin API de memoria

- `GET /memory`
- `GET /sessions/{id}/memory`
- `GET /sessions/{id}/memory/summary`
- `GET /sessions/{id}/memory/history`
- `GET /sessions/{id}/memory/context`

## Memory-informed decisioning (Fase 3.3)

El motor de políticas ahora puede consumir una vista resumida de memoria operacional por sesión (`PolicyMemoryContext`) sin depender de stores raw. Esta capa mantiene el comportamiento determinista y explicable, y no introduce ML.

### Políticas con influencia de memoria

- `SelectNextSitePolicy`: aplica boost/penalty por historial de éxito/fallo, ocupación y severidad de riesgo recordada.
- `ThreatResponsePolicy`: puede reforzar `Withdraw` ante patrones repetidos de riesgo alto o riesgo recordado del sitio actual.
- `TransitPolicy`: adapta `Wait` vs `Navigate` con base en patrones recordados de espera prolongada.
- `AbortPolicy`: refuerza `PauseActivity`/`Abort` ante patrones repetidos de fallos recientes.

Cada influencia se registra como `MemoryInfluenceTrace` dentro de `PolicyEvaluationExplanation`, y se expone en el `DecisionPlan` para auditoría.

### Configuración

`MultiSessionHost:PolicyEngine:MemoryDecisioning`:

```json
{
  "PolicyEngine": {
    "MemoryDecisioning": {
      "EnableMemoryDecisioning": true,
      "SiteSelection": {
        "EnableMemoryInfluence": true,
        "PreferSuccessfulWorksites": true,
        "PenalizeFailedWorksites": true,
        "PenalizeOccupiedWorksites": true,
        "AvoidHighRiskWorksites": true,
        "MinimumSuccessfulVisits": 1,
        "FailurePenaltyWeight": 0.3,
        "OccupancyPenaltyWeight": 0.25,
        "SuccessBoostWeight": 0.4,
        "StaleMemoryPenaltyMode": "SoftPenalty",
        "AvoidWorksitesAboveRememberedRiskSeverity": "High"
      },
      "ThreatResponse": {
        "UseRepeatedRiskPattern": true,
        "WithdrawOnRepeatedHighRisk": true,
        "RepeatedHighRiskThreshold": 2,
        "AvoidWorksiteWithRememberedRisk": true,
        "AvoidRiskSeverityThreshold": "High"
      },
      "Transit": {
        "UseTimingMemory": true,
        "LongWaitThresholdMs": 5000,
        "MaxRememberedWaitBeforeMoveOnMs": 10000,
        "AdaptToRememberedDelays": true
      },
      "Abort": {
        "AbortOnRepeatedFailures": true,
        "RepeatedFailureThreshold": 3,
        "FailureWindowMinutes": 60,
        "MemoryReinforceAbortPriorityBoost": 50
      }
    }
  }
}
```

Ejemplo abreviado:

```json
{
  "sessionId": "alpha",
  "capturedAtUtc": "2026-04-15T12:00:00Z",
  "updatedAtUtc": "2026-04-15T12:05:00Z",
  "summary": {
    "knownWorksiteCount": 1,
    "activeRiskMemoryCount": 2,
    "activePresenceMemoryCount": 1,
    "timingObservationCount": 1,
    "outcomeObservationCount": 1,
    "lastUpdatedAtUtc": "2026-04-15T12:05:00Z",
    "topRememberedRiskSeverity": "High",
    "mostRecentOutcomeKind": "success"
  },
  "knownWorksites": [
    {
      "worksiteKey": "worksite:primary-worksite",
      "worksiteLabel": "primary-worksite",
      "tags": [ "SelectSite" ],
      "lastSelectedAtUtc": "2026-04-15T12:04:30Z",
      "lastArrivedAtUtc": "2026-04-15T12:05:00Z",
      "lastOutcome": "success",
      "lastObservedRiskSeverity": "High",
      "visitCount": 1,
      "successCount": 1,
      "failureCount": 0,
      "lastKnownConfidence": 0.9,
      "isStale": false
    }
  ],
  "recentRiskObservations": [],
  "recentPresenceObservations": [],
  "recentTimingObservations": [],
  "recentOutcomeObservations": [],
  "warnings": [],
  "metadata": {}
}
```

## Runtime persistence (Fase 3.2)

Fase 3.2 agrega la primera capa durable real para estado operacional runtime, adicional a la persistencia de bindings que ya existía. El worker sigue siendo Worker-first sobre Generic Host y la Admin API sigue en el mismo proceso/DI container. Los stores principales siguen siendo in-memory; la persistencia guarda envelopes resumidos para rehidratación segura después de reinicio. Desde Fase 4.1, el envelope también incluye el estado de policy control por sesión y su historial acotado.

### Qué se persiste

- `SessionActivitySnapshot`, incluyendo su historial de transiciones ya acotado.
- `SessionOperationalMemorySnapshot`.
- historial acotado de `MemoryObservationRecord`.
- último `DecisionPlan`.
- historial acotado de planes (`DecisionPlanHistoryEntry`).
- último `DecisionPlanExecutionResult`.
- historial acotado de `DecisionPlanExecutionRecord`.
- `SessionPolicyControlState`.
- historial acotado de `SessionPolicyControlHistoryEntry`.
- metadata pequeña de persistencia, como `schemaVersion` y `savedAtUtc`.

### Qué no se persiste

- raw UI snapshot JSON.
- `UiTree` proyectado.
- árboles semánticos raw o payloads grandes.
- estado de attachment/procesos/ventanas.
- bindings de sesión dentro del envelope runtime.

Los bindings mantienen su mecanismo existente (`BindingStorePersistenceMode` y `BindingStoreFilePath`). Fase 3.2 no lo reemplaza ni duplica.

### Backend local JSON

El backend inicial es `JsonFile`. Guarda un archivo determinístico por sesión bajo `RuntimePersistence:BasePath` con sufijo `.runtime.json`. Cada escritura usa archivo temporal y replace/move atómico. El directorio se crea automáticamente.

Lectura tolerante:

- si no existe archivo, la sesión arranca sin estado rehidratado.
- si un archivo está corrupto, se registra warning, se marca error de persistencia y se ignora ese archivo.
- un archivo corrupto de una sesión no impide cargar otras sesiones.
- sesiones persistidas que ya no están en configuración se ignoran sin borrar el archivo.

### Configuración

`MultiSessionHost:RuntimePersistence`:

```json
{
  "RuntimePersistence": {
    "EnableRuntimePersistence": true,
    "Mode": "JsonFile",
    "BasePath": "data/runtime-state",
    "SchemaVersion": 1,
    "MaxDecisionHistoryEntries": 50,
    "PersistDecisionHistory": true,
    "PersistActivityState": true,
    "PersistOperationalMemory": true,
    "PersistDecisionExecution": true,
    "AutoFlushAfterStateChanges": true,
    "FailOnPersistenceErrors": false
  }
}
```

Defaults:

- `EnableRuntimePersistence=true`
- `Mode=JsonFile`
- `BasePath=runtime-state`
- `AutoFlushAfterStateChanges=true`
- `FailOnPersistenceErrors=false`

Validaciones:

- `BasePath` es obligatorio cuando `Mode=JsonFile` y la persistencia está habilitada.
- `SchemaVersion > 0`.
- `MaxDecisionHistoryEntries > 0`.
- límites negativos no son válidos.
- `Mode=None` no es válido si `EnableRuntimePersistence=true`.

### Rehidratación

Al arrancar, el worker:

1. inicializa bindings con el mecanismo existente.
2. inicializa sesiones/stores normales del coordinator.
3. carga envelopes persistidos para sesiones configuradas.
4. rehidrata actividad, memoria operacional, historial de planes, ejecución y policy control.
5. ignora sesiones persistidas ausentes de la configuración actual.

Después de mutaciones relevantes, el coordinator hace flush del envelope actual cuando `AutoFlushAfterStateChanges=true`. Esto ocurre después de evaluación de políticas, ejecución de planes, updates de actividad, memoria operacional y cambios de policy control. Los errores se loguean y quedan visibles por Admin API; solo hacen fallar la operación si `FailOnPersistenceErrors=true`.

### Admin API de persistencia

- `GET /persistence`
- `POST /persistence/flush`
- `GET /sessions/{id}/persistence`
- `POST /sessions/{id}/persistence/flush`
- `GET /sessions/{id}/decision-plan/history`

Los endpoints de status exponen:

- persistencia habilitada/modo/base path/schema.
- timestamps `LastLoadedAtUtc` y `LastSavedAtUtc`.
- `LastError` si hubo error de carga o guardado.
- si la sesión fue rehidratada desde disco.
- path del archivo cuando aplica.
- conteos de historiales persistidos.

## Modelo de dominio por sesión

`SessionDomainState` es un snapshot inmutable, genérico y orientado a actividad. Existe para que las capas futuras puedan tomar decisiones sobre la sesión sin acoplarse al shape técnico de la UI proyectada. El estado se crea durante el bootstrap de cada sesión con `Source=Bootstrap`, valores seguros (`Unknown`, `Idle`, `None`, `null`) y timestamps iniciales.

Sub-estados incluidos:

- `NavigationState`: estado alto nivel de movimiento/transición, destino/ruta/progreso si se conoce.
- `CombatState`: actividad idle/engaged/recovering y postura ofensiva/defensiva genérica.
- `ThreatState`: severidad, conteos genéricos, seguridad y señales.
- `TargetState`: target activo, target primario y conteos de tracked/locked/selected.
- `CompanionState`: disponibilidad/salud y conteos de assets de soporte.
- `ResourceState`: porcentajes/capacidades/cargas genéricas y flags degraded/critical.
- `LocationState`: contexto/lugar/sub-lugar, base/home/safe si se conoce y confianza.

### Diferencia con SessionUiState

`SessionUiState` conserva el mundo observado de UI: raw snapshot JSON, `UiTree`, diff y work items planificados. `SessionDomainState` conserva una vista estable de dominio: qué parece estar haciendo la sesión, qué tan saludable/degradada está y qué señales faltan o fallaron.

La extracción semántica queda como una capa intermedia porque el árbol UI es técnico y cambiante, mientras que dominio necesita señales estables. Los extractores leen el `UiTree`, emiten hallazgos genéricos (`DetectedList`, `DetectedTarget`, `DetectedAlert`, `DetectedTransitState`, `DetectedResource`, `DetectedCapability`, `DetectedPresenceEntity`) y el proyector de dominio decide qué hallazgos son suficientemente fuertes para actualizar `NavigationState`, `ThreatState`, `TargetState`, `ResourceState`, `CompanionState` o `LocationState`.

Esto evita que `DefaultSessionDomainStateProjectionService` acumule traversal raw del árbol. Su responsabilidad queda acotada a mapear:

- metadata runtime
- `SessionUiState`
- `UiSemanticExtractionResult`
- `RiskAssessmentResult`
- fallbacks conservadores

### Extracción semántica

La capa vive en `MultiSessionHost.Desktop/Extraction` e incluye:

- `IUiTreeQueryService`: flatten, visible descendants, role/text/attribute lookup, ancestors/siblings/path, text candidates y role families.
- `IUiSemanticClassifier`: heurísticas ligeras por role, texto, attributes, shape de hijos, visibility, enabled, selection, progress/toggle metadata e identificadores.
- `IUiSemanticExtractor`: contrato composable para detectores.
- `IUiSemanticExtractionPipeline`: ejecuta todos los extractores y normaliza un resultado inmutable.
- `ISessionSemanticExtractionStore`: store thread-safe por sesión para diagnóstico.

Detectores incluidos:

- `ListDetectorExtractor`: listas, item counts, selección, labels visibles y scrollability.
- `TargetDetectorExtractor`: nodos selected/active/focused o target-like.
- `AlertDetectorExtractor`: banners/status/messages con severidad genérica.
- `TransitStateDetectorExtractor`: progress/loading/blocked/transition signals.
- `ResourceCapabilityDetectorExtractor`: recursos con porcentajes/valores y capacidades enabled/disabled/active/cooling-down.
- `PresenceEntityDetectorExtractor`: colecciones de entidades presentes/cercanas.

### Clasificación de riesgo

La clasificación de riesgo vive en `MultiSessionHost.Desktop/Risk` y es una capa separada de los extractores. No reemplaza `IUiSemanticExtractionPipeline` ni agrega lógica de clasificación a los detectores: toma el `UiSemanticExtractionResult` ya producido y lo transforma en candidatos genéricos evaluables.

Modelos principales:

- `RiskCandidate`: entidad candidata con `CandidateId`, `SessionId`, `Source`, `Name`, `Type`, `Tags`, `Signals`, `Confidence` y metadata.
- `RiskRule`: regla configurada por nombre, tipo y tags.
- `RiskEntityAssessment`: clasificación por entidad con disposition, severity, priority, suggested policy, regla aplicada y razones.
- `RiskAssessmentSummary`: conteos safe/unknown/threat, severidad más alta, prioridad más alta, política superior y top candidate.
- `RiskAssessmentResult`: resultado persistido por sesión.

Las fuentes soportadas son genéricas: `Target`, `Presence`, `Alert`, `Transit`, `Resource` y `Capability`. Los targets derivan nombre desde label, tipo desde kind y tags como `selected`, `active` o `focused`. Las presencias derivan nombre/tipo/membership/status/count. Las alertas usan message, severity/source y tags como `alert`, `warning`, `error` o `critical`.

### Reglas de riesgo

La configuración vive bajo `MultiSessionHost:RiskClassification`:

```json
{
  "EnableRiskClassification": true,
  "DefaultUnknownDisposition": "Unknown",
  "DefaultUnknownSeverity": "Unknown",
  "DefaultUnknownPolicy": "Observe",
  "MaxReturnedEntities": 100,
  "RequireExplicitSafeMatch": true,
  "Rules": [
    {
      "RuleName": "safe-label",
      "Enabled": true,
      "MatchByName": [ "trusted", "safe", "allowed" ],
      "NameMatchMode": "Contains",
      "Disposition": "Safe",
      "Severity": "Low",
      "Priority": 10,
      "SuggestedPolicy": "Ignore",
      "Reason": "Configured safe label."
    },
    {
      "RuleName": "unknown-tag",
      "Enabled": true,
      "MatchByTags": [ "unknown" ],
      "Disposition": "Unknown",
      "Severity": "Low",
      "Priority": 100,
      "SuggestedPolicy": "Observe",
      "Reason": "Unknown-tagged candidates should be observed."
    },
    {
      "RuleName": "warning-type",
      "Enabled": true,
      "MatchByType": [ "Warning", "Critical" ],
      "TypeMatchMode": "Exact",
      "Disposition": "Threat",
      "Severity": "Critical",
      "Priority": 800,
      "SuggestedPolicy": "Withdraw",
      "Reason": "Warning and critical typed candidates require withdrawal."
    },
    {
      "RuleName": "priority-label",
      "Enabled": true,
      "MatchByName": [ "priority" ],
      "NameMatchMode": "Contains",
      "Disposition": "Threat",
      "Severity": "High",
      "Priority": 900,
      "SuggestedPolicy": "Prioritize",
      "Reason": "Priority-labeled candidates should be handled first."
    }
  ]
}
```

Matching is deterministic. Rules are evaluated in configured order and first match wins. When a rule supplies more than one matcher family, the families combine with AND semantics: name must match if configured, type must match if configured, and tags must match if configured. `MatchByTags` defaults to any tag; set `RequireAllTags=true` to require every configured tag. Name/type match modes are `Exact`, `Contains`, `StartsWith` and `EndsWith`. If no rule matches, the candidate is classified from the default unknown settings and carries a reason explaining that no explicit rule matched.

### Integración en refresh

La actualización ocurre en `DefaultSessionUiRefreshService.ProjectAsync`, después de:

1. leer o capturar el raw snapshot
2. normalizar a `UiTree`
3. calcular diff
4. planificar work items
5. persistir `SessionUiState`
6. ejecutar `IUiSemanticExtractionPipeline` sobre el `UiTree`
7. persistir `UiSemanticExtractionResult` en `ISessionSemanticExtractionStore`
8. ejecutar `IRiskClassificationPipeline`
9. persistir `RiskAssessmentResult` en `ISessionRiskAssessmentStore`
10. proyectar y persistir `SessionDomainState` usando extracción semántica y evaluación de riesgo
11. ejecutar `IPolicyEngine`
12. persistir `DecisionPlan` en `ISessionDecisionPlanStore`

Diagrama actualizado:

```text
Ui snapshot
  -> UiTree
  -> UiTree query helpers
  -> semantic classifier
  -> detector extractors
  -> UiSemanticExtractionResult
  -> semantic extraction store
  -> risk candidate builder
  -> risk rules
  -> risk classifier
  -> RiskAssessmentResult
  -> risk assessment store
  -> domain projection
  -> SessionDomainState
  -> policy engine
  -> policy results
  -> decision plan aggregator
  -> DecisionPlan
  -> decision plan store
  -> Admin API inspection
```

`DefaultSessionDomainStateProjectionService` consume `RiskAssessmentResult` de forma opcional. Cuando existe, `ThreatState.Severity` viene de la amenaza clasificada más fuerte, `UnknownCount` viene del summary unknown, `HostileCount` del summary threat, `NeutralCount` del summary safe, `IsSafe` solo es true cuando no quedan threats ni unknowns, y `Signals` incluye razones, reglas y políticas superiores. `ThreatState` también expone `TopSuggestedPolicy`, `TopEntityLabel` y `TopEntityType`.

Si la proyección UI falla, el mismo servicio registra el error en `SessionUiState` y degrada el snapshot de dominio con `Source=UiRefreshFailure` y warnings.

## Policy engine

El policy engine es la capa de comportamiento. Vive después de la proyección de dominio y consume `SessionDomainState`, `UiSemanticExtractionResult` y `RiskAssessmentResult` sin reemplazar esas capas. Las políticas ya no contienen constantes de comportamiento: selección, espera, retiro, pausa, umbrales, prioridades, allowlists y denylists salen de reglas configurables bajo `PolicyEngine:Rules`. Cuando una sesión tiene la policy pausada, el engine no evalúa el conjunto normal de policies y devuelve un `DecisionPlan` bloqueado e inspeccionable con una razón explícita. Su salida normal es un `DecisionPlan` inmutable con:

- `PlanStatus`
- `DecisionDirective[]`
- `DecisionReason[]`
- `PolicyExecutionSummary`
- `DecisionPlanExplanation`
- `Warnings`

Las directivas son instrucciones planificadas como `Observe`, `SelectSite`, `PrioritizeTarget`, `AvoidTarget`, `ConserveResource`, `Wait`, `Withdraw`, `PauseActivity` o `Abort`. Esta fase solo planifica: no invoca `UiCommandExecutor`, no encola work items y no interactúa con targets. Cada directiva conserva metadata explicable y consistente: `matchedRuleName`, `reasonRuleName`, `matchedCriteria`, `policyRuleFamily`, `policyName`, `ruleIntent`, `sourceScope`, `isFallback`, `minimumWaitMs`, `notBeforeUtc`, `thresholdName` y `policyMode`.

Orden default:

1. `AbortPolicy`
2. `ThreatResponsePolicy`
3. `TransitPolicy`
4. `ResourceUsagePolicy`
5. `TargetPrioritizationPolicy`
6. `SelectNextSitePolicy`

Precedencia default, ahora expresada como `PolicyEngine:AggregationRules`:

- `abort-overrides`: conserva `Abort` y suprime el resto.
- `blocking-response-over-selection`: `Withdraw` y `PauseActivity` suprimen selección/navegación normal.
- `transit-wait-stability`: `Wait` suprime navegación, selección y uso de recursos de menor prioridad cuando no existe una directiva bloqueante más fuerte.
- las directivas se ordenan por prioridad y después por política/kind/id para mantener determinismo.

El orden y límites se configuran bajo `MultiSessionHost:PolicyEngine`:

```json
{
  "PolicyEngine": {
    "EnablePolicyEngine": true,
    "PolicyOrder": [ "AbortPolicy", "ThreatResponsePolicy", "TransitPolicy", "ResourceUsagePolicy", "TargetPrioritizationPolicy", "SelectNextSitePolicy" ],
    "MaxReturnedDirectives": 10,
    "BlockOnAbort": true,
    "PreferThreatResponseOverSelection": true,
    "PreferTransitStability": true,
    "MinDirectivePriority": 0,
    "AggregationRules": {
      "SuppressionRules": [
        {
          "RuleName": "abort-overrides",
          "TriggerDirectiveKinds": [ "Abort" ],
          "PreserveDirectiveKinds": [ "Abort" ],
          "SuppressedDirectiveKinds": [ "*" ]
        },
        {
          "RuleName": "blocking-response-over-selection",
          "TriggerDirectiveKinds": [ "Withdraw", "PauseActivity" ],
          "SuppressedDirectiveKinds": [ "SelectSite", "Navigate", "SelectTarget" ]
        },
        {
          "RuleName": "transit-wait-stability",
          "TriggerDirectiveKinds": [ "Wait" ],
          "SuppressedDirectiveKinds": [ "SelectSite", "Navigate", "SelectTarget", "PrioritizeTarget", "UseResource" ],
          "SuppressLowerPriorityOnly": true,
          "BlockedByDirectiveKinds": [ "Withdraw", "PauseActivity", "AvoidTarget" ]
        }
      ],
      "StatusRules": [
        {
          "RuleName": "aborting-directives",
          "Status": "Aborting",
          "DirectiveKinds": [ "Abort" ],
          "IncludePolicyAbortFlag": true
        },
        {
          "RuleName": "blocked-directives",
          "Status": "Blocked",
          "DirectiveKinds": [ "Withdraw", "PauseActivity", "Wait" ]
        }
      ]
    }
  }
}
```

### Reglas configurables de comportamiento

`IPolicyRuleProvider` normaliza la configuración en reglas inmutables y `IPolicyRuleMatcher` aplica matching determinístico por orden configurado. Las políticas construyen candidatos normalizados desde el estado existente y no leen config raw directamente.

Las familias efectivas se preservan durante la normalización. Una familia top-level reemplaza solo a su familia nested equivalente; si falta esa familia top-level, se usa la familia nested. No hay override mayorista por política.

- `SiteSelection.AllowRules`
- `SiteSelection.Fallback`
- `ThreatResponse.RetreatRules`
- `ThreatResponse.DenyRules`
- `ThreatResponse.Fallback`
- `TargetPrioritization.PriorityRules`
- `TargetPrioritization.DenyRules`
- `TargetPrioritization.Fallback`
- `ResourceUsage.Rules`
- `ResourceUsage.Fallback`
- `Transit.Rules`
- `Transit.Fallback`
- `Abort.Rules`
- `Abort.Fallback`

Cada regla normalizada incluye `PolicyName`, `RuleFamily`, `RuleIntent`, `SourceScope` e `IsFallback`. Los fallbacks son reglas configurables con `IsFallback=true`; se evalúan solo cuando las reglas explícitas de la política no producen directiva. Un fallback habilitado debe tener `RuleName`, `DirectiveKind`, `Priority` y `Reason`, no debe declarar matchers y su directive kind debe estar permitido para la familia.

- `SelectNextSitePolicy`: usa `SiteSelection.AllowRules` y `SiteSelection.Fallback`.
- `ThreatResponsePolicy`: usa `ThreatResponse.RetreatRules`, `ThreatResponse.DenyRules` y `ThreatResponse.Fallback`.
- `TargetPrioritizationPolicy`: usa `TargetPrioritization.PriorityRules`, `TargetPrioritization.DenyRules` y `TargetPrioritization.Fallback`.
- `ResourceUsagePolicy`: usa `ResourceUsage.Rules` y `ResourceUsage.Fallback`.
- `TransitPolicy`: usa `Transit.Rules` y `Transit.Fallback`.
- `AbortPolicy`: usa `Abort.Rules` y `Abort.Fallback`.

Ejemplo genérico:

```json
{
  "PolicyEngine": {
    "Rules": {
      "SiteSelection": {
        "IgnoreNonAllowlistedSites": true,
        "UnknownSiteLabel": "unknown-worksite",
        "DefaultSiteLabel": "worksite",
        "AllowRules": [
          {
            "RuleName": "allowed-worksites",
            "MatchLabels": [ "primary-site", "backup-site" ],
            "LabelMatchMode": "Exact",
            "MatchTags": [ "approved" ],
            "AllowedThreatSeverities": [ "None", "Low", "Unknown" ],
            "RequireIdleNavigation": true,
            "RequireIdleActivity": true,
            "RequireNoActiveTarget": true,
            "DirectiveKind": "SelectSite",
            "Priority": 250,
            "SuggestedPolicy": "SelectSite",
            "TargetLabelTemplate": "{siteLabel}"
          }
        ],
        "Fallback": {
          "RuleName": "observe-when-no-site-matches",
          "Enabled": true,
          "DirectiveKind": "Observe",
          "Priority": 150,
          "SuggestedPolicy": "Observe",
          "Reason": "No site-selection rule matched."
        }
      },
      "ThreatResponse": {
        "DenyRules": [
          {
            "RuleName": "deny-dangerous-entities",
            "MatchTags": [ "dangerous" ],
            "DirectiveKind": "Withdraw",
            "Priority": 920,
            "SuggestedPolicy": "Withdraw",
            "Blocks": true,
            "MinimumWaitMs": 15000,
            "PolicyMode": "retreat"
          }
        ],
        "RetreatRules": [
          {
            "RuleName": "critical-alert-hide-window",
            "MatchTypes": [ "CriticalAlert" ],
            "MinRiskSeverity": "Critical",
            "DirectiveKind": "PauseActivity",
            "Priority": 930,
            "SuggestedPolicy": "PauseActivity",
            "Blocks": true,
            "MinimumWaitMs": 30000,
            "PolicyMode": "seek-safety"
          }
        ]
      },
      "TargetPrioritization": {
        "PriorityRules": [
          {
            "RuleName": "prioritize-approved-high-confidence",
            "MatchTags": [ "approved" ],
            "MinConfidence": 0.75,
            "DirectiveKind": "PrioritizeTarget",
            "Priority": 610,
            "SuggestedPolicy": "Prioritize"
          }
        ],
        "DenyRules": [
          {
            "RuleName": "never-primary-denylisted",
            "MatchTags": [ "deny-primary" ],
            "DirectiveKind": "AvoidTarget",
            "Priority": 620,
            "SuggestedPolicy": "Avoid"
          }
        ]
      },
      "ResourceUsage": {
        "Rules": [
          {
            "RuleName": "withdraw-low-resource",
            "MaxResourcePercent": 15,
            "ThresholdName": "resourcePercent",
            "DirectiveKind": "Withdraw",
            "Priority": 720,
            "SuggestedPolicy": "Withdraw",
            "Blocks": true
          },
          {
            "RuleName": "conserve-degraded-resource",
            "MaxResourcePercent": 35,
            "DirectiveKind": "ConserveResource",
            "Priority": 560,
            "SuggestedPolicy": "ConserveResource"
          }
        ]
      },
      "Transit": {
        "Rules": [
          {
            "RuleName": "wait-while-progress-low",
            "MatchNavigationStatuses": [ "InProgress" ],
            "MaxProgressPercent": 75,
            "DirectiveKind": "Wait",
            "Priority": 650,
            "SuggestedPolicy": "Wait",
            "Blocks": true,
            "MinimumWaitMs": 5000
          }
        ],
        "Fallback": {
          "RuleName": "observe-transit-no-match",
          "Enabled": false,
          "DirectiveKind": "Observe",
          "Priority": 150,
          "SuggestedPolicy": "Observe",
          "Reason": "No transit rule matched."
        }
      },
      "Abort": {
        "Rules": [
          {
            "RuleName": "abort-faulted-runtime",
            "MatchSessionStatuses": [ "Faulted" ],
            "DirectiveKind": "Abort",
            "Priority": 1000,
            "SuggestedPolicy": "Abort",
            "Blocks": true,
            "Aborts": true
          },
          {
            "RuleName": "pause-warning-burst",
            "MinWarningCount": 3,
            "MatchResourceCritical": true,
            "DirectiveKind": "PauseActivity",
            "Priority": 950,
            "SuggestedPolicy": "PauseActivity",
            "Blocks": true,
            "MinimumWaitMs": 10000
          }
        ]
      }
    }
  }
}
```

### Coordinación de ejecución real

Los gates de lifecycle por sesión evitan carreras durante `start/stop/pause/resume`, pero no eran suficientes para la ejecución real contra targets. Antes, un command UI, un refresh, un work item y una invalidación de attachment podían tocar el mismo target por rutas distintas.

Ahora toda operación target-facing pasa por una lease común:

```text
Worker session
  -> DesktopTargetSessionDriver / UiCommandExecutor / DefaultSessionAttachmentRuntime
  -> IExecutionResourceResolver
  -> IExecutionCoordinator
  -> session resource key
  -> target resource key
  -> optional global resource key
  -> execution lease
  -> attachment / target adapter / interaction adapter / UI refresh
  -> release
```

Scopes soportados:

- `Session`: serializa operaciones target-facing para la misma sesión.
- `Target`: serializa sesiones distintas si resuelven al mismo target efectivo.
- `Global`: opcional; limita concurrencia global de operaciones target-facing.

Operation kinds coordinados:

- `WorkItem`
- `UiCommand`
- `UiRefresh`
- `AttachmentEnsure`
- `AttachmentInvalidate`

### Modelo de resource key

La key de sesión usa `SessionId`. La key global usa una identidad fija para operaciones target-facing. La key de target no usa solamente la sesión: se deriva del target efectivo resuelto después de aplicar profile, binding runtime, variables y overrides.

La identidad de target incluye:

- `DesktopTargetKind`
- `ProfileName`
- `ProcessName`
- `BaseAddress` si existe
- `WindowTitleFragment` si existe
- `CommandLineFragment` si existe

Esto permite que `alpha` y `beta` sigan corriendo en paralelo cuando apuntan a targets distintos, pero se serialicen si un rebind los hace resolver al mismo target físico/cooperativo.

### Cooldown por target

`ExecutionCoordination.DefaultTargetCooldownMs` agrega una pausa mínima entre operaciones sobre la misma key de target. El cooldown se aplica después de liberar la lease anterior del target. Un waiter por cooldown no hace busy-wait: el coordinator programa un wake-up async cuando expira la ventana.

El default es `0`, por lo que no hay cooldown salvo que se configure.

## Session Activity Lifecycle State Machine

Después de que el policy engine produce un `DecisionPlan`, el evaluador de actividad determina el estado del ciclo de vida de la sesión. Este es un modelo explícito, de primera clase, que explica la sesión como `estado + transición + razón + timestamp`, separado de `SessionDomainState` y `DecisionPlan`.

### Estados de actividad

El evaluador mantiene la sesión en uno de 11 estados:

- **Idle**: sin actividad, sin directivas accionables.
- **SelectingWorksite**: plan indica fase de selección de sitio de trabajo.
- **Traveling**: navegación en progreso activo hacia destino.
- **Arriving**: navegación completada, en destino.
- **WaitingForSpawn**: en ubicación, esperando oportunidad de objetivo/acción.
- **Engaging**: combate activo o acoplamiento de objetivo.
- **MonitoringRisk**: actividad en curso, riesgo presente, sin estado más fuerte aplicable.
- **Withdrawing**: directiva de retiro crítico de seguridad activa.
- **Hiding**: directiva `PauseActivity` para mitigación de riesgo (búsqueda de seguridad).
- **Recovering**: recuperación de estado anterior de bloqueo (degradación de recurso/combate).
- **Faulted**: error terminal, sesión/runtime/dominio degradado.

### Precedencia de evaluación

El evaluador sigue un orden explícito de precedencia (se evalúan en este orden; primera coincidencia gana):

1. **Faulted** – indica error runtime o estado de sesión degradado
2. **Withdrawing** – `DecisionPlan` contiene directiva `Withdraw` O indicador crítico de riesgo
3. **Hiding** – `DecisionPlan` contiene directiva `PauseActivity`
4. **Recovering** – ruta de recuperación degradada después de Withdrawing/Hiding
5. **Traveling** – navegación en progreso
6. **Arriving** – navegación completada, contexto de destino detectado
7. **WaitingForSpawn** – en ubicación, sin acoplamiento aún, plan es observar/esperar
8. **Engaging** – combate/acoplamiento de objetivo activo
9. **MonitoringRisk** – actividad en curso, riesgo presente
10. **SelectingWorksite** – fase de selección de sitio activa
11. **Idle** – fallback cuando ningún estado más fuerte aplica

### Evaluación determinística

La evaluación consume:

- `SessionId`
- `SessionDomainState` actual
- `RiskAssessmentResult` si existe
- `DecisionPlan` más reciente
- snapshot de actividad anterior (si existe)
- timestamp actual

Produce:

- nuevo `SessionActivitySnapshot` con estado actual, anterior, razón de transición y metadata
- `SessionActivityTransition` si el estado cambió
- `ReasonCode` y `Reason` explicables
- metadata dict con señales de dominio que motivaron la decisión

La evaluación es **determinística**: los mismos inputs siempre producen el mismo estado.

### Transiciones e historial

Cada transición se registra con:

- `FromState` y `ToState`
- `ReasonCode` genérico (ej: `navigation-in-progress`, `active-engagement-detected`, `decision-plan-withdraw`)
- `Reason` legible
- `OccurredAtUtc` timestamp
- metadata dict

El historial es **append-only** y **acotado**: se conservan hasta 1000 transiciones más recientes por sesión para evitar crecimiento de memoria ilimitado.

Las reevaluaciones con el mismo estado **no crean transiciones duplicadas**: si el estado no cambió, solo se actualiza el snapshot, pero no se añade al historial.

### Integración en el flujo de refresh

Después de que se completa el pipeline de decisión:

```text
ui refresh
  -> semantic extraction
  -> risk classification
  -> domain projection
  -> policy engine
  -> decision plan store
  -> activity state evaluation    [NUEVO]
  -> activity state store         [NUEVO]
  -> Admin API inspection
```

La evaluación ocurre en `DefaultSessionUiRefreshService.ProjectAsync()` después de que el policy engine produce el `DecisionPlan`, usando:

- `ISessionActivityStateEvaluator` para calcular el nuevo estado
- `ISessionActivityStateStore` para persistir el snapshot y el historial

### Admin API de actividad

Tres nuevos endpoints inspeccionan el estado y historial de actividad:

- `GET /activity` – devuelve snapshots de actividad de todas las sesiones
- `GET /sessions/{id}/activity` – snapshot actual de actividad para la sesión `{id}`
- `GET /sessions/{id}/activity/history` – historial de transiciones para la sesión `{id}`, acotado a las últimas 100 entradas

Respuesta de ejemplo para `GET /sessions/alpha/activity`:

```json
{
  "sessionId": "alpha",
  "currentState": "Engaging",
  "previousState": "WaitingForSpawn",
  "lastTransitionAtUtc": "2025-01-15T14:32:18.567Z",
  "reasonCode": "active-engagement-detected",
  "reason": "Target is actively engaged and combat state indicates hostility.",
  "metadata": {
    "source_directives": "['Prioritize']",
    "active_target": "enemy_fighter",
    "threat_count": 3,
    "threat_severity": "Critical",
    "source_risk_policy": "ThreatResponse"
  },
  "isTerminal": false
}
```

Respuesta de ejemplo para `GET /sessions/alpha/activity/history`:

```json
{
  "sessionId": "alpha",
  "entries": [
    {
      "fromState": "Idle",
      "toState": "SelectingWorksite",
      "reasonCode": "decision-plan-select-site",
      "reason": "Decision plan contains SelectSite directive.",
      "occurredAtUtc": "2025-01-15T14:25:01.123Z",
      "metadata": {
        "source_directives": "['SelectSite']",
        "navigation_status": "Idle"
      }
    },
    {
      "fromState": "SelectingWorksite",
      "toState": "Traveling",
      "reasonCode": "navigation-in-progress",
      "reason": "Navigation state indicates active transit.",
      "occurredAtUtc": "2025-01-15T14:28:45.456Z",
      "metadata": {
        "destination": "primary-worksite",
        "progress_percent": 45
      }
    },
    {
      "fromState": "Traveling",
      "toState": "Arriving",
      "reasonCode": "navigation-arrival-detected",
      "reason": "Navigation just completed; now at destination.",
      "occurredAtUtc": "2025-01-15T14:31:02.789Z",
      "metadata": {
        "destination": "primary-worksite",
        "location_context": "worksite-entrance"
      }
    },
    {
      "fromState": "Arriving",
      "toState": "WaitingForSpawn",
      "reasonCode": "no-actionable-directives",
      "reason": "At location, no engagement or navigation directive active.",
      "occurredAtUtc": "2025-01-15T14:31:15.234Z",
      "metadata": {
        "plan_status": "WaitingForUpdate"
      }
    },
    {
      "fromState": "WaitingForSpawn",
      "toState": "Engaging",
      "reasonCode": "active-engagement-detected",
      "reason": "Target is actively engaged and combat state indicates hostility.",
      "occurredAtUtc": "2025-01-15T14:32:18.567Z",
      "metadata": {
        "active_target": "enemy_fighter",
        "threat_count": 3,
        "threat_severity": "Critical"
      }
    }
  ],
  "count": 5
}
```

### Garantías e invariantes

- **Por sesión**: cada `SessionId` tiene su propio snapshot y historial, aislado mediante lock.
- **Thread-safe**: el store usa sincronización por lock para mantener aislamiento cuando múltiples threads acceden a datos por sesión.
- **Determinístico**: la misma entrada (domain, risk, plan, previous state) siempre produce el mismo nuevo estado.
- **No reemplaza**: este modelo explica el ciclo de vida de la actividad; no reemplaza `SessionDomainState`, `RiskAssessmentResult` ni `DecisionPlan`.
- **Append-only history**: las transiciones solo se añaden cuando el estado cambia; historial acotado evita crescimiento ilimitado.
- **Explícito y centralizado**: toda lógica de transición vive en `ISessionActivityStateEvaluator`; evita decisiones de estado esparcidas entre políticas.

## Store de bindings editable en runtime

`DesktopTargetProfile` sigue siendo **config-driven** e inmutable durante la ejecución. Lo que ahora es editable en caliente es `SessionTargetBinding`.

### Precedencia de bindings al arrancar

El orden de carga es:

1. se cargan profiles configurados desde `DesktopTargets`
2. se cargan bindings configurados desde `SessionTargetBindings`
3. el `InMemorySessionTargetBindingStore` se inicializa con esos bindings
4. si hay persistencia habilitada, se cargan bindings persistidos y pisan por `SessionId` a los configurados
5. desde ese momento el store runtime queda autoritativo

Resumen práctico:

- los bindings de `appsettings` siguen sirviendo como defaults
- los bindings persistidos ganan para la misma `SessionId`
- los cambios hechos por API viven en el store runtime inmediatamente
- si borras un binding configurado, la sesión queda sin resolver hasta que crees otro
- si reinicias el worker y ese `SessionId` no existe en el archivo persistido, vuelve a aplicar el default de configuración

### Qué pasa cuando cambia un binding

- no hace falta reiniciar el worker
- la siguiente resolución usa el binding nuevo
- si había attachment activo para esa sesión, se invalida y se remueve del store de attachments
- `GET /sessions/{id}/target` muestra el binding y target nuevos inmediatamente
- el siguiente `ui/refresh`, command o attach vuelve a conectarse usando el target actualizado
- el aislamiento entre sesiones se mantiene porque el store está indexado por `SessionId`

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
    "BindingStorePersistenceMode": "JsonFile",
    "BindingStoreFilePath": "data/session-target-bindings.json",
    "RuntimePersistence": {
      "EnableRuntimePersistence": true,
      "Mode": "JsonFile",
      "BasePath": "data/runtime-state",
      "SchemaVersion": 1,
      "MaxDecisionHistoryEntries": 50,
      "PersistDecisionHistory": true,
      "PersistActivityState": true,
      "PersistOperationalMemory": true,
      "PersistDecisionExecution": true,
      "AutoFlushAfterStateChanges": true,
      "FailOnPersistenceErrors": false
    },
    "ExecutionCoordination": {
      "EnableTargetCoordination": true,
      "EnableGlobalCoordination": false,
      "DefaultTargetCooldownMs": 0,
      "MaxConcurrentGlobalTargetOperations": 1,
      "WaitWarningThresholdMs": 1000,
      "SessionExclusiveOperationKinds": [
        "WorkItem",
        "UiCommand",
        "UiRefresh",
        "AttachmentEnsure",
        "AttachmentInvalidate"
      ],
      "TargetExclusiveOperationKinds": [
        "WorkItem",
        "UiCommand",
        "UiRefresh",
        "AttachmentEnsure",
        "AttachmentInvalidate"
      ],
      "GlobalExclusiveOperationKinds": []
    },
    "RiskClassification": {
      "EnableRiskClassification": true,
      "DefaultUnknownDisposition": "Unknown",
      "DefaultUnknownSeverity": "Unknown",
      "DefaultUnknownPolicy": "Observe",
      "MaxReturnedEntities": 100,
      "RequireExplicitSafeMatch": true,
      "Rules": [
        {
          "RuleName": "safe-label",
          "MatchByName": [ "trusted", "safe", "allowed" ],
          "NameMatchMode": "Contains",
          "Disposition": "Safe",
          "Severity": "Low",
          "Priority": 10,
          "SuggestedPolicy": "Ignore",
          "Reason": "Configured safe label."
        },
        {
          "RuleName": "warning-type",
          "MatchByType": [ "Warning", "Critical" ],
          "TypeMatchMode": "Exact",
          "Disposition": "Threat",
          "Severity": "Critical",
          "Priority": 800,
          "SuggestedPolicy": "Withdraw",
          "Reason": "Warning and critical typed candidates require withdrawal."
        }
      ]
    },
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
- `BindingStorePersistenceMode` debe ser válido.
- `BindingStoreFilePath` es obligatorio cuando `BindingStorePersistenceMode=JsonFile`.
- cada `DesktopTargetProfile` debe tener `ProfileName` único y `Kind` válido.
- cada `SessionTargetBinding` debe apuntar a una sesión configurada.
- cada binding debe apuntar a un profile existente.
- cada sesión requiere binding cuando `DriverMode=DesktopTargetAdapter`.
- si un template usa variables como `{Port}`, cada binding debe proveerlas.
- `BaseAddressTemplate` debe renderizar una URL absoluta válida para los targets HTTP.
- `ExecutionCoordination.DefaultTargetCooldownMs` no puede ser negativo.
- `ExecutionCoordination.MaxConcurrentGlobalTargetOperations` debe ser mayor que cero.
- `ExecutionCoordination.WaitWarningThresholdMs` no puede ser negativo.
- los operation kinds configurados para coordinación deben existir.
- si `RiskClassification.EnableRiskClassification=true`, debe existir al menos una regla activa.
- cada regla de riesgo activa debe tener `RuleName` único, prioridad entre 0 y 1000 y al menos un matcher por nombre, tipo o tag.
- `PolicyEngine.PolicyOrder` no puede estar vacío, no puede repetir políticas y solo acepta políticas conocidas.
- `PolicyEngine.MaxReturnedDirectives` debe ser mayor que cero.
- las prioridades de políticas deben estar entre 0 y 1000.
- los thresholds de recursos deben estar entre 0 y 100 y critical no puede ser mayor que degraded.
- cada regla de política activa debe tener `RuleName` único dentro de su familia.
- cada familia solo acepta directive kinds compatibles con su política.
- prioridades de reglas de política deben estar entre 0 y 1000.
- duraciones `MinimumWaitMs` no pueden ser negativas.
- rangos de progreso, recurso y confianza deben ser coherentes.
- reglas no-site deben declarar al menos un matcher o threshold.
- fallbacks habilitados deben tener `RuleName`, `DirectiveKind`, `Priority` y `Reason`, no pueden declarar matchers y deben usar directive kinds permitidos para su familia.
- reglas de agregación deben tener nombres únicos, directive kinds válidos y status válidos.
- `RuntimePersistence.BasePath` es obligatorio cuando `RuntimePersistence.Mode=JsonFile`.
- `RuntimePersistence.SchemaVersion` debe ser mayor que cero.
- `RuntimePersistence.MaxDecisionHistoryEntries` debe ser mayor que cero.
- `RuntimePersistence.Mode=None` solo es válido cuando `RuntimePersistence.EnableRuntimePersistence=false`.

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
- `GET /coordination`
- `GET /coordination/sessions/{id}`
- `GET /sessions/{id}/ui`
- `GET /sessions/{id}/ui/raw`
- `POST /sessions/{id}/ui/refresh`
- `GET /domain`
- `GET /sessions/{id}/domain`
- `GET /semantic`
- `GET /sessions/{id}/semantic`
- `GET /sessions/{id}/semantic/summary`
- `GET /sessions/{id}/semantic/lists`
- `GET /sessions/{id}/semantic/alerts`
- `GET /risk`
- `GET /sessions/{id}/risk`
- `GET /sessions/{id}/risk/summary`
- `GET /sessions/{id}/risk/entities`
- `GET /sessions/{id}/risk/threats`
- `GET /decision-plans`
- `GET /sessions/{id}/decision-plan`
- `GET /sessions/{id}/decision-plan/explanation`
- `GET /sessions/{id}/decision-plan/summary`
- `GET /sessions/{id}/decision-plan/directives`
- `GET /sessions/{id}/decision-plan/history`
- `POST /sessions/{id}/decision-plan/evaluate`
- `GET /persistence`
- `GET /sessions/{id}/persistence`
- `POST /persistence/flush`
- `POST /sessions/{id}/persistence/flush`
- `GET /policy`
- `GET /sessions/{id}/policy-state`
- `GET /sessions/{id}/policy-state/history`
- `POST /sessions/{id}/pause-policy`
- `POST /sessions/{id}/resume-policy`
- `GET /policy-rules`
- `GET /policy-rules/site-selection`
- `GET /policy-rules/threat-response`
- `GET /policy-rules/target-prioritization`
- `GET /policy-rules/resource-usage`
- `GET /policy-rules/transit`
- `GET /policy-rules/abort`

Endpoints nuevos de inspección:

- `GET /targets`
- `GET /targets/{profileName}`
- `GET /sessions/{id}/target`
- `GET /bindings`
- `GET /bindings/{sessionId}`

### Control de policy por sesión (Fase 4.1)

La policy control no detiene el runtime de la sesión. Solo bloquea la planificación y ejecución dirigidas por policy para una sesión concreta.

- `POST /sessions/{id}/pause-policy` marca la policy como pausada para esa sesión.
- `POST /sessions/{id}/resume-policy` vuelve a habilitar la policy para esa sesión.
- `GET /sessions/{id}/policy-state` expone el estado actual.
- `GET /sessions/{id}/policy-state/history` expone el historial acotado.
- `GET /policy` devuelve el estado de policy de todas las sesiones configuradas.

Mientras la policy está pausada, el runtime puede seguir haciendo refresh de UI, extracción semántica, clasificación de riesgo, proyección de dominio, actualización de memoria operacional y flush de persistencia. Lo que se bloquea de forma determinista es la evaluación normal de policies y la ejecución manual o automática de planes basados en esas policies. Si la persistencia runtime está habilitada, el estado pausado y su historial se rehidratan después del reinicio.

Ejemplo de pausa:

```json
{
  "reasonCode": "operator:maintenance",
  "reason": "Temporarily freeze policy-driven decisions",
  "changedBy": "ops",
  "metadata": {
    "ticket": "INC-1234"
  }
}
```

Ejemplo de conflicto al ejecutar mientras está pausada:

```json
{
  "error": "Policy-driven execution is paused for session 'alpha'."
}
```

Endpoints nuevos de comandos semánticos:

- `POST /sessions/{id}/commands`
- `POST /sessions/{id}/nodes/{nodeId}/click`
- `POST /sessions/{id}/nodes/{nodeId}/invoke`
- `POST /sessions/{id}/nodes/{nodeId}/text`
- `POST /sessions/{id}/nodes/{nodeId}/toggle`
- `POST /sessions/{id}/nodes/{nodeId}/select`

Endpoints nuevos de mutación de bindings:

- `PUT /bindings/{sessionId}`
- `DELETE /bindings/{sessionId}`

`/sessions/{id}/target` expone:

- profile resuelto
- binding aplicado
- target renderizado
- attachment actual si existe
- adapter seleccionado

`/coordination` expone:

- ejecuciones activas
- ejecuciones esperando
- resource keys retenidas o con waiters
- duración de espera/running
- último completion por resource
- cooldown activo por target
- contención por scope

`/sessions/{id}/decision-plan/explanation` expone:

- reglas efectivas normalizadas por familia
- trazas por política con reglas consideradas, rechazadas y matched
- `RejectedReason`, `MatchedCriteria`, `FallbackUsed` y directivas producidas
- reglas de agregación aplicadas, incluyendo suppression/status
- directivas finales con metadata estándar

Forma abreviada:

```json
{
  "sessionId": "alpha",
  "effectiveRules": {
    "threatResponseRetreatRules": [
      { "ruleName": "default-critical-threat", "ruleFamily": "ThreatResponse.RetreatRules", "isFallback": false }
    ]
  },
  "policyEvaluations": [
    {
      "policyName": "ThreatResponsePolicy",
      "matchedRuleName": "default-critical-threat",
      "fallbackUsed": false,
      "ruleTraces": [
        {
          "ruleFamily": "ThreatResponse.RetreatRules",
          "ruleName": "default-critical-threat",
          "outcome": "Matched",
          "matchedCriteria": [ "minThreatSeverity" ],
          "producedDirectiveKinds": [ "Withdraw" ]
        }
      ]
    }
  ],
  "aggregationRulesApplied": [
    { "ruleName": "blocking-response-over-selection", "ruleType": "Suppression", "applied": true }
  ],
  "finalDirectives": [
    {
      "directiveKind": "Withdraw",
      "metadata": {
        "matchedRuleName": "default-critical-threat",
        "policyRuleFamily": "ThreatResponse.RetreatRules",
        "isFallback": "False"
      }
    }
  ]
}
```

Ejemplos:

```powershell
Invoke-RestMethod http://localhost:5088/coordination
Invoke-RestMethod http://localhost:5088/coordination/sessions/alpha
Invoke-RestMethod http://localhost:5088/domain
Invoke-RestMethod http://localhost:5088/sessions/alpha/domain
Invoke-RestMethod http://localhost:5088/semantic
Invoke-RestMethod http://localhost:5088/sessions/alpha/semantic
Invoke-RestMethod http://localhost:5088/sessions/alpha/semantic/summary
Invoke-RestMethod http://localhost:5088/sessions/alpha/semantic/lists
Invoke-RestMethod http://localhost:5088/sessions/alpha/semantic/alerts
Invoke-RestMethod http://localhost:5088/risk
Invoke-RestMethod http://localhost:5088/sessions/alpha/risk
Invoke-RestMethod http://localhost:5088/sessions/alpha/risk/summary
Invoke-RestMethod http://localhost:5088/sessions/alpha/risk/entities
Invoke-RestMethod http://localhost:5088/sessions/alpha/risk/threats
Invoke-RestMethod http://localhost:5088/decision-plans
Invoke-RestMethod http://localhost:5088/sessions/alpha/decision-plan
Invoke-RestMethod http://localhost:5088/sessions/alpha/decision-plan/explanation
Invoke-RestMethod http://localhost:5088/sessions/alpha/decision-plan/summary
Invoke-RestMethod http://localhost:5088/sessions/alpha/decision-plan/directives
Invoke-RestMethod http://localhost:5088/sessions/alpha/decision-plan/history
Invoke-RestMethod -Method Post http://localhost:5088/sessions/alpha/decision-plan/evaluate
Invoke-RestMethod http://localhost:5088/persistence
Invoke-RestMethod http://localhost:5088/sessions/alpha/persistence
Invoke-RestMethod -Method Post http://localhost:5088/sessions/alpha/persistence/flush
Invoke-RestMethod http://localhost:5088/policy-rules
Invoke-RestMethod http://localhost:5088/policy-rules/site-selection
Invoke-RestMethod http://localhost:5088/policy-rules/threat-response
Invoke-RestMethod http://localhost:5088/policy-rules/target-prioritization
Invoke-RestMethod http://localhost:5088/policy-rules/resource-usage
Invoke-RestMethod http://localhost:5088/policy-rules/transit
Invoke-RestMethod http://localhost:5088/policy-rules/abort
```

Escenarios esperados:

- misma sesión: un `UiCommand` y un `ui/refresh` se serializan por la key `session:alpha`.
- dos sesiones, mismo target efectivo: `alpha` y `beta` esperan por la misma key `target:...`.
- dos sesiones, targets distintos: pueden ejecutar en paralelo porque las keys de target son diferentes.

### Decisión sobre UI state faltante

Si una sesión todavía no tiene `UiTree` proyectado, el executor hace auto-refresh antes de resolver el comando. Después de un comando exitoso dispara un refresh posterior para dejar el árbol actualizado. Las fallas semánticas devuelven `409 Conflict` con `UiCommandResultDto`.

### Payload para upsert de binding

`PUT /bindings/{sessionId}` acepta:

- `TargetProfileName`
- `Variables`
- `Overrides`

`SessionId` siempre viene de la ruta, no del body.

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
2. agrega un `SessionTargetBinding` en configuración o por Admin API
3. define `SessionId`
4. define `TargetProfileName`
5. agrega variables como `Port`
6. si hace falta, usa `Overrides` para esa sesión

## Cómo editar bindings en runtime

Listar bindings actuales:

```powershell
Invoke-RestMethod http://localhost:5088/bindings
```

Consultar un binding:

```powershell
Invoke-RestMethod http://localhost:5088/bindings/alpha
```

Crear o actualizar un binding:

```powershell
Invoke-RestMethod -Method Put `
  -Uri http://localhost:5088/bindings/alpha `
  -ContentType 'application/json' `
  -Body '{
    "targetProfileName":"test-app",
    "variables":{
      "Port":"7102"
    },
    "overrides":null
  }'
```

Borrar un binding runtime:

```powershell
Invoke-RestMethod -Method Delete http://localhost:5088/bindings/alpha
```

Verificar el target resuelto después del cambio:

```powershell
Invoke-RestMethod http://localhost:5088/sessions/alpha/target
```

Si borras un binding, `/sessions/{id}/target` pasa a devolver conflicto hasta que crees uno nuevo.

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
Invoke-RestMethod http://localhost:5088/bindings
Invoke-RestMethod http://localhost:5088/sessions/alpha/target
Invoke-RestMethod http://localhost:5088/coordination
Invoke-RestMethod http://localhost:5088/coordination/sessions/alpha
Invoke-RestMethod -Method Post http://localhost:5088/sessions/alpha/ui/refresh
Invoke-RestMethod http://localhost:5088/sessions/alpha/ui
Invoke-RestMethod http://localhost:5088/sessions/alpha/ui/raw
Invoke-RestMethod http://localhost:5088/sessions/alpha/risk
Invoke-RestMethod http://localhost:5088/sessions/alpha/risk/summary
Invoke-RestMethod http://localhost:5088/sessions/alpha/risk/threats
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
- validación de persistencia `JsonFile`
- validación de `RuntimePersistence`
- backend runtime JSON con escritura/carga y archivos corruptos tolerados
- rehidratación de actividad, memoria operacional, decisión y ejecución
- historial acotado de `DecisionPlan`
- endpoints `/persistence`
- endpoint `/sessions/{id}/persistence`
- endpoint `/sessions/{id}/decision-plan/history`
- rehidratación de historial de decisión después de reinicio del worker
- errores por binding faltante, profile inexistente o variables faltantes
- render de templates por binding
- store runtime editable de bindings
- persistencia JSON y precedencia de startup
- endpoints `GET /bindings`
- endpoints `PUT /bindings/{sessionId}`
- endpoints `DELETE /bindings/{sessionId}`
- rebind runtime sin reiniciar el worker
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
- exclusión de ejecución por sesión
- exclusión por target efectivo compartido
- concurrencia cuando los targets efectivos son distintos
- cooldown por target
- cancelación limpia de waiters
- snapshot de coordinación activo/en espera
- endpoints `/coordination`
- rebind runtime actualizando la key de target y reattach
- defaults de `SessionDomainState`
- aislamiento y updates de `ISessionDomainStateStore`
- proyección de dominio con UI disponible, ausente y degradada
- endpoints `GET /domain`
- endpoint `GET /sessions/{id}/domain`
- actualización de dominio después de `ui/refresh`
- aislamiento de snapshots de dominio entre sesiones
- lectura de dominio después de cambios runtime de binding
- extracción semántica genérica desde targets, alerts, transit, resources, capabilities y presence
- store e inspección de extracción semántica por sesión
- candidate builder de riesgo desde targets, presence y alerts
- reglas de riesgo por nombre, tipo y tags
- precedencia first-match-wins y fallback unknown
- clasificación safe, unknown, threat y prioritized threat
- agregación de severity, priority y top policy
- store de risk assessment aislado por sesión
- endpoints `GET /risk`
- endpoints `GET /sessions/{id}/risk`
- endpoints `GET /sessions/{id}/risk/summary`
- endpoints `GET /sessions/{id}/risk/entities`
- endpoints `GET /sessions/{id}/risk/threats`
- flujo integrado `ui/refresh` -> semantic extraction -> risk classification -> domain projection
- dominio alimentado por severidad, conteos y señales de riesgo
- policy engine dedicado después de domain projection
- políticas `Abort`, `ThreatResponse`, `Transit`, `ResourceUsage`, `TargetPrioritization` y `SelectNextSite`
- agregación determinística de directivas y precedencia abort/threat/transit
- store de `DecisionPlan` aislado por sesión
- endpoints `GET /decision-plans`
- endpoints `GET /sessions/{id}/decision-plan`
- endpoints `GET /sessions/{id}/decision-plan/explanation`
- endpoints `GET /sessions/{id}/decision-plan/summary`
- endpoints `GET /sessions/{id}/decision-plan/directives`
- endpoint `POST /sessions/{id}/decision-plan/evaluate`
- flujo integrado `ui/refresh` -> semantic extraction -> risk classification -> domain projection -> policy engine -> decision plan
