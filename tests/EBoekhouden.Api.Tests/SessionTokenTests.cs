using System.Net;
using System.Text.Json;
using EBoekhouden.Api.Http;
using Xunit;

namespace EBoekhouden.Api.Tests;

public sealed class SessionTokenTests
{
    [Fact]
    public async Task SessionsAreLazyCachedAndUseOnlyTheInternalPost()
    {
        using var harness = new HttpHarness(session: async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/session", request.RequestUri!.PathAndQuery);
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
            Assert.Equal(HttpHarness.Credential, body.RootElement.GetProperty("accessToken").GetString());
            Assert.Equal("Tests", body.RootElement.GetProperty("source").GetString());
            Assert.Equal(2, body.RootElement.EnumerateObject().Count());
            return HttpHarness.Session();
        });
        var provider = harness.Tokens;
        Assert.Equal(0, harness.Sessions.Calls);
        var first = await provider.GetAsync(TestContext.Current.CancellationToken);
        var second = await provider.GetAsync(TestContext.Current.CancellationToken);
        Assert.Same(first, second);
        Assert.Equal(1, harness.Sessions.Calls);
        Assert.DoesNotContain(first.Value, first.ToString());
    }

    [Fact]
    public async Task ConcurrentColdAndEarlyRefreshCallsCreateOneSessionPerGeneration()
    {
        var entered = HttpHarness.Signal();
        var release = HttpHarness.Signal();
        using var harness = new HttpHarness(session: async (_, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return HttpHarness.Session(seconds: 100);
        });
        var cold = Enumerable.Range(0, 20).Select(_ => harness.Tokens.GetAsync(TestContext.Current.CancellationToken)).ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, harness.Sessions.Calls);
        release.SetResult();
        var first = await Task.WhenAll(cold);
        Assert.All(first, token => Assert.Same(first[0], token));
        harness.Time.Advance(TimeSpan.FromSeconds(89));
        Assert.Same(first[0], await harness.Tokens.GetAsync(TestContext.Current.CancellationToken));
        harness.Time.Advance(TimeSpan.FromSeconds(1));
        var fresh = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => harness.Tokens.GetAsync(TestContext.Current.CancellationToken)));
        Assert.Equal(2, harness.Sessions.Calls);
        Assert.NotSame(first[0], fresh[0]);
        Assert.All(fresh, token => Assert.Same(fresh[0], token));
    }

    [Fact]
    public async Task StaleInvalidationCannotDiscardNewLeaseEvenWhenTokenTextIsIdentical()
    {
        using var harness = new HttpHarness();
        var old = await harness.Tokens.GetAsync(TestContext.Current.CancellationToken);
        harness.Tokens.Invalidate(old);
        var current = await harness.Tokens.GetAsync(TestContext.Current.CancellationToken);
        Assert.NotSame(old, current);
        harness.Tokens.Invalidate(old);
        Assert.Same(current, await harness.Tokens.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, harness.Sessions.Calls);
    }

    [Fact]
    public async Task WaitingCallerCanCancelWithoutCancelingLeaderOrPoisoningTheLock()
    {
        var entered = HttpHarness.Signal();
        var release = HttpHarness.Signal();
        using var harness = new HttpHarness(session: async (_, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return HttpHarness.Session();
        });
        var leader = harness.Tokens.GetAsync(TestContext.Current.CancellationToken);
        await entered.Task;
        using var cancel = new CancellationTokenSource();
        var waiter = harness.Tokens.GetAsync(cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        release.SetResult();
        var token = await leader;
        Assert.Same(token, await harness.Tokens.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, harness.Sessions.Calls);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Tokens.GetAsync(canceled.Token));
    }

    [Fact]
    public async Task CanceledLeaderReleasesRefreshLockForRecovery()
    {
        var calls = 0;
        var entered = HttpHarness.Signal();
        using var harness = new HttpHarness(session: async (_, ct) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            return HttpHarness.Session();
        });
        using var cancel = new CancellationTokenSource();
        var first = harness.Tokens.GetAsync(cancel.Token);
        await entered.Task;
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.NotNull(await harness.Tokens.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, harness.Sessions.Calls);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(307)]
    public async Task FailedSessionPostsAreNeverAutomaticallyRetried(int status)
    {
        var calls = 0;
        using var harness = new HttpHarness(session: (_, _) => Task.FromResult(
            ++calls == 1 ? HttpHarness.Response((HttpStatusCode)status) : HttpHarness.Session()));
        var failure = await Assert.ThrowsAsync<ApiTransportException>(() => harness.Tokens.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(TransportFailure.SessionRejected, failure.Failure);
        Assert.Equal((HttpStatusCode)status, failure.StatusCode);
        Assert.DoesNotContain("synthetic-body", failure.ToString());
        Assert.Equal(1, harness.Sessions.Calls);
        Assert.NotNull(await harness.Tokens.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, harness.Sessions.Calls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"token\":null,\"expiresIn\":100}")]
    [InlineData("{\"token\":\"x\",\"expiresIn\":0}")]
    [InlineData("{\"token\":\"x\",\"expiresIn\":-1}")]
    [InlineData("{\"token\":\"\",\"expiresIn\":100}")]
    [InlineData("{\"token\":\"header injection\\r\\n\",\"expiresIn\":100}")]
    public async Task InvalidResponsesAreNotCachedAndBodiesAreNotExposed(string body)
    {
        using var harness = new HttpHarness(session: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body) }));
        var failure = await Assert.ThrowsAsync<ApiTransportException>(() => harness.Tokens.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(TransportFailure.InvalidSession, failure.Failure);
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain(body, failure.ToString());
        Assert.Equal(1, harness.Sessions.Calls);
    }

    [Fact]
    public async Task SessionTimeoutIsControlledByTimeProviderAndAllowsRecovery()
    {
        var calls = 0;
        using var harness = new HttpHarness(session: async (_, ct) =>
        {
            if (++calls == 1) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return HttpHarness.Session();
        });
        var request = harness.Tokens.GetAsync(TestContext.Current.CancellationToken);
        harness.Time.Advance(TimeSpan.FromSeconds(10));
        var failure = await Assert.ThrowsAsync<ApiTransportException>(() => request);
        Assert.Equal(TransportFailure.Timeout, failure.Failure);
        Assert.NotNull(await harness.Tokens.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, harness.Sessions.Calls);
    }

    [Fact]
    public async Task NetworkExceptionsAreSanitizedAndLockIsReleased()
    {
        var calls = 0;
        using var harness = new HttpHarness(session: (_, _) => ++calls == 1
            ? throw new HttpRequestException("synthetic-api-credential and a financial body")
            : Task.FromResult(HttpHarness.Session()));
        var error = await Assert.ThrowsAsync<ApiTransportException>(() => harness.Tokens.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(TransportFailure.Network, error.Failure);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(HttpHarness.Credential, error.ToString());
        Assert.NotNull(await harness.Tokens.GetAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VeryShortTtlIsUsefulAndExpiryIsMeasuredFromRequestStart()
    {
        using var harness = new HttpHarness(session: (_, _) => Task.FromResult(HttpHarness.Session(seconds: 1)));
        var first = await harness.Tokens.GetAsync(TestContext.Current.CancellationToken);
        harness.Time.Advance(TimeSpan.FromMilliseconds(800));
        Assert.Same(first, await harness.Tokens.GetAsync(TestContext.Current.CancellationToken));
        harness.Time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.NotSame(first, await harness.Tokens.GetAsync(TestContext.Current.CancellationToken));
        harness.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.NotNull(await harness.Tokens.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, harness.Sessions.Calls);
    }

    [Fact]
    public async Task SessionAlreadyTooOldAtResponseTimeIsRejectedAndRefreshCanRecover()
    {
        HttpHarness? harness = null;
        var calls = 0;
        using (harness = new HttpHarness(session: (_, _) =>
        {
            if (++calls == 1) harness!.Time.Advance(TimeSpan.FromSeconds(2));
            return Task.FromResult(HttpHarness.Session(seconds: 1));
        }))
        {
            var error = await Assert.ThrowsAsync<ApiTransportException>(() => harness.Tokens.GetAsync(TestContext.Current.CancellationToken));
            Assert.Equal(TransportFailure.InvalidSession, error.Failure);
            Assert.NotNull(await harness.Tokens.GetAsync(TestContext.Current.CancellationToken));
            Assert.Equal(2, calls);
        }
    }
}
