using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.UiModel.Interfaces;

namespace MultiSessionHost.Desktop.Targets;

public sealed class DefaultUiTreeNormalizerResolver : IUiTreeNormalizerResolver
{
    private readonly SelfHostedHttpUiTreeNormalizer _selfHostedHttpUiTreeNormalizer;
    private readonly TestAppUiTreeNormalizer _testAppUiTreeNormalizer;
    private readonly WindowsUiAutomationUiTreeNormalizer _windowsUiAutomationUiTreeNormalizer;

    public DefaultUiTreeNormalizerResolver(
        SelfHostedHttpUiTreeNormalizer selfHostedHttpUiTreeNormalizer,
        TestAppUiTreeNormalizer testAppUiTreeNormalizer,
        WindowsUiAutomationUiTreeNormalizer windowsUiAutomationUiTreeNormalizer)
    {
        _selfHostedHttpUiTreeNormalizer = selfHostedHttpUiTreeNormalizer;
        _testAppUiTreeNormalizer = testAppUiTreeNormalizer;
        _windowsUiAutomationUiTreeNormalizer = windowsUiAutomationUiTreeNormalizer;
    }

    public IUiTreeNormalizer Resolve(ResolvedDesktopTargetContext context) =>
        context.Profile.Kind switch
        {
            DesktopTargetKind.SelfHostedHttpDesktop => _selfHostedHttpUiTreeNormalizer,
            DesktopTargetKind.DesktopTestApp => _testAppUiTreeNormalizer,
            DesktopTargetKind.WindowsUiAutomationDesktop => _windowsUiAutomationUiTreeNormalizer,
            _ => throw new InvalidOperationException($"Desktop target kind '{context.Profile.Kind}' is not supported.")
        };
}
