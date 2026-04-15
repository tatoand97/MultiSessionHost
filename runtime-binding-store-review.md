# Runtime-Editable Binding Store Review

## Refactor Plan
1. Add a runtime binding store, persistence abstraction, and startup bootstrap overlay.
2. Keep profiles config-backed while moving binding resolution to the runtime store.
3. Add binding mutation management with validation, persistence, and stale-attachment invalidation.
4. Expose CRUD binding endpoints in the in-process Admin API.
5. Add unit, integration, and live rebinding tests, then document precedence and mutation behavior.

## File Tree
### New Files
- `MultiSessionHost.Contracts/Sessions/BindingStoreSnapshotDto.cs`
- `MultiSessionHost.Contracts/Sessions/SessionTargetBindingUpsertRequest.cs`
- `MultiSessionHost.Core/Enums/BindingStorePersistenceMode.cs`
- `MultiSessionHost.Desktop/Attachments/DefaultSessionAttachmentRuntime.cs`
- `MultiSessionHost.Desktop/Bindings/BindingStoreSnapshot.cs`
- `MultiSessionHost.Desktop/Bindings/DesktopTargetProfileResolution.cs`
- `MultiSessionHost.Desktop/Bindings/InMemorySessionTargetBindingStore.cs`
- `MultiSessionHost.Desktop/Bindings/ISessionTargetBindingBootstrapper.cs`
- `MultiSessionHost.Desktop/Bindings/ISessionTargetBindingManager.cs`
- `MultiSessionHost.Desktop/Bindings/ISessionTargetBindingPersistence.cs`
- `MultiSessionHost.Desktop/Bindings/ISessionTargetBindingStore.cs`
- `MultiSessionHost.Desktop/Bindings/JsonFileSessionTargetBindingPersistence.cs`
- `MultiSessionHost.Desktop/Bindings/NoOpSessionTargetBindingPersistence.cs`
- `MultiSessionHost.Desktop/Bindings/SessionTargetBindingManager.cs`
- `MultiSessionHost.Desktop/Bindings/SessionTargetBindingModelMapper.cs`
- `MultiSessionHost.Desktop/Bindings/SessionTargetBindingStoreBootstrapper.cs`
- `MultiSessionHost.Desktop/Bindings/SessionTargetBindingValidation.cs`
- `MultiSessionHost.Desktop/Interfaces/IDesktopTargetProfileCatalog.cs`
- `MultiSessionHost.Desktop/Interfaces/ISessionAttachmentRuntime.cs`
- `MultiSessionHost.Desktop/Targets/ConfiguredDesktopTargetProfileCatalog.cs`
- `MultiSessionHost.Tests/Desktop/SessionTargetBindingManagerTests.cs`
- `MultiSessionHost.Tests/Desktop/SessionTargetBindingStoreTests.cs`
- `MultiSessionHost.Tests/Hosting/WorkerBindingAdminApiIntegrationTests.cs`
### Modified Files
- `MultiSessionHost.AdminApi/AdminApiEndpointRouteBuilderExtensions.cs`
- `MultiSessionHost.AdminApi/Mapping/DtoMappingExtensions.cs`
- `MultiSessionHost.Core/Configuration/SessionHostOptions.cs`
- `MultiSessionHost.Core/Configuration/SessionHostOptionsExtensions.cs`
- `MultiSessionHost.Desktop/Commands/UiCommandExecutor.cs`
- `MultiSessionHost.Desktop/DependencyInjection/DesktopServiceCollectionExtensions.cs`
- `MultiSessionHost.Desktop/Drivers/DesktopTargetSessionDriver.cs`
- `MultiSessionHost.Desktop/MultiSessionHost.Desktop.csproj`
- `MultiSessionHost.Desktop/Targets/ConfiguredDesktopTargetProfileResolver.cs`
- `MultiSessionHost.Tests/Configuration/SessionHostOptionsValidationTests.cs`
- `MultiSessionHost.Tests/Desktop/DesktopTargetAdapterSystemTests.cs`
- `MultiSessionHost.Tests/Desktop/DesktopTargetProfileResolverTests.cs`
- `MultiSessionHost.Tests/Desktop/SessionAttachmentResolverTests.cs`
- `MultiSessionHost.Worker/WorkerHostService.cs`
- `MultiSessionHost.Worker/appsettings.Development.json`
- `MultiSessionHost.Worker/appsettings.json`
- `README.md`

## New Files
### `MultiSessionHost.Contracts/Sessions/BindingStoreSnapshotDto.cs`
`$ext
namespace MultiSessionHost.Contracts.Sessions;

public sealed record BindingStoreSnapshotDto(
    long Version,
    DateTimeOffset LastUpdatedAtUtc,
    IReadOnlyCollection<SessionTargetBindingDto> Bindings);

```

### `MultiSessionHost.Contracts/Sessions/SessionTargetBindingUpsertRequest.cs`
`$ext
namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionTargetBindingUpsertRequest(
    string TargetProfileName,
    IReadOnlyDictionary<string, string> Variables,
    DesktopTargetProfileOverrideDto? Overrides);

```

### `MultiSessionHost.Core/Enums/BindingStorePersistenceMode.cs`
`$ext
namespace MultiSessionHost.Core.Enums;

public enum BindingStorePersistenceMode
{
    None = 0,
    JsonFile = 1
}

```

### `MultiSessionHost.Desktop/Attachments/DefaultSessionAttachmentRuntime.cs`
`$ext
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Attachments;

public sealed class DefaultSessionAttachmentRuntime : ISessionAttachmentRuntime
{
    private readonly ISessionRegistry _sessionRegistry;
    private readonly ISessionStateStore _sessionStateStore;
    private readonly ISessionAttachmentResolver _attachmentResolver;
    private readonly IAttachedSessionStore _attachedSessionStore;
    private readonly IDesktopTargetProfileResolver _targetProfileResolver;
    private readonly IDesktopTargetAdapterRegistry _adapterRegistry;

    public DefaultSessionAttachmentRuntime(
        ISessionRegistry sessionRegistry,
        ISessionStateStore sessionStateStore,
        ISessionAttachmentResolver attachmentResolver,
        IAttachedSessionStore attachedSessionStore,
        IDesktopTargetProfileResolver targetProfileResolver,
        IDesktopTargetAdapterRegistry adapterRegistry)
    {
        _sessionRegistry = sessionRegistry;
        _sessionStateStore = sessionStateStore;
        _attachmentResolver = attachmentResolver;
        _attachedSessionStore = attachedSessionStore;
        _targetProfileResolver = targetProfileResolver;
        _adapterRegistry = adapterRegistry;
    }

    public Task<DesktopSessionAttachment?> GetAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        _attachedSessionStore.GetAsync(sessionId, cancellationToken).AsTask();

    public async Task<DesktopSessionAttachment> EnsureAttachedAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var context = _targetProfileResolver.Resolve(snapshot);
        var adapter = _adapterRegistry.Resolve(context.Profile.Kind);
        var current = await _attachedSessionStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);

        if (current is not null && AreEquivalent(current.Target, context.Target))
        {
            await adapter.ValidateAttachmentAsync(snapshot, context, current, cancellationToken).ConfigureAwait(false);
            return current;
        }

        if (current is not null)
        {
            await adapter.DetachAsync(snapshot, context, current, cancellationToken).ConfigureAwait(false);
            await _attachedSessionStore.RemoveAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
        }

        var attachment = await _attachmentResolver.ResolveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await adapter.ValidateAttachmentAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
        await adapter.AttachAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
        await _attachedSessionStore.SetAsync(attachment, cancellationToken).ConfigureAwait(false);

        return attachment;
    }

    public async Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var current = await _attachedSessionStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (current is null)
        {
            return false;
        }

        var definition = _sessionRegistry.GetById(sessionId);
        var state = await _sessionStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var snapshot = definition is not null && state is not null
            ? new SessionSnapshot(definition, state, PendingWorkItems: 0)
            : null;

        if (snapshot is not null)
        {
            ResolvedDesktopTargetContext context;

            try
            {
                context = _targetProfileResolver.Resolve(snapshot);
            }
            catch (InvalidOperationException)
            {
                context = CreateFallbackContext(snapshot, current);
            }

            var adapter = _adapterRegistry.Resolve(current.Target.Kind);
            await adapter.DetachAsync(snapshot, context, current, cancellationToken).ConfigureAwait(false);
        }

        await _attachedSessionStore.RemoveAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static ResolvedDesktopTargetContext CreateFallbackContext(SessionSnapshot snapshot, DesktopSessionAttachment attachment)
    {
        var profile = new DesktopTargetProfile(
            attachment.Target.ProfileName,
            attachment.Target.Kind,
            attachment.Target.ProcessName,
            attachment.Target.WindowTitleFragment,
            attachment.Target.CommandLineFragment,
            attachment.Target.BaseAddress?.ToString(),
            attachment.Target.MatchingMode,
            attachment.Target.Metadata,
            SupportsUiSnapshots: true,
            SupportsStateEndpoint: attachment.BaseAddress is not null);
        var binding = new SessionTargetBinding(snapshot.SessionId, attachment.Target.ProfileName, new Dictionary<string, string>(), Overrides: null);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SessionId"] = snapshot.SessionId.Value
        };

        return new ResolvedDesktopTargetContext(snapshot.SessionId, profile, binding, attachment.Target, variables);
    }

    private static bool AreEquivalent(DesktopSessionTarget left, DesktopSessionTarget right) =>
        left.SessionId == right.SessionId &&
        left.ProfileName == right.ProfileName &&
        left.Kind == right.Kind &&
        left.MatchingMode == right.MatchingMode &&
        string.Equals(left.ProcessName, right.ProcessName, StringComparison.Ordinal) &&
        string.Equals(left.WindowTitleFragment, right.WindowTitleFragment, StringComparison.Ordinal) &&
        string.Equals(left.CommandLineFragment, right.CommandLineFragment, StringComparison.Ordinal) &&
        Equals(left.BaseAddress, right.BaseAddress) &&
        HaveSameMetadata(left.Metadata, right.Metadata);

    private static bool HaveSameMetadata(
        IReadOnlyDictionary<string, string?> left,
        IReadOnlyDictionary<string, string?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var otherValue) || !string.Equals(value, otherValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

```

### `MultiSessionHost.Desktop/Bindings/BindingStoreSnapshot.cs`
`$ext
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

public sealed record BindingStoreSnapshot(
    long Version,
    DateTimeOffset LastUpdatedAtUtc,
    IReadOnlyCollection<SessionTargetBinding> Bindings);

```

### `MultiSessionHost.Desktop/Bindings/DesktopTargetProfileResolution.cs`
`$ext
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

internal static class DesktopTargetProfileResolution
{
    public static DesktopTargetProfile ApplyOverrides(DesktopTargetProfile profile, DesktopTargetProfileOverride? overrides)
    {
        if (overrides is null)
        {
            return profile;
        }

        var metadata = profile.Metadata
            .Concat(overrides.Metadata)
            .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Last().Value, StringComparer.OrdinalIgnoreCase);

        return profile with
        {
            ProcessName = overrides.ProcessName ?? profile.ProcessName,
            WindowTitleFragment = overrides.WindowTitleFragment ?? profile.WindowTitleFragment,
            CommandLineFragmentTemplate = overrides.CommandLineFragmentTemplate ?? profile.CommandLineFragmentTemplate,
            BaseAddressTemplate = overrides.BaseAddressTemplate ?? profile.BaseAddressTemplate,
            MatchingMode = overrides.MatchingMode ?? profile.MatchingMode,
            Metadata = metadata,
            SupportsUiSnapshots = overrides.SupportsUiSnapshots ?? profile.SupportsUiSnapshots,
            SupportsStateEndpoint = overrides.SupportsStateEndpoint ?? profile.SupportsStateEndpoint
        };
    }

    public static IReadOnlyDictionary<string, string> BuildVariables(
        SessionId sessionId,
        IReadOnlyDictionary<string, string> bindingVariables)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SessionId"] = sessionId.Value
        };

        foreach (var (key, value) in bindingVariables)
        {
            variables[key] = value;
        }

        return variables;
    }

    public static string RenderRequired(string template, IReadOnlyDictionary<string, string> variables, string fieldName)
    {
        var rendered = SessionHostTemplateRenderer.Render(template, variables).Trim();
        return rendered.Length == 0
            ? throw new InvalidOperationException($"The rendered {fieldName} is empty.")
            : rendered;
    }

    public static string? RenderOptional(string? template, IReadOnlyDictionary<string, string> variables) =>
        string.IsNullOrWhiteSpace(template)
            ? null
            : SessionHostTemplateRenderer.Render(template, variables).Trim();

    public static Uri? RenderUri(string? template, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        var rendered = SessionHostTemplateRenderer.Render(template, variables);
        return Uri.TryCreate(rendered, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException($"The rendered BaseAddressTemplate '{rendered}' is not a valid absolute URI.");
    }

    public static IReadOnlyDictionary<string, string?> RenderMetadata(
        IReadOnlyDictionary<string, string?> metadata,
        IReadOnlyDictionary<string, string> variables) =>
        metadata.ToDictionary(
            static pair => pair.Key,
            pair => string.IsNullOrWhiteSpace(pair.Value) ? pair.Value : SessionHostTemplateRenderer.Render(pair.Value, variables),
            StringComparer.OrdinalIgnoreCase);
}

```

