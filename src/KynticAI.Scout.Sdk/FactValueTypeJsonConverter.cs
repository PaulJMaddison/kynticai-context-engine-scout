using System.Text.Json;
using System.Text.Json.Serialization;

namespace KynticAI.Scout.Sdk;

/// <summary>
/// JSON converter for <see cref="FactValueType"/> that matches the API wire
/// format: the API serialises the enum as its integer value (System.Text.Json
/// default), so the SDK reads the integer encoding and also tolerates the
/// string encoding (for example <c>"Number"</c>) for backward compatibility.
/// </summary>
public sealed class FactValueTypeJsonConverter : JsonConverter<FactValueType>
{
    public override FactValueType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return (FactValueType)reader.GetInt32();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (value is not null && Enum.TryParse<FactValueType>(value, ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            throw new JsonException($"The value '{value}' is not a valid {nameof(FactValueType)}.");
        }

        throw new JsonException($"Unexpected token {reader.TokenType} when reading {nameof(FactValueType)}.");
    }

    public override void Write(Utf8JsonWriter writer, FactValueType value, JsonSerializerOptions options)
        => writer.WriteNumberValue((int)value);
}
