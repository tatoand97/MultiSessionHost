using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Interfaces;

public interface IExecutionCoordinator
{
    Task<IExecutionLease> AcquireAsync(ExecutionRequest request, CancellationToken cancellationToken);

    Task<ExecutionCoordinationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
