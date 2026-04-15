using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Attachments;

public sealed class DefaultSessionAttachmentResolver : ISessionAttachmentResolver
{
    private readonly IDesktopTargetProfileResolver _targetProfileResolver;
    private readonly IProcessLocator _processLocator;
    private readonly IWindowLocator _windowLocator;
    private readonly IDesktopTargetMatcher _targetMatcher;
    private readonly IClock _clock;

    public DefaultSessionAttachmentResolver(
        IDesktopTargetProfileResolver targetProfileResolver,
        IProcessLocator processLocator,
        IWindowLocator windowLocator,
        IDesktopTargetMatcher targetMatcher,
        IClock clock)
    {
        _targetProfileResolver = targetProfileResolver;
        _processLocator = processLocator;
        _windowLocator = windowLocator;
        _targetMatcher = targetMatcher;
        _clock = clock;
    }

    public ValueTask<DesktopSessionAttachment> ResolveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var context = _targetProfileResolver.Resolve(snapshot);
        var target = context.Target;
        var processes = _processLocator.GetProcesses(target.ProcessName);

        if (processes.Count == 0)
        {
            processes = _processLocator.GetProcesses();
        }

        var windows = _windowLocator.GetWindows();
        var (selectedProcess, selectedWindow) = _targetMatcher.Match(processes.ToArray(), windows.ToArray(), target);

        return ValueTask.FromResult(
            new DesktopSessionAttachment(
                snapshot.SessionId,
                target,
                selectedProcess,
                selectedWindow,
                target.BaseAddress,
                _clock.UtcNow));
    }
}
