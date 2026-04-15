using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Interfaces;

public interface IExecutionLease : IAsyncDisposable
{
    ExecutionLeaseMetadata Metadata { get; }
}
