using System.Text.Json;
using System.Text.Json.Serialization;
using KotobaSenpai.Core.Llm;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>Keeps the agent CLI wire contract: <see cref="AgentResultCode"/> serializes as its snake_case value.</summary>
internal sealed class AgentResultCodeJsonConverter : JsonConverter<AgentResultCode>
{
    public override AgentResultCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => AgentResultCodes.TryParseWire(reader.GetString())
            ?? throw new JsonException("Unknown agent result code.");

    public override void Write(Utf8JsonWriter writer, AgentResultCode value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToWire());
}
