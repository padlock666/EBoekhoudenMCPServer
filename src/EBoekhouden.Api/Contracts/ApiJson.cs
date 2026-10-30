using System.Text.Json;

namespace EBoekhouden.Api.Contracts;

public static class ApiJson
{
    // Accept both documented casings. Numeric strings are restricted to the annotated mutation type.
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            AllowDuplicateProperties = false,
        };
        options.Converters.Add(new ExactDecimalConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

public sealed record ApiPage<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Count { get; init; }
}
