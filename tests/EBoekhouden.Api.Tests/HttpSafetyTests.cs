using System.Net;
using System.Net.Http.Headers;
using EBoekhouden.Api.Configuration;
using EBoekhouden.Api.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EBoekhouden.Api.Tests;

public sealed class HttpSafetyTests
{
    [Theory]
    [InlineData("/v1/relation")]
    [InlineData("/v1/relation/-1")]
    [InlineData("/v1/invoice")]
    [InlineData("/v1/invoice/0")]
    [InlineData("/v1/mutation")]
    [InlineData("/v1/mutation/2147483647")]
    [InlineData("/v1/mutation/invoice/outstanding")]
    [InlineData("/v1/ledger")]
    [InlineData("/v1/ledger/1")]
    [InlineData("/v1/ledger/balances")]
    [InlineData("/v1/ledger/-2147483648/balance")]
    public async Task ExactApprovedReadPathsDispatch(string path)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(HttpHarness.Response(HttpStatusCode.OK)));
        using var invoker = new HttpMessageInvoker(new RequestGuardHandler(new Uri("https://api.e-boekhouden.nl/"), false) { InnerHandler = handler });
        using var message = Get("https://api.e-boekhouden.nl" + path);
        using var response = await invoker.SendAsync(message, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("POST", "https://api.e-boekhouden.nl/v1/relation")]
    [InlineData("PATCH", "https://api.e-boekhouden.nl/v1/ledger/1")]
    [InlineData("DELETE", "https://api.e-boekhouden.nl/v1/session")]
    [InlineData("HEAD", "https://api.e-boekhouden.nl/v1/invoice")]
    [InlineData("GET", "https://api.e-boekhouden.nl/v1/session")]
    [InlineData("GET", "https://api.e-boekhouden.nl/v1/administration")]
    [InlineData("GET", "https://api.e-boekhouden.nl/v2/relation")]
    [InlineData("GET", "https://api.e-boekhouden.nl/v1/relation/")]
    [InlineData("GET", "https://api.e-boekhouden.nl/V1/relation")]
    [InlineData("GET", "https://api.e-boekhouden.nl/v1/relation/2147483648")]
    [InlineData("GET", "https://api.e-boekhouden.nl/v1/relation/01")]
    [InlineData("GET", "https://api.e-boekhouden.nl/v1/relation/1/balance")]
    [InlineData("GET", "https://api.e-boekhouden.nl/v1/relation/%31")]
    [InlineData("GET", "https://api.e-boekhouden.nl/v1/relation/1%2Fbalance")]
    [InlineData("GET", "https://api.e-boekhouden.nl/v1/unapproved/../relation")]
    [InlineData("GET", "https://api.e-boekhouden.nl/v1/relation#fragment")]
    [InlineData("GET", "https://api.e-boekhouden.nl:444/v1/relation")]
    [InlineData("GET", "http://api.e-boekhouden.nl/v1/relation")]
    [InlineData("GET", "https://api.e-boekhouden.nl.evil.invalid/v1/relation")]
    [InlineData("GET", "https://user@api.e-boekhouden.nl/v1/relation")]
    [InlineData("GET", "http://127.0.0.1:12345/v1/relation")]
    public async Task MethodRouteAndOriginRestrictionsRejectBeforeDispatch(string method, string url)
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Must not dispatch"));
        using var invoker = new HttpMessageInvoker(new RequestGuardHandler(new Uri("https://api.e-boekhouden.nl/"), false) { InnerHandler = handler });
        using var request = Get(url);
        request.Method = new HttpMethod(method);
        var error = await Assert.ThrowsAsync<ApiTransportException>(() => invoker.SendAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(TransportFailure.RequestRejected, error.Failure);
        Assert.Equal(0, handler.Calls);
        Assert.DoesNotContain(url, error.Message);
    }

    [Theory]
    [InlineData("POST", "/v1/session", true, true)]
    [InlineData("POST", "/v1/relation", true, false)]
    [InlineData("GET", "/v1/session", false, false)]
    [InlineData("DELETE", "/v1/session", false, false)]
    [InlineData("POST", "/v1/session?extra=1", true, false)]
    [InlineData("POST", "/v1/session", false, false)]
    public async Task SessionGuardPermitsOnlyInternalSessionPost(string method, string path, bool body, bool allowed)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(HttpHarness.Session()));
        using var invoker = new HttpMessageInvoker(new RequestGuardHandler(new Uri("https://api.e-boekhouden.nl/"), true) { InnerHandler = handler });
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://api.e-boekhouden.nl" + path);
        if (body) request.Content = new StringContent("{}");
        if (allowed)
        {
            using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(1, handler.Calls);
        }
        else
        {
            await Assert.ThrowsAsync<ApiTransportException>(() => invoker.SendAsync(request, TestContext.Current.CancellationToken));
            Assert.Equal(0, handler.Calls);
        }
    }

    [Fact]
    public void ProductionHandlerDisablesRedirectsAndCookies()
    {
        using var handler = ApiServiceCollectionExtensions.CreatePrimaryHandler(new EBoekhoudenOptions());
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.Equal(5, handler.MaxConnectionsPerServer);
        Assert.Equal(TimeSpan.FromSeconds(10), handler.ConnectTimeout);
    }

    [Fact]
    public async Task GuardRejectsReadBodiesMissingBearerAndSessionAuthorization()
    {
        var origin = new Uri("https://api.e-boekhouden.nl/");
        var readHandler = new StubHandler((_, _) => throw new InvalidOperationException("Must not dispatch"));
        using var reads = new HttpMessageInvoker(new RequestGuardHandler(origin, false) { InnerHandler = readHandler });
        using var body = Get("https://api.e-boekhouden.nl/v1/relation");
        body.Content = new StringContent("{}");
        await Assert.ThrowsAsync<ApiTransportException>(() => reads.SendAsync(body, TestContext.Current.CancellationToken));
        using var anonymous = new HttpRequestMessage(HttpMethod.Get, "https://api.e-boekhouden.nl/v1/relation");
        await Assert.ThrowsAsync<ApiTransportException>(() => reads.SendAsync(anonymous, TestContext.Current.CancellationToken));
        var sessionHandler = new StubHandler((_, _) => throw new InvalidOperationException("Must not dispatch"));
        using var sessions = new HttpMessageInvoker(new RequestGuardHandler(origin, true) { InnerHandler = sessionHandler });
        using var authorized = Get("https://api.e-boekhouden.nl/v1/session");
        authorized.Method = HttpMethod.Post;
        authorized.Content = new StringContent("{}");
        await Assert.ThrowsAsync<ApiTransportException>(() => sessions.SendAsync(authorized, TestContext.Current.CancellationToken));
        Assert.Equal(0, readHandler.Calls);
        Assert.Equal(0, sessionHandler.Calls);
    }

    [Theory]
    [InlineData("http://localhost:12345/")]
    [InlineData("https://outside.invalid/")]
    [InlineData("http://192.0.2.1/")]
    [InlineData("http://127.0.0.1/api/")]
    [InlineData("http://127.0.0.1/?test=1")]
    [InlineData("http://127.0.0.1/#fragment")]
    [InlineData("http://user@127.0.0.1/")]
    [InlineData("ftp://127.0.0.1/")]
    public void TestOverrideCannotUseRemoteOrNonRootOrigins(string origin) =>
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddEBoekhoudenApiForTesting(HttpHarness.Configuration(), new Uri(origin)));

    [Theory]
    [InlineData("http://127.0.0.1:12345/")]
    [InlineData("http://[::1]:12345/")]
    public void ExplicitNumericLoopbackOriginsArePermitted(string origin)
    {
        var services = new ServiceCollection();
        services.AddEBoekhoudenApiForTesting(HttpHarness.Configuration(), new Uri(origin));
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ApiServiceCollectionExtensions.ReadClientName);
        Assert.Equal(new Uri(origin), client.BaseAddress);
    }

    [Fact]
    public void ProductionOriginCannotBeChangedByConfigurationAndRegistrationIsSingleCredential()
    {
        var configuration = HttpHarness.Configuration(new() { ["EBoekhouden:BaseAddress"] = "http://127.0.0.1/" });
        var services = new ServiceCollection().AddEBoekhoudenApi(configuration);
        Assert.Throws<InvalidOperationException>(() => services.AddEBoekhoudenApi(configuration));
        using var provider = services.BuildServiceProvider();
        // Unknown options now fail closed instead of being silently ignored.
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IHttpClientFactory>().CreateClient(ApiServiceCollectionExtensions.ReadClientName));
        using var normal = new ServiceCollection().AddEBoekhoudenApi(HttpHarness.Configuration()).BuildServiceProvider();
        using var client = normal.GetRequiredService<IHttpClientFactory>().CreateClient(ApiServiceCollectionExtensions.ReadClientName);
        Assert.Equal("https://api.e-boekhouden.nl/", client.BaseAddress!.AbsoluteUri);
    }

    [Theory]
    [InlineData("Keyring:Application", " ")]
    [InlineData("Keyring:Account", "")]
    [InlineData("Source", "")]
    [InlineData("Source", "longerthan10")]
    [InlineData("Source", "bad-source")]
    [InlineData("Source", "Tests\n")]
    [InlineData("SessionTimeout", "00:00:00")]
    [InlineData("AttemptTimeout", "00:00:31")]
    [InlineData("OperationTimeout", "00:10:00")]
    [InlineData("RefreshBeforeExpiry", "-00:00:01")]
    [InlineData("RetryBaseDelay", "00:00:06")]
    [InlineData("MaxRetryDelay", "00:00:00")]
    [InlineData("MaxReadAttempts", "0")]
    [InlineData("MaxReadAttempts", "6")]
    [InlineData("MaxConcurrentReads", "33")]
    [InlineData("CircuitFailureThreshold", "0")]
    [InlineData("CircuitBreakDuration", "00:00:00")]
    public void StartupValidationFailsLocallyWithoutNetworkOrSecretsInErrors(string key, string value)
    {
        var services = new ServiceCollection().AddEBoekhoudenApi(HttpHarness.Configuration(new() { ["EBoekhouden:" + key] = value }));
        using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.DoesNotContain(HttpHarness.Credential, error.ToString());
    }

    [Fact]
    public void ValidStartupValidationAndDependencyResolutionNeverAcquireASession()
    {
        using var harness = new HttpHarness();
        harness.Services.GetRequiredService<IStartupValidator>().Validate();
        Assert.NotNull(harness.Transport);
        Assert.NotNull(harness.Tokens);
        Assert.Equal(0, harness.Sessions.Calls);
        Assert.Equal(0, harness.Reads.Calls);
    }

    [Fact]
    public async Task TraceLoggingExcludesCredentialsHeadersBodiesAndFinancialQueries()
    {
        using var harness = new HttpHarness();
        using var response = await harness.Transport.SendAsync(ReadRequest.Relations(new(new(), Name: new("eq", "synthetic-sensitive-query"))), TestContext.Current.CancellationToken);
        Assert.Empty(harness.Logs.Messages);
    }

    private static HttpRequestMessage Get(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "synthetic-token");
        return request;
    }
}
