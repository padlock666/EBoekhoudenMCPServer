using System.Text.Json;
using System.Text.Json.Nodes;
using EBoekhouden.Mcp.Contracts;
using ModelContextProtocol.Protocol;

namespace EBoekhouden.Mcp.Tools;

internal static class ToolSchemas
{
    public static void Refine<TRequest>(Tool tool)
    {
        var generated = JsonNode.Parse(tool.InputSchema.GetRawText())!.AsObject();
        var input = generated["properties"]!["request"]!.DeepClone().AsObject();
        input["type"] = "object";
        if (generated["$defs"] is { } definitions) input["$defs"] = definitions.DeepClone();
        Visit(input, node =>
        {
            if (node["$ref"] is JsonValue reference && reference.TryGetValue<string>(out var path))
                node["$ref"] = path.Replace("#/properties/request", "#", StringComparison.Ordinal);
            if (node["properties"] is not null) node["additionalProperties"] = false;
        });
        var properties = input["properties"]!.AsObject();
        foreach (var field in properties.ToArray())
        {
            var schema = Resolve(input, field.Value!);
            schema["description"] = Description(field.Key);
            if (field.Key == "limit") { schema["minimum"] = 1; schema["maximum"] = 2000; schema["default"] = 50; }
            else if (field.Key == "offset") { schema["minimum"] = 0; schema["maximum"] = int.MaxValue; schema["default"] = 0; }
            else if (field.Key is "relationId" or "ledgerId" or "costCenterId" ||
                field.Key == "id" && typeof(TRequest) != typeof(SearchEntriesRequest))
            { schema["minimum"] = int.MinValue; schema["maximum"] = int.MaxValue; }
            if (schema["properties"] is JsonObject filter)
            {
                var ordered = filter.ContainsKey("to");
                filter["op"]!["enum"] = ordered
                    ? JsonSerializer.SerializeToNode(new[] { "eq", "not_eq", "gt", "gte", "lt", "lte", "range" })
                    : JsonSerializer.SerializeToNode(new[] { "eq", "not_eq", "like", "not_like" });
                if (ordered)
                {
                    if (typeof(TRequest) == typeof(SearchEntriesRequest) && field.Key == "id")
                        foreach (var endpoint in new[] { "value", "to" })
                        { filter[endpoint]!["minimum"] = long.MinValue; filter[endpoint]!["maximum"] = long.MaxValue; }
                    var to = filter["value"]!.DeepClone();
                    schema["allOf"] = JsonNode.Parse("""[{"if":{"properties":{"op":{"const":"range"}},"required":["op"]},"then":{"required":["to"],"properties":{}},"else":{"properties":{"to":{"type":"null"}}}}]""");
                    schema["allOf"]![0]!["then"]!["properties"]!["to"] = to;
                }
                if (typeof(TRequest) == typeof(SearchLedgerAccountsRequest)) filter["value"]!["maxLength"] = 10;
            }
        }
        if (typeof(TRequest) == typeof(SearchEntriesRequest))
            properties["type"]!["enum"] = JsonSerializer.SerializeToNode(new object?[]
                { "invoice_received", "invoice_sent", "invoice_payment_received", "invoice_payment_sent", "money_received", "money_sent", "journal_entry", null });
        if (typeof(TRequest) == typeof(ListOutstandingInvoicesRequest))
            properties["side"]!["enum"] = JsonSerializer.SerializeToNode(new[] { "receivable", "payable" });
        if (typeof(TRequest) == typeof(GetLedgerBalancesRequest))
        {
            properties["mode"]!["enum"] = JsonSerializer.SerializeToNode(new[] { "as_of", "period_change" });
            input["allOf"] = JsonNode.Parse("""[{"if":{"properties":{"mode":{"const":"period_change"}},"required":["mode"]},"then":{"required":["from"],"properties":{"from":{"type":"string","format":"date"}}},"else":{"properties":{"from":{"type":"null"}}}}]""");
        }
        tool.InputSchema = JsonSerializer.SerializeToElement(input);

        var output = JsonNode.Parse(tool.OutputSchema!.Value.GetRawText())!.AsObject();
        output["type"] = "object";
        Visit(output, node =>
        {
            if (node["properties"] is not JsonObject fields) return;
            node["required"] = new JsonArray(fields.Select(p => (JsonNode?)JsonValue.Create(p.Key)).ToArray());
            node["additionalProperties"] = false;
            foreach (var field in fields.ToArray())
            {
                var schema = Resolve(output, field.Value!);
                if (field.Key == "mode") schema["enum"] = JsonSerializer.SerializeToNode(new[] { "as_of", "period_change" });
                if (field.Key is "returnedCount" or "nextOffset") schema["minimum"] = 0;
                if (field.Key == "limit") { schema["minimum"] = 1; schema["maximum"] = 2000; }
                if (field.Key == "offset") { schema["minimum"] = 0; schema["maximum"] = int.MaxValue; }
                if (field.Key is "balance" or "amount" or "totalAmount" or "totalExcl" or "vatAmount" or
                    "paidAmount" or "outstandingAmount" or "pricePerUnit" or "quantity" or "discountAmount" or "discountPercentage")
                    schema["pattern"] = @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$";
                if ((field.Key is "id" or "mutationId") && HasStringType(schema))
                    schema["pattern"] = @"^-?(?:0|[1-9][0-9]*)$";
            }
        });
        tool.OutputSchema = JsonSerializer.SerializeToElement(output);
    }

    private static JsonObject Resolve(JsonObject root, JsonNode node)
    {
        while (node["$ref"] is JsonValue reference)
        {
            var path = reference.GetValue<string>();
            if (!path.StartsWith("#/", StringComparison.Ordinal)) throw new InvalidOperationException("Unsupported generated schema reference.");
            node = root;
            foreach (var part in path[2..].Split('/')) node = node[part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)]!;
        }
        return node.AsObject();
    }

    private static bool HasStringType(JsonObject node) => node["type"]?.ToJsonString().Contains("\"string\"", StringComparison.Ordinal) == true;
    private static void Visit(JsonNode node, Action<JsonObject> action)
    {
        if (node is JsonObject obj)
        {
            action(obj);
            foreach (var value in obj.Select(p => p.Value).ToArray()) if (value is not null) Visit(value, action);
        }
        else if (node is JsonArray array)
            foreach (var value in array.ToArray()) if (value is not null) Visit(value, action);
    }

    private static string Description(string name) => name switch
    {
        "limit" => "Page size: default 50, between 1 and 2000. Full pages give only a candidate next offset.",
        "offset" => "Number of items to skip; nonnegative int32, default 0. No snapshot guarantee.",
        "date" => "Accounting date filter in YYYY-MM-DD; inclusive ordered ranges use value/to.",
        "id" => "Resource ID; entry search accepts an int64 integer filter, detail paths accept int32 IDs.",
        "type" => "Relation type string filter (B/P), or descriptive entry type as applicable.",
        "side" => "receivable maps to D; payable maps to C. Items use mutationId.",
        "mode" => "as_of returns balance at to; period_change returns movement over inclusive from/to.",
        "from" => "Required for period_change; absent/null for as_of. Must not exceed to.",
        "to" => "Required inclusive accounting date in YYYY-MM-DD.",
        "ledgerId" => "Optional int32 ledger ID; omit for all ledgers.",
        "costCenterId" => "Optional plain int32 cost-center ID.",
        "relationId" => "Optional plain int32 relation ID, not an operator filter.",
        _ => "Optional string filter: eq, not_eq, like, not_like. Raw % and ? are wildcards; do not URI-encode values.",
    };
}
