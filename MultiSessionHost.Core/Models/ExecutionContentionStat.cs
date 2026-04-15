using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record ExecutionContentionStat(
    ExecutionScope Scope,
    long ContentionHits);
