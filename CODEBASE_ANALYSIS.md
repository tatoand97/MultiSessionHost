# BotEve Codebase Implementation Analysis

## 1. Policy Engine Structure

### Main Policy Engine Files

**Location:** `MultiSessionHost.Desktop/Policy/`

#### Core Engine Components

- **[DefaultPolicyEngine.cs](MultiSessionHost.Desktop/Policy/DefaultPolicyEngine.cs)** - Main orchestrator that:
  - Builds `PolicyEvaluationContext` from session state
  - Orders and executes policies sequentially
  - Delegates aggregation to `IDecisionPlanAggregator`
  - Handles persistence via `IRuntimePersistenceCoordinator`
  - Checks `options.PolicyEngine.BlockOnAbort` to stop evaluation early if any policy aborts

- **[PolicyInterfaces.cs](MultiSessionHost.Desktop/Policy/PolicyInterfaces.cs)** - Defines:
  - `IPolicyEngine` - Main evaluation interface
  - `IPolicy` - Individual policy interface with `Name` property and `EvaluateAsync()` method
  - `IDecisionPlanAggregator` - Combines policy results into final plan
  - `ISessionDecisionPlanStore` - Persistence for decision plans with history

#### Evaluation Context Structure

**[PolicyEvaluationContext.cs](MultiSessionHost.Desktop/Policy/PolicyEvaluationContext.cs)** - sealed record with:
```csharp
public sealed record PolicyEvaluationContext(
    SessionId SessionId,
    SessionSnapshot SessionSnapshot,
    SessionUiState? SessionUiState,
    SessionDomainState SessionDomainState,
    UiSemanticExtractionResult? UiSemanticExtractionResult,
    RiskAssessmentResult? RiskAssessmentResult,
    ResolvedDesktopTargetContext? ResolvedDesktopTargetContext,
    DesktopSessionAttachment? DesktopSessionAttachment,
    DateTimeOffset Now);
```

### Decision Plan Generation

**[DecisionModels.cs](MultiSessionHost.Desktop/Policy/DecisionModels.cs)** defines the complete decision hierarchy:

#### DecisionPlan Structure
```csharp
public sealed record DecisionPlan(
    SessionId SessionId,
    DateTimeOffset PlannedAtUtc,
    DecisionPlanStatus PlanStatus,  // Unknown, Idle, Ready, Blocked, Aborting
    IReadOnlyList<DecisionDirective> Directives,
    IReadOnlyList<DecisionReason> Reasons,
    PolicyExecutionSummary Summary,
    IReadOnlyList<string> Warnings,
    DecisionPlanExplanation? Explanation = null)
```

#### DecisionDirective Structure
```csharp
public sealed record DecisionDirective(
    string DirectiveId,
    DecisionDirectiveKind DirectiveKind,  // Observe, Navigate, SelectSite, SelectTarget, etc.
    int Priority,
    string SourcePolicy,
    string? TargetId,
    string? TargetLabel,
    string? SuggestedPolicy,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<DecisionReason> Reasons);
```

#### DecisionPlanExplanation Structure
```csharp
public sealed record DecisionPlanExplanation(
    IReadOnlyList<PolicyEvaluationExplanation> PolicyEvaluations,  // Per-policy traces
    IReadOnlyList<AggregationRuleApplicationTrace> AggregationRulesApplied,
    IReadOnlyList<string> FinalDirectiveKinds,
    IReadOnlyList<string> FinalWarnings,
    IReadOnlyList<string> FinalReasonCodes);
```

#### PolicyEvaluationExplanation
```csharp
public sealed record PolicyEvaluationExplanation(
    string PolicyName,
    string? CandidateSummary,
    IReadOnlyList<PolicyRuleEvaluationTrace> RuleTraces,  // Detailed rule matching
    string? MatchedRuleName,
    bool FallbackUsed,
    IReadOnlyList<string> ProducedDirectiveKinds);
```

### Main Policies

**[Policies in MultiSessionHost.Desktop/Policy/](MultiSessionHost.Desktop/Policy/)**

