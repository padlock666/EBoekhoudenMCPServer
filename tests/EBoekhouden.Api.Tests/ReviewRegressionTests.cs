using System.Net;
using System.Text.Json;
using EBoekhouden.Api.Contracts;
using EBoekhouden.Api.Errors;
using EBoekhouden.Api.Http;
using Xunit;

namespace EBoekhouden.Api.Tests;

public sealed class ReviewRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MutationTypeAcceptsTheSchemaStringAsWellAsTheNumericExamples(bool detail)
    {
        const string item = """{"id":12,"date":"2026-01-01","type":"2"}""";
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(ClientTestSupport.Response(
            detail ? item : "{\"items\":[" + item + "],\"count\":1}")));
        var client = harness.Client();
        var type = detail
            ? (await client.GetMutationAsync(12, TestContext.Current.CancellationToken)).Type
            : Assert.Single((await client.GetMutationsAsync(new(new()), TestContext.Current.CancellationToken)).Items).Type;
        Assert.Equal(2, type);
    }

    [Theory]
    [InlineData("\"99\"")]
    [InlineData("99")]
    public void UnknownNumericMutationTypesArePreserved(string type)
    {
        var entry = JsonSerializer.Deserialize<EntryDto>(
            "{\"id\":12,\"date\":\"2026-01-01\",\"type\":" + type + "}", ApiJson.Options)!;
        Assert.Equal(99, entry.Type);
    }

    [Theory]
    [InlineData("\"two\"")]
    [InlineData("\"2.5\"")]
    [InlineData("\"2147483648\"")]
    [InlineData("true")]
    public void MutationTypeCompatibilityDoesNotAcceptNonIntegerCodes(string type) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EntryDto>(
            "{\"id\":12,\"date\":\"2026-01-01\",\"type\":" + type + "}", ApiJson.Options));

    [Fact]
    public void MutationTypeCompatibilityDoesNotCoerceIdentity() =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EntryDto>(
            """{"id":"12","date":"2026-01-01","type":"2"}""", ApiJson.Options));

    [Theory]
    [InlineData("0.12345678901234567890123456789")]
    [InlineData("1e-29")]
    [InlineData("79228162514264337593543950335.1")]
    public async Task InexactFinancialNumbersFailInsteadOfReturningRoundedSuccess(string number)
    {
        var body = "{\"code\":\"SYN\",\"type\":\"BAL\",\"balance\":" + number + "}";
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(ClientTestSupport.Response(body)));
        var error = await Assert.ThrowsAsync<ApiException>(() => harness.Client().GetLedgerBalanceAsync(
            new("as_of", new DateOnly(2026, 1, 1), ledgerId: 7), TestContext.Current.CancellationToken));
        Assert.Equal(ApiErrorKind.InvalidResponse, error.Kind);
        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
        Assert.Equal(1, harness.Reads.Calls);
        Assert.DoesNotContain(number, error.ToString());
    }

    [Theory]
    [InlineData("1.2300", "1.2300")]
    [InlineData("123e-2", "1.23")]
    [InlineData("1e-28", "0.0000000000000000000000000001")]
    [InlineData("1.00000000000000000000000000000", "1")]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335", "-79228162514264337593543950335")]
    public void ExactlyRepresentableNumbersRemainAccepted(string number, string expected)
    {
        var invoice = JsonSerializer.Deserialize<InvoiceDto>(
            "{\"id\":1,\"date\":\"2026-01-01\",\"totalAmount\":" + number + "}", ApiJson.Options)!;
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), invoice.TotalAmount);
        if (number == "1.2300") Assert.Contains("1.2300", JsonSerializer.Serialize(invoice, ApiJson.Options));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedErrorBodiesPreserveStatusAndDoNotRetryPermissionDenials(bool session)
    {
        var contents = new List<ClientTestSupport.TrackedJsonContent>();
        Task<HttpResponseMessage> Reject(HttpRequestMessage _, CancellationToken ct)
        {
            var content = ClientTestSupport.Json(new string('x', session ? 16 * 1024 + 1 : 16 * 1024 * 1024 + 1));
            contents.Add(content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = content });
        }
        using var harness = session ? new HttpHarness(session: Reject) : new HttpHarness(read: Reject);
        var error = await Assert.ThrowsAsync<ApiException>(() => harness.CompleteWithTimeAsync(
            harness.Client().GetRelationAsync(17, TestContext.Current.CancellationToken)));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal(session ? ApiErrorKind.Transport : ApiErrorKind.Http, error.Kind);
        if (session) Assert.Equal(TransportFailure.SessionRejected, error.TransportFailure);
        Assert.Null(error.Code);
        Assert.Null(error.TraceId);
        Assert.Null(error.InnerException);
        Assert.Equal(1, harness.Sessions.Calls);
        Assert.Equal(session ? 0 : 1, harness.Reads.Calls);
        Assert.All(contents, content => Assert.True(content.Disposed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrokenErrorStreamsPreserveStatusButCallerCancellationStillWins(bool cancel)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var content = new BrokenContent(() => { if (cancel) cancellation.Cancel(); });
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = content }));
        var call = harness.Client().GetRelationAsync(17, cancellation.Token);
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        else
        {
            var error = await Assert.ThrowsAsync<ApiException>(() => call);
            Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
            Assert.Equal(ApiErrorKind.Http, error.Kind);
            Assert.Null(error.TraceId);
            Assert.DoesNotContain("synthetic-private", error.ToString());
        }
        Assert.True(content.Disposed);
        Assert.Equal(1, harness.Reads.Calls);
    }

    private sealed class BrokenContent(Action beforeFailure) : HttpContent
    {
        public bool Disposed { get; private set; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            beforeFailure();
            throw new IOException("synthetic-private-body-failure");
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