### `MultiSessionHost.Desktop/Bindings/InMemorySessionTargetBindingStore.cs`
`$ext
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

public sealed class InMemorySessionTargetBindingStore : ISessionTargetBindingStore
{
    private readonly object _gate = new();
    private readonly IClock _clock;
    private Dictionary<SessionId, SessionTargetBinding> _bindingsBySessionId;
    private long _version;
    private DateTimeOffset _lastUpdatedAtUtc;

    public InMemorySessionTargetBindingStore(SessionHostOptions options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);

        _clock = clock;
        _bindingsBySessionId = options.SessionTargetBindings
            .Select(SessionTargetBindingModelMapper.MapBinding)
            .ToDictionary(static binding => binding.SessionId);
        _lastUpdatedAtUtc = _clock.UtcNow;
    }

    public Task<IReadOnlyCollection<SessionTargetBinding>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyCollection<SessionTargetBinding>>(
                _bindingsBySessionId.Values
                    .OrderBy(static binding => binding.SessionId.Value, StringComparer.OrdinalIgnoreCase)
                    .Select(SessionTargetBindingModelMapper.NormalizeBinding)
                    .ToArray());
        }
    }

    public Task<SessionTargetBinding?> GetAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(
                _bindingsBySessionId.TryGetValue(sessionId, out var binding)
                    ? SessionTargetBindingModelMapper.NormalizeBinding(binding)
                    : null);
        }
    }

    public Task<SessionTargetBinding> UpsertAsync(SessionTargetBinding binding, CancellationToken cancellationToken)
    {
        var normalized = SessionTargetBindingModelMapper.NormalizeBinding(binding);

        lock (_gate)
        {
            _bindingsBySessionId[normalized.SessionId] = normalized;
            _version++;
            _lastUpdatedAtUtc = _clock.UtcNow;
        }

        return Task.FromResult(SessionTargetBindingModelMapper.NormalizeBinding(normalized));
    }

    public Task<bool> DeleteAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var removed = _bindingsBySessionId.Remove(sessionId);

            if (removed)
            {
                _version++;
                _lastUpdatedAtUtc = _clock.UtcNow;
            }

            return Task.FromResult(removed);
        }
    }

    public Task<BindingStoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(
                new BindingStoreSnapshot(
                    _version,
                    _lastUpdatedAtUtc,
                    _bindingsBySessionId.Values
                        .OrderBy(static binding => binding.SessionId.Value, StringComparer.OrdinalIgnoreCase)
                        .Select(SessionTargetBindingModelMapper.NormalizeBinding)
                        .ToArray()));
        }
    }
}

```

### `MultiSessionHost.Desktop/Bindings/ISessionTargetBindingBootstrapper.cs`
`$ext
namespace MultiSessionHost.Desktop.Bindings;

public interface ISessionTargetBindingBootstrapper
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

```

### `MultiSessionHost.Desktop/Bindings/ISessionTargetBindingManager.cs`
`$ext
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

public interface ISessionTargetBindingManager
{
    Task<BindingStoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task<SessionTargetBinding?> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task<SessionTargetBinding> UpsertAsync(SessionTargetBinding binding, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(SessionId sessionId, CancellationToken cancellationToken);
}

```

### `MultiSessionHost.Desktop/Bindings/ISessionTargetBindingPersistence.cs`
`$ext
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

public interface ISessionTargetBindingPersistence
{
    Task<IReadOnlyCollection<SessionTargetBinding>> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(IReadOnlyCollection<SessionTargetBinding> bindings, CancellationToken cancellationToken);
}

```

### `MultiSessionHost.Desktop/Bindings/ISessionTargetBindingStore.cs`
`$ext
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

public interface ISessionTargetBindingStore
{
    Task<IReadOnlyCollection<SessionTargetBinding>> GetAllAsync(CancellationToken cancellationToken);

    Task<SessionTargetBinding?> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task<SessionTargetBinding> UpsertAsync(SessionTargetBinding binding, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task<BindingStoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}

```

### `MultiSessionHost.Desktop/Bindings/JsonFileSessionTargetBindingPersistence.cs`
`$ext
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

public sealed class JsonFileSessionTargetBindingPersistence : ISessionTargetBindingPersistence
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;

    public JsonFileSessionTargetBindingPersistence(SessionHostOptions options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        var configuredPath = options.BindingStoreFilePath
            ?? throw new InvalidOperationException("BindingStoreFilePath must be configured when BindingStorePersistenceMode=JsonFile.");

        _filePath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));
    }

    public async Task<IReadOnlyCollection<SessionTargetBinding>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var bindings = await JsonSerializer.DeserializeAsync<PersistedBindingRecord[]>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
                ?? [];

            return bindings
                .Select(ToModel)
                .OrderBy(static binding => binding.SessionId.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"The binding store file '{_filePath}' is malformed.", exception);
        }
    }

    public async Task SaveAsync(IReadOnlyCollection<SessionTargetBinding> bindings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempFilePath = Path.Combine(directory ?? string.Empty, $"{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        var payload = bindings
            .OrderBy(static binding => binding.SessionId.Value, StringComparer.OrdinalIgnoreCase)
            .Select(ToRecord)
            .ToArray();

        try
        {
            await using (var stream = File.Create(tempFilePath))
            {
                await JsonSerializer.SerializeAsync(stream, payload, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(_filePath))
            {
                File.Replace(tempFilePath, _filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempFilePath, _filePath);
            }
        }
        catch
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }

            throw;
        }
    }

    private static PersistedBindingRecord ToRecord(SessionTargetBinding binding) =>
        new(
            binding.SessionId.Value,
            binding.TargetProfileName,
            binding.Variables
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            binding.Overrides is null
                ? null
                : new PersistedOverrideRecord(
                    binding.Overrides.ProcessName,
                    binding.Overrides.WindowTitleFragment,
                    binding.Overrides.CommandLineFragmentTemplate,
                    binding.Overrides.BaseAddressTemplate,
                    binding.Overrides.MatchingMode?.ToString(),
                    binding.Overrides.Metadata
                        .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                    binding.Overrides.SupportsUiSnapshots,
                    binding.Overrides.SupportsStateEndpoint));

    private static SessionTargetBinding ToModel(PersistedBindingRecord binding) =>
        SessionTargetBindingModelMapper.NormalizeBinding(
            new SessionTargetBinding(
                new SessionId(binding.SessionId),
                binding.TargetProfileName,
                binding.Variables.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                binding.Overrides is null
                    ? null
                    : new DesktopTargetProfileOverride(
                        binding.Overrides.ProcessName,
                        binding.Overrides.WindowTitleFragment,
                        binding.Overrides.CommandLineFragmentTemplate,
                        binding.Overrides.BaseAddressTemplate,
                        ParseMatchingMode(binding.Overrides.MatchingMode),
                        binding.Overrides.Metadata.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                        binding.Overrides.SupportsUiSnapshots,
                        binding.Overrides.SupportsStateEndpoint)));

    private static DesktopSessionMatchingMode? ParseMatchingMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse<DesktopSessionMatchingMode>(value, ignoreCase: true, out var matchingMode)
            ? matchingMode
            : throw new InvalidOperationException($"DesktopSessionMatchingMode '{value}' is not valid in the persisted binding store.");
    }

    private sealed record PersistedBindingRecord(
        string SessionId,
        string TargetProfileName,
        IReadOnlyDictionary<string, string> Variables,
        PersistedOverrideRecord? Overrides);

    private sealed record PersistedOverrideRecord(
        string? ProcessName,
        string? WindowTitleFragment,
        string? CommandLineFragmentTemplate,
        string? BaseAddressTemplate,
        string? MatchingMode,
        IReadOnlyDictionary<string, string?> Metadata,
        bool? SupportsUiSnapshots,
        bool? SupportsStateEndpoint);
}

```

### `MultiSessionHost.Desktop/Bindings/NoOpSessionTargetBindingPersistence.cs`
`$ext
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

public sealed class NoOpSessionTargetBindingPersistence : ISessionTargetBindingPersistence
{
    public Task<IReadOnlyCollection<SessionTargetBinding>> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<SessionTargetBinding>>([]);

    public Task SaveAsync(IReadOnlyCollection<SessionTargetBinding> bindings, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

```

### `MultiSessionHost.Desktop/Bindings/SessionTargetBindingManager.cs`
`$ext
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

public sealed class SessionTargetBindingManager : ISessionTargetBindingManager
{
    private readonly ISessionTargetBindingStore _bindingStore;
    private readonly ISessionTargetBindingPersistence _persistence;
    private readonly ISessionRegistry _sessionRegistry;
    private readonly IDesktopTargetProfileCatalog _profileCatalog;
    private readonly ISessionAttachmentRuntime _sessionAttachmentRuntime;

    public SessionTargetBindingManager(
        ISessionTargetBindingStore bindingStore,
        ISessionTargetBindingPersistence persistence,
        ISessionRegistry sessionRegistry,
        IDesktopTargetProfileCatalog profileCatalog,
        ISessionAttachmentRuntime sessionAttachmentRuntime)
    {
        _bindingStore = bindingStore;
        _persistence = persistence;
        _sessionRegistry = sessionRegistry;
        _profileCatalog = profileCatalog;
        _sessionAttachmentRuntime = sessionAttachmentRuntime;
    }

    public Task<BindingStoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
        _bindingStore.GetSnapshotAsync(cancellationToken);

    public Task<SessionTargetBinding?> GetAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        _bindingStore.GetAsync(sessionId, cancellationToken);

    public async Task<SessionTargetBinding> UpsertAsync(SessionTargetBinding binding, CancellationToken cancellationToken)
    {
        var normalized = SessionTargetBindingModelMapper.NormalizeBinding(binding);
        ValidateBinding(normalized);

        var previous = await _bindingStore.GetAsync(normalized.SessionId, cancellationToken).ConfigureAwait(false);
        var upserted = await _bindingStore.UpsertAsync(normalized, cancellationToken).ConfigureAwait(false);

        try
        {
            await PersistSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RestorePreviousBindingAsync(previous, normalized.SessionId, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await _sessionAttachmentRuntime.InvalidateAsync(normalized.SessionId, cancellationToken).ConfigureAwait(false);
        return upserted;
    }

    public async Task<bool> DeleteAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var previous = await _bindingStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (previous is null)
        {
            return false;
        }

        await _bindingStore.DeleteAsync(sessionId, cancellationToken).ConfigureAwait(false);

        try
        {
            await PersistSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await _bindingStore.UpsertAsync(previous, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await _sessionAttachmentRuntime.InvalidateAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void ValidateBinding(SessionTargetBinding binding)
    {
        var configuredSessionIds = _sessionRegistry.GetAll()
            .Select(static definition => definition.Id.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!SessionTargetBindingValidation.TryValidate(binding, configuredSessionIds, _profileCatalog, out var error))
        {
            throw new InvalidOperationException(error);
        }
    }

    private async Task PersistSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _bindingStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        await _persistence.SaveAsync(snapshot.Bindings, cancellationToken).ConfigureAwait(false);
    }

    private async Task RestorePreviousBindingAsync(
        SessionTargetBinding? previous,
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        if (previous is null)
        {
            await _bindingStore.DeleteAsync(sessionId, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _bindingStore.UpsertAsync(previous, cancellationToken).ConfigureAwait(false);
    }
}

```

### `MultiSessionHost.Desktop/Bindings/SessionTargetBindingModelMapper.cs`
`$ext
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

internal static class SessionTargetBindingModelMapper
{
    public static DesktopTargetProfile MapProfile(DesktopTargetProfileOptions options) =>
        new(
            options.ProfileName.Trim(),
            options.Kind,
            options.ProcessName.Trim(),
            TrimToNull(options.WindowTitleFragment),
            TrimToNull(options.CommandLineFragmentTemplate),
            TrimToNull(options.BaseAddressTemplate),
            options.MatchingMode,
            NormalizeMetadata(options.Metadata),
            options.SupportsUiSnapshots,
            options.SupportsStateEndpoint);

    public static SessionTargetBinding MapBinding(SessionTargetBindingOptions options) =>
        NormalizeBinding(
            new SessionTargetBinding(
                new SessionId(options.SessionId),
                options.TargetProfileName.Trim(),
                options.Variables.ToDictionary(
                    static pair => pair.Key.Trim(),
                    static pair => pair.Value ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase),
                options.Overrides is null ? null : MapOverrides(options.Overrides)));

    public static SessionTargetBinding NormalizeBinding(SessionTargetBinding binding) =>
        new(
            binding.SessionId,
            binding.TargetProfileName.Trim(),
            binding.Variables.ToDictionary(
                static pair => pair.Key.Trim(),
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase),
            binding.Overrides is null ? null : NormalizeOverride(binding.Overrides));

    public static DesktopTargetProfileOverride MapOverrides(DesktopTargetProfileOverrideOptions options) =>
        new(
            TrimToNull(options.ProcessName),
            TrimToNull(options.WindowTitleFragment),
            TrimToNull(options.CommandLineFragmentTemplate),
            TrimToNull(options.BaseAddressTemplate),
            options.MatchingMode,
            NormalizeMetadata(options.Metadata),
            options.SupportsUiSnapshots,
            options.SupportsStateEndpoint);

    public static DesktopTargetProfileOverride NormalizeOverride(DesktopTargetProfileOverride profileOverride) =>
        new(
            TrimToNull(profileOverride.ProcessName),
            TrimToNull(profileOverride.WindowTitleFragment),
            TrimToNull(profileOverride.CommandLineFragmentTemplate),
            TrimToNull(profileOverride.BaseAddressTemplate),
            profileOverride.MatchingMode,
            NormalizeMetadata(profileOverride.Metadata),
            profileOverride.SupportsUiSnapshots,
            profileOverride.SupportsStateEndpoint);

    public static IReadOnlyDictionary<string, string?> NormalizeMetadata(IReadOnlyDictionary<string, string?> metadata) =>
        metadata.ToDictionary(
            static pair => pair.Key.Trim(),
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

```

