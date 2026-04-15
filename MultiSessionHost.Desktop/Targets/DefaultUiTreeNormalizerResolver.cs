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

    public DefaultUiTreeNormalizerResolver(
        SelfHostedHttpUiTreeNormalizer selfHostedHttpUiTreeNormalizer,
        TestAppUiTreeNormalizer testAppUiTreeNormalizer)
    {
        _selfHostedHttpUiTreeNormalizer = selfHostedHttpUiTreeNormalizer;
        _testAppUiTreeNormalizer = testAppUiTreeNormalizer;
    }

    public IUiTreeNormalizer Resolve(ResolvedDesktopTargetContext context) =>
        context.Profile.Kind switch
        {
            DesktopTargetKind.SelfHostedHttpDesktop => _selfHostedHttpUiTreeNormalizer,
            DesktopTargetKind.DesktopTestApp => _testAppUiTreeNormalizer,
            _ => throw new InvalidOperationException($"Desktop target kind '{context.Profile.Kind}' is not supported.")
        };
}
