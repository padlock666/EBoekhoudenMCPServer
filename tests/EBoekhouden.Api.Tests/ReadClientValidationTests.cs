using System.Net;
using EBoekhouden.Api.Errors;
using EBoekhouden.Api.Filtering;
using EBoekhouden.Api.Http;
using Xunit;

namespace EBoekhouden.Api.Tests;

public sealed class ReadClientValidationTests
{
    public static IEnumerable<object[]> InvalidRoots()
    {
        foreach (var endpoint in new[] { "relations", "relation", "invoices", "invoice", "mutations", "mutation", "outstanding", "ledgers", "ledger", "balances", "balance" })
            foreach (var body in new[] { "{}", "null", "{invalid json", "[]", "" })
                yield return [endpoint, body];
    }

    [Theory]
    [MemberData(nameof(InvalidRoots))]
    public async Task EveryEndpointRejectsMissingMalformedOrWrongShapeResponsesAndDisposesContent(string endpoint, string body)
    {
        var content = ClientTestSupport.Json(body);
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var error = await Assert.ThrowsAsync<ApiException>(() => ClientTestSupport.Call(harness.Client(), endpoint, TestContext.Current.CancellationToken));
        Assert.Equal(ApiErrorKind.InvalidResponse, error.Kind);
        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
        Assert.Null(error.InnerException);
        Assert.Null(error.Code);
        Assert.Null(error.TraceId);
        Assert.Equal("e-Boekhouden returned an invalid response.", error.Message);
        Assert.True(content.Disposed);
        Assert.Equal(1, harness.Reads.Calls); // Invalid JSON is not a new HTTP retry category.
        Assert.Empty(harness.Logs.Messages);
    }