### `MultiSessionHost.Desktop/Bindings/SessionTargetBindingStoreBootstrapper.cs`
`$ext
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Desktop.Interfaces;

namespace MultiSessionHost.Desktop.Bindings;

public sealed class SessionTargetBindingStoreBootstrapper : ISessionTargetBindingBootstrapper
{
    private readonly ISessionTargetBindingStore _bindingStore;
    private readonly ISessionTargetBindingPersistence _persistence;
    private readonly IDesktopTargetProfileCatalog _profileCatalog;
    private readonly IReadOnlySet<string> _configuredSessionIds;
    private int _initialized;

    public SessionTargetBindingStoreBootstrapper(
        SessionHostOptions options,
        ISessionTargetBindingStore bindingStore,
        ISessionTargetBindingPersistence persistence,
        IDesktopTargetProfileCatalog profileCatalog)
    {
        ArgumentNullException.ThrowIfNull(options);

        _bindingStore = bindingStore;
        _persistence = persistence;
        _profileCatalog = profileCatalog;
        _configuredSessionIds = options.Sessions
            .Select(static session => session.SessionId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        var persistedBindings = await _persistence.LoadAsync(cancellationToken).ConfigureAwait(false);
        var seenSessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var binding in persistedBindings)
        {
            if (!seenSessions.Add(binding.SessionId.Value))
            {
                throw new InvalidOperationException($"The persisted binding store contains duplicate session '{binding.SessionId}'.");
            }

            if (!SessionTargetBindingValidation.TryValidate(binding, _configuredSessionIds, _profileCatalog, out var error))
            {
                throw new InvalidOperationException($"The persisted binding for session '{binding.SessionId}' is invalid. {error}");
            }

            await _bindingStore.UpsertAsync(binding, cancellationToken).ConfigureAwait(false);
        }
    }
}

```

### `MultiSessionHost.Desktop/Bindings/SessionTargetBindingValidation.cs`
`$ext
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

internal static class SessionTargetBindingValidation
{
    private static readonly string[] ReservedTemplateVariables = ["SessionId"];

    public static bool TryValidate(
        SessionTargetBinding binding,
        IReadOnlySet<string> configuredSessionIds,
        IDesktopTargetProfileCatalog profileCatalog,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(profileCatalog);

        var sessionId = binding.SessionId.Value;

        if (!configuredSessionIds.Contains(sessionId))
        {
            error = $"Session target binding '{sessionId}' does not match a configured session.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(binding.TargetProfileName))
        {
            error = $"Session target binding '{sessionId}' must define TargetProfileName.";
            return false;
        }

        var profile = profileCatalog.TryGetProfile(binding.TargetProfileName);

        if (profile is null)
        {
            error = $"Session target binding '{sessionId}' references unknown profile '{binding.TargetProfileName}'.";
            return false;
        }

        if (!TryValidateBindingVariables(sessionId, binding, out error))
        {
            return false;
        }

        if (binding.Overrides?.MatchingMode is { } matchingMode && !Enum.IsDefined(matchingMode))
        {
            error = $"Session target binding '{sessionId}' has an invalid override MatchingMode '{matchingMode}'.";
            return false;
        }

        var effectiveProfile = DesktopTargetProfileResolution.ApplyOverrides(profile, binding.Overrides);

        if (string.IsNullOrWhiteSpace(effectiveProfile.ProcessName))
        {
            error = $"Session target binding '{sessionId}' resolved an empty ProcessName.";
            return false;
        }

        if (!TryValidateMatchingInputs(
                $"binding '{sessionId}'",
                effectiveProfile.WindowTitleFragment,
                effectiveProfile.CommandLineFragmentTemplate,
                effectiveProfile.MatchingMode,
                out error))
        {
            return false;
        }

        var variables = DesktopTargetProfileResolution.BuildVariables(binding.SessionId, binding.Variables);
        var requiredVariables = SessionHostTemplateRenderer.GetVariableNames(
            GetTemplatedValues(
                effectiveProfile.ProcessName,
                effectiveProfile.WindowTitleFragment,
                effectiveProfile.CommandLineFragmentTemplate,
                effectiveProfile.BaseAddressTemplate,
                effectiveProfile.Metadata.Values));
        var missingVariables = requiredVariables
            .Except(variables.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingVariables.Length > 0)
        {
            error = $"Session target binding '{sessionId}' is missing required template variables: {string.Join(", ", missingVariables)}.";
            return false;
        }

        if (RequiresHttpBaseAddress(effectiveProfile.Kind))
        {
            if (string.IsNullOrWhiteSpace(effectiveProfile.BaseAddressTemplate))
            {
                error = $"Session target binding '{sessionId}' resolved an empty BaseAddressTemplate.";
                return false;
            }

            var renderedBaseAddress = SessionHostTemplateRenderer.Render(effectiveProfile.BaseAddressTemplate, variables);

            if (!Uri.TryCreate(renderedBaseAddress, UriKind.Absolute, out _))
            {
                error = $"Session target binding '{sessionId}' rendered an invalid BaseAddressTemplate '{renderedBaseAddress}'.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static IEnumerable<string?> GetTemplatedValues(
        string? processName,
        string? windowTitleFragment,
        string? commandLineFragmentTemplate,
        string? baseAddressTemplate,
        IEnumerable<string?> metadataValues)
    {
        yield return processName;
        yield return windowTitleFragment;
        yield return commandLineFragmentTemplate;
        yield return baseAddressTemplate;

        foreach (var value in metadataValues)
        {
            yield return value;
        }
    }

    private static bool TryValidateBindingVariables(
        string sessionId,
        SessionTargetBinding binding,
        out string? error)
    {
        foreach (var (key, value) in binding.Variables)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                error = $"Session target binding '{sessionId}' contains a variable with an empty key.";
                return false;
            }

            if (ReservedTemplateVariables.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                error = $"Session target binding '{sessionId}' cannot override reserved variable '{key}'.";
                return false;
            }

            if (value is null)
            {
                error = $"Session target binding '{sessionId}' contains a null value for variable '{key}'.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool TryValidateMatchingInputs(
        string scope,
        string? windowTitleFragment,
        string? commandLineFragmentTemplate,
        DesktopSessionMatchingMode matchingMode,
        out string? error)
    {
        switch (matchingMode)
        {
            case DesktopSessionMatchingMode.WindowTitle when string.IsNullOrWhiteSpace(windowTitleFragment):
                error = $"{scope} requires WindowTitleFragment when MatchingMode=WindowTitle.";
                return false;

            case DesktopSessionMatchingMode.CommandLine when string.IsNullOrWhiteSpace(commandLineFragmentTemplate):
                error = $"{scope} requires CommandLineFragmentTemplate when MatchingMode=CommandLine.";
                return false;

            case DesktopSessionMatchingMode.WindowTitleAndCommandLine
                when string.IsNullOrWhiteSpace(windowTitleFragment) || string.IsNullOrWhiteSpace(commandLineFragmentTemplate):
                error = $"{scope} requires both WindowTitleFragment and CommandLineFragmentTemplate when MatchingMode=WindowTitleAndCommandLine.";
                return false;

            default:
                error = null;
                return true;
        }
    }

    private static bool RequiresHttpBaseAddress(DesktopTargetKind kind) =>
        kind is DesktopTargetKind.SelfHostedHttpDesktop or DesktopTargetKind.DesktopTestApp;
}

```

### `MultiSessionHost.Desktop/Interfaces/IDesktopTargetProfileCatalog.cs`
`$ext
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IDesktopTargetProfileCatalog
{
    IReadOnlyCollection<DesktopTargetProfile> GetProfiles();

    DesktopTargetProfile? TryGetProfile(string profileName);
}

```

### `MultiSessionHost.Desktop/Interfaces/ISessionAttachmentRuntime.cs`
`$ext
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface ISessionAttachmentRuntime
{
    Task<DesktopSessionAttachment?> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task<DesktopSessionAttachment> EnsureAttachedAsync(SessionSnapshot snapshot, CancellationToken cancellationToken);

    Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken cancellationToken);
}

```

### `MultiSessionHost.Desktop/Targets/ConfiguredDesktopTargetProfileCatalog.cs`
`$ext
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Desktop.Bindings;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Targets;

public sealed class ConfiguredDesktopTargetProfileCatalog : IDesktopTargetProfileCatalog
{
    private readonly IReadOnlyDictionary<string, DesktopTargetProfile> _profilesByName;

    public ConfiguredDesktopTargetProfileCatalog(SessionHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _profilesByName = options.DesktopTargets
            .Select(SessionTargetBindingModelMapper.MapProfile)
            .ToDictionary(static profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<DesktopTargetProfile> GetProfiles() =>
        _profilesByName.Values
            .OrderBy(static profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public DesktopTargetProfile? TryGetProfile(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        return _profilesByName.TryGetValue(profileName.Trim(), out var profile) ? profile : null;
    }
}

```

### `MultiSessionHost.Tests/Desktop/SessionTargetBindingManagerTests.cs`
`$ext
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Bindings;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.Infrastructure.Registry;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Desktop;

public sealed class SessionTargetBindingManagerTests
{
    [Fact]
    public async Task UpsertAsync_FailsForUnknownSession()
    {
        var manager = await CreateManagerAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.UpsertAsync(
                new SessionTargetBinding(
                    new("missing"),
                    "test-app",
                    new Dictionary<string, string> { ["Port"] = "7100" },
                    Overrides: null),
                CancellationToken.None));

        Assert.Contains("does not match a configured session", exception.Message);
    }

    [Fact]
    public async Task UpsertAsync_FailsForUnknownProfile()
    {
        var manager = await CreateManagerAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.UpsertAsync(
                new SessionTargetBinding(
                    new("alpha"),
                    "missing-profile",
                    new Dictionary<string, string> { ["Port"] = "7100" },
                    Overrides: null),
                CancellationToken.None));

        Assert.Contains("unknown profile", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpsertAsync_FailsForMissingTemplateVariables()
    {
        var options = new SessionHostOptions
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            DesktopTargets =
            [
                new DesktopTargetProfileOptions
                {
                    ProfileName = "test-app",
                    Kind = DesktopTargetKind.DesktopTestApp,
                    ProcessName = "MultiSessionHost.TestDesktopApp",
                    WindowTitleFragment = "[SessionId: {SessionId}]",
                    CommandLineFragmentTemplate = "--session-id {SessionId} --tenant {Tenant}",
                    BaseAddressTemplate = "http://127.0.0.1:{Port}/",
                    MatchingMode = DesktopSessionMatchingMode.WindowTitleAndCommandLine,
                    SupportsUiSnapshots = true,
                    SupportsStateEndpoint = true
                }
            ],
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };
        var registry = new InMemorySessionRegistry();
        await registry.RegisterAsync(options.ToSessionDefinitions().Single(), CancellationToken.None);
        var manager = new SessionTargetBindingManager(
            new InMemorySessionTargetBindingStore(options, new FakeClock(DateTimeOffset.UtcNow)),
            new NoOpSessionTargetBindingPersistence(),
            registry,
            new ConfiguredDesktopTargetProfileCatalog(options),
            new StubSessionAttachmentRuntime());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.UpsertAsync(
                new SessionTargetBinding(
                    new("alpha"),
                    "test-app",
                    new Dictionary<string, string> { ["Port"] = "7100" },
                    Overrides: null),
                CancellationToken.None));

        Assert.Contains("Tenant", exception.Message);
    }

    private static async Task<SessionTargetBindingManager> CreateManagerAsync()
    {
        var options = new SessionHostOptions
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile()],
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };
        var registry = new InMemorySessionRegistry();
        await registry.RegisterAsync(options.ToSessionDefinitions().Single(), CancellationToken.None);

        return new SessionTargetBindingManager(
            new InMemorySessionTargetBindingStore(options, new FakeClock(DateTimeOffset.UtcNow)),
            new NoOpSessionTargetBindingPersistence(),
            registry,
            new ConfiguredDesktopTargetProfileCatalog(options),
            new StubSessionAttachmentRuntime());
    }

    private sealed class StubSessionAttachmentRuntime : ISessionAttachmentRuntime
    {
        public Task<DesktopSessionAttachment?> GetAsync(Core.Models.SessionId sessionId, CancellationToken cancellationToken) =>
            Task.FromResult<DesktopSessionAttachment?>(null);

        public Task<DesktopSessionAttachment> EnsureAttachedAsync(Core.Models.SessionSnapshot snapshot, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> InvalidateAsync(Core.Models.SessionId sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}

```

