using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Xunit;

namespace EBoekhouden.IntegrationTests;

public sealed class ExecutableTests
{
    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2026-07-28")]
    public async Task LegacyInitializationAndModernDiscoveryCanBothInvokeTools(string protocolVersion)
    {
        await using var api = await FakeUpstream.StartAsync();
        await using var host = StdioProcess.Start(api.Origin);
        await host.ConnectAsync(protocolVersion);
        Assert.Equal(10, (await host.Client.ListToolsAsync(cancellationToken: host.Token)).Count);
        ExecutableTests.Success(await host.CallAsync("get_relation", """{"id":17}"""));
        Assert.Equal(1, api.Sessions);
        Assert.Equal(1, api.Reads);
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [Fact]
    public async Task InitializationSchemasAndAllElevenReadRoutesWorkOverActualStdio()
    {
        await using var api = await FakeUpstream.StartAsync();
        await using var host = StdioProcess.Start(api.Origin);
        await host.ConnectAsync();
        Assert.Equal("EBoekhoudenMcp", host.Client.ServerInfo!.Name);
        Assert.NotNull(host.Client.ServerCapabilities.Tools);
        Assert.Contains("\"method\":\"initialize\"", host.Sent);
        Assert.Contains("notifications/initialized", host.Sent);
        var tools = await host.Client.ListToolsAsync(cancellationToken: host.Token);
        using var snapshotStream = typeof(ExecutableTests).Assembly.GetManifestResourceStream("EBoekhouden.IntegrationTests.tool-schemas.json")!;
        using var snapshot = await JsonDocument.ParseAsync(snapshotStream, cancellationToken: host.Token);
        Assert.Equal(10, tools.Count);
        Assert.Equal(snapshot.RootElement.EnumerateArray().Select(t => t.GetProperty("name").GetString()).Order(), tools.Select(t => t.Name).Order());
        foreach (var tool in tools)
        {
            var expected = snapshot.RootElement.EnumerateArray().Single(t => t.GetProperty("name").GetString() == tool.Name);
            Assert.True(JsonElement.DeepEquals(expected, JsonSerializer.SerializeToElement(tool.ProtocolTool)), tool.Name);
        }
        Assert.Empty(api.Requests); // Initialization and discovery do not authenticate.
        (string Tool, string Args, string Target)[] calls =
        [
            ("search_relations", """{"name":{"op":"like","value":"%synthetic-private-financial-data%"}}""", "/v1/relation?limit=50&offset=0&name%5Blike%5D=%25synthetic-private-financial-data%25"),
            ("get_relation", """{"id":17}""", "/v1/relation/17"),
            ("search_invoices", """{"relationId":17,"date":{"op":"range","value":"2026-01-01","to":"2026-03-31"}}""", "/v1/invoice?limit=50&offset=0&relationId=17&date%5Brange%5D=2026-01-01%2C2026-03-31"),
            ("get_invoice", """{"id":23}""", "/v1/invoice/23"),
            ("search_entries", """{"type":"invoice_sent","id":{"op":"gte","value":10}}""", "/v1/mutation?limit=50&offset=0&type=2&id%5Bgte%5D=10"),
            ("get_entry", """{"id":12}""", "/v1/mutation/12"),
            ("list_outstanding_invoices", """{"side":"receivable"}""", "/v1/mutation/invoice/outstanding?limit=50&offset=0&credDeb=D"),
            ("search_ledger_accounts", """{"category":{"op":"not_eq","value":"BAL"}}""", "/v1/ledger?limit=50&offset=0&category%5Bnot_eq%5D=BAL"),
            ("get_ledger_account", """{"id":7}""", "/v1/ledger/7"),
            ("get_ledger_balances", """{"mode":"as_of","to":"2026-03-31","ledgerId":7}""", "/v1/ledger/7/balance?to=2026-03-31"),
            ("get_ledger_balances", """{"mode":"period_change","from":"2026-01-01","to":"2026-03-31","costCenterId":9}""", "/v1/ledger/balances?costCenterId=9&from=2026-01-01&to=2026-03-31"),
        ];
        foreach (var (name, args, _) in calls)
        {
            var data = Success(await host.CallAsync(name, args));
            switch (name)
            {
                case "search_relations": Assert.Equal(3, data.GetProperty("items")[0].EnumerateObject().Count()); break;
                case "get_invoice":
                    Assert.Equal("121.00", data.GetProperty("totalAmount").GetString());
                    Assert.Equal("2.1250", data.GetProperty("items")[0].GetProperty("quantity").GetString());
                    Assert.Equal(JsonValueKind.Null, data.GetProperty("items")[1].GetProperty("quantity").ValueKind);
                    Assert.False(data.TryGetProperty("paymentStatus", out _));
                    break;
                case "get_entry": Assert.Equal("12", data.GetProperty("id").GetString()); break;
                case "list_outstanding_invoices": Assert.Equal("12", data.GetProperty("items")[0].GetProperty("mutationId").GetString()); break;
                case "search_ledger_accounts": Assert.Equal("FUTURE", data.GetProperty("items")[1].GetProperty("category").GetString()); break;
                case "get_ledger_balances": Assert.False(data.TryGetProperty("page", out _)); break;
            }
        }
        Assert.Equal(calls.Select(c => c.Target), api.Requests.Where(r => r.Method == "GET").Select(r => r.Target));
        Assert.Equal(1, api.Sessions);
        await host.CloseInputAndWaitAsync();
        host.AssertCleanOutput();
        Assert.DoesNotContain("Synthetic Relation", host.Stderr);
        Assert.False(host.Stdout.Contains(FakeUpstream.Credential, StringComparison.Ordinal));
        Assert.False(host.Stdout.Contains(FakeUpstream.SessionPrefix, StringComparison.Ordinal));
        api.AssertHealthy();
    }

    [Fact]
    public async Task InvalidInputsIncludingRawDuplicatesCauseZeroHttpRequests()
    {
        await using var api = await FakeUpstream.StartAsync();
        await using var host = StdioProcess.Start(api.Origin);
        await host.ConnectAsync();
        (string Tool, string Args)[] invalid =
        [
            ("search_relations", """{"sort":"synthetic-private-financial-data"}"""),
            ("search_relations", """{"name":{"op":"gt","value":"synthetic-private-financial-data"}}"""),
            ("search_relations", """{"limit":2001}"""), ("search_relations", """{"offset":-1}"""),
            ("get_relation", """{"Id":17}"""), ("get_invoice", """{"id":null}"""),
            ("get_entry", """{"id":2147483648}"""), ("search_entries", """{"type":"2"}"""),
            ("search_invoices", """{"date":{"op":"range","value":"2026-03-31","to":"2026-01-01"}}"""),
            ("list_outstanding_invoices", "{}"),
            ("search_ledger_accounts", """{"code":{"op":"eq","value":"too-long-code"}}"""),
            ("get_ledger_balances", """{"mode":"as_of","from":"2026-01-01","to":"2026-03-31"}"""),
            ("get_ledger_balances", """{"mode":"period_change","to":"2026-03-31"}"""),
            ("get_ledger_balances", """{"mode":"as_of","to":"2026-02-30"}"""),
        ];
        foreach (var (name, args) in invalid) Assert.Equal("invalid_arguments", Failure(await host.CallAsync(name, args)).GetProperty("kind").GetString());
        var duplicate = await host.Client.SendRequestAsync(new JsonRpcRequest
        {
            Method = "tools/call", Params = JsonNode.Parse("""{"name":"get_relation","arguments":{"id":17,"id":18}}"""),
        }, host.Token);
        Assert.True(duplicate.Result!["isError"]!.GetValue<bool>());
        Assert.Empty(api.Requests);
        await host.CloseInputAndWaitAsync();
        host.AssertCleanOutput();
        api.AssertHealthy();
    }

    [Theory]
    [InlineData("missing-credential")]
    [InlineData("bad-source")]
    [InlineData("bad-binding")]
    [InlineData("production-override")]
    [InlineData("missing-origin")]
    [InlineData("remote-origin")]
    [InlineData("hostname-origin")]
    [InlineData("path-origin")]
    [InlineData("userinfo-origin")]
    [InlineData("query-origin")]
    [InlineData("fragment-origin")]
    public async Task StartupValidationAndOriginGateFailClosedWithoutHttp(string scenario)
    {
        await using var api = await FakeUpstream.StartAsync();
        var environment = new Dictionary<string, string?>();
        switch (scenario)
        {
            case "missing-credential": environment["EBoekhouden__AccessToken"] = ""; break;
            case "bad-source": environment["EBoekhouden__Source"] = FakeUpstream.PrivateData; break;
            case "bad-binding": environment["EBoekhouden__MaxReadAttempts"] = FakeUpstream.PrivateData; break;
            case "production-override": environment["DOTNET_ENVIRONMENT"] = "Production"; break;
            case "missing-origin": environment["EBoekhoudenTest__UpstreamOrigin"] = null; break;
            case "remote-origin": environment["EBoekhoudenTest__UpstreamOrigin"] = "https://example.invalid/"; break;
            case "hostname-origin": environment["EBoekhoudenTest__UpstreamOrigin"] = "http://localhost:12345/"; break;
            case "path-origin": environment["EBoekhoudenTest__UpstreamOrigin"] = api.Origin.AbsoluteUri + "v1/"; break;
            case "userinfo-origin": environment["EBoekhoudenTest__UpstreamOrigin"] = $"http://user@127.0.0.1:{api.Origin.Port}/"; break;
            case "query-origin": environment["EBoekhoudenTest__UpstreamOrigin"] = api.Origin.AbsoluteUri + "?override=1"; break;
            case "fragment-origin": environment["EBoekhoudenTest__UpstreamOrigin"] = api.Origin.AbsoluteUri + "#override"; break;
        }
        await using var host = StdioProcess.Start(api.Origin, environment);
        await host.WaitForExitAsync();
        Assert.Equal(1, host.ExitCode);
        Assert.Equal("", host.Stdout);
        Assert.Equal("The e-Boekhouden MCP host could not start or continue. Check external configuration and transport setup.", host.Stderr.Trim());
        StdioProcess.AssertSafe(host.Stderr);
        Assert.Empty(api.Requests);
        api.AssertHealthy();
    }

    [Theory]
    [InlineData("session-rejected", 403, 1, 0)]
    [InlineData("session-transient", 503, 1, 0)]
    [InlineData("session-invalid", 200, 1, 0)]
    [InlineData("forbidden", 403, 1, 1)]
    [InlineData("unauthorized", 401, 2, 2)]
    [InlineData("validation", 400, 1, 1)]
    [InlineData("malformed", 200, 1, 1)]
    public async Task AuthenticationAndResponseFailuresAreBoundedSafeToolErrors(string scenario, int status, int sessions, int reads)
    {
        await using var api = await FakeUpstream.StartAsync();
        var body = JsonSerializer.Serialize(new { code = "SYNTHETIC_001", traceId = "TRACE-123", message = FakeUpstream.Credential + FakeUpstream.PrivateData + FakeUpstream.SessionPrefix });
        api.Respond = (context, number, _) => Task.FromResult(number > 0
            ? scenario switch
            {
                "session-rejected" => new FakeUpstream.Reply(403, body),
                "session-transient" => new FakeUpstream.Reply(503, body),
                "session-invalid" => new FakeUpstream.Reply(200, """{"token":"synthetic-session-secret-1","expiresIn":0}"""),
                _ => FakeUpstream.Session(number),
            }
            : new FakeUpstream.Reply(status, scenario == "malformed" ? "not-json " + FakeUpstream.PrivateData : body));
        await using var host = StdioProcess.Start(api.Origin);
        await host.ConnectAsync();
        var error = Failure(await host.CallAsync("search_relations"));
        Assert.Equal("upstream_failure", error.GetProperty("kind").GetString());
        Assert.Equal(status, error.GetProperty("status").GetInt32());
        if (scenario is not ("session-invalid" or "malformed"))
        {
            Assert.Equal("SYNTHETIC_001", error.GetProperty("code").GetString());
            Assert.Equal("TRACE-123", error.GetProperty("traceId").GetString());
        }
        Assert.Equal(sessions, api.Sessions);
        Assert.Equal(reads, api.Reads);
        await host.CloseInputAndWaitAsync();
        host.AssertCleanOutput();
        StdioProcess.AssertSafe(host.Stdout);
        api.AssertHealthy();
    }

    [Fact]
    public async Task PaginationReturnsCandidateOffsetsAndDoesNotInventTotalsOrTraverse()
    {
        await using var api = await FakeUpstream.StartAsync();
        api.Respond = (context, number, _) => Task.FromResult(number > 0 ? FakeUpstream.Session(number)
            : context.Request.Query["offset"] == "1" ? new FakeUpstream.Reply(200, """{"items":[],"count":0}""")
            : FakeUpstream.DefaultReply(context.Request, number));
        await using var host = StdioProcess.Start(api.Origin);
        await host.ConnectAsync();
        var first = Success(await host.CallAsync("search_relations", """{"limit":1}"""));
        var page = first.GetProperty("page");
        Assert.Equal(1, page.GetProperty("returnedCount").GetInt32());
        Assert.Equal(1, page.GetProperty("nextOffset").GetInt64());
        Assert.False(page.TryGetProperty("total", out _));
        Assert.Equal(1, api.Reads);
        var last = Success(await host.CallAsync("search_relations", """{"limit":1,"offset":1}"""));
        Assert.Empty(last.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, last.GetProperty("page").GetProperty("nextOffset").ValueKind);
        Assert.Equal(new[] { "/v1/relation?limit=1&offset=0", "/v1/relation?limit=1&offset=1" }, api.Requests.Where(r => r.Method == "GET").Select(r => r.Target));
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BalanceModesKeepRequestedDatesAndUpstreamValuesWithoutAggregation(bool singleLedger)
    {
        await using var api = await FakeUpstream.StartAsync();
        await using var host = StdioProcess.Start(api.Origin);
        await host.ConnectAsync();
        foreach (var mode in new[] { "as_of", "period_change" })
        {
            var args = new JsonObject { ["mode"] = mode, ["to"] = "2026-03-31", ["costCenterId"] = 9 };
            if (mode == "period_change") args["from"] = "2026-01-01";
            if (singleLedger) args["ledgerId"] = 7;
            var data = Success(await host.CallAsync("get_ledger_balances", args.ToJsonString()));
            Assert.Equal(mode, data.GetProperty("mode").GetString());
            Assert.Equal("2026-03-31", data.GetProperty("to").GetString());
            Assert.Equal(mode == "as_of" ? null : "2026-01-01", data.GetProperty("from").GetString());
            Assert.Equal(9, data.GetProperty("costCenterId").GetInt32());
            Assert.Equal(singleLedger ? 1 : 2, data.GetProperty("returnedCount").GetInt32());
            Assert.Equal(singleLedger ? "-12.3456" : "500.12345678", data.GetProperty("items")[0].GetProperty("balance").GetString());
            Assert.False(data.TryGetProperty("total", out _));
            var path = singleLedger ? "/v1/ledger/7/balance" : "/v1/ledger/balances";
            var query = mode == "as_of" ? "?costCenterId=9&to=2026-03-31" : "?costCenterId=9&from=2026-01-01&to=2026-03-31";
            Assert.Equal(path + query, api.Requests.Last().Target);
        }
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [Fact]
    public async Task RedirectIsNotFollowedEvenToAnotherLoopbackServer()
    {
        await using var destination = await FakeUpstream.StartAsync();
        await using var api = await FakeUpstream.StartAsync();
        api.Respond = (_, number, _) => Task.FromResult(number > 0 ? FakeUpstream.Session(number) : new FakeUpstream.Reply(302, "{}", destination.Origin.AbsoluteUri + "v1/relation"));
        await using var host = StdioProcess.Start(api.Origin);
        await host.ConnectAsync();
        Assert.Equal(302, Failure(await host.CallAsync("search_relations")).GetProperty("status").GetInt32());
        Assert.Empty(destination.Requests);
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy(); destination.AssertHealthy();
    }

    [Fact]
    public async Task DisposingAfterAProtocolFailureReapsChildAndReleasesPort()
    {
        var api = await FakeUpstream.StartAsync();
        var origin = api.Origin;
        var host = StdioProcess.Start(origin);
        var id = host.Id;
        try
        {
            await host.ConnectAsync();
            await Assert.ThrowsAnyAsync<Exception>(async () => await host.CallAsync("not_an_approved_tool"));
        }
        finally { try { await host.DisposeAsync(); } finally { await api.DisposeAsync(); } }
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(id));
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, origin.Port);
        listener.Start(); // Same port is immediately reusable after Kestrel cleanup.
    }

    internal static JsonElement Success(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        var structured = result.StructuredContent!.Value;
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var parsed = JsonDocument.Parse(text);
        Assert.True(JsonElement.DeepEquals(structured, parsed.RootElement));
        return structured;
    }

    internal static JsonElement Failure(CallToolResult result)
    {
        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        StdioProcess.AssertSafe(text);
        using var parsed = JsonDocument.Parse(text);
        Assert.False(parsed.RootElement.TryGetProperty("items", out _));
        return parsed.RootElement.Clone();
    }
}
