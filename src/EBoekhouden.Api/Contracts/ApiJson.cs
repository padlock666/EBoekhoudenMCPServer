using System.Text.Json;

namespace EBoekhouden.Api.Contracts;

public static class ApiJson
{
    // Accept documented camelCase and PascalCase examples, but never coerce numbers.
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

public sealed record ApiPage<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Count { get; init; }
}
