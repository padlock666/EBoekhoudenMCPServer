using System.Text.Json;
using System.Text.Json.Serialization;

namespace EBoekhouden.Api.Contracts;

// The built-in decimal reader can round excess precision or underflow to zero.
// Financial values must either survive exactly or fail as an invalid response.
internal sealed class ExactDecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number) throw new JsonException("Expected a decimal number.");
        using var document = JsonDocument.ParseValue(ref reader);
        var number = document.RootElement;
        if (!number.TryGetDecimal(out var value) ||
            // .NET 10 DeepEquals compares equivalent decimal representations without rounding.
            !JsonElement.DeepEquals(number, JsonSerializer.SerializeToElement(value)))
            throw new JsonException("The number cannot be represented exactly as a decimal.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}
