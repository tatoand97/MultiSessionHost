using System.Text.Json;
using System.Text.Json.Serialization;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Persistence;

internal sealed class SessionIdJsonConverter : JsonConverter<SessionId>
{
    public override SessionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? throw new JsonException("SessionId cannot be null."));

    public override void Write(Utf8JsonWriter writer, SessionId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
