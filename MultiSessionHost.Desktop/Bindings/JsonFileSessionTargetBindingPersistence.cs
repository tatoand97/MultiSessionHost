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