### `MultiSessionHost.Tests/Desktop/SessionTargetBindingStoreTests.cs`
`$ext
using Microsoft.Extensions.Hosting;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Bindings;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Desktop;

public sealed class SessionTargetBindingStoreTests
{
    [Fact]
    public async Task Store_InitializesFromConfiguredBindings()
    {
        var options = TestOptionsFactory.CreateDesktopTestAppOptions(7100, TestOptionsFactory.Session("alpha", startupDelayMs: 0));
        var store = new InMemorySessionTargetBindingStore(options, new FakeClock(DateTimeOffset.UtcNow));

        var binding = await store.GetAsync(new("alpha"), CancellationToken.None);

        Assert.NotNull(binding);
        Assert.Equal("test-app", binding!.TargetProfileName);
        Assert.Equal("7100", binding.Variables["Port"]);
    }

    [Fact]
    public async Task UpsertAsync_ReplacesExistingBindingForTheSameSession()
    {
        var options = TestOptionsFactory.CreateDesktopTestAppOptions(7100, TestOptionsFactory.Session("alpha", startupDelayMs: 0));
        var store = new InMemorySessionTargetBindingStore(options, new FakeClock(DateTimeOffset.UtcNow));

        await store.UpsertAsync(
            new(
                new("alpha"),
                "test-app",
                new Dictionary<string, string> { ["Port"] = "7200" },
                Overrides: null),
            CancellationToken.None);

        var binding = await store.GetAsync(new("alpha"), CancellationToken.None);

        Assert.NotNull(binding);
        Assert.Equal("7200", binding!.Variables["Port"]);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheBinding()
    {
        var options = TestOptionsFactory.CreateDesktopTestAppOptions(7100, TestOptionsFactory.Session("alpha", startupDelayMs: 0));
        var store = new InMemorySessionTargetBindingStore(options, new FakeClock(DateTimeOffset.UtcNow));

        var removed = await store.DeleteAsync(new("alpha"), CancellationToken.None);
        var binding = await store.GetAsync(new("alpha"), CancellationToken.None);

        Assert.True(removed);
        Assert.Null(binding);
    }

    [Fact]
    public async Task JsonPersistence_RoundTripsBindings()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "msh-tests", $"{Guid.NewGuid():N}", "bindings.json");
        var options = CreatePersistenceOptions(filePath);
        var persistence = CreatePersistence(options);
        var bindings = new[]
        {
            new SessionTargetBinding(
                new("beta"),
                "test-app",
                new Dictionary<string, string> { ["Port"] = "7101" },
                new DesktopTargetProfileOverride(
                    ProcessName: null,
                    WindowTitleFragment: "[SessionId: {SessionId}]",
                    CommandLineFragmentTemplate: "--session-id {SessionId}",
                    BaseAddressTemplate: null,
                    MatchingMode: DesktopSessionMatchingMode.WindowTitleAndCommandLine,
                    Metadata: new Dictionary<string, string?> { ["UiSource"] = "DesktopTestApp" },
                    SupportsUiSnapshots: true,
                    SupportsStateEndpoint: true))
        };

        await persistence.SaveAsync(bindings, CancellationToken.None);
        var loaded = await persistence.LoadAsync(CancellationToken.None);

        var binding = Assert.Single(loaded);
        Assert.Equal("beta", binding.SessionId.Value);
        Assert.Equal("7101", binding.Variables["Port"]);
        Assert.NotNull(binding.Overrides);
        Assert.Equal(DesktopSessionMatchingMode.WindowTitleAndCommandLine, binding.Overrides!.MatchingMode);
    }

    [Fact]
    public async Task Bootstrapper_PersistedBindingOverridesConfiguredBinding()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "msh-tests", $"{Guid.NewGuid():N}", "bindings.json");
        var options = CreatePersistenceOptions(filePath);
        var persistence = CreatePersistence(options);
        await persistence.SaveAsync(
            [
                new SessionTargetBinding(
                    new("alpha"),
                    "test-app",
                    new Dictionary<string, string> { ["Port"] = "7999" },
                    Overrides: null)
            ],
            CancellationToken.None);

        var store = new InMemorySessionTargetBindingStore(options, new FakeClock(DateTimeOffset.UtcNow));
        var bootstrapper = new SessionTargetBindingStoreBootstrapper(
            options,
            store,
            persistence,
            new ConfiguredDesktopTargetProfileCatalog(options));

        await bootstrapper.InitializeAsync(CancellationToken.None);
        var binding = await store.GetAsync(new("alpha"), CancellationToken.None);

        Assert.NotNull(binding);
        Assert.Equal("7999", binding!.Variables["Port"]);
    }

    private static SessionHostOptions CreatePersistenceOptions(string filePath) =>
        new()
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            BindingStorePersistenceMode = BindingStorePersistenceMode.JsonFile,
            BindingStoreFilePath = filePath,
            DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile()],
            SessionTargetBindings = [TestOptionsFactory.SessionTargetBinding("alpha", "test-app", "7100")],
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

    private static JsonFileSessionTargetBindingPersistence CreatePersistence(SessionHostOptions options) =>
        new(options, new TestHostEnvironment(Path.GetDirectoryName(options.BindingStoreFilePath!)!));

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ApplicationName = "Tests";
            EnvironmentName = "Development";
            ContentRootFileProvider = null!;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; }

        public string ContentRootPath { get; set; }

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
    }
}

```

### `MultiSessionHost.Tests/Hosting/WorkerBindingAdminApiIntegrationTests.cs`
`$ext
using System.Net;
using System.Net.Http.Json;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Hosting;

public sealed class WorkerBindingAdminApiIntegrationTests
{
    [Fact]
    public async Task BindingsEndpoints_ListUpsertPersistGetAndDeleteRuntimeBindings()
    {
        var persistenceDirectory = Path.Combine(Path.GetTempPath(), "msh-tests", Guid.NewGuid().ToString("N"));
        var persistencePath = Path.Combine(persistenceDirectory, "bindings.json");
        var options = new SessionHostOptions
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            EnableAdminApi = true,
            AdminApiUrl = "http://127.0.0.1:0",
            BindingStorePersistenceMode = BindingStorePersistenceMode.JsonFile,
            BindingStoreFilePath = persistencePath,
            DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile()],
            SessionTargetBindings = [TestOptionsFactory.SessionTargetBinding("alpha", "test-app", "7100")],
            Sessions = [TestOptionsFactory.Session("alpha", enabled: false, startupDelayMs: 0)]
        };

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        var snapshot = await client.GetFromJsonAsync<BindingStoreSnapshotDto>("/bindings");

        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Bindings);
        Assert.Equal("7100", snapshot.Bindings.Single().Variables["Port"]);

        var upsertRequest = new SessionTargetBindingUpsertRequest(
            "test-app",
            new Dictionary<string, string> { ["Port"] = "7200" },
            Overrides: null);
        var putResponse = await client.PutAsJsonAsync("/bindings/alpha", upsertRequest);
        putResponse.EnsureSuccessStatusCode();
        var updated = await putResponse.Content.ReadFromJsonAsync<SessionTargetBindingDto>();

        Assert.NotNull(updated);
        Assert.Equal("7200", updated!.Variables["Port"]);
        Assert.True(File.Exists(persistencePath));
        var persistedJson = await File.ReadAllTextAsync(persistencePath);
        Assert.Contains("\"port\": \"7200\"", persistedJson, StringComparison.OrdinalIgnoreCase);

        var fetched = await client.GetFromJsonAsync<SessionTargetBindingDto>("/bindings/alpha");

        Assert.NotNull(fetched);
        Assert.Equal("7200", fetched!.Variables["Port"]);

        var deleteResponse = await client.DeleteAsync("/bindings/alpha");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var deletedBindingResponse = await client.GetAsync("/bindings/alpha");
        var deletedTargetResponse = await client.GetAsync("/sessions/alpha/target");

        Assert.Equal(HttpStatusCode.NotFound, deletedBindingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deletedTargetResponse.StatusCode);
    }

    [Fact]
    public async Task SessionsTargetReflectsRuntimeBindingChangesWithoutRestart()
    {
        const string workerSessionId = "alpha";
        const int firstPort = 7920;
        const int secondPort = 7921;

        TestDesktopAppProcessHost? firstApp = null;
        TestDesktopAppProcessHost? secondApp = null;

        try
        {
            firstApp = await TestDesktopAppProcessHost.StartAsync(workerSessionId, firstPort);

            var options = new SessionHostOptions
            {
                DriverMode = DriverMode.DesktopTargetAdapter,
                EnableUiSnapshots = true,
                EnableAdminApi = true,
                AdminApiUrl = "http://127.0.0.1:0",
                DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile()],
                SessionTargetBindings =
                [
                    TestOptionsFactory.SessionTargetBinding(workerSessionId, "test-app", firstPort.ToString())
                ],
                Sessions = [TestOptionsFactory.Session(workerSessionId, startupDelayMs: 0)]
            };

            await using var harness = await WorkerHostHarness.StartAsync(options);
            var client = Assert.IsType<HttpClient>(harness.Client);

            await TestWait.UntilAsync(
                () => harness.Coordinator.GetSession(new SessionId(workerSessionId))?.Runtime.CurrentStatus == SessionStatus.Running,
                TimeSpan.FromSeconds(10),
                "The worker runtime did not start the rebound desktop session in time.");

            var initialRefresh = await client.PostAsync($"/sessions/{workerSessionId}/ui/refresh", content: null);
            initialRefresh.EnsureSuccessStatusCode();

            var initialTarget = await client.GetFromJsonAsync<SessionTargetDto>($"/sessions/{workerSessionId}/target");

            Assert.NotNull(initialTarget);
            Assert.NotNull(initialTarget!.Attachment);
            Assert.Equal(firstApp.ProcessId, initialTarget.Attachment!.ProcessId);
            Assert.Equal($"http://127.0.0.1:{firstPort}/", initialTarget.Target.BaseAddress);

            await firstApp.DisposeAsync();
            firstApp = null;
            secondApp = await TestDesktopAppProcessHost.StartAsync(workerSessionId, secondPort);

            var updateResponse = await client.PutAsJsonAsync(
                $"/bindings/{workerSessionId}",
                new SessionTargetBindingUpsertRequest(
                    "test-app",
                    new Dictionary<string, string> { ["Port"] = secondPort.ToString() },
                    Overrides: null));
            updateResponse.EnsureSuccessStatusCode();

            var updatedTarget = await client.GetFromJsonAsync<SessionTargetDto>($"/sessions/{workerSessionId}/target");

            Assert.NotNull(updatedTarget);
            Assert.Equal($"http://127.0.0.1:{secondPort}/", updatedTarget!.Target.BaseAddress);
            Assert.Null(updatedTarget.Attachment);

            var refreshResponse = await client.PostAsync($"/sessions/{workerSessionId}/ui/refresh", content: null);
            refreshResponse.EnsureSuccessStatusCode();
            var reboundTarget = await client.GetFromJsonAsync<SessionTargetDto>($"/sessions/{workerSessionId}/target");

            Assert.NotNull(reboundTarget);
            Assert.NotNull(reboundTarget!.Attachment);
            Assert.Equal(secondApp.ProcessId, reboundTarget.Attachment!.ProcessId);
            Assert.Equal($"http://127.0.0.1:{secondPort}/", reboundTarget.Target.BaseAddress);
        }
        finally
        {
            if (secondApp is not null)
            {
                await secondApp.DisposeAsync();
            }

            if (firstApp is not null)
            {
                await firstApp.DisposeAsync();
            }
        }
    }
}

```