1. **[SelectNextSitePolicy.cs](MultiSessionHost.Desktop/Policy/SelectNextSitePolicy.cs)**
   - Creates `PolicyRuleCandidate` for site selection
   - Applies `SiteSelectionAllowRules` first, then `SiteSelectionFallbackRules`
   - Integrates with location state to determine site label and type

2. **[ThreatResponsePolicy.cs](MultiSessionHost.Desktop/Policy/ThreatResponsePolicy.cs)**
   - Creates threat response candidates
   - Applies rule sequence: RetreatRules → DenyRules → FallbackRules
   - Uses fallback candidates when no match found

3. **[TargetPrioritizationPolicy.cs](MultiSessionHost.Desktop/Policy/TargetPrioritizationPolicy.cs)**
   - Creates target priority candidates
   - Applies: TargetPriorityRules → TargetDenyRules → TargetFallbackRules
   - Integrates fallback candidate handling

4. **[TransitPolicy.cs](MultiSessionHost.Desktop/Policy/TransitPolicy.cs)**
   - Manages transit directives
   - Applies: TransitRules → TransitFallbackRules

5. **[ResourceUsagePolicy.cs](MultiSessionHost.Desktop/Policy/ResourceUsagePolicy.cs)**
   - Manages resource constraints
   - Applies: ResourceUsageRules → ResourceUsageFallbackRules

6. **[AbortPolicy.cs](MultiSessionHost.Desktop/Policy/AbortPolicy.cs)**
   - Generates abort directives when conditions met
   - Applies: AbortRules → AbortFallbackRules
   - Can trigger early evaluation halt via `BlockOnAbort`

### Policy Rule System

**[PolicyRuleModels.cs](MultiSessionHost.Desktop/Policy/PolicyRuleModels.cs)**

- **PolicyRuleSet** - Container with 6 rule families:
  - SiteSelectionAllowRules / SiteSelectionFallbackRules
  - ThreatResponseRetreatRules / DenyRules / FallbackRules
  - TargetPriorityRules / DenyRules / FallbackRules
  - ResourceUsageRules / FallbackRules
  - TransitRules / FallbackRules
  - AbortRules / FallbackRules

- **PolicyRule** - Abstract base with extensive matching criteria:
  - Label/Type/Tag matching with modes (Exact, Prefix, Contains)
  - Threat/Risk severity thresholds
  - Session status requirements
  - Navigation/Activity state requirements
  - Resource constraints (health, capacity, energy)
  - Custom metrics and confidence thresholds
  - Action: DirectiveKind, Priority, Blocks flag, Aborts flag

---

## 2. Operational Memory Structure

**Location:** `MultiSessionHost.Desktop/Memory/`

### SessionOperationalMemorySnapshot

**[SessionOperationalMemoryModels.cs](MultiSessionHost.Desktop/Memory/SessionOperationalMemoryModels.cs)**

```csharp
public sealed record SessionOperationalMemorySnapshot(
    SessionId SessionId,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    SessionOperationalMemorySummary Summary,
    IReadOnlyList<WorksiteObservation> KnownWorksites,
    IReadOnlyList<RiskObservation> RecentRiskObservations,
    IReadOnlyList<PresenceObservation> RecentPresenceObservations,
    IReadOnlyList<TimingObservation> RecentTimingObservations,
    IReadOnlyList<OutcomeObservation> RecentOutcomeObservations,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> Metadata)
```

### Memory Observation Categories

**MemoryObservationCategory enum:**
- `Worksite` - Location information
- `Risk` - Threat/danger assessments
- `Presence` - Entity presence tracking
- `Timing` - Duration measurements
- `Outcome` - Action results

### Observation Types

1. **WorksiteObservation** - Location records:
   - WorksiteKey, Label, Tags
   - FirstObservedAtUtc, LastObservedAtUtc, LastSelectedAtUtc, LastArrivedAtUtc
   - LastOutcome, VisitCount, SuccessCount, FailureCount
   - OccupancySignals, IsStale flag

2. **RiskObservation** - Threat records:
   - ObservationId, EntityKey/Label, SourceKey/Label
   - RiskSeverity, RiskPolicySuggestion
   - FirstObservedAtUtc, LastObservedAtUtc, Count
   - IsStale flag

