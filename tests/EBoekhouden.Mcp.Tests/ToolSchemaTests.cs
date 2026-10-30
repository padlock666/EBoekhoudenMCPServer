using System.Text.Json;
using Xunit;

namespace EBoekhouden.Mcp.Tests;

public sealed class ToolSchemaTests
{
    [Fact]
    public async Task ActualWireSchemasMatchReviewedSnapshotAndOnlyTenApprovedTools()
    {
        await using var harness = await ProtocolHarness.StartAsync();
        var tools = await harness.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var expectedNames = ToolExecutionTests.Calls().Select(c => (string)c[0]).Order().ToArray();
        Assert.Equal(expectedNames, tools.Select(t => t.Name).Order());
        using var stream = typeof(ToolSchemaTests).Assembly.GetManifestResourceStream("EBoekhouden.Mcp.Tests.tool-schemas.json")!;
        using var snapshot = await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        foreach (var tool in tools)
        {
            var actual = JsonSerializer.SerializeToElement(tool.ProtocolTool);
            var expected = snapshot.RootElement.EnumerateArray().Single(t => t.GetProperty("name").GetString() == tool.Name);
            Assert.True(JsonElement.DeepEquals(expected, actual), $"Reviewed schema differs for {tool.Name}.");
            var annotations = tool.ProtocolTool.Annotations!;
            Assert.True(annotations.ReadOnlyHint);
            Assert.False(annotations.DestructiveHint);
            Assert.True(annotations.IdempotentHint);
            Assert.True(annotations.OpenWorldHint);
            var input = tool.JsonSchema;
            Assert.Equal("object", input.GetProperty("type").GetString());
            Assert.False(input.GetProperty("additionalProperties").GetBoolean());
            Assert.False(input.GetProperty("properties").TryGetProperty("request", out _));
            Assert.False(input.GetProperty("properties").TryGetProperty("cancellationToken", out _));
            Assert.Equal("object", tool.ReturnJsonSchema!.Value.GetProperty("type").GetString());
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        }
        Assert.Equal(0, harness.Sessions);
        Assert.Equal(0, harness.Reads);
    }

    [Fact]
    public async Task PublishedSchemaExpressesLimitsRequiredValuesEnumsDatesAndDecimalStrings()
    {
        await using var harness = await ProtocolHarness.StartAsync();
        var tools = await harness.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        JsonElement Input(string name) => tools.Single(t => t.Name == name).JsonSchema;
        var search = Input("search_relations").GetProperty("properties");
        Assert.Equal(50, search.GetProperty("limit").GetProperty("default").GetInt32());
        Assert.Equal(2000, search.GetProperty("limit").GetProperty("maximum").GetInt32());
        Assert.Equal(0, search.GetProperty("offset").GetProperty("minimum").GetInt32());
        Assert.Equal(new[] { "eq", "not_eq", "like", "not_like" }, search.GetProperty("name").GetProperty("properties").GetProperty("op").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("id", Assert.Single(Input("get_entry").GetProperty("required").EnumerateArray()).GetString());
        Assert.Equal(int.MaxValue, Input("get_entry").GetProperty("properties").GetProperty("id").GetProperty("maximum").GetInt32());
        var balances = Input("get_ledger_balances");
        Assert.Equal(new[] { "mode", "to" }, balances.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("date", balances.GetProperty("properties").GetProperty("to").GetProperty("format").GetString());
        Assert.Equal("period_change", balances.GetProperty("allOf")[0].GetProperty("if").GetProperty("properties").GetProperty("mode").GetProperty("const").GetString());
        var money = tools.Single(t => t.Name == "get_invoice").ReturnJsonSchema!.Value.GetProperty("properties").GetProperty("totalAmount");
        Assert.Contains("string", money.GetProperty("type").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("number", money.GetProperty("type").EnumerateArray().Select(e => e.GetString()));
        Assert.True(money.TryGetProperty("pattern", out _));
    }
}
