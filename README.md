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
  -> Admin API inspection
```

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
  -> Admin API inspection
```

`DefaultSessionDomainStateProjectionService` consume `RiskAssessmentResult` de forma opcional. Cuando existe, `ThreatState.Severity` viene de la amenaza clasificada más fuerte, `UnknownCount` viene del summary unknown, `HostileCount` del summary threat, `NeutralCount` del summary safe, `IsSafe` solo es true cuando no quedan threats ni unknowns, y `Signals` incluye razones, reglas y políticas superiores. `ThreatState` también expone `TopSuggestedPolicy`, `TopEntityLabel` y `TopEntityType`.

Si la proyección UI falla, el mismo servicio registra el error en `SessionUiState` y degrada el snapshot de dominio con `Source=UiRefreshFailure` y warnings.

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

Endpoints nuevos de inspección:

- `GET /targets`
- `GET /targets/{profileName}`
- `GET /sessions/{id}/target`
- `GET /bindings`
- `GET /bindings/{sessionId}`

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