3. **PresenceObservation** - Entity presence records:
   - ObservationId, EntityKey/Label/Type, Status
   - FirstObservedAtUtc, LastObservedAtUtc, Count
   - IsStale flag

4. **TimingObservation** - Performance metrics:
   - TimingKey, Kind (type of operation)
   - LastDurationMs, SampleCount, Min/Max/Average durations

5. **OutcomeObservation** - Action results:
   - OutcomeId, RelatedWorksiteKey, RelatedDirectiveKind
   - ResultKind, ObservedAtUtc, Message

### SessionOperationalMemorySummary
```csharp
public sealed record SessionOperationalMemorySummary(
    int KnownWorksiteCount,
    int ActiveRiskMemoryCount,
    int ActivePresenceMemoryCount,
    int TimingObservationCount,
    int OutcomeObservationCount,
    DateTimeOffset LastUpdatedAtUtc,
    RiskSeverity TopRememberedRiskSeverity,
    string? MostRecentOutcomeKind)
```

### Memory Update Pattern

**[DefaultSessionOperationalMemoryUpdater.cs](MultiSessionHost.Desktop/Memory/DefaultSessionOperationalMemoryUpdater.cs)**

Updates through **SessionOperationalMemoryUpdateContext**:
```csharp
public sealed record SessionOperationalMemoryUpdateContext(
    SessionId SessionId,
    SessionOperationalMemorySnapshot? PreviousSnapshot,
    SessionDomainState? DomainState,
    UiSemanticExtractionResult? SemanticExtraction,
    RiskAssessmentResult? RiskAssessment,
    DecisionPlan? DecisionPlan,
    DecisionPlanExecutionResult? ExecutionResult,
    SessionActivitySnapshot? ActivitySnapshot,
    DateTimeOffset Now)
```

**Memory Update Process:**
1. Projects worksites from domain/extraction state
2. Projects risk observations from risk assessment results
3. Projects presence observations from domain state
4. Projects timing observations from activity/execution
5. Projects outcomes from execution results
6. Applies staleness marking (configurable by `StaleAfterMinutes`)
7. Maintains bounded history per observation type
8. Creates new `SessionOperationalMemorySnapshot` with updated collections

### Memory Interfaces

**[SessionOperationalMemoryInterfaces.cs](MultiSessionHost.Desktop/Memory/SessionOperationalMemoryInterfaces.cs)**

- **ISessionOperationalMemoryReader** - Read operations
- **ISessionOperationalMemoryStore** - Full CRUD with restore capability
- **ISessionOperationalMemoryUpdater** - Update logic with context

---

## 3. Runtime Persistence

**Location:** `MultiSessionHost.Desktop/Persistence/`

### Persistence Models

**[RuntimePersistenceModels.cs](MultiSessionHost.Desktop/Persistence/RuntimePersistenceModels.cs)**

#### SessionRuntimePersistenceEnvelope
Complete snapshot for serialization:
```csharp
public sealed record SessionRuntimePersistenceEnvelope(
    int SchemaVersion,
    SessionId SessionId,
    DateTimeOffset SavedAtUtc,
    SessionActivitySnapshot? ActivitySnapshot,
    SessionOperationalMemorySnapshot? OperationalMemorySnapshot,
    IReadOnlyList<MemoryObservationRecord> OperationalMemoryHistory,
    DecisionPlan? LatestDecisionPlan,
    IReadOnlyList<DecisionPlanHistoryEntry> DecisionPlanHistory,
    DecisionPlanExecutionResult? LatestDecisionExecution,
    IReadOnlyList<DecisionPlanExecutionRecord> DecisionExecutionHistory,
    IReadOnlyDictionary<string, string> Metadata)
```

#### RuntimePersistenceSessionStatus
Status tracking for each session:
```csharp
public sealed record RuntimePersistenceSessionStatus(
    SessionId SessionId,
    bool Rehydrated,
    DateTimeOffset? LastLoadedAtUtc,
    DateTimeOffset? LastSavedAtUtc,
    string? LastError,
    string? PersistedPath,
    int ActivityHistoryCount,
    int OperationalMemoryHistoryCount,
    int DecisionPlanHistoryCount,
    int DecisionExecutionHistoryCount)
```