## Modified Files Diff
### `MultiSessionHost.AdminApi/AdminApiEndpointRouteBuilderExtensions.cs`
```diff
diff --git a/MultiSessionHost.AdminApi/AdminApiEndpointRouteBuilderExtensions.cs b/MultiSessionHost.AdminApi/AdminApiEndpointRouteBuilderExtensions.cs
index a65b7fb..ce9afc4 100644
--- a/MultiSessionHost.AdminApi/AdminApiEndpointRouteBuilderExtensions.cs
+++ b/MultiSessionHost.AdminApi/AdminApiEndpointRouteBuilderExtensions.cs
@@ -7,6 +7,7 @@ using MultiSessionHost.Contracts.Sessions;
 using MultiSessionHost.Core.Enums;
 using MultiSessionHost.Core.Interfaces;
 using MultiSessionHost.Core.Models;
+using MultiSessionHost.Desktop.Bindings;
 using MultiSessionHost.Desktop.Interfaces;
 using MultiSessionHost.UiModel.Models;
 
@@ -18,6 +19,106 @@ public static class AdminApiEndpointRouteBuilderExtensions
     {
         ArgumentNullException.ThrowIfNull(endpoints);
 
+        endpoints.MapGet(
+            "/bindings",
+            async Task<IResult> (
+                HttpContext httpContext,
+                IAdminAuthorizationPolicy authorizationPolicy,
+                ISessionTargetBindingManager bindingManager,
+                CancellationToken cancellationToken) =>
+            {
+                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
+                {
+                    return Results.Unauthorized();
+                }
+
+                var snapshot = await bindingManager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
+                return Results.Ok(snapshot.ToDto());
+            });
+
+        endpoints.MapGet(
+            "/bindings/{sessionId}",
+            async Task<IResult> (
+                string sessionId,
+                HttpContext httpContext,
+                IAdminAuthorizationPolicy authorizationPolicy,
+                ISessionTargetBindingManager bindingManager,
+                CancellationToken cancellationToken) =>
+            {
+                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
+                {
+                    return Results.Unauthorized();
+                }
+
+                if (!TryParseSessionId(sessionId, out var parsedSessionId, out var error))
+                {
+                    return Results.BadRequest(new { Error = error });
+                }
+
+                var binding = await bindingManager.GetAsync(parsedSessionId, cancellationToken).ConfigureAwait(false);
+                return binding is null ? Results.NotFound() : Results.Ok(binding.ToDto());
+            });
+
+        endpoints.MapPut(
+            "/bindings/{sessionId}",
+            async Task<IResult> (
+                string sessionId,
+                SessionTargetBindingUpsertRequest request,
+                HttpContext httpContext,
+                IAdminAuthorizationPolicy authorizationPolicy,
+                ISessionTargetBindingManager bindingManager,
+                CancellationToken cancellationToken) =>
+            {
+                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
+                {
+                    return Results.Unauthorized();
+                }
+
+                if (!TryParseSessionId(sessionId, out var parsedSessionId, out var error))
+                {
+                    return Results.BadRequest(new { Error = error });
+                }
+
+                if (!TryCreateBinding(parsedSessionId, request, out var binding, out error))
+                {
+                    return Results.BadRequest(new { Error = error });
+                }
+
+                try
+                {
+                    var upserted = await bindingManager.UpsertAsync(binding!, cancellationToken).ConfigureAwait(false);
+                    return Results.Ok(upserted.ToDto());
+                }
+                catch (InvalidOperationException exception)
+                {
+                    return Results.BadRequest(new { Error = exception.Message });
+                }
+            });
+
+        endpoints.MapDelete(
+            "/bindings/{sessionId}",
+            async Task<IResult> (
+                string sessionId,
+                HttpContext httpContext,
+                IAdminAuthorizationPolicy authorizationPolicy,
+                ISessionTargetBindingManager bindingManager,
+                CancellationToken cancellationToken) =>
+            {
+                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
+                {
+                    return Results.Unauthorized();
+                }
+
+                if (!TryParseSessionId(sessionId, out var parsedSessionId, out var error))
+                {
+                    return Results.BadRequest(new { Error = error });
+                }
+
+                return await bindingManager.DeleteAsync(parsedSessionId, cancellationToken).ConfigureAwait(false)
+                    ? Results.NoContent()
+                    : Results.NotFound();
+            });
+
         endpoints.MapGet(
             "/health",
             async Task<IResult> (HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
@@ -560,6 +661,83 @@ public static class AdminApiEndpointRouteBuilderExtensions
         return true;
     }
 
+    private static bool TryCreateBinding(
+        SessionId sessionId,
+        SessionTargetBindingUpsertRequest request,
+        out Desktop.Models.SessionTargetBinding? binding,
+        out string? error)
+    {
+        if (request is null)
+        {
+            binding = null;
+            error = "Request body is required.";
+            return false;
+        }
+
+        if (string.IsNullOrWhiteSpace(request.TargetProfileName))
+        {
+            binding = null;
+            error = "TargetProfileName is required.";
+            return false;
+        }
+
+        if (!TryCreateOverride(request.Overrides, out var profileOverride, out error))
+        {
+            binding = null;
+            return false;
+        }
+
+        binding = new Desktop.Models.SessionTargetBinding(
+            sessionId,
+            request.TargetProfileName.Trim(),
+            (request.Variables ?? new Dictionary<string, string>())
+                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase),
+            profileOverride);
+        error = null;
+        return true;
+    }
+
+    private static bool TryCreateOverride(
+        DesktopTargetProfileOverrideDto? overrideDto,
+        out Desktop.Models.DesktopTargetProfileOverride? profileOverride,
+        out string? error)
+    {
+        if (overrideDto is null)
+        {
+            profileOverride = null;
+            error = null;
+            return true;
+        }
+
+        DesktopSessionMatchingMode? matchingMode = null;
+        DesktopSessionMatchingMode parsedMatchingMode = default;
+
+        if (!string.IsNullOrWhiteSpace(overrideDto.MatchingMode) &&
+            !Enum.TryParse<DesktopSessionMatchingMode>(overrideDto.MatchingMode, ignoreCase: true, out parsedMatchingMode))
+        {
+            profileOverride = null;
+            error = $"MatchingMode '{overrideDto.MatchingMode}' is not valid.";
+            return false;
+        }
+
+        if (!string.IsNullOrWhiteSpace(overrideDto.MatchingMode))
+        {
+            matchingMode = parsedMatchingMode;
+        }
+
+        profileOverride = new Desktop.Models.DesktopTargetProfileOverride(
+            overrideDto.ProcessName,
+            overrideDto.WindowTitleFragment,
+            overrideDto.CommandLineFragmentTemplate,
+            overrideDto.BaseAddressTemplate,
+            matchingMode,
+            overrideDto.Metadata ?? new Dictionary<string, string?>(),
+            overrideDto.SupportsUiSnapshots,
+            overrideDto.SupportsStateEndpoint);
+        error = null;
+        return true;
+    }
+
     private static bool TryCreateNodeId(
         string? value,
         out UiNodeId? nodeId,
```

### `MultiSessionHost.AdminApi/Mapping/DtoMappingExtensions.cs`
```diff
diff --git a/MultiSessionHost.AdminApi/Mapping/DtoMappingExtensions.cs b/MultiSessionHost.AdminApi/Mapping/DtoMappingExtensions.cs
index 6da3235..9d04236 100644
--- a/MultiSessionHost.AdminApi/Mapping/DtoMappingExtensions.cs
+++ b/MultiSessionHost.AdminApi/Mapping/DtoMappingExtensions.cs
@@ -1,6 +1,7 @@
 using System.Text.Json;
 using MultiSessionHost.Contracts.Sessions;
 using MultiSessionHost.Core.Models;
+using MultiSessionHost.Desktop.Bindings;
 using MultiSessionHost.Desktop.Interfaces;
 using MultiSessionHost.Desktop.Models;
 
@@ -123,6 +124,12 @@ public static class DtoMappingExtensions
             binding.Variables,
             binding.Overrides?.ToDto());
 
+    public static BindingStoreSnapshotDto ToDto(this BindingStoreSnapshot snapshot) =>
+        new(
+            snapshot.Version,
+            snapshot.LastUpdatedAtUtc,
+            snapshot.Bindings.Select(static binding => binding.ToDto()).ToArray());
+
     public static ResolvedDesktopTargetDto ToDto(this DesktopSessionTarget target) =>
         new(
             target.SessionId.Value,
```

### `MultiSessionHost.Core/Configuration/SessionHostOptions.cs`
```diff
diff --git a/MultiSessionHost.Core/Configuration/SessionHostOptions.cs b/MultiSessionHost.Core/Configuration/SessionHostOptions.cs
index de0ad03..5b674f3 100644
--- a/MultiSessionHost.Core/Configuration/SessionHostOptions.cs
+++ b/MultiSessionHost.Core/Configuration/SessionHostOptions.cs
@@ -21,6 +21,10 @@ public sealed class SessionHostOptions
 
     public bool EnableUiSnapshots { get; init; }
 
+    public BindingStorePersistenceMode BindingStorePersistenceMode { get; init; } = BindingStorePersistenceMode.None;
+
+    public string? BindingStoreFilePath { get; init; }
+
     public IReadOnlyList<DesktopTargetProfileOptions> DesktopTargets { get; init; } = [];
 
     public IReadOnlyList<SessionTargetBindingOptions> SessionTargetBindings { get; init; } = [];
```

### `MultiSessionHost.Core/Configuration/SessionHostOptionsExtensions.cs`
```diff
diff --git a/MultiSessionHost.Core/Configuration/SessionHostOptionsExtensions.cs b/MultiSessionHost.Core/Configuration/SessionHostOptionsExtensions.cs
index 3249b8f..80ac3c9 100644
--- a/MultiSessionHost.Core/Configuration/SessionHostOptionsExtensions.cs
+++ b/MultiSessionHost.Core/Configuration/SessionHostOptionsExtensions.cs
@@ -64,6 +64,19 @@ public static class SessionHostOptionsExtensions
             return false;
         }
 
+        if (!Enum.IsDefined(options.BindingStorePersistenceMode))
+        {
+            error = $"BindingStorePersistenceMode '{options.BindingStorePersistenceMode}' is not valid.";
+            return false;
+        }
+
+        if (options.BindingStorePersistenceMode == BindingStorePersistenceMode.JsonFile &&
+            string.IsNullOrWhiteSpace(options.BindingStoreFilePath))
+        {
+            error = "BindingStoreFilePath is required when BindingStorePersistenceMode=JsonFile.";
+            return false;
+        }
+
         if (options.Sessions.Count == 0)
         {
             error = "At least one session must be configured.";
```

### `MultiSessionHost.Desktop/Commands/UiCommandExecutor.cs`
```diff
diff --git a/MultiSessionHost.Desktop/Commands/UiCommandExecutor.cs b/MultiSessionHost.Desktop/Commands/UiCommandExecutor.cs
index 8d4729c..fe7b3c6 100644
--- a/MultiSessionHost.Desktop/Commands/UiCommandExecutor.cs
+++ b/MultiSessionHost.Desktop/Commands/UiCommandExecutor.cs
@@ -12,8 +12,7 @@ public sealed class UiCommandExecutor : IUiCommandExecutor
 {
     private readonly ISessionCoordinator _sessionCoordinator;
     private readonly IDesktopTargetProfileResolver _targetProfileResolver;
-    private readonly IDesktopTargetAdapterRegistry _targetAdapterRegistry;
-    private readonly IAttachedSessionStore _attachedSessionStore;
+    private readonly ISessionAttachmentRuntime _sessionAttachmentRuntime;
     private readonly IUiActionResolver _actionResolver;
     private readonly IReadOnlyDictionary<DesktopTargetKind, IUiInteractionAdapter> _interactionAdapters;
     private readonly IClock _clock;
@@ -22,8 +21,7 @@ public sealed class UiCommandExecutor : IUiCommandExecutor
     public UiCommandExecutor(
         ISessionCoordinator sessionCoordinator,
         IDesktopTargetProfileResolver targetProfileResolver,
-        IDesktopTargetAdapterRegistry targetAdapterRegistry,
-        IAttachedSessionStore attachedSessionStore,
+        ISessionAttachmentRuntime sessionAttachmentRuntime,
         IUiActionResolver actionResolver,
         IEnumerable<IUiInteractionAdapter> interactionAdapters,
         IClock clock,
@@ -31,8 +29,7 @@ public sealed class UiCommandExecutor : IUiCommandExecutor
     {
         _sessionCoordinator = sessionCoordinator;
         _targetProfileResolver = targetProfileResolver;
-        _targetAdapterRegistry = targetAdapterRegistry;
-        _attachedSessionStore = attachedSessionStore;
+        _sessionAttachmentRuntime = sessionAttachmentRuntime;
         _actionResolver = actionResolver;
         _clock = clock;
         _logger = logger;
@@ -81,18 +78,7 @@ public sealed class UiCommandExecutor : IUiCommandExecutor
             }
 
             var context = _targetProfileResolver.Resolve(session);
-            var targetAdapter = _targetAdapterRegistry.Resolve(context.Profile.Kind);
-            var attachment = await _attachedSessionStore.GetAsync(command.SessionId, cancellationToken).ConfigureAwait(false);
-
-            if (attachment is null)
-            {
-                return Fail(
-                    command,
-                    $"Session '{command.SessionId}' is active but has no current target attachment.",
-                    UiCommandFailureCodes.TargetNotAttached);
-            }
-
-            await targetAdapter.ValidateAttachmentAsync(session, context, attachment, cancellationToken).ConfigureAwait(false);
+            var attachment = await _sessionAttachmentRuntime.EnsureAttachedAsync(session, cancellationToken).ConfigureAwait(false);
 
             var resolvedAction = _actionResolver.Resolve(uiState.ProjectedTree, command);
             var interactionAdapter = ResolveInteractionAdapter(context.Profile.Kind);
```

### `MultiSessionHost.Desktop/DependencyInjection/DesktopServiceCollectionExtensions.cs`
```diff
diff --git a/MultiSessionHost.Desktop/DependencyInjection/DesktopServiceCollectionExtensions.cs b/MultiSessionHost.Desktop/DependencyInjection/DesktopServiceCollectionExtensions.cs
index 7e47751..3b136c7 100644
--- a/MultiSessionHost.Desktop/DependencyInjection/DesktopServiceCollectionExtensions.cs
+++ b/MultiSessionHost.Desktop/DependencyInjection/DesktopServiceCollectionExtensions.cs
@@ -1,6 +1,9 @@
 using Microsoft.Extensions.DependencyInjection;
+using MultiSessionHost.Core.Configuration;
+using MultiSessionHost.Core.Enums;
 using MultiSessionHost.Desktop.Adapters;
 using MultiSessionHost.Desktop.Attachments;
+using MultiSessionHost.Desktop.Bindings;
 using MultiSessionHost.Desktop.Commands;
 using MultiSessionHost.Desktop.Drivers;
 using MultiSessionHost.Desktop.Interfaces;
@@ -28,10 +31,28 @@ public static class DesktopServiceCollectionExtensions
 
         services.AddSingleton<IProcessLocator, Win32ProcessLocator>();
         services.AddSingleton<IWindowLocator, Win32WindowLocator>();
+        services.AddSingleton<IDesktopTargetProfileCatalog, ConfiguredDesktopTargetProfileCatalog>();
+        services.AddSingleton<InMemorySessionTargetBindingStore>();
+        services.AddSingleton<ISessionTargetBindingStore>(static serviceProvider => serviceProvider.GetRequiredService<InMemorySessionTargetBindingStore>());
+        services.AddSingleton<ISessionTargetBindingPersistence>(
+            static serviceProvider =>
+            {
+                var options = serviceProvider.GetRequiredService<SessionHostOptions>();
+
+                return options.BindingStorePersistenceMode switch
+                {
+                    BindingStorePersistenceMode.None => new NoOpSessionTargetBindingPersistence(),
+                    BindingStorePersistenceMode.JsonFile => ActivatorUtilities.CreateInstance<JsonFileSessionTargetBindingPersistence>(serviceProvider),
+                    _ => throw new InvalidOperationException($"BindingStorePersistenceMode '{options.BindingStorePersistenceMode}' is not supported.")
+                };
+            });
+        services.AddSingleton<ISessionTargetBindingBootstrapper, SessionTargetBindingStoreBootstrapper>();
+        services.AddSingleton<ISessionTargetBindingManager, SessionTargetBindingManager>();
         services.AddSingleton<IDesktopTargetProfileResolver, ConfiguredDesktopTargetProfileResolver>();
         services.AddSingleton<IDesktopTargetMatcher, DefaultDesktopTargetMatcher>();
         services.AddSingleton<ISessionAttachmentResolver, DefaultSessionAttachmentResolver>();
         services.AddSingleton<IAttachedSessionStore, InMemoryAttachedSessionStore>();
+        services.AddSingleton<ISessionAttachmentRuntime, DefaultSessionAttachmentRuntime>();
         services.AddSingleton<IUiSnapshotSerializer, JsonUiSnapshotSerializer>();
         services.AddSingleton<IUiSnapshotProvider, SelfHostedHttpUiSnapshotProvider>();
         services.AddSingleton<SelfHostedHttpUiTreeNormalizer>();
```

