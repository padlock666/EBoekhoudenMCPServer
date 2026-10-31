using System.Net;
using System.Text.Json;
using EBoekhouden.Api.Errors;
using EBoekhouden.Api.Filtering;
using EBoekhouden.Api.Http;
using Xunit;

namespace EBoekhouden.Api.Tests;

public sealed class ReadClientErrorTests
{
    [Theory]
    [InlineData("error-dictionary", 400)]
    [InlineData("error-array", 400)]
    [InlineData("security-error", 403)]
    public async Task ErrorShapesKeepHttpStatusAndDiscardReflectedMetadata(string fixture, int status)
    {
        var body = ClientTestSupport.Fixture(fixture);
        var content = ClientTestSupport.Json(body);
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = content }),
            configure: options => options.MaxReadAttempts = 1);
        var error = await Assert.ThrowsAsync<ApiException>(() => harness.Client().GetRelationAsync(17, TestContext.Current.CancellationToken));
        Assert.Equal(ApiErrorKind.Http, error.Kind);
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Null(error.Code);
        Assert.Null(error.TraceId);
        Assert.Null(error.InnerException);
        Assert.Empty(error.Data);
        Assert.DoesNotContain("Synthetic private", error.ToString());
        Assert.DoesNotContain("Synthetic financial", error.ToString());
        Assert.DoesNotContain(body, error.ToString());
        Assert.True(content.Disposed);
        Assert.Equal(1, harness.Reads.Calls);
        Assert.Equal(1, harness.Sessions.Calls);
        Assert.Empty(harness.Logs.Messages);
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
    public async Task NoEndpointConvertsNotFoundIntoAnEmptySuccess(string endpoint)
    {
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(ClientTestSupport.Response(ClientTestSupport.Fixture("error-array"), HttpStatusCode.NotFound)));
        var error = await Assert.ThrowsAsync<ApiException>(() => ClientTestSupport.Call(harness.Client(), endpoint, TestContext.Current.CancellationToken));
        Assert.Equal(ApiErrorKind.Http, error.Kind);
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Null(error.Code);
        Assert.Equal("The requested e-Boekhouden resource was not found.", error.Message);
        Assert.Equal(1, harness.Reads.Calls);
    }

    [Theory]
    [InlineData("<html>synthetic private server body</html>")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("{\"code\":123,\"traceId\":true,\"message\":\"synthetic private data\"}")]
    [InlineData("{\"code\":\"bad code <secret>\",\"traceId\":\"https://private.example.invalid/\"}")]
    [InlineData("{\"code\":\"MUT_007\\r\\nHeader\",\"traceId\":\"user@example.invalid\"}")]
    [InlineData("{\"code\":\"MUT_007\",\"Code\":\"MUT_008\",\"traceId\":\"TRACE-A\",\"TraceId\":\"TRACE-B\"}")]
    public async Task MalformedNonJsonUnsafeOrAmbiguousDiagnosticsDoNotMaskTheHttpFailure(string body)
    {
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(ClientTestSupport.Response(body, HttpStatusCode.BadRequest)));
        var error = await Assert.ThrowsAsync<ApiException>(() => harness.Client().GetRelationAsync(17, TestContext.Current.CancellationToken));
        Assert.Equal(ApiErrorKind.Http, error.Kind);
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Null(error.Code);
        Assert.Null(error.TraceId);
        Assert.Null(error.InnerException);
        Assert.Equal("e-Boekhouden rejected the request.", error.Message);
        Assert.Empty(harness.Logs.Messages);
    }

    [Fact]
    public async Task ErrorMetadataIsSuppressedRegardlessOfLengthOrAlphabet()
    {
        var body = JsonSerializer.Serialize(new { code = new string('X', 65), traceId = new string('T', 129) });
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(ClientTestSupport.Response(body, HttpStatusCode.BadRequest)));
        var overlong = await Assert.ThrowsAsync<ApiException>(() => harness.Client().GetRelationAsync(17, TestContext.Current.CancellationToken));
        Assert.Null(overlong.Code);
        Assert.Null(overlong.TraceId);
        body = JsonSerializer.Serialize(new { code = "FUTURE_999", traceId = "TRACE-001", message = new string('X', 65536) });
        var oversized = await Assert.ThrowsAsync<ApiException>(() => harness.Client().GetRelationAsync(17, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Null(oversized.Code);
        Assert.Null(oversized.TraceId);
        body = """{"code":"FUTURE_999","traceId":"TRACE-001"}""";
        var future = await Assert.ThrowsAsync<ApiException>(() => harness.Client().GetRelationAsync(17, TestContext.Current.CancellationToken));
        Assert.Null(future.Code);
        Assert.Null(future.TraceId);
    }

    [Theory]
    [InlineData("synthetic-api-credential")]
    [InlineData("synthetic-session")]
    [InlineData("TRACE-synthetic-session-SUFFIX")]
    public async Task ReflectedApiAndSessionCredentialsAreRemovedFromDiagnosticFields(string reflected)
    {
        var body = JsonSerializer.Serialize(new { code = "EXAMPLE_001", traceId = reflected, message = HttpHarness.Credential });
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(ClientTestSupport.Response(body, HttpStatusCode.BadRequest)));
        var error = await Assert.ThrowsAsync<ApiException>(() => harness.Client().GetRelationAsync(17, TestContext.Current.CancellationToken));
        Assert.Null(error.Code);
        Assert.Null(error.TraceId);
        Assert.DoesNotContain(HttpHarness.Credential, error.ToString());
        Assert.DoesNotContain("synthetic-session", error.ToString());
        Assert.Empty(harness.Logs.Messages);
    }

    [Fact]
    public async Task FilterValueEchoesAreRemovedEvenWhenWrappedInWildcards()
    {
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(ClientTestSupport.Response(
            """{"code":"EXAMPLE_001","traceId":"TRACE-PRIVATEVALUE-001","title":"PrivateValue"}""", HttpStatusCode.BadRequest)));
        var error = await Assert.ThrowsAsync<ApiException>(() => harness.Client().GetRelationsAsync(
            new(new(), Name: new("like", "%PrivateValue%")), TestContext.Current.CancellationToken));
        Assert.Null(error.Code);
        Assert.Null(error.TraceId);
        Assert.DoesNotContain("PrivateValue", error.ToString());
    }

    [Fact]
    public async Task SessionErrorsSuppressMetadataWithoutSendingAGetOrRetryingPost()
    {
        var content = ClientTestSupport.Json(ClientTestSupport.Fixture("security-error"));
        using var harness = new HttpHarness(session: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = content }));
        var error = await Assert.ThrowsAsync<ApiException>(() => harness.Client().GetInvoiceAsync(23, TestContext.Current.CancellationToken));
        Assert.Equal(ApiErrorKind.Transport, error.Kind);
        Assert.Equal(TransportFailure.SessionRejected, error.TransportFailure);
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Null(error.Code);
        Assert.Null(error.TraceId);
        Assert.True(content.Disposed);
        Assert.Equal(1, harness.Sessions.Calls);
        Assert.Equal(0, harness.Reads.Calls);
        Assert.Null(error.InnerException);
        Assert.Empty(harness.Logs.Messages);
    }

    [Fact]
    public async Task InvalidSessionsAndNetworkFailuresAreNormalizedWithoutRawExceptions()
    {
        using var invalid = new HttpHarness(session: (_, _) => Task.FromResult(ClientTestSupport.Response("synthetic-private-invalid-token-body")));
        var session = await Assert.ThrowsAsync<ApiException>(() => invalid.Client().GetRelationAsync(17, TestContext.Current.CancellationToken));
        Assert.Equal(ApiErrorKind.Transport, session.Kind);
        Assert.Equal(TransportFailure.InvalidSession, session.TransportFailure);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Null(session.InnerException);
        Assert.DoesNotContain("synthetic-private-invalid-token-body", session.ToString());
        using var network = new HttpHarness(read: (_, _) => throw new HttpRequestException("synthetic-private-network-details"),
            configure: o => o.MaxReadAttempts = 1);
        var failure = await Assert.ThrowsAsync<ApiException>(() => network.Client().GetRelationAsync(17, TestContext.Current.CancellationToken));
        Assert.Equal(ApiErrorKind.Transport, failure.Kind);
        Assert.Equal(TransportFailure.Network, failure.TransportFailure);
        Assert.Null(failure.StatusCode);
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain("synthetic-private-network-details", failure.ToString());
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(307)]
    public async Task FinalStatusSurvivesTransportAuthenticationRetryAndRedirectPolicies(int status)
    {
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(ClientTestSupport.Response(
            ClientTestSupport.Fixture("error-array"), (HttpStatusCode)status)), configure: o => o.MaxReadAttempts = 1);
        var error = await Assert.ThrowsAsync<ApiException>(() => harness.Client().GetLedgerAsync(7, TestContext.Current.CancellationToken));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Null(error.Code);
        Assert.Equal(1, harness.Reads.Calls);
    }
}
