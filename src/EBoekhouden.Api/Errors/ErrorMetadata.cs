using System.Text.Json;
using System.Text.RegularExpressions;

namespace EBoekhouden.Api.Errors;

internal readonly partial record struct ErrorMetadata(string? Code, string? TraceId)
{
    private const int MaximumBytes = 64 * 1024;

    public static async Task<ErrorMetadata> ReadAsync(HttpResponseMessage response, string credential,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (response.Content.Headers.ContentLength > MaximumBytes) return default;
        try
        {
            // The transport already buffered the response; bound the diagnostic JSON parser separately.
            var buffer = new byte[MaximumBytes + 1];
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                length += read;
            }
            if (length > MaximumBytes) return default;
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(buffer.AsMemory(0, length));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return default;
            var sensitive = SensitiveValues(response.RequestMessage, credential);
            return new(Sanitize(UniqueString(document.RootElement, "code"), CodePattern(), sensitive),
                Sanitize(UniqueString(document.RootElement, "traceId"), TracePattern(), sensitive));
        }
        catch (JsonException) { return default; }
        catch (IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }
    }

    private static string? UniqueString(JsonElement root, string name)
    {
        string? value = null;
        var matches = 0;
        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            matches++;
            if (property.Value.ValueKind == JsonValueKind.String) value = property.Value.GetString();
        }
        return matches == 1 ? value : null;
    }

    private static string? Sanitize(string? value, Regex pattern, IReadOnlyList<(string Value, bool Secret)> sensitive)
    {
        if (value is null || !pattern.IsMatch(value)) return null;
        foreach (var item in sensitive)
        {
            if (value.Equals(item.Value, StringComparison.OrdinalIgnoreCase) ||
                ((item.Secret || item.Value.Length >= 4) && value.Contains(item.Value, StringComparison.OrdinalIgnoreCase)))
                return null;
        }
        return value;
    }

    private static List<(string Value, bool Secret)> SensitiveValues(HttpRequestMessage? request, string credential)
    {
        var values = new List<(string, bool)>();
        if (!string.IsNullOrEmpty(credential)) values.Add((credential, true));
        if (request?.Headers.Authorization?.Parameter is { Length: > 0 } token) values.Add((token, true));
        if (request?.RequestUri is { IsAbsoluteUri: true } uri)
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length != 2 || parts[0] is "limit" or "offset") continue;
                var value = Uri.UnescapeDataString(parts[1]).Trim('%', '?');
                if (value.Length > 0) values.Add((value, false));
            }
        }
        return values;
    }

    [GeneratedRegex(@"\A[A-Z][A-Z0-9_]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._:\-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TracePattern();
}