### `MultiSessionHost.Desktop/Drivers/DesktopTargetSessionDriver.cs`
```diff
diff --git a/MultiSessionHost.Desktop/Drivers/DesktopTargetSessionDriver.cs b/MultiSessionHost.Desktop/Drivers/DesktopTargetSessionDriver.cs
index b6f4070..6769246 100644
--- a/MultiSessionHost.Desktop/Drivers/DesktopTargetSessionDriver.cs
+++ b/MultiSessionHost.Desktop/Drivers/DesktopTargetSessionDriver.cs
@@ -14,8 +14,7 @@ namespace MultiSessionHost.Desktop.Drivers;
 public sealed class DesktopTargetSessionDriver : ISessionDriver
 {
     private readonly SessionHostOptions _options;
-    private readonly ISessionAttachmentResolver _attachmentResolver;
-    private readonly IAttachedSessionStore _attachedSessionStore;
+    private readonly ISessionAttachmentRuntime _sessionAttachmentRuntime;
     private readonly IDesktopTargetProfileResolver _targetProfileResolver;
     private readonly IDesktopTargetAdapterRegistry _adapterRegistry;
     private readonly IUiSnapshotSerializer _uiSnapshotSerializer;
@@ -28,8 +27,7 @@ public sealed class DesktopTargetSessionDriver : ISessionDriver
 
     public DesktopTargetSessionDriver(
         SessionHostOptions options,
-        ISessionAttachmentResolver attachmentResolver,
-        IAttachedSessionStore attachedSessionStore,
+        ISessionAttachmentRuntime sessionAttachmentRuntime,
         IDesktopTargetProfileResolver targetProfileResolver,
         IDesktopTargetAdapterRegistry adapterRegistry,
         IUiSnapshotSerializer uiSnapshotSerializer,
@@ -41,8 +39,7 @@ public sealed class DesktopTargetSessionDriver : ISessionDriver
         ILogger<DesktopTargetSessionDriver> logger)
     {
         _options = options;
-        _attachmentResolver = attachmentResolver;
-        _attachedSessionStore = attachedSessionStore;
+        _sessionAttachmentRuntime = sessionAttachmentRuntime;
         _targetProfileResolver = targetProfileResolver;
         _adapterRegistry = adapterRegistry;
         _uiSnapshotSerializer = uiSnapshotSerializer;
@@ -56,30 +53,19 @@ public sealed class DesktopTargetSessionDriver : ISessionDriver
 
     public async Task AttachAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
     {
-        var context = _targetProfileResolver.Resolve(snapshot);
-        var adapter = _adapterRegistry.Resolve(context.Profile.Kind);
-        var attachment = await _attachmentResolver.ResolveAsync(snapshot, cancellationToken).ConfigureAwait(false);
-
-        await adapter.ValidateAttachmentAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
-        await adapter.AttachAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
-        await _attachedSessionStore.SetAsync(attachment, cancellationToken).ConfigureAwait(false);
+        await _sessionAttachmentRuntime.EnsureAttachedAsync(snapshot, cancellationToken).ConfigureAwait(false);
     }
 
     public async Task DetachAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
     {
-        var context = _targetProfileResolver.Resolve(snapshot);
-        var adapter = _adapterRegistry.Resolve(context.Profile.Kind);
-        var attachment = await _attachedSessionStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
-
-        await adapter.DetachAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
-        await _attachedSessionStore.RemoveAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
+        await _sessionAttachmentRuntime.InvalidateAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
     }
 
     public async Task ExecuteWorkItemAsync(SessionSnapshot snapshot, SessionWorkItem workItem, CancellationToken cancellationToken)
     {
         var context = _targetProfileResolver.Resolve(snapshot);
         var adapter = _adapterRegistry.Resolve(context.Profile.Kind);
-        var attachment = await EnsureAttachmentAsync(snapshot, context, adapter, cancellationToken).ConfigureAwait(false);
+        var attachment = await _sessionAttachmentRuntime.EnsureAttachedAsync(snapshot, cancellationToken).ConfigureAwait(false);
 
         switch (workItem.Kind)
         {
@@ -99,31 +85,6 @@ public sealed class DesktopTargetSessionDriver : ISessionDriver
         }
     }
 
-    private async Task<DesktopSessionAttachment> EnsureAttachmentAsync(
-        SessionSnapshot snapshot,
-        ResolvedDesktopTargetContext context,
-        IDesktopTargetAdapter adapter,
-        CancellationToken cancellationToken)
-    {
-        var current = await _attachedSessionStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
-
-        if (current is not null && AreEquivalent(current.Target, context.Target))
-        {
-            return current;
-        }
-
-        if (current is not null)
-        {
-            await adapter.DetachAsync(snapshot, context, current, cancellationToken).ConfigureAwait(false);
-            await _attachedSessionStore.RemoveAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
-        }
-
-        await AttachAsync(snapshot, cancellationToken).ConfigureAwait(false);
-
-        return await _attachedSessionStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false)
-            ?? throw new InvalidOperationException($"Session '{snapshot.SessionId}' could not be attached.");
-    }
-
     private async Task FetchUiSnapshotAsync(
         SessionId sessionId,
         SessionSnapshot snapshot,
@@ -250,34 +211,4 @@ public sealed class DesktopTargetSessionDriver : ISessionDriver
         }
     }
 
-    private static bool AreEquivalent(DesktopSessionTarget left, DesktopSessionTarget right) =>
-        left.SessionId == right.SessionId &&
-        left.ProfileName == right.ProfileName &&
-        left.Kind == right.Kind &&
-        left.MatchingMode == right.MatchingMode &&
-        string.Equals(left.ProcessName, right.ProcessName, StringComparison.Ordinal) &&
-        string.Equals(left.WindowTitleFragment, right.WindowTitleFragment, StringComparison.Ordinal) &&
-        string.Equals(left.CommandLineFragment, right.CommandLineFragment, StringComparison.Ordinal) &&
-        Equals(left.BaseAddress, right.BaseAddress) &&
-        HaveSameMetadata(left.Metadata, right.Metadata);
-
-    private static bool HaveSameMetadata(
-        IReadOnlyDictionary<string, string?> left,
-        IReadOnlyDictionary<string, string?> right)
-    {
-        if (left.Count != right.Count)
-        {
-            return false;
-        }
-
-        foreach (var (key, value) in left)
-        {
-            if (!right.TryGetValue(key, out var otherValue) || !string.Equals(value, otherValue, StringComparison.Ordinal))
-            {
-                return false;
-            }
-        }
-
-        return true;
-    }
 }
```

### `MultiSessionHost.Desktop/MultiSessionHost.Desktop.csproj`
```diff
diff --git a/MultiSessionHost.Desktop/MultiSessionHost.Desktop.csproj b/MultiSessionHost.Desktop/MultiSessionHost.Desktop.csproj
index 90ca78d..fd34d49 100644
--- a/MultiSessionHost.Desktop/MultiSessionHost.Desktop.csproj
+++ b/MultiSessionHost.Desktop/MultiSessionHost.Desktop.csproj
@@ -7,6 +7,7 @@
 
   <ItemGroup>
     <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.5" />
+    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.5" />
     <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.5" />
     <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.5" />
     <PackageReference Include="System.Management" Version="10.0.0" />
```

### `MultiSessionHost.Desktop/Targets/ConfiguredDesktopTargetProfileResolver.cs`
```diff
diff --git a/MultiSessionHost.Desktop/Targets/ConfiguredDesktopTargetProfileResolver.cs b/MultiSessionHost.Desktop/Targets/ConfiguredDesktopTargetProfileResolver.cs
index 8365d3d..1ac5ce2 100644
--- a/MultiSessionHost.Desktop/Targets/ConfiguredDesktopTargetProfileResolver.cs
+++ b/MultiSessionHost.Desktop/Targets/ConfiguredDesktopTargetProfileResolver.cs
@@ -1,5 +1,5 @@
-using MultiSessionHost.Core.Configuration;
 using MultiSessionHost.Core.Models;
+using MultiSessionHost.Desktop.Bindings;
 using MultiSessionHost.Desktop.Interfaces;
 using MultiSessionHost.Desktop.Models;
 
@@ -7,34 +7,23 @@ namespace MultiSessionHost.Desktop.Targets;
 
 public sealed class ConfiguredDesktopTargetProfileResolver : IDesktopTargetProfileResolver
 {
-    private readonly IReadOnlyDictionary<string, DesktopTargetProfile> _profilesByName;
-    private readonly IReadOnlyDictionary<SessionId, SessionTargetBinding> _bindingsBySessionId;
+    private readonly IDesktopTargetProfileCatalog _profileCatalog;
+    private readonly ISessionTargetBindingStore _bindingStore;
 
-    public ConfiguredDesktopTargetProfileResolver(SessionHostOptions options)
+    public ConfiguredDesktopTargetProfileResolver(
+        IDesktopTargetProfileCatalog profileCatalog,
+        ISessionTargetBindingStore bindingStore)
     {
-        ArgumentNullException.ThrowIfNull(options);
-
-        _profilesByName = options.DesktopTargets
-            .Select(MapProfile)
-            .ToDictionary(static profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase);
-        _bindingsBySessionId = options.SessionTargetBindings
-            .Select(MapBinding)
-            .ToDictionary(static binding => binding.SessionId);
+        _profileCatalog = profileCatalog;
+        _bindingStore = bindingStore;
     }
 
-    public IReadOnlyCollection<DesktopTargetProfile> GetProfiles() =>
-        _profilesByName.Values
-            .OrderBy(static profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase)
-            .ToArray();
+    public IReadOnlyCollection<DesktopTargetProfile> GetProfiles() => _profileCatalog.GetProfiles();
 
-    public DesktopTargetProfile? TryGetProfile(string profileName)
-    {
-        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
-        return _profilesByName.TryGetValue(profileName.Trim(), out var profile) ? profile : null;
-    }
+    public DesktopTargetProfile? TryGetProfile(string profileName) => _profileCatalog.TryGetProfile(profileName);
 
     public SessionTargetBinding? TryGetBinding(SessionId sessionId) =>
-        _bindingsBySessionId.TryGetValue(sessionId, out var binding) ? binding : null;
+        _bindingStore.GetAsync(sessionId, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
 
     public ResolvedDesktopTargetContext Resolve(SessionSnapshot snapshot)
     {
@@ -44,136 +33,19 @@ public sealed class ConfiguredDesktopTargetProfileResolver : IDesktopTargetProfi
             ?? throw new InvalidOperationException($"Session '{snapshot.SessionId}' does not have a SessionTargetBinding.");
         var profile = TryGetProfile(binding.TargetProfileName)
             ?? throw new InvalidOperationException($"Target profile '{binding.TargetProfileName}' could not be found for session '{snapshot.SessionId}'.");
-        var effectiveProfile = ApplyOverrides(profile, binding.Overrides);
-        var variables = BuildVariables(snapshot.SessionId, binding.Variables);
+        var effectiveProfile = DesktopTargetProfileResolution.ApplyOverrides(profile, binding.Overrides);
+        var variables = DesktopTargetProfileResolution.BuildVariables(snapshot.SessionId, binding.Variables);
         var target = new DesktopSessionTarget(
             snapshot.SessionId,
             effectiveProfile.ProfileName,
             effectiveProfile.Kind,
             effectiveProfile.MatchingMode,
-            RenderRequired(effectiveProfile.ProcessName, variables, "ProcessName"),
-            RenderOptional(effectiveProfile.WindowTitleFragment, variables),
-            RenderOptional(effectiveProfile.CommandLineFragmentTemplate, variables),
-            RenderUri(effectiveProfile.BaseAddressTemplate, variables),
-            RenderMetadata(effectiveProfile.Metadata, variables));
+            DesktopTargetProfileResolution.RenderRequired(effectiveProfile.ProcessName, variables, "ProcessName"),
+            DesktopTargetProfileResolution.RenderOptional(effectiveProfile.WindowTitleFragment, variables),
+            DesktopTargetProfileResolution.RenderOptional(effectiveProfile.CommandLineFragmentTemplate, variables),
+            DesktopTargetProfileResolution.RenderUri(effectiveProfile.BaseAddressTemplate, variables),
+            DesktopTargetProfileResolution.RenderMetadata(effectiveProfile.Metadata, variables));
 
         return new ResolvedDesktopTargetContext(snapshot.SessionId, effectiveProfile, binding, target, variables);
     }
-
-    private static DesktopTargetProfile MapProfile(DesktopTargetProfileOptions options) =>
-        new(
-            options.ProfileName.Trim(),
-            options.Kind,
-            options.ProcessName.Trim(),
-            TrimToNull(options.WindowTitleFragment),
-            TrimToNull(options.CommandLineFragmentTemplate),
-            TrimToNull(options.BaseAddressTemplate),
-            options.MatchingMode,
-            CreateMetadata(options.Metadata),
-            options.SupportsUiSnapshots,
-            options.SupportsStateEndpoint);
-
-    private static SessionTargetBinding MapBinding(SessionTargetBindingOptions options) =>
-        new(
-            new SessionId(options.SessionId),
-            options.TargetProfileName.Trim(),
-            options.Variables.ToDictionary(
-                static pair => pair.Key.Trim(),
-                static pair => pair.Value ?? string.Empty,
-                StringComparer.OrdinalIgnoreCase),
-            options.Overrides is null ? null : MapOverrides(options.Overrides));
-
-    private static DesktopTargetProfileOverride MapOverrides(DesktopTargetProfileOverrideOptions options) =>
-        new(
-            TrimToNull(options.ProcessName),
-            TrimToNull(options.WindowTitleFragment),
-            TrimToNull(options.CommandLineFragmentTemplate),
-            TrimToNull(options.BaseAddressTemplate),
-            options.MatchingMode,
-            CreateMetadata(options.Metadata),
-            options.SupportsUiSnapshots,
-            options.SupportsStateEndpoint);
-
-    private static DesktopTargetProfile ApplyOverrides(DesktopTargetProfile profile, DesktopTargetProfileOverride? overrides)
-    {
-        if (overrides is null)
-        {
-            return profile;
-        }
-
-        var metadata = profile.Metadata
-            .Concat(overrides.Metadata)
-            .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
-            .ToDictionary(static group => group.Key, static group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
-
-        return profile with
-        {
-            ProcessName = overrides.ProcessName ?? profile.ProcessName,
-            WindowTitleFragment = overrides.WindowTitleFragment ?? profile.WindowTitleFragment,
-            CommandLineFragmentTemplate = overrides.CommandLineFragmentTemplate ?? profile.CommandLineFragmentTemplate,
-            BaseAddressTemplate = overrides.BaseAddressTemplate ?? profile.BaseAddressTemplate,
-            MatchingMode = overrides.MatchingMode ?? profile.MatchingMode,
-            Metadata = metadata,
-            SupportsUiSnapshots = overrides.SupportsUiSnapshots ?? profile.SupportsUiSnapshots,
-            SupportsStateEndpoint = overrides.SupportsStateEndpoint ?? profile.SupportsStateEndpoint
-        };
-    }
-
-    private static IReadOnlyDictionary<string, string> BuildVariables(SessionId sessionId, IReadOnlyDictionary<string, string> bindingVariables)
-    {
-        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
-        {
-            ["SessionId"] = sessionId.Value
-        };
-
-        foreach (var (key, value) in bindingVariables)
-        {
-            variables[key] = value;
-        }
-
-        return variables;
-    }
-
-    private static string RenderRequired(string template, IReadOnlyDictionary<string, string> variables, string fieldName)
-    {
-        var rendered = SessionHostTemplateRenderer.Render(template, variables).Trim();
-        return rendered.Length == 0
-            ? throw new InvalidOperationException($"The rendered {fieldName} is empty.")
-            : rendered;
-    }
-
-    private static string? RenderOptional(string? template, IReadOnlyDictionary<string, string> variables) =>
-        string.IsNullOrWhiteSpace(template)
-            ? null
-            : SessionHostTemplateRenderer.Render(template, variables).Trim();
-
-    private static Uri? RenderUri(string? template, IReadOnlyDictionary<string, string> variables)
-    {
-        if (string.IsNullOrWhiteSpace(template))
-        {
-            return null;
-        }
-
-        var rendered = SessionHostTemplateRenderer.Render(template, variables);
-        return Uri.TryCreate(rendered, UriKind.Absolute, out var uri)
-            ? uri
-            : throw new InvalidOperationException($"The rendered BaseAddressTemplate '{rendered}' is not a valid absolute URI.");
-    }
-
-    private static IReadOnlyDictionary<string, string?> RenderMetadata(
-        IReadOnlyDictionary<string, string?> metadata,
-        IReadOnlyDictionary<string, string> variables) =>
-        metadata.ToDictionary(
-            static pair => pair.Key,
-            pair => string.IsNullOrWhiteSpace(pair.Value) ? pair.Value : SessionHostTemplateRenderer.Render(pair.Value, variables),
-            StringComparer.OrdinalIgnoreCase);
-
-    private static IReadOnlyDictionary<string, string?> CreateMetadata(IReadOnlyDictionary<string, string?> metadata) =>
-        metadata.ToDictionary(
-            static pair => pair.Key.Trim(),
-            static pair => pair.Value,
-            StringComparer.OrdinalIgnoreCase);
-
-    private static string? TrimToNull(string? value) =>
-        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
 }
```

