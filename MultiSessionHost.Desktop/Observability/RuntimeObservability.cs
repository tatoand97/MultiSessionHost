using System.Diagnostics;
using System.Diagnostics.Metrics;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Observability;

public static class RuntimeObservability
{
    public static readonly ActivitySource ActivitySource = new("MultiSessionHost.Runtime");
    public static readonly Meter Meter = new("MultiSessionHost.Runtime", "1.0.0");

    public static readonly Counter<long> SessionsStarted = Meter.CreateCounter<long>("sessions.started");
    public static readonly Counter<long> SessionsStopped = Meter.CreateCounter<long>("sessions.stopped");
    public static readonly Counter<long> SessionsPaused = Meter.CreateCounter<long>("sessions.paused");
    public static readonly Counter<long> SessionsResumed = Meter.CreateCounter<long>("sessions.resumed");
    public static readonly Counter<long> SessionsFaulted = Meter.CreateCounter<long>("sessions.faulted");
    public static readonly Counter<long> UiSnapshotsTotal = Meter.CreateCounter<long>("ui.snapshots.total");
    public static readonly Counter<long> SemanticExtractionsTotal = Meter.CreateCounter<long>("semantic.extractions.total");
    public static readonly Counter<long> PolicyEvaluationsTotal = Meter.CreateCounter<long>("policy.evaluations.total");
    public static readonly Counter<long> DecisionExecutionsTotal = Meter.CreateCounter<long>("decision.executions.total");
    public static readonly Counter<long> CommandExecutionsTotal = Meter.CreateCounter<long>("command.executions.total");
    public static readonly Counter<long> PersistenceFlushTotal = Meter.CreateCounter<long>("persistence.flush.total");
    public static readonly Counter<long> PersistenceRehydrateTotal = Meter.CreateCounter<long>("persistence.rehydrate.total");
    public static readonly Counter<long> AttachmentsAttachTotal = Meter.CreateCounter<long>("attachments.attach.total");
    public static readonly Counter<long> AttachmentsReattachTotal = Meter.CreateCounter<long>("attachments.reattach.total");
    public static readonly Counter<long> AttachmentsInvalidateTotal = Meter.CreateCounter<long>("attachments.invalidate.total");
    public static readonly Counter<long> AdapterErrorsTotal = Meter.CreateCounter<long>("adapter.errors.total");
    public static readonly Counter<long> DecisionsWithdrawTotal = Meter.CreateCounter<long>("decisions.withdraw.total");
    public static readonly Counter<long> DecisionsAbortTotal = Meter.CreateCounter<long>("decisions.abort.total");
    public static readonly Counter<long> DecisionsHideTotal = Meter.CreateCounter<long>("decisions.hide.total");
    public static readonly Counter<long> DecisionsWaitTotal = Meter.CreateCounter<long>("decisions.wait.total");

    public static readonly Histogram<double> UiSnapshotDuration = Meter.CreateHistogram<double>("ui.snapshot.duration.ms");
    public static readonly Histogram<double> SemanticExtractionDuration = Meter.CreateHistogram<double>("semantic.extraction.duration.ms");
    public static readonly Histogram<double> RiskClassificationDuration = Meter.CreateHistogram<double>("risk.classification.duration.ms");
    public static readonly Histogram<double> DomainProjectionDuration = Meter.CreateHistogram<double>("domain.projection.duration.ms");
    public static readonly Histogram<double> PolicyEvaluationDuration = Meter.CreateHistogram<double>("policy.evaluation.duration.ms");
    public static readonly Histogram<double> DecisionExecutionDuration = Meter.CreateHistogram<double>("decision.execution.duration.ms");
    public static readonly Histogram<double> CommandExecutionDuration = Meter.CreateHistogram<double>("command.execution.duration.ms");
    public static readonly Histogram<double> PersistenceFlushDuration = Meter.CreateHistogram<double>("persistence.flush.duration.ms");
    public static readonly Histogram<double> PersistenceRehydrateDuration = Meter.CreateHistogram<double>("persistence.rehydrate.duration.ms");
    public static readonly Histogram<double> AttachmentResolveDuration = Meter.CreateHistogram<double>("attachment.resolve.duration.ms");
    public static readonly Histogram<double> AttachmentRefreshDuration = Meter.CreateHistogram<double>("attachment.refresh.duration.ms");
}