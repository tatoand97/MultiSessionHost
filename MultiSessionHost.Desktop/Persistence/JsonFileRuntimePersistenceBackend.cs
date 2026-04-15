using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Persistence;

public sealed class JsonFileRuntimePersistenceBackend : IRuntimePersistenceBackend
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new SessionIdJsonConverter() }
    };

    private readonly string _rootDirectory;
    private readonly ILogger<JsonFileRuntimePersistenceBackend> _logger;

    public JsonFileRuntimePersistenceBackend(
        SessionHostOptions options,
        IHostEnvironment environment,
        ILogger<JsonFileRuntimePersistenceBackend> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        var configuredPath = options.RuntimePersistence.BasePath
            ?? throw new InvalidOperationException("RuntimePersistence.BasePath must be configured when RuntimePersistence.Mode=JsonFile.");
        _rootDirectory = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));
        _logger = logger;
    }

    public async Task<RuntimePersistenceLoadResult> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return new RuntimePersistenceLoadResult([], []);
        }

        var envelopes = new List<SessionRuntimePersistenceEnvelope>();
        var errors = new List<RuntimePersistenceLoadError>();

        foreach (var filePath in Directory.EnumerateFiles(_rootDirectory, "*.runtime.json", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = File.OpenRead(filePath);
                var envelope = await JsonSerializer.DeserializeAsync<SessionRuntimePersistenceEnvelope>(
                    stream,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);

                if (envelope is null)
                {
                    errors.Add(new RuntimePersistenceLoadError(null, filePath, "Runtime persistence file was empty."));
                    continue;
                }

                envelopes.Add(envelope);
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Skipping malformed runtime persistence file '{Path}'.", filePath);
                errors.Add(new RuntimePersistenceLoadError(null, filePath, exception.Message));
            }
        }

        return new RuntimePersistenceLoadResult(envelopes, errors);
    }

    public async Task<SessionRuntimePersistenceEnvelope?> LoadSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var filePath = GetSessionPath(sessionId);
        if (!File.Exists(filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<SessionRuntimePersistenceEnvelope>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveSessionAsync(SessionRuntimePersistenceEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        Directory.CreateDirectory(_rootDirectory);
        var filePath = GetSessionPath(envelope.SessionId);
        var tempFilePath = Path.Combine(_rootDirectory, $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = File.Create(tempFilePath))
            {
                await JsonSerializer.SerializeAsync(stream, envelope, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(filePath))
            {
                File.Replace(tempFilePath, filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempFilePath, filePath);
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

    public Task DeleteSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var filePath = GetSessionPath(sessionId);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetSessionPathAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(GetSessionPath(sessionId));

    private string GetSessionPath(SessionId sessionId) =>
        Path.Combine(_rootDirectory, $"{SanitizeFileName(sessionId.Value)}.runtime.json");

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Select(character => invalid.Contains(character) ? '_' : character).ToArray();
        return new string(chars);
    }
}