### `MultiSessionHost.Tests/Configuration/SessionHostOptionsValidationTests.cs`
```diff
diff --git a/MultiSessionHost.Tests/Configuration/SessionHostOptionsValidationTests.cs b/MultiSessionHost.Tests/Configuration/SessionHostOptionsValidationTests.cs
index 850a97c..390b3e7 100644
--- a/MultiSessionHost.Tests/Configuration/SessionHostOptionsValidationTests.cs
+++ b/MultiSessionHost.Tests/Configuration/SessionHostOptionsValidationTests.cs
@@ -150,4 +150,23 @@ public sealed class SessionHostOptionsValidationTests
         Assert.False(valid);
         Assert.Contains("Tenant", error);
     }
+
+    [Fact]
+    public void TryValidate_FailsWhenJsonFilePersistenceHasNoPath()
+    {
+        var options = new SessionHostOptions
+        {
+            DriverMode = DriverMode.DesktopTargetAdapter,
+            EnableUiSnapshots = true,
+            BindingStorePersistenceMode = BindingStorePersistenceMode.JsonFile,
+            DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile()],
+            SessionTargetBindings = [TestOptionsFactory.SessionTargetBinding("alpha", "test-app", "7100")],
+            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
+        };
+
+        var valid = options.TryValidate(out var error);
+
+        Assert.False(valid);
+        Assert.Contains("BindingStoreFilePath", error);
+    }
 }
```

### `MultiSessionHost.Tests/Desktop/DesktopTargetAdapterSystemTests.cs`
```diff
diff --git a/MultiSessionHost.Tests/Desktop/DesktopTargetAdapterSystemTests.cs b/MultiSessionHost.Tests/Desktop/DesktopTargetAdapterSystemTests.cs
index 6def4cb..266bd47 100644
--- a/MultiSessionHost.Tests/Desktop/DesktopTargetAdapterSystemTests.cs
+++ b/MultiSessionHost.Tests/Desktop/DesktopTargetAdapterSystemTests.cs
@@ -5,7 +5,6 @@ using MultiSessionHost.Core.Enums;
 using MultiSessionHost.Core.Interfaces;
 using MultiSessionHost.Core.Models;
 using MultiSessionHost.Desktop.Adapters;
-using MultiSessionHost.Desktop.Attachments;
 using MultiSessionHost.Desktop.DependencyInjection;
 using MultiSessionHost.Desktop.Drivers;
 using MultiSessionHost.Desktop.Interfaces;
@@ -89,6 +88,7 @@ public sealed class DesktopTargetAdapterSystemTests
         await uiStateStore.InitializeAsync(SessionUiState.Create(sessionId), CancellationToken.None);
 
         var adapter = new SpyDesktopTargetAdapter(DesktopTargetKind.SelfHostedHttpDesktop);
+        var attachmentRuntime = new StubSessionAttachmentRuntime(attachment);
         var driver = new DesktopTargetSessionDriver(
             new SessionHostOptions
             {
@@ -96,8 +96,7 @@ public sealed class DesktopTargetAdapterSystemTests
                 EnableUiSnapshots = false,
                 Sessions = [TestOptionsFactory.Session("alpha")]
             },
-            new StubSessionAttachmentResolver(attachment),
-            new InMemoryAttachedSessionStore(),
+            attachmentRuntime,
             new StubDesktopTargetProfileResolver(context),
             new DesktopTargetAdapterRegistry([adapter]),
             new JsonUiSnapshotSerializer(),
@@ -113,8 +112,9 @@ public sealed class DesktopTargetAdapterSystemTests
             SessionWorkItem.Create(sessionId, SessionWorkItemKind.Tick, DateTimeOffset.UtcNow, "adapter dispatch test"),
             CancellationToken.None);
 
-        Assert.Equal(1, adapter.ValidateCalls);
-        Assert.Equal(1, adapter.AttachCalls);
+        Assert.Equal(1, attachmentRuntime.EnsureCalls);
+        Assert.Equal(0, adapter.ValidateCalls);
+        Assert.Equal(0, adapter.AttachCalls);
         Assert.Equal(1, adapter.ExecuteCalls);
         Assert.Equal(SessionWorkItemKind.Tick, adapter.LastWorkItem!.Kind);
     }
@@ -182,17 +182,28 @@ public sealed class DesktopTargetAdapterSystemTests
             throw new NotSupportedException();
     }
 
-    private sealed class StubSessionAttachmentResolver : ISessionAttachmentResolver
+    private sealed class StubSessionAttachmentRuntime : ISessionAttachmentRuntime
     {
         private readonly DesktopSessionAttachment _attachment;
 
-        public StubSessionAttachmentResolver(DesktopSessionAttachment attachment)
+        public StubSessionAttachmentRuntime(DesktopSessionAttachment attachment)
         {
             _attachment = attachment;
         }
 
-        public ValueTask<DesktopSessionAttachment> ResolveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken) =>
-            ValueTask.FromResult(_attachment);
+        public int EnsureCalls { get; private set; }
+
+        public Task<DesktopSessionAttachment?> GetAsync(SessionId sessionId, CancellationToken cancellationToken) =>
+            Task.FromResult<DesktopSessionAttachment?>(_attachment);
+
+        public Task<DesktopSessionAttachment> EnsureAttachedAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
+        {
+            EnsureCalls++;
+            return Task.FromResult(_attachment);
+        }
+
+        public Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken cancellationToken) =>
+            Task.FromResult(true);
     }
 
     private sealed class StubDesktopTargetProfileResolver : IDesktopTargetProfileResolver
```

### `MultiSessionHost.Tests/Desktop/DesktopTargetProfileResolverTests.cs`
```diff
diff --git a/MultiSessionHost.Tests/Desktop/DesktopTargetProfileResolverTests.cs b/MultiSessionHost.Tests/Desktop/DesktopTargetProfileResolverTests.cs
index d14214c..9a587aa 100644
--- a/MultiSessionHost.Tests/Desktop/DesktopTargetProfileResolverTests.cs
+++ b/MultiSessionHost.Tests/Desktop/DesktopTargetProfileResolverTests.cs
@@ -1,6 +1,7 @@
 using MultiSessionHost.Core.Configuration;
 using MultiSessionHost.Core.Enums;
 using MultiSessionHost.Core.Models;
+using MultiSessionHost.Desktop.Bindings;
 using MultiSessionHost.Desktop.Targets;
 using MultiSessionHost.Tests.Common;
 
@@ -48,7 +49,7 @@ public sealed class DesktopTargetProfileResolverTests
             ],
             Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
         };
-        var resolver = new ConfiguredDesktopTargetProfileResolver(options);
+        var resolver = CreateResolver(options);
 
         var context = resolver.Resolve(CreateSnapshot(options, "alpha"));
 
@@ -77,7 +78,7 @@ public sealed class DesktopTargetProfileResolverTests
                 TestOptionsFactory.Session("beta", startupDelayMs: 0)
             ]
         };
-        var resolver = new ConfiguredDesktopTargetProfileResolver(options);
+        var resolver = CreateResolver(options);
 
         var alpha = resolver.Resolve(CreateSnapshot(options, "alpha"));
         var beta = resolver.Resolve(CreateSnapshot(options, "beta"));
@@ -115,7 +116,7 @@ public sealed class DesktopTargetProfileResolverTests
             ],
             Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
         };
-        var resolver = new ConfiguredDesktopTargetProfileResolver(options);
+        var resolver = CreateResolver(options);
 
         var context = resolver.Resolve(CreateSnapshot(options, "alpha"));
 
@@ -133,4 +134,9 @@ public sealed class DesktopTargetProfileResolverTests
 
         return new SessionSnapshot(definition, state, PendingWorkItems: 0);
     }
+
+    private static ConfiguredDesktopTargetProfileResolver CreateResolver(SessionHostOptions options) =>
+        new(
+            new ConfiguredDesktopTargetProfileCatalog(options),
+            new InMemorySessionTargetBindingStore(options, new FakeClock(DateTimeOffset.UtcNow)));
 }
```

### `MultiSessionHost.Tests/Desktop/SessionAttachmentResolverTests.cs`
```diff
diff --git a/MultiSessionHost.Tests/Desktop/SessionAttachmentResolverTests.cs b/MultiSessionHost.Tests/Desktop/SessionAttachmentResolverTests.cs
index ad3932b..88825f0 100644
--- a/MultiSessionHost.Tests/Desktop/SessionAttachmentResolverTests.cs
+++ b/MultiSessionHost.Tests/Desktop/SessionAttachmentResolverTests.cs
@@ -2,6 +2,7 @@ using MultiSessionHost.Core.Configuration;
 using MultiSessionHost.Core.Enums;
 using MultiSessionHost.Core.Models;
 using MultiSessionHost.Desktop.Attachments;
+using MultiSessionHost.Desktop.Bindings;
 using MultiSessionHost.Desktop.Processes;
 using MultiSessionHost.Desktop.Targets;
 using MultiSessionHost.Desktop.Windows;
@@ -23,7 +24,7 @@ public sealed class SessionAttachmentResolverTests
 
         var options = CreateDesktopOptions(basePort, alphaId, betaId);
         var resolver = new DefaultSessionAttachmentResolver(
-            new ConfiguredDesktopTargetProfileResolver(options),
+            CreateProfileResolver(options),
             new Win32ProcessLocator(),
             new Win32WindowLocator(),
             new DefaultDesktopTargetMatcher(),
@@ -54,7 +55,7 @@ public sealed class SessionAttachmentResolverTests
 
         var options = CreateDesktopOptions(basePort, sessionIds);
         var resolver = new DefaultSessionAttachmentResolver(
-            new ConfiguredDesktopTargetProfileResolver(options),
+            CreateProfileResolver(options),
             new Win32ProcessLocator(),
             new Win32WindowLocator(),
             new DefaultDesktopTargetMatcher(),
@@ -86,4 +87,9 @@ public sealed class SessionAttachmentResolverTests
 
         return new SessionSnapshot(definition, state, PendingWorkItems: 0);
     }
+
+    private static ConfiguredDesktopTargetProfileResolver CreateProfileResolver(SessionHostOptions options) =>
+        new(
+            new ConfiguredDesktopTargetProfileCatalog(options),
+            new InMemorySessionTargetBindingStore(options, new FakeClock(DateTimeOffset.UtcNow)));
 }
```