#### RuntimePersistenceStatusSnapshot
Global persistence status:
```csharp
public sealed record RuntimePersistenceStatusSnapshot(
    bool Enabled,
    string Mode,
    string? BasePath,
    int SchemaVersion,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<RuntimePersistenceSessionStatus> Sessions)
```

### RuntimePersistenceCoordinator

**[RuntimePersistenceCoordinator.cs](MultiSessionHost.Desktop/Persistence/RuntimePersistenceCoordinator.cs)**

#### Rehydration Process

**`RehydrateAsync()`** - Called at startup:
1. Loads all persisted envelopes via `IRuntimePersistenceBackend`
2. Filters for configured sessions
3. For each envelope:
   - Calls `RehydrateEnvelopeAsync()` which:
     - Restores `SessionActivitySnapshot` to activity store
     - Restores `SessionOperationalMemorySnapshot` + history to memory store
     - Restores decision plan + history to decision plan store
     - Restores execution result + history to execution store
   - Updates status tracking (marked as `Rehydrated=true`)
4. Logs warnings for unconfigured sessions

#### Persistence Configuration

Selective persistence via `RuntimePersistenceOptions`:
- `EnableRuntimePersistence` - Master toggle
- `Mode` - JsonFile, SqlServer, etc.
- `BasePath` - Location for files/database
- `PersistActivityState` - Include activity snapshots
- `PersistOperationalMemory` - Include memory snapshots
- `PersistDecisionHistory` - Include decision plans
- `PersistDecisionExecution` - Include execution results
- `AutoFlushAfterStateChanges` - Auto-persist after updates
- `MaxDecisionHistoryEntries` - Bounded history size
- `SchemaVersion` - Format version tracking

#### Flush Operations

**`FlushSessionAsync()`** and **`FlushAllAsync()`**:
1. Build `SessionRuntimePersistenceEnvelope` from stores
2. Call `IRuntimePersistenceBackend.SaveSessionAsync()`
3. Update status tracking (`LastSavedAtUtc`, history counts, path)

---

## 4. Session Domain State

**Location:** `MultiSessionHost.Core/Models/SessionDomainState.cs`

### SessionDomainState Structure

```csharp
public sealed record SessionDomainState(
    SessionId SessionId,
    DateTimeOffset? CapturedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version,
    DomainSnapshotSource Source,  // Driver, Bootstrap, Rehydrated, etc.
    NavigationState Navigation,
    CombatState Combat,
    ThreatState Threat,
    TargetState Target,
    CompanionState Companions,
    ResourceState Resources,
    LocationState Location,
    IReadOnlyList<string> Warnings)
```

### Sub-States

**NavigationState**
- Status (Idle, Transiting, Arrived)
- IsTransitioning bool
- DestinationLabel, RouteLabel
- ProgressPercent, StartedAtUtc, UpdatedAtUtc

**CombatState**
- Status (Idle, Engaged, Retreating)
- ActivityPhase, OffensiveActionsActive, DefensivePostureActive
- EngagedAtUtc, UpdatedAtUtc

**ThreatState**
- Severity (Unknown, Safe, Warning, Critical)
- UnknownCount, NeutralCount, HostileCount
- IsSafe, LastThreatChangedAtUtc
- Signals (detections), TopSuggestedPolicy, TopEntityLabel

**TargetState**
- HasActiveTarget, PrimaryTargetId, PrimaryTargetLabel
- TrackedTargetCount, LockedTargetCount, SelectedTargetCount
- Status, LastTargetChangedAtUtc

**CompanionState**
- Status (Idle, Active, Deployed)
- AreAvailable, AreHealthy
- ActiveCount, DeployedCount, DockedCount, IdleCount

**ResourceState**
- HealthPercent, CapacityPercent, EnergyPercent
- AvailableChargeCount, CapacityCount
- IsDegraded, IsCritical

**LocationState**
- ContextLabel, SubLocationLabel
- IsBaseOrHome, IsUnknown
- Confidence (High, Medium, Low)
- ArrivedAtUtc, UpdatedAtUtc

### RiskAssessmentResult

