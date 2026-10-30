using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Xunit;

namespace EBoekhouden.Mcp.Tests;

public sealed class ToolExecutionTests
{
    public static IEnumerable<object[]> Calls()
    {
        yield return ["search_relations", """{"name":{"op":"like","value":"%Synthetic%"},"limit":1}""", "/v1/relation?limit=1&offset=0&name%5Blike%5D=%25Synthetic%25"];
        yield return ["get_relation", """{"id":17}""", "/v1/relation/17"];
        yield return ["search_invoices", """{"relationId":17,"date":{"op":"range","value":"2026-01-01","to":"2026-03-31"}}""", "/v1/invoice?limit=50&offset=0&relationId=17&date%5Brange%5D=2026-01-01%2C2026-03-31"];
        yield return ["get_invoice", """{"id":23}""", "/v1/invoice/23"];
        yield return ["search_entries", """{"type":"invoice_sent","id":{"op":"gte","value":10}}""", "/v1/mutation?limit=50&offset=0&type=2&id%5Bgte%5D=10"];
        yield return ["get_entry", """{"id":12}""", "/v1/mutation/12"];
        yield return ["list_outstanding_invoices", """{"side":"payable"}""", "/v1/mutation/invoice/outstanding?limit=50&offset=0&credDeb=C"];
        yield return ["search_ledger_accounts", """{"category":{"op":"not_eq","value":"BAL"}}""", "/v1/ledger?limit=50&offset=0&category%5Bnot_eq%5D=BAL"];
        yield return ["get_ledger_account", """{"id":7}""", "/v1/ledger/7"];
        yield return ["get_ledger_balances", """{"mode":"as_of","to":"2026-03-31","ledgerId":7}""", "/v1/ledger/7/balance?to=2026-03-31"];
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task ToolsInvokeOnlyTheirMappedRouteAndReturnStructuredPlusMatchingText(string name, string arguments, string path)
    {
        await using var harness = await ProtocolHarness.StartAsync();
        var result = await harness.Client.CallToolAsync(name, Args(arguments), cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEqual(true, result.IsError);
        var data = result.StructuredContent!.Value;
        Assert.Equal(JsonValueKind.Object, data.ValueKind);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.True(JsonElement.DeepEquals(data, JsonDocument.Parse(text.Text).RootElement));
        Assert.Equal(path, Assert.Single(harness.Requests));
        Assert.Equal(1, harness.Reads);
        Assert.Equal(1, harness.Sessions);
        switch (name)
        {
            case "search_relations":
                Assert.Equal(3, data.GetProperty("items")[0].EnumerateObject().Count());
                Assert.Equal(1, data.GetProperty("page").GetProperty("nextOffset").GetInt64());
                break;
            case "get_relation": Assert.Equal("Synthetic Relation", data.GetProperty("name").GetString()); break;
            case "search_invoices": Assert.Equal("121.00", data.GetProperty("items")[0].GetProperty("totalAmount").GetString()); break;
            case "get_invoice":
                Assert.Equal("2.1250", data.GetProperty("items")[0].GetProperty("quantity").GetString());
                Assert.Equal(JsonValueKind.Null, data.GetProperty("items")[1].GetProperty("quantity").ValueKind);
                Assert.Equal(JsonValueKind.Null, data.GetProperty("vatAmount").ValueKind);
                break;
            case "search_entries": Assert.Equal("12", data.GetProperty("items")[0].GetProperty("id").GetString()); break;
            case "get_entry": Assert.Equal("12", data.GetProperty("id").GetString()); break;
            case "list_outstanding_invoices": Assert.Equal("12", data.GetProperty("items")[0].GetProperty("mutationId").GetString()); break;
            case "search_ledger_accounts": Assert.Equal("FUTURE", data.GetProperty("items")[1].GetProperty("category").GetString()); break;
            case "get_ledger_account": Assert.Equal("NEWCAT", data.GetProperty("category").GetString()); break;
            case "get_ledger_balances":
                Assert.Equal("as_of", data.GetProperty("mode").GetString());
                Assert.Equal(JsonValueKind.Null, data.GetProperty("from").ValueKind);
                Assert.Equal("-12.3456", data.GetProperty("items")[0].GetProperty("balance").GetString());
                break;
        }
    }

    [Fact]
    public async Task PeriodBalancesUseUnpaginatedAllLedgerRouteAndEchoScope()
    {
        await using var harness = await ProtocolHarness.StartAsync();
        var result = await harness.Client.CallToolAsync("get_ledger_balances", Args("""{"mode":"period_change","from":"2026-01-01","to":"2026-03-31","costCenterId":9}"""), cancellationToken: TestContext.Current.CancellationToken);
        var data = result.StructuredContent!.Value;
        Assert.Equal("period_change", data.GetProperty("mode").GetString());
        Assert.Equal("2026-01-01", data.GetProperty("from").GetString());
        Assert.Equal(9, data.GetProperty("costCenterId").GetInt32());
        Assert.Equal(2, data.GetProperty("returnedCount").GetInt32());
        Assert.False(data.TryGetProperty("page", out _));
        Assert.Equal("/v1/ledger/balances?costCenterId=9&from=2026-01-01&to=2026-03-31", Assert.Single(harness.Requests));
    }

    [Theory]
    [InlineData("search_relations", "{\"sort\":\"private-query\"}")]
    [InlineData("search_relations", "{\"name\":{\"op\":\"gt\",\"value\":\"private-query\"}}")]
    [InlineData("search_relations", "{\"limit\":0}")]
    [InlineData("search_relations", "{\"limit\":2001}")]
    [InlineData("search_relations", "{\"offset\":-1}")]
    [InlineData("get_relation", "{}")]
    [InlineData("get_relation", "{\"id\":\"17\"}")]
    [InlineData("get_relation", "{\"Id\":17}")]
    [InlineData("get_invoice", "{\"id\":null}")]
    [InlineData("search_invoices", "{\"relationId\":{\"op\":\"eq\",\"value\":17}}")]
    [InlineData("search_invoices", "{\"date\":{\"op\":\"range\",\"value\":\"2026-03-31\",\"to\":\"2026-01-01\"}}")]
    [InlineData("get_entry", "{\"id\":2147483648}")]
    [InlineData("search_entries", "{\"type\":\"unknown\"}")]
    [InlineData("search_entries", "{\"id\":{\"op\":\"range\",\"value\":1}}")]
    [InlineData("search_entries", "{\"id\":{\"op\":\"eq\",\"value\":1,\"to\":2}}")]
    [InlineData("list_outstanding_invoices", "{\"side\":\"D\"}")]
    [InlineData("list_outstanding_invoices", "{}")]
    [InlineData("search_ledger_accounts", "{\"code\":{\"op\":\"eq\",\"value\":\"more-than-ten\"}}")]
    [InlineData("get_ledger_account", "{}")]
    [InlineData("get_ledger_balances", "{\"mode\":\"as_of\",\"to\":\"2026-01-01\",\"from\":\"2026-01-01\"}")]
    [InlineData("get_ledger_balances", "{\"mode\":\"period_change\",\"to\":\"2026-01-01\"}")]
    [InlineData("get_ledger_balances", "{\"mode\":\"as_of\",\"to\":\"2026-02-30\"}")]
    [InlineData("get_ledger_balances", "{\"mode\":\"as_of\",\"to\":\"2026-01-01\",\"limit\":50}")]
    public async Task InvalidArgumentsAreToolErrorsAndCauseZeroUpstreamTraffic(string name, string arguments)
    {
        await using var harness = await ProtocolHarness.StartAsync();
        var result = await harness.Client.CallToolAsync(name, Args(arguments), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("invalid_arguments", JsonDocument.Parse(text).RootElement.GetProperty("kind").GetString());
        Assert.DoesNotContain("private-query", text);
        Assert.Equal(0, harness.Reads);
        Assert.Equal(0, harness.Sessions);
    }

    [Fact]
    public async Task GenuineEmptyPagesRemainSuccessfulWithoutCandidateNextOffset()
    {
        await using var harness = await ProtocolHarness.StartAsync((_, _) => Task.FromResult(ProtocolHarness.Json("""{"items":[],"count":0}""")));
        var result = await harness.Client.CallToolAsync("search_relations", Args("{}"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEqual(true, result.IsError);
        var data = result.StructuredContent!.Value;
        Assert.Empty(data.GetProperty("items").EnumerateArray());
        Assert.Equal(0, data.GetProperty("page").GetProperty("returnedCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("page").GetProperty("nextOffset").ValueKind);
    }

    [Fact]
    public async Task UpstreamFailuresAreSafeToolErrorsRatherThanEmptyPages()
    {
        await using var harness = await ProtocolHarness.StartAsync((_, _) => Task.FromResult(ProtocolHarness.Json(
            """{"code":"EXAMPLE_001","traceId":"TRACE-123","message":"synthetic-secret financial payload"}""", HttpStatusCode.Forbidden)));
        var result = await harness.Client.CallToolAsync("search_relations", Args("{}"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        var error = JsonDocument.Parse(text).RootElement;
        Assert.Equal(403, error.GetProperty("status").GetInt32());
        Assert.Equal("EXAMPLE_001", error.GetProperty("code").GetString());
        Assert.Equal("TRACE-123", error.GetProperty("traceId").GetString());
        Assert.DoesNotContain("synthetic-secret", text);
        Assert.False(error.TryGetProperty("items", out _));
    }

    [Theory]
    [InlineData("{\"id\":17,\"id\":18}")]
    [InlineData("{\"id\":17,\"extra\":null}")]
    [InlineData("null")]
    public async Task RawArgumentsCannotBypassStrictDeserialization(string arguments)
    {
        await using var harness = await ProtocolHarness.StartAsync();
        var response = await harness.Client.SendRequestAsync(new JsonRpcRequest
        {
            Method = "tools/call", Params = JsonNode.Parse("{\"name\":\"get_relation\",\"arguments\":" + arguments + "}"),
        }, TestContext.Current.CancellationToken);
        Assert.True(response.Result!["isError"]!.GetValue<bool>());
        Assert.Equal(0, harness.Reads);
        Assert.Equal(0, harness.Sessions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedResponsesAndUnexpectedFailuresNeverReturnSuccessOrPrivateDetails(bool unexpected)
    {
        await using var harness = await ProtocolHarness.StartAsync((_, _) => unexpected
            ? throw new InvalidOperationException("synthetic-secret")
            : Task.FromResult(ProtocolHarness.Json("""{"items":[],"count":3,"private":"synthetic-secret"}""")));
        var result = await harness.Client.CallToolAsync("search_relations", Args("{}"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.DoesNotContain("synthetic-secret", text);
        Assert.DoesNotContain("items", text);
    }

    [Fact]
    public async Task CancellationReachesTheUpstreamHandler()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await ProtocolHarness.StartAsync(async (_, token) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { if (token.IsCancellationRequested) canceled.SetResult(); }
            throw new InvalidOperationException("Unreachable");
        });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var call = harness.Client.SendRequestAsync(new JsonRpcRequest { Id = new RequestId(123), Method = "tools/call", Params = JsonNode.Parse("""{"name":"search_relations","arguments":{}}""") }, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await harness.Client.SendNotificationAsync("notifications/cancelled", new CancelledNotificationParams { RequestId = new RequestId(123) }, cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, harness.Reads);
    }

    internal static Dictionary<string, object?> Args(string json) => JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
}