### `MultiSessionHost.Worker/WorkerHostService.cs`
```diff
diff --git a/MultiSessionHost.Worker/WorkerHostService.cs b/MultiSessionHost.Worker/WorkerHostService.cs
index d26e16f..8bbdf5b 100644
--- a/MultiSessionHost.Worker/WorkerHostService.cs
+++ b/MultiSessionHost.Worker/WorkerHostService.cs
@@ -1,20 +1,24 @@
 using MultiSessionHost.Core.Configuration;
 using MultiSessionHost.Core.Interfaces;
+using MultiSessionHost.Desktop.Bindings;
 
 namespace MultiSessionHost.Worker;
 
 public sealed class WorkerHostService : BackgroundService
 {
     private readonly ISessionCoordinator _sessionCoordinator;
+    private readonly ISessionTargetBindingBootstrapper _bindingBootstrapper;
     private readonly SessionHostOptions _options;
     private readonly ILogger<WorkerHostService> _logger;
 
     public WorkerHostService(
         ISessionCoordinator sessionCoordinator,
+        ISessionTargetBindingBootstrapper bindingBootstrapper,
         SessionHostOptions options,
         ILogger<WorkerHostService> logger)
     {
         _sessionCoordinator = sessionCoordinator;
+        _bindingBootstrapper = bindingBootstrapper;
         _options = options;
         _logger = logger;
     }
@@ -30,6 +34,8 @@ public sealed class WorkerHostService : BackgroundService
                 _options.AdminApiUrl);
         }
 
+        await _bindingBootstrapper.InitializeAsync(cancellationToken).ConfigureAwait(false);
+
         await base.StartAsync(cancellationToken).ConfigureAwait(false);
     }
```

### `MultiSessionHost.Worker/appsettings.Development.json`
```diff
diff --git a/MultiSessionHost.Worker/appsettings.Development.json b/MultiSessionHost.Worker/appsettings.Development.json
index 110086d..32c2582 100644
--- a/MultiSessionHost.Worker/appsettings.Development.json
+++ b/MultiSessionHost.Worker/appsettings.Development.json
@@ -13,6 +13,8 @@
     "AdminApiUrl": "http://localhost:5088",
     "DriverMode": "NoOp",
     "EnableUiSnapshots": false,
+    "BindingStorePersistenceMode": "None",
+    "BindingStoreFilePath": null,
     "DesktopTargets": [],
     "SessionTargetBindings": []
   }
```

### `MultiSessionHost.Worker/appsettings.json`
```diff
diff --git a/MultiSessionHost.Worker/appsettings.json b/MultiSessionHost.Worker/appsettings.json
index 3e8e2a3..35704cf 100644
--- a/MultiSessionHost.Worker/appsettings.json
+++ b/MultiSessionHost.Worker/appsettings.json
@@ -14,6 +14,8 @@
     "AdminApiUrl": "http://localhost:5088",
     "DriverMode": "NoOp",
     "EnableUiSnapshots": false,
+    "BindingStorePersistenceMode": "None",
+    "BindingStoreFilePath": null,
     "DesktopTargets": [],
     "SessionTargetBindings": [],
     "Sessions": [
```

### `README.md`
```diff
diff --git a/README.md b/README.md
index 1749c00..43db0c4 100644
--- a/README.md
+++ b/README.md
@@ -8,6 +8,8 @@ La integración de escritorio ya no está acoplada a `MultiSessionHost.TestDeskt
 
 - `DesktopTargetProfile`
 - `SessionTargetBinding`
+- `ISessionTargetBindingStore`
+- `ISessionTargetBindingPersistence`
 - `IDesktopTargetAdapter`
 - `IDesktopTargetAdapterRegistry`
 - `UiCommand`
@@ -49,14 +51,30 @@ No incluye ni pretende incluir:
 - `SessionTargetBinding`
   - vincula una `SessionId` con un profile
   - aporta variables y overrides opcionales por sesión
+- `ISessionTargetBindingStore`
+  - mantiene bindings mutables en memoria
+  - se inicializa desde `SessionTargetBindings`
+  - pasa a ser la fuente runtime de verdad después del arranque
+- `ISessionTargetBindingPersistence`
+  - carga bindings persistidos al arrancar
+  - guarda el snapshot runtime después de cada mutación
+- `ConfiguredDesktopTargetProfileCatalog`
+  - mantiene `DesktopTargetProfile` como configuración inmutable
 - `ConfiguredDesktopTargetProfileResolver`
-  - resuelve binding + profile
+  - resuelve binding runtime + profile configurado
   - aplica overrides
   - renderiza templates con variables de sesión
+- `ISessionTargetBindingManager`
+  - valida create/update/delete
+  - persiste cambios
+  - invalida attachments obsoletos por sesión
 - `DefaultSessionAttachmentResolver`
   - usa profile/binding resueltos
   - enumera procesos/ventanas
   - aplica el `MatchingMode`
+- `DefaultSessionAttachmentRuntime`
+  - garantiza attach lazy con el binding más reciente
+  - invalida attachments obsoletos después de una mutación
 - `DesktopTargetSessionDriver`
   - driver real configurable
   - delega attach/detach/work items/snapshots al adapter correcto
@@ -81,7 +99,9 @@ No incluye ni pretende incluir:
 ```text
 Worker session
   -> DesktopTargetSessionDriver
+  -> ISessionTargetBindingStore
   -> SessionTargetBinding
+  -> IDesktopTargetProfileCatalog
   -> DesktopTargetProfile
   -> IDesktopTargetAdapterRegistry
   -> IDesktopTargetAdapter
@@ -92,6 +112,37 @@ Worker session
   -> planned work items
 ```
 
+## Store de bindings editable en runtime
+
+`DesktopTargetProfile` sigue siendo **config-driven** e inmutable durante la ejecución. Lo que ahora es editable en caliente es `SessionTargetBinding`.
+
+### Precedencia de bindings al arrancar
+
+El orden de carga es:
+
+1. se cargan profiles configurados desde `DesktopTargets`
+2. se cargan bindings configurados desde `SessionTargetBindings`
+3. el `InMemorySessionTargetBindingStore` se inicializa con esos bindings
+4. si hay persistencia habilitada, se cargan bindings persistidos y pisan por `SessionId` a los configurados
+5. desde ese momento el store runtime queda autoritativo
+
+Resumen práctico:
+
+- los bindings de `appsettings` siguen sirviendo como defaults
+- los bindings persistidos ganan para la misma `SessionId`
+- los cambios hechos por API viven en el store runtime inmediatamente
+- si borras un binding configurado, la sesión queda sin resolver hasta que crees otro
+- si reinicias el worker y ese `SessionId` no existe en el archivo persistido, vuelve a aplicar el default de configuración
+
+### Qué pasa cuando cambia un binding
+
+- no hace falta reiniciar el worker
+- la siguiente resolución usa el binding nuevo
+- si había attachment activo para esa sesión, se invalida y se remueve del store de attachments
+- `GET /sessions/{id}/target` muestra el binding y target nuevos inmediatamente
+- el siguiente `ui/refresh`, command o attach vuelve a conectarse usando el target actualizado
+- el aislamiento entre sesiones se mantiene porque el store está indexado por `SessionId`
+
 ## Capa de comandos semánticos
 
 La vista `UiTree` no ejecuta nada directamente. El flujo nuevo queda así:
@@ -162,6 +213,8 @@ La sección sigue siendo `MultiSessionHost`.
     "AdminApiUrl": "http://localhost:5088",
     "DriverMode": "DesktopTargetAdapter",
     "EnableUiSnapshots": true,
+    "BindingStorePersistenceMode": "JsonFile",
+    "BindingStoreFilePath": "data/session-target-bindings.json",
     "DesktopTargets": [
       {
         "ProfileName": "test-app",
@@ -226,6 +279,8 @@ La sección sigue siendo `MultiSessionHost`.
 
 - `DriverMode` debe ser válido.
 - `EnableUiSnapshots=true` requiere `DriverMode=DesktopTargetAdapter`.
+- `BindingStorePersistenceMode` debe ser válido.
+- `BindingStoreFilePath` es obligatorio cuando `BindingStorePersistenceMode=JsonFile`.
 - cada `DesktopTargetProfile` debe tener `ProfileName` único y `Kind` válido.
 - cada `SessionTargetBinding` debe apuntar a una sesión configurada.
 - cada binding debe apuntar a un profile existente.
@@ -254,6 +309,8 @@ Endpoints nuevos de inspección:
 - `GET /targets`
 - `GET /targets/{profileName}`
 - `GET /sessions/{id}/target`
+- `GET /bindings`
+- `GET /bindings/{sessionId}`
 
 Endpoints nuevos de comandos semánticos:
 
@@ -264,6 +321,11 @@ Endpoints nuevos de comandos semánticos:
 - `POST /sessions/{id}/nodes/{nodeId}/toggle`
 - `POST /sessions/{id}/nodes/{nodeId}/select`
 
+Endpoints nuevos de mutación de bindings:
+
+- `PUT /bindings/{sessionId}`
+- `DELETE /bindings/{sessionId}`
+
 `/sessions/{id}/target` expone:
 
 - profile resuelto
@@ -276,6 +338,16 @@ Endpoints nuevos de comandos semánticos:
 
 Si una sesión todavía no tiene `UiTree` proyectado, el executor hace auto-refresh antes de resolver el comando. Después de un comando exitoso dispara un refresh posterior para dejar el árbol actualizado. Las fallas semánticas devuelven `409 Conflict` con `UiCommandResultDto`.
 
+### Payload para upsert de binding
+
+`PUT /bindings/{sessionId}` acepta:
+
+- `TargetProfileName`
+- `Variables`
+- `Overrides`
+
+`SessionId` siempre viene de la ruta, no del body.
+
 ## Cómo agregar un nuevo target kind
 
 1. agrega un valor nuevo a `DesktopTargetKind`
@@ -296,12 +368,55 @@ Scheduler y coordinator no necesitan cambios.
 ## Cómo bindear una sesión a un profile
 
 1. crea o reutiliza un `DesktopTargetProfile`
-2. agrega un `SessionTargetBinding`
+2. agrega un `SessionTargetBinding` en configuración o por Admin API
 3. define `SessionId`
 4. define `TargetProfileName`
 5. agrega variables como `Port`
 6. si hace falta, usa `Overrides` para esa sesión
 
+## Cómo editar bindings en runtime
+
+Listar bindings actuales:
+
+```powershell
+Invoke-RestMethod http://localhost:5088/bindings
+```
+
+Consultar un binding:
+
+```powershell
+Invoke-RestMethod http://localhost:5088/bindings/alpha
+```
+
+Crear o actualizar un binding:
+
+```powershell
+Invoke-RestMethod -Method Put `
+  -Uri http://localhost:5088/bindings/alpha `
+  -ContentType 'application/json' `
+  -Body '{
+    "targetProfileName":"test-app",
+    "variables":{
+      "Port":"7102"
+    },
+    "overrides":null
+  }'
+```
+
+Borrar un binding runtime:
+
+```powershell
+Invoke-RestMethod -Method Delete http://localhost:5088/bindings/alpha
+```
+
+Verificar el target resuelto después del cambio:
+
+```powershell
+Invoke-RestMethod http://localhost:5088/sessions/alpha/target
+```
+
+Si borras un binding, `/sessions/{id}/target` pasa a devolver conflicto hasta que crees uno nuevo.
+
 ## Cómo probar con MultiSessionHost.TestDesktopApp
 
 Compila primero:
@@ -329,6 +444,7 @@ Pruebas HTTP rápidas:
 
 ```powershell
 Invoke-RestMethod http://localhost:5088/targets
+Invoke-RestMethod http://localhost:5088/bindings
 Invoke-RestMethod http://localhost:5088/sessions/alpha/target
 Invoke-RestMethod -Method Post http://localhost:5088/sessions/alpha/ui/refresh
 Invoke-RestMethod http://localhost:5088/sessions/alpha/ui
@@ -383,8 +499,15 @@ La suite cubre ahora:
 
 - parse y validación de `DesktopTargets`
 - parse y validación de `SessionTargetBindings`
+- validación de persistencia `JsonFile`
 - errores por binding faltante, profile inexistente o variables faltantes
 - render de templates por binding
+- store runtime editable de bindings
+- persistencia JSON y precedencia de startup
+- endpoints `GET /bindings`
+- endpoints `PUT /bindings/{sessionId}`
+- endpoints `DELETE /bindings/{sessionId}`
+- rebind runtime sin reiniciar el worker
 - aislamiento entre sesiones
 - registry de adapters
 - selección de adapter por el driver real
```