    [Theory]
    [InlineData("relations", "{\"items\":[{}],\"count\":1}")]
    [InlineData("relations", "{\"items\":[null],\"count\":1}")]
    [InlineData("relations", "{\"items\":null,\"count\":0}")]
    [InlineData("relations", "{\"items\":[],\"count\":-1}")]
    [InlineData("relations", "{\"items\":[],\"count\":1}")]
    [InlineData("relation", "{\"id\":17,\"Id\":18}")]
    [InlineData("relation", "{\"id\":17,\"id\":18}")]
    [InlineData("invoice", "{\"id\":23}")]
    [InlineData("invoice", "{\"id\":23,\"date\":\"2026-02-30\"}")]
    [InlineData("invoice", "{\"id\":23,\"date\":\"2026-01-01\",\"totalAmount\":\"123.45\"}")]
    [InlineData("invoice", "{\"id\":23,\"date\":\"2026-01-01\",\"items\":[null]}")]
    [InlineData("invoice", "{\"id\":23,\"date\":\"2026-01-01\",\"items\":[{\"ledgerId\":7,\"vatCode\":\"X\"}]}")]
    [InlineData("invoice", "{\"id\":23,\"date\":\"2026-01-01\",\"items\":[{\"ledgerId\":7,\"description\":\"Synthetic\",\"vatCode\":null}]}")]
    [InlineData("mutation", "{\"id\":12}")]
    [InlineData("mutation", "{\"id\":12,\"date\":\"2026-01-01\",\"rows\":[null]}")]
    [InlineData("mutation", "{\"id\":12,\"date\":\"2026-01-01\",\"vat\":[null]}")]
    [InlineData("outstanding", "{\"items\":[{\"date\":\"2026-01-01\"}],\"count\":1}")]
    [InlineData("outstanding", "{\"items\":[{\"mutationId\":12}],\"count\":1}")]
    [InlineData("balance", "{\"code\":\"1000\",\"type\":\"DEB\"}")]
    [InlineData("balance", "{\"code\":null,\"type\":\"DEB\",\"balance\":0}")]
    [InlineData("balance", "{\"code\":\"1000\",\"type\":\"DEB\",\"balance\":1e100}")]
    [InlineData("balances", "{\"items\":[{\"code\":\"1000\",\"type\":\"DEB\"}],\"count\":1}")]
    public async Task InvalidEssentialFieldsCountsAndNestedElementsFailAtTheApiBoundary(string endpoint, string body)
    {
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(ClientTestSupport.Response(body)));
        var error = await Assert.ThrowsAsync<ApiException>(() => ClientTestSupport.Call(harness.Client(), endpoint, TestContext.Current.CancellationToken));
        Assert.Equal(ApiErrorKind.InvalidResponse, error.Kind);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("relations")]
    [InlineData("invoices")]
    [InlineData("mutations")]
    [InlineData("outstanding")]
    [InlineData("ledgers")]
    [InlineData("balances")]
    public async Task EmptyListsAreSuccessfulAndDisposalStillOccurs(string endpoint)
    {
        var content = ClientTestSupport.Json("""{"items":[],"count":0}""");
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        await ClientTestSupport.Call(harness.Client(), endpoint, TestContext.Current.CancellationToken);
        Assert.True(content.Disposed);
        Assert.Equal(1, harness.Reads.Calls);
    }

    [Fact]
    public async Task OptionalDetailFieldsRemainNullAndExplicitEmptyArraysRemainEmpty()
    {
        var body = """{"id":23,"date":"0001-01-01"}""";
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(ClientTestSupport.Response(body)));
        var minimal = await harness.Client().GetInvoiceAsync(23, TestContext.Current.CancellationToken);
        Assert.Null(minimal.TotalAmount);
        Assert.Null(minimal.TotalExcl);
        Assert.Null(minimal.VatAmount);
        Assert.Null(minimal.Items);
        Assert.Equal(DateOnly.MinValue, minimal.Date); // Explicit spec date is not replaced by today's date.
        body = """{"id":23,"date":"2026-01-01","items":[]}""";
        Assert.Empty((await harness.Client().GetInvoiceAsync(23, TestContext.Current.CancellationToken)).Items!);
        body = """{"id":17}""";
        var relation = await harness.Client().GetRelationAsync(17, TestContext.Current.CancellationToken);
        Assert.Null(relation.Inactive);
        Assert.Null(relation.Mandate);
        Assert.Null(relation.Date);
        body = """{"id":12,"date":"2026-01-01"}""";
        var mutation = await harness.Client().GetMutationAsync(12, TestContext.Current.CancellationToken);
        Assert.Null(mutation.Rows);
        Assert.Null(mutation.Vat);
        body = """{"id":7}""";
        Assert.Null((await harness.Client().GetLedgerAsync(7, TestContext.Current.CancellationToken)).Category);
        Assert.Equal(1, harness.Sessions.Calls); // All methods reuse the same provider.
    }

    [Fact]
    public async Task WideMutationIdentitiesArePreservedInAllThreeResources()
    {
        var body = """{"items":[{"id":9223372036854775807,"date":"2026-01-01"}],"count":1}""";
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(ClientTestSupport.Response(body)));
        var list = await harness.Client().GetMutationsAsync(new(new(), Id: new("eq", long.MaxValue)), TestContext.Current.CancellationToken);
        Assert.Equal(long.MaxValue, Assert.Single(list.Items).Id);
        body = """{"id":2147483648,"date":"2026-01-01"}""";
        Assert.Equal(2147483648L, (await harness.Client().GetMutationAsync(12, TestContext.Current.CancellationToken)).Id);
        body = """{"items":[{"mutationId":9223372036854775807,"date":"2026-01-01"}],"count":1}""";
        var outstanding = await harness.Client().GetOutstandingInvoicesAsync(new(new(), "D"), TestContext.Current.CancellationToken);
        Assert.Equal(long.MaxValue, Assert.Single(outstanding.Items).MutationId);
        // The int32 detail path and int64 response disagreement is preserved, not silently narrowed.
    }

    [Fact]
    public async Task PaginatedResponsesCannotExceedLimitButAllBalancesHaveNoPaginationLimit()
    {
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(ClientTestSupport.Response(ClientTestSupport.Fixture("ledgers-list"))));
        var error = await Assert.ThrowsAsync<ApiException>(() => harness.Client().GetLedgersAsync(new(new(1)), TestContext.Current.CancellationToken));
        Assert.Equal(ApiErrorKind.InvalidResponse, error.Kind);
        // All balances still validate the envelope, but have no 2000-item pagination contract.
        var items = string.Join(',', Enumerable.Repeat("""{"code":"SYN","type":"DEB","balance":0}""", 2001));
        using var all = new HttpHarness(read: (_, _) => Task.FromResult(ClientTestSupport.Response("{\"items\":[" + items + "],\"count\":2001}")));
        Assert.Equal(2001, (await all.Client().GetLedgerBalancesAsync(new("as_of", new(2026, 1, 1)), TestContext.Current.CancellationToken)).Count);
    }

    [Theory]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(206)]
    public async Task OnlyTheSpecified200SuccessShapeIsAccepted(int status)
    {
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(ClientTestSupport.Response("""{"id":17}""", (HttpStatusCode)status)));
        var error = await Assert.ThrowsAsync<ApiException>(() => harness.Client().GetRelationAsync(17, TestContext.Current.CancellationToken));
        Assert.Equal(ApiErrorKind.InvalidResponse, error.Kind);
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
    }

    [Fact]
    public void InvalidQueryShapesFailBeforeSessionAcquisition()
    {
        using var harness = new HttpHarness();
        var client = harness.Client();
        Assert.Throws<ArgumentException>(() => { _ = client.GetLedgerBalancesAsync(new("as_of", new(2026, 1, 1), ledgerId: 7), TestContext.Current.CancellationToken); });
        Assert.Throws<ArgumentException>(() => { _ = client.GetLedgerBalanceAsync(new("as_of", new(2026, 1, 1)), TestContext.Current.CancellationToken); });
        Assert.Throws<ArgumentNullException>(() => { _ = client.GetRelationsAsync(null!, TestContext.Current.CancellationToken); });
        Assert.Throws<ArgumentNullException>(() => { _ = client.GetRelationsAsync(new(null!), TestContext.Current.CancellationToken); });
        Assert.Throws<ArgumentException>(() => { _ = client.GetOutstandingInvoicesAsync(new(new(), "X"), TestContext.Current.CancellationToken); });
        Assert.Throws<ArgumentException>(() => { _ = client.GetLedgersAsync(new(new(), Code: new("eq", "over-ten-characters")), TestContext.Current.CancellationToken); });
        Assert.Equal(0, harness.Sessions.Calls);
        Assert.Equal(0, harness.Reads.Calls);
    }

    [Theory]
    [InlineData("relations")]
    [InlineData("relation")]
    [InlineData("invoices")]
    [InlineData("invoice")]
    [InlineData("mutations")]
    [InlineData("mutation")]
    [InlineData("outstanding")]
    [InlineData("ledgers")]
    [InlineData("ledger")]
    [InlineData("balances")]
    [InlineData("balance")]
    public async Task AlreadyCanceledCallsNeverAcquireSessionsOrSendReads(string endpoint)
    {
        using var harness = new HttpHarness();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClientTestSupport.Call(harness.Client(), endpoint, canceled.Token));
        Assert.Equal(canceled.Token, error.CancellationToken);
        Assert.Equal(0, harness.Sessions.Calls);
        Assert.Equal(0, harness.Reads.Calls);
    }

    [Fact]
    public async Task CallerCancellationAndOperationDeadlinesRemainDistinctFromUpstreamErrors()
    {
        using var harness = new HttpHarness(read: async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        });
        using var cancellation = new CancellationTokenSource();
        var task = harness.Client().GetRelationAsync(17, cancellation.Token);
        cancellation.Cancel();
        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Null(canceled.InnerException);

        using var timeout = new HttpHarness(read: async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        }, configure: o => { o.AttemptTimeout = TimeSpan.FromSeconds(2); o.OperationTimeout = TimeSpan.FromSeconds(2); });
        var expired = timeout.Client().GetRelationAsync(17, TestContext.Current.CancellationToken);
        timeout.Time.Advance(TimeSpan.FromSeconds(2));
        var error = await Assert.ThrowsAsync<ApiException>(() => expired);
        Assert.Equal(ApiErrorKind.Transport, error.Kind);
        Assert.Equal(TransportFailure.Timeout, error.TransportFailure);
        Assert.Null(error.InnerException);
    }
}
