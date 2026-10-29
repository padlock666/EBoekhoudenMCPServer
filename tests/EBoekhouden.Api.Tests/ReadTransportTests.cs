using System.Net;
using System.Net.Http.Headers;
using EBoekhouden.Api.Filtering;
using EBoekhouden.Api.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EBoekhouden.Api.Tests;

public sealed class ReadTransportTests
{
    [Fact]
    public async Task MixedAuthenticationAndTransientFailuresShareOneBudgetAndRecreateMessages()
    {
        var calls = 0;
        var discarded = new List<TrackingContent>();
        using var harness = new HttpHarness(read: (request, _) =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("synthetic-session", request.Headers.Authorization.Parameter);
            Assert.Null(request.Content);
            var status = ++calls switch { 1 => HttpStatusCode.ServiceUnavailable, 2 => HttpStatusCode.Unauthorized, _ => HttpStatusCode.BadGateway };
            var content = new TrackingContent();
            discarded.Add(content);
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        });
        using var response = await harness.CompleteWithTimeAsync(harness.Transport.SendAsync(ReadRequest.Relation(1), TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(3, harness.Reads.Calls);
        Assert.Equal(2, harness.Sessions.Calls);
        Assert.Equal(3, harness.Reads.Requests.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.All(discarded.Take(2), content => Assert.True(content.Disposed));
        Assert.False(discarded[2].Disposed);
        using var client = harness.Services.GetRequiredService<IHttpClientFactory>().CreateClient(ApiServiceCollectionExtensions.ReadClientName);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task RepeatedUnauthorizedStopsAfterOneAuthenticationReplay()
    {
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(HttpHarness.Response(HttpStatusCode.Unauthorized)));
        using var response = await harness.Transport.SendAsync(ReadRequest.Invoice(1), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, harness.Reads.Calls);
        Assert.Equal(2, harness.Sessions.Calls);
    }

    [Fact]
    public async Task SingleAttemptBudgetDoesNotCreateAnExtraAuthenticationAttempt()
    {
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(HttpHarness.Response(HttpStatusCode.Unauthorized)),
            configure: options => options.MaxReadAttempts = 1);
        using var response = await harness.Transport.SendAsync(ReadRequest.Entry(1), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, harness.Reads.Calls);
        Assert.Equal(1, harness.Sessions.Calls);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    [InlineData(501)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task PermissionAndOtherPermanentFailuresAndRedirectsDoNotRefreshOrRetry(int status)
    {
        using var harness = new HttpHarness(read: (_, _) =>
        {
            var response = HttpHarness.Response((HttpStatusCode)status);
            response.Headers.Location = new Uri("https://unapproved.invalid/");
            return Task.FromResult(response);
        });
        using var response = await harness.Transport.SendAsync(ReadRequest.Ledger(1), TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal(1, harness.Reads.Calls);
        Assert.Equal(1, harness.Sessions.Calls);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task TransientStatusRetriesCanRecoverWithoutNewSessions(int status)
    {
        var count = 0;
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(HttpHarness.Response(
            ++count == 1 ? (HttpStatusCode)status : HttpStatusCode.OK)));
        using var response = await harness.CompleteWithTimeAsync(harness.Transport.SendAsync(ReadRequest.Relations(new(new())), TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, harness.Reads.Calls);
        Assert.Equal(1, harness.Sessions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryAfterDeltaAndDateAreNotShortened(bool date)
    {
        var calls = 0;
        HttpHarness? harness = null;
        using (harness = new HttpHarness(read: (_, _) =>
        {
            var response = HttpHarness.Response(++calls == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
            if (calls == 1) response.Headers.RetryAfter = date
                ? new RetryConditionHeaderValue(harness!.Time.GetUtcNow() + TimeSpan.FromSeconds(2))
                : new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
            return Task.FromResult(response);
        }))
        {
            var request = harness.Transport.SendAsync(ReadRequest.Relation(1), TestContext.Current.CancellationToken);
            Assert.Equal(1, harness.Reads.Calls);
            harness.Time.Advance(TimeSpan.FromMilliseconds(1900));
            Assert.Equal(1, harness.Reads.Calls);
            harness.Time.Advance(TimeSpan.FromMilliseconds(100));
            using var response = await request.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, harness.Reads.Calls);
        }
    }

    [Fact]
    public async Task ExcessiveRetryAfterReturnsFailureInsteadOfRetryingTooEarly()
    {
        using var harness = new HttpHarness(read: (_, _) =>
        {
            var response = HttpHarness.Response(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        });
        using var response = await harness.Transport.SendAsync(ReadRequest.Relation(1), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, harness.Reads.Calls);
    }

    [Fact]
    public async Task NetworkFailureRetriesAreBoundedAndExceptionsContainNoSensitiveValues()
    {
        using var harness = new HttpHarness(read: (_, _) => throw new HttpRequestException("synthetic-session financial-query-value"));
        var task = harness.Transport.SendAsync(ReadRequest.Relations(new(new(), Name: new("eq", "financial-query-value"))), TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<ApiTransportException>(() => harness.CompleteWithTimeAsync(task));
        Assert.Equal(TransportFailure.Network, error.Failure);
        Assert.Equal(3, harness.Reads.Calls);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("synthetic-session", error.ToString());
        Assert.DoesNotContain("financial-query-value", error.ToString());
        Assert.Empty(harness.Logs.Messages);
    }

    [Fact]
    public async Task DelayedOldUnauthorizedResponseCannotInvalidateTokenRefreshedByAnotherRead()
    {
        var oldEntered = HttpHarness.Signal();
        var releaseOld = HttpHarness.Signal();
        var reads = 0;
        var sessions = 0;
        using var harness = new HttpHarness(session: (_, _) => Task.FromResult(HttpHarness.Session("session-" + Interlocked.Increment(ref sessions))),
            read: async (request, ct) =>
            {
                var call = Interlocked.Increment(ref reads);
                if (call == 1)
                {
                    Assert.Equal("session-1", request.Headers.Authorization!.Parameter);
                    oldEntered.SetResult();
                    await releaseOld.Task.WaitAsync(ct);
                    return HttpHarness.Response(HttpStatusCode.Unauthorized);
                }
                if (call == 2) return HttpHarness.Response(HttpStatusCode.Unauthorized);
                Assert.Equal("session-2", request.Headers.Authorization!.Parameter);
                return HttpHarness.Response(HttpStatusCode.OK);
            });
        var old = harness.Transport.SendAsync(ReadRequest.Relation(1), TestContext.Current.CancellationToken);
        await oldEntered.Task;
        using var current = await harness.Transport.SendAsync(ReadRequest.Relation(2), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        releaseOld.SetResult();
        using var replay = await old;
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(2, sessions);
        Assert.Equal(4, reads);
    }

    [Fact]
    public async Task CancelDuringRetryDelayStopsWithoutAnotherAttempt()
    {
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(HttpHarness.Response(HttpStatusCode.ServiceUnavailable)));
        using var cancellation = new CancellationTokenSource();
        var task = harness.Transport.SendAsync(ReadRequest.Relation(1), cancellation.Token);
        Assert.Equal(1, harness.Reads.Calls);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, harness.Reads.Calls);
    }

    [Fact]
    public async Task CallerCancellationWinsANetworkFailureRaceWithoutRetry()
    {
        using var cancel = new CancellationTokenSource();
        using var harness = new HttpHarness(read: (_, _) =>
        {
            cancel.Cancel();
            throw new HttpRequestException("synthetic-sensitive-query");
        });
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.Transport.SendAsync(ReadRequest.Relation(1), cancel.Token));
        Assert.Equal(cancel.Token, error.CancellationToken);
        Assert.Null(error.InnerException);
        Assert.Equal(1, harness.Reads.Calls);
    }

    [Fact]
    public async Task AttemptTimeoutsHaveBoundedRetryCount()
    {
        using var harness = new HttpHarness(read: async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        }, configure: options => options.AttemptTimeout = TimeSpan.FromSeconds(2));
        var error = await Assert.ThrowsAsync<ApiTransportException>(() => harness.CompleteWithTimeAsync(
            harness.Transport.SendAsync(ReadRequest.Relation(1), TestContext.Current.CancellationToken)));
        Assert.Equal(TransportFailure.Timeout, error.Failure);
        Assert.Equal(3, harness.Reads.Calls);
    }

    [Fact]
    public async Task OperationTimeoutIncludesBodyBufferingAndDoesNotRetryAfterDeadline()
    {
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new BlockingContent() }), configure: options =>
        { options.OperationTimeout = TimeSpan.FromSeconds(2); options.AttemptTimeout = TimeSpan.FromSeconds(2); });
        var task = harness.Transport.SendAsync(ReadRequest.Relation(1), TestContext.Current.CancellationToken);
        harness.Time.Advance(TimeSpan.FromSeconds(2));
        var error = await Assert.ThrowsAsync<ApiTransportException>(() => task);
        Assert.Equal(TransportFailure.Timeout, error.Failure);
        Assert.Equal(1, harness.Reads.Calls);
    }

    [Fact]
    public async Task ConcurrencyLimitAndQueueCancellationReleasePermits()
    {
        var twoEntered = HttpHarness.Signal();
        var release = HttpHarness.Signal();
        var active = 0;
        using var harness = new HttpHarness(read: async (_, ct) =>
        {
            var count = Interlocked.Increment(ref active);
            Assert.InRange(count, 1, 2);
            if (count == 2) twoEntered.TrySetResult();
            try { await release.Task.WaitAsync(ct); }
            finally { Interlocked.Decrement(ref active); }
            return HttpHarness.Response(HttpStatusCode.OK);
        }, configure: options => options.MaxConcurrentReads = 2);
        var first = harness.Transport.SendAsync(ReadRequest.Relation(1), TestContext.Current.CancellationToken);
        var second = harness.Transport.SendAsync(ReadRequest.Relation(2), TestContext.Current.CancellationToken);
        await twoEntered.Task;
        using var cancel = new CancellationTokenSource();
        var queued = harness.Transport.SendAsync(ReadRequest.Relation(3), cancel.Token);
        Assert.Equal(2, harness.Reads.Calls);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        release.SetResult();
        using var a = await first;
        using var b = await second;
        using var after = await harness.Transport.SendAsync(ReadRequest.Relation(4), TestContext.Current.CancellationToken);
        Assert.Equal(3, harness.Reads.Calls);
        Assert.Equal(1, harness.Sessions.Calls);
    }

    [Fact]
    public async Task QueuedReadsHaveAnOverallDeadlineAndCanRecoverAfterTimeout()
    {
        var block = true;
        using var harness = new HttpHarness(read: async (_, ct) =>
        {
            if (block) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return HttpHarness.Response(HttpStatusCode.OK);
        }, configure: options =>
        {
            options.MaxConcurrentReads = 1;
            options.AttemptTimeout = TimeSpan.FromSeconds(2);
            options.OperationTimeout = TimeSpan.FromSeconds(2);
        });
        var active = harness.Transport.SendAsync(ReadRequest.Relation(1), TestContext.Current.CancellationToken);
        var queued = harness.Transport.SendAsync(ReadRequest.Relation(2), TestContext.Current.CancellationToken);
        harness.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TransportFailure.Timeout, (await Assert.ThrowsAsync<ApiTransportException>(() => active)).Failure);
        Assert.Equal(TransportFailure.Timeout, (await Assert.ThrowsAsync<ApiTransportException>(() => queued)).Failure);
        Assert.Equal(1, harness.Reads.Calls);
        block = false;
        using var recovered = await harness.Transport.SendAsync(ReadRequest.Relation(3), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    private sealed class TrackingContent : StringContent
    {
        public bool Disposed { get; private set; }
        public TrackingContent() : base("synthetic") { }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class BlockingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("Expected cancellation-aware content serialization.");
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
