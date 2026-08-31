using System.Text.Json;
using System.Text.Json.Serialization;

namespace RestaurantAgent.Extractor.Ui;

/// <summary>
/// Tolera respuestas de versiones anteriores del agente/API que serializaban algunos valores
/// escalares como números o booleanos, aunque el DTO actual los represente como texto.
/// </summary>
internal sealed class FlexibleStringJsonConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => ReadNumber(ref reader),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => throw new JsonException("El valor JSON debe ser texto o un escalar.")
        };
    }

    private static string ReadNumber(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetRawText();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
