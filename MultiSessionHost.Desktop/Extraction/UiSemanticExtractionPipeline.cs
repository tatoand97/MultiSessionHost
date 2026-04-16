using MultiSessionHost.Desktop.Observability;

namespace MultiSessionHost.Desktop.Extraction;

public sealed class UiSemanticExtractionPipeline : IUiSemanticExtractionPipeline
{
    private readonly IReadOnlyList<IUiSemanticExtractor> _extractors;
    private readonly ITargetSemanticPackageResolver _packageResolver;
    private readonly IObservabilityRecorder _observabilityRecorder;

    public UiSemanticExtractionPipeline(
        IEnumerable<IUiSemanticExtractor> extractors,
        ITargetSemanticPackageResolver packageResolver,
        IObservabilityRecorder observabilityRecorder)
    {
        _extractors = extractors.ToArray();
        _packageResolver = packageResolver;
        _observabilityRecorder = observabilityRecorder;
    }

    public async ValueTask<UiSemanticExtractionResult> ExtractAsync(UiSemanticExtractionContext context, CancellationToken cancellationToken)
    {
        var lists = new List<DetectedList>();
        var targets = new List<DetectedTarget>();
        var alerts = new List<DetectedAlert>();
        var transitStates = new List<DetectedTransitState>();
        var resources = new List<DetectedResource>();
        var capabilities = new List<DetectedCapability>();
        var presenceEntities = new List<DetectedPresenceEntity>();
        var warnings = new List<string>();
        var packages = new List<TargetSemanticPackageResult>();

        foreach (var extractor in _extractors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var contribution = await extractor.ExtractAsync(context, cancellationToken).ConfigureAwait(false);
            lists.AddRange(contribution.Lists);
            targets.AddRange(contribution.Targets);
            alerts.AddRange(contribution.Alerts);
            transitStates.AddRange(contribution.TransitStates);
            resources.AddRange(contribution.Resources);
            capabilities.AddRange(contribution.Capabilities);
            presenceEntities.AddRange(contribution.PresenceEntities);
            warnings.AddRange(contribution.Warnings);
        }

        var genericResult = new UiSemanticExtractionResult(
            context.SessionId,
            context.Now,
            lists.DistinctBy(static item => item.NodeId).ToArray(),
            targets.DistinctBy(static item => item.NodeId).ToArray(),
            alerts.DistinctBy(static item => item.NodeId).ToArray(),
            transitStates.DistinctBy(static item => string.Join('|', item.NodeIds)).ToArray(),
            resources.DistinctBy(static item => item.NodeId).ToArray(),
            capabilities.DistinctBy(static item => item.NodeId).ToArray(),
            presenceEntities.DistinctBy(static item => item.NodeId).ToArray(),
            [],
            warnings.Distinct(StringComparer.Ordinal).ToArray(),
            new Dictionary<string, DetectionConfidence>(StringComparer.Ordinal)
            {
                ["lists"] = SummarizeConfidence(lists.Select(static item => item.Confidence)),
                ["targets"] = SummarizeConfidence(targets.Select(static item => item.Confidence)),
                ["alerts"] = SummarizeConfidence(alerts.Select(static item => item.Confidence)),
                ["transitStates"] = SummarizeConfidence(transitStates.Select(static item => item.Confidence)),
                ["resources"] = SummarizeConfidence(resources.Select(static item => item.Confidence)),
                ["capabilities"] = SummarizeConfidence(capabilities.Select(static item => item.Confidence)),
                ["presenceEntities"] = SummarizeConfidence(presenceEntities.Select(static item => item.Confidence))
            });

        var selection = _packageResolver.ResolveSelection(context.TargetContext);
        if (selection is not null)
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["packageName"] = selection.PackageName,
                ["metadataKey"] = selection.MetadataKey
            };

            await _observabilityRecorder.RecordActivityAsync(
                context.SessionId,
                "semantic.package.selected",
                SessionObservabilityOutcome.Success.ToString(),
                TimeSpan.Zero,
                null,
                null,
                nameof(UiSemanticExtractionPipeline),
                metadata,
                cancellationToken).ConfigureAwait(false);

