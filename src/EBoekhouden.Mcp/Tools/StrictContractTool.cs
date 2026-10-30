using System.Text.Json;
using EBoekhouden.Api.Errors;
using EBoekhouden.Mcp.Contracts;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EBoekhouden.Mcp.Tools;

// SDK binding otherwise nests a request parameter and ignores unknown top-level arguments.
internal sealed class StrictContractTool<TRequest>(McpServerTool inner) : DelegatingMcpServerTool(inner) where TRequest : class
{
    public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        var original = request.Params;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Preserve the original JSON arguments to detect duplicate fields before dictionary binding loses them.
            var arguments = ReadArguments(request);
            if (arguments.ValueKind != JsonValueKind.Object || arguments.Deserialize<TRequest>(ContractJson.Options) is null)
                return Error("invalid_arguments", "Arguments must match the documented tool schema.");
            request.Params = new CallToolRequestParams
            {
                Name = original.Name,
                Arguments = new Dictionary<string, JsonElement> { ["request"] = arguments },
            };
            return await base.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JsonException) { return Error("invalid_arguments", "Arguments must match the documented tool schema."); }
        catch (ArgumentException) { return Error("invalid_arguments", "Arguments must satisfy the documented filters, ranges and limits."); }
        catch (ApiException error)
        {
            return Error("upstream_failure", error.Message, error.StatusCode is { } status ? (int)status : null,
                error.Code, error.TraceId, error.TransportFailure?.ToString());
        }
        catch (InvalidDataException) { return Error("invalid_response", "e-Boekhouden returned an invalid response."); }
        catch (Exception) { return Error("internal_error", "The tool could not complete the request."); }
        finally { request.Params = original; }
    }

    private static JsonElement ReadArguments(RequestContext<CallToolRequestParams> request)
    {
        if (request.JsonRpcRequest.Params is { } parameters)
        {
            using var doc = JsonDocument.Parse(parameters.ToJsonString());
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var arguments = default(JsonElement);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new JsonException();
                if (property.Name == "arguments") arguments = property.Value;
            }
            if (arguments.ValueKind != JsonValueKind.Undefined) return arguments.Clone();
        }
        return JsonSerializer.SerializeToElement(request.Params.Arguments ?? new Dictionary<string, JsonElement>());
    }

    private static CallToolResult Error(string kind, string message, int? status = null,
        string? code = null, string? traceId = null, string? transportFailure = null) => new()
    {
        IsError = true,
        // Error content deliberately does not masquerade as the advertised success output schema.
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new ToolFailure(kind, message, status, code, traceId, transportFailure), ContractJson.Options) }],
    };

    private sealed record ToolFailure(string Kind, string Message, int? Status, string? Code, string? TraceId, string? TransportFailure);
}