**[RiskModels.cs](MultiSessionHost.Desktop/Risk/RiskModels.cs)**

```csharp
public sealed record RiskAssessmentResult(
    SessionId SessionId,
    DateTimeOffset AssessedAtUtc,
    IReadOnlyList<RiskEntityAssessment> Entities,
    RiskAssessmentSummary Summary,
    IReadOnlyList<string> Warnings)
```

#### RiskEntityAssessment
```csharp
public sealed record RiskEntityAssessment(
    string CandidateId,
    RiskEntitySource Source,
    string Name,
    string Type,
    IReadOnlyList<string> Tags,
    RiskDisposition Disposition,  // Unknown, Safe, Threat, Critical
    RiskSeverity Severity,  // Unknown, Low, Medium, High, Critical
    int Priority,
    RiskPolicySuggestion SuggestedPolicy,  // SelectTarget, Avoid, Withdraw, etc.
    string? MatchedRuleName,
    IReadOnlyList<string> Reasons,
    double Confidence,
    IReadOnlyDictionary<string, string> Metadata)
```

#### RiskAssessmentSummary
```csharp
public sealed record RiskAssessmentSummary(
    int SafeCount,
    int UnknownCount,
    int ThreatCount,
    RiskSeverity HighestSeverity,
    int HighestPriority,
    bool HasWithdrawPolicy,
    string? TopCandidateId,
    string? TopCandidateName,
    string? TopCandidateType,
    RiskPolicySuggestion TopSuggestedPolicy)
```

### Activity State Machine

**[SessionActivityModels.cs](MultiSessionHost.Desktop/Activity/SessionActivityModels.cs)**

#### SessionActivitySnapshot
```csharp
public sealed record SessionActivitySnapshot(
    SessionId SessionId,
    SessionActivityStateKind CurrentState,  // Idle, Active, Paused, Executing, Waiting, Faulted
    SessionActivityStateKind? PreviousState,
    DateTimeOffset? LastTransitionAtUtc,
    string? LastReasonCode,
    string? LastReason,
    IReadOnlyDictionary<string, string> LastMetadata,
    IReadOnlyList<SessionActivityHistoryEntry> History)
{
    public bool IsTerminal => CurrentState == SessionActivityStateKind.Faulted;
}
```

#### SessionActivityTransition
```csharp
public sealed record SessionActivityTransition(
    SessionActivityStateKind FromState,
    SessionActivityStateKind ToState,
    string ReasonCode,
    string Reason,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, string> Metadata)
```

#### SessionActivityEvaluationContext
```csharp
public sealed record SessionActivityEvaluationContext(
    SessionId SessionId,
    SessionDomainState DomainState,
    DecisionPlan DecisionPlan,
    RiskAssessmentResult? RiskAssessment,
    SessionActivitySnapshot? PreviousSnapshot,
    DateTimeOffset EvaluatedAtUtc)
```

---

## 5. Admin API Endpoints

**Location:** `MultiSessionHost.AdminApi/AdminApiEndpointRouteBuilderExtensions.cs`

### Global Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/sessions` | GET | List all sessions with metrics |
| `/sessions/{id}` | GET | Get specific session info |
| `/sessions/{id}/ui` | GET | Get UI state (semantic) |
| `/sessions/{id}/ui/raw` | GET | Get raw UI capture |
| `/sessions/{id}/domain` | GET | Get domain state |
| `/domain` | GET | Get all domain states |
| `/decision-plans` | GET | Get all decision plans |
| `/decision-executions` | GET | Get all execution results |
| `/memory` | GET | Get all memory snapshots |
| `/persistence` | GET | Get persistence status |
| `/persistence/flush` | POST | Flush all sessions |
| `/bindings` | GET/PUT/DELETE | Session target bindings |
| `/health` | GET | Process health status |
| `/coordination` | GET | Execution coordination status |
| `/coordination/sessions/{id}` | GET | Session coordination status |
| `/policy-rules` | GET | All policy rules |
| `/policy-rules/site-selection` | GET | Site selection rules |
| `/policy-rules/threat-response` | GET | Threat response rules |
| `/policy-rules/target-prioritization` | GET | Target prioritization rules |
| `/policy-rules/resource-usage` | GET | Resource usage rules |
| `/policy-rules/transit` | GET | Transit rules |
| `/policy-rules/abort` | GET | Abort rules |