            var package = _packageResolver.ResolvePackage(selection.PackageName);
            if (package is null)
            {
                warnings.Add($"Semantic package '{selection.PackageName}' was configured but not registered.");
                await _observabilityRecorder.RecordActivityAsync(
                    context.SessionId,
                    "semantic.package.failed",
                    SessionObservabilityOutcome.Failure.ToString(),
                    TimeSpan.Zero,
                    "semantic.package.missing",
                    $"Semantic package '{selection.PackageName}' was configured but not registered.",
                    nameof(UiSemanticExtractionPipeline),
                    metadata,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var packageMetadata = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
                {
                    ["packageVersion"] = package.PackageVersion
                };

                await _observabilityRecorder.RecordActivityAsync(
                    context.SessionId,
                    "semantic.package.started",
                    SessionObservabilityOutcome.Success.ToString(),
                    TimeSpan.Zero,
                    null,
                    null,
                    nameof(UiSemanticExtractionPipeline),
                    packageMetadata,
                    cancellationToken).ConfigureAwait(false);

                var packageStartedAt = DateTimeOffset.UtcNow;
                try
                {
                    var packageResult = await package.ExtractAsync(new TargetSemanticPackageContext(context, genericResult), cancellationToken).ConfigureAwait(false);
                    packages.Add(packageResult);
                    warnings.AddRange(packageResult.Warnings);

                    if (_observabilityRecorder is not null)
                    {
                        await _observabilityRecorder.RecordActivityAsync(
                            context.SessionId,
                            "semantic.package.succeeded",
                            SessionObservabilityOutcome.Success.ToString(),
                            DateTimeOffset.UtcNow - packageStartedAt,
                            null,
                            null,
                            nameof(UiSemanticExtractionPipeline),
                            packageMetadata,
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (packageResult.Succeeded)
                    {
                        RuntimeObservability.SemanticPackageTotal.Add(1, new KeyValuePair<string, object?>("session.id", context.SessionId.Value));
                        RuntimeObservability.SemanticPackageDuration.Record((DateTimeOffset.UtcNow - packageStartedAt).TotalMilliseconds, new KeyValuePair<string, object?>("session.id", context.SessionId.Value));
                        RuntimeObservability.SemanticPackagePresenceCount.Add(packageResult.EveLike?.Presence.VisibleEntityCount ?? 0, new KeyValuePair<string, object?>("session.id", context.SessionId.Value));
                        RuntimeObservability.SemanticPackageOverviewCount.Add(packageResult.EveLike?.OverviewEntries.Count ?? 0, new KeyValuePair<string, object?>("session.id", context.SessionId.Value));
                        RuntimeObservability.SemanticPackageProbeCount.Add(packageResult.EveLike?.ProbeScannerEntries.Count ?? 0, new KeyValuePair<string, object?>("session.id", context.SessionId.Value));
                    }
                }
                catch (Exception exception)
                {
                    warnings.Add($"Semantic package '{selection.PackageName}' failed: {exception.Message}");
                    RuntimeObservability.SemanticPackageFailureTotal.Add(1, new KeyValuePair<string, object?>("session.id", context.SessionId.Value));
                    await _observabilityRecorder.RecordActivityAsync(
                        context.SessionId,
                        "semantic.package.failed",
                        SessionObservabilityOutcome.Failure.ToString(),
                        DateTimeOffset.UtcNow - packageStartedAt,
                        "semantic.package.failed",
                        exception.Message,
                        nameof(UiSemanticExtractionPipeline),
                        packageMetadata,
                        cancellationToken).ConfigureAwait(false);

                    packages.Add(new TargetSemanticPackageResult(
                        package.PackageName,
                        package.PackageVersion,
                        false,
                        DetectionConfidence.Unknown,
                        [exception.Message],
                        new Dictionary<string, DetectionConfidence>(StringComparer.Ordinal),
                        exception.Message,
                        EveLike: null));
                }
            }
        }

        return new UiSemanticExtractionResult(
            context.SessionId,
            context.Now,
            lists.DistinctBy(static item => item.NodeId).ToArray(),
            targets.DistinctBy(static item => item.NodeId).ToArray(),
            alerts.DistinctBy(static item => item.NodeId).ToArray(),
            transitStates.DistinctBy(static item => string.Join('|', item.NodeIds)).ToArray(),
            resources.DistinctBy(static item => item.NodeId).ToArray(),
            capabilities.DistinctBy(static item => item.NodeId).ToArray(),
            presenceEntities.DistinctBy(static item => item.NodeId).ToArray(),
            packages,
            warnings.Distinct(StringComparer.Ordinal).ToArray(),
            genericResult.ConfidenceSummary);
    }

    private static DetectionConfidence SummarizeConfidence(IEnumerable<DetectionConfidence> values)
    {
        var materialized = values.ToArray();

        if (materialized.Length == 0)
        {
            return DetectionConfidence.Unknown;
        }

        return materialized.Max();
    }
}