### Session-Specific Decision Plan Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/sessions/{id}/decision-plan` | GET | Latest decision plan |
| `/sessions/{id}/decision-plan/explanation` | GET | Detailed explanation with rule traces |
| `/sessions/{id}/decision-plan/summary` | GET | Summary (counts only) |
| `/sessions/{id}/decision-plan/history` | GET | Decision plan history entries |
| `/sessions/{id}/decision-plan/directives` | GET | Current plan directives |
| `/sessions/{id}/decision-plan/evaluate` | POST | Trigger evaluation |
| `/sessions/{id}/decision-plan/execute` | POST | Execute current plan |

### Session-Specific Execution Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/sessions/{id}/decision-execution` | GET | Current execution result |
| `/sessions/{id}/decision-execution/history` | GET | Execution history |

### Session-Specific Persistence Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/sessions/{id}/persistence` | GET | Persistence status for session |
| `/sessions/{id}/persistence/flush` | POST | Flush single session |

### Session-Specific Memory Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/sessions/{id}/memory` | GET | Memory snapshot |

### Response DTO Structures

All endpoints return DTOs mapped from internal models:

**DecisionPlanDto**
```
SessionId, PlannedAtUtc, PlanStatus
Directives: DecisionDirectiveDto[]
Reasons: DecisionReasonDto[]
Summary: PolicyExecutionSummaryDto
Warnings: string[]
```

**DecisionPlanExecutionDto**
```
SessionId, PlanFingerprint, ExecutedAtUtc, StartedAtUtc, CompletedAtUtc
ExecutionStatus (Succeeded/Failed/Deferred/Blocked/Aborted/NoOp)
WasAutoExecuted: bool
DirectiveResults: DecisionDirectiveExecutionResultDto[]
Summary: DecisionPlanExecutionSummaryDto
DeferredUntilUtc: DateTimeOffset?
FailureReason: string?
Warnings: string[]
Metadata: Dictionary<string, string>
```

**DecisionPlanExecutionSummaryDto**
```
TotalDirectives, SucceededCount, FailedCount, SkippedCount
DeferredCount, NotHandledCount, BlockedCount, AbortedCount
ExecutedDirectiveKinds, SkippedDirectiveKinds, UnhandledDirectiveKinds
```

**RuntimePersistenceSessionStatusDto**
```
SessionId, Rehydrated
LastLoadedAtUtc, LastSavedAtUtc, LastError, PersistedPath
ActivityHistoryCount, OperationalMemoryHistoryCount
DecisionPlanHistoryCount, DecisionExecutionHistoryCount
```

---

## Key Integration Points

### Policy Evaluation Flow
1. `DefaultPolicyEngine.EvaluateAsync()` → builds `PolicyEvaluationContext`
2. Executes policies in configured order (stops if abort and `BlockOnAbort=true`)
3. Each policy uses `PolicyRuleMatcher` against candidates
4. Results aggregated by `IDecisionPlanAggregator`
5. Decision plan stored in `ISessionDecisionPlanStore`
6. Optionally flushed to persistent storage

### Memory Update Flow
1. `DefaultSessionOperationalMemoryUpdater` receives context
2. Projects observations from domain state, risk assessment, execution results
3. Bounds observations by configured maximums
4. Marks observations stale if beyond `StaleAfterMinutes`
5. Updates store with new snapshot + observation records

### Persistence Flow
1. On startup: `RuntimePersistenceCoordinator.RehydrateAsync()` loads and restores
2. On state change: Auto-flush if `AutoFlushAfterStateChanges=true`
3. Manual flush via `/persistence/flush` or session-specific endpoint
4. Each flush builds envelope from all stores and delegates to backend

### Decision Execution Flow
1. Decision plan executed by `DefaultDecisionPlanExecutor`
2. Each directive executed by registered handlers
3. Results collected into `DecisionPlanExecutionResult`
4. Execution result persisted to `ISessionDecisionPlanExecutionStore`
5. Activity state updated based on execution outcome
