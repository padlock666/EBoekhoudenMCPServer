using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Xunit;

namespace EBoekhouden.IntegrationTests;

public sealed class ConcurrencyTests
{
    [Fact]
    public async Task ColdCallsShareOneSessionAndStale401CannotInvalidateRefreshedSession()
    {
        await using var api = await FakeUpstream.StartAsync();
        var firstSession = Signal(); var releaseFirstSession = Signal();
        var secondSession = Signal(); var releaseSecondSession = Signal();
        var allOldReads = Signal(); var releaseEarly401 = Signal(); var releaseLate401 = Signal(); var threeReplays = Signal();
        var rejectOld = 0; var oldReads = 0; var replays = 0;
        api.Respond = async (context, number, token) =>
        {
            if (number == 1) { firstSession.TrySetResult(); await releaseFirstSession.Task.WaitAsync(token); return FakeUpstream.Session(1); }
            if (number == 2) { secondSession.TrySetResult(); await releaseSecondSession.Task.WaitAsync(token); return FakeUpstream.Session(2); }
            if (Volatile.Read(ref rejectOld) == 1 && context.Request.Headers.Authorization == "Bearer " + FakeUpstream.SessionPrefix + "1")
            {
                var index = Interlocked.Increment(ref oldReads);
                if (index == 4) allOldReads.TrySetResult();
                await (index == 4 ? releaseLate401 : releaseEarly401).Task.WaitAsync(token);
                return new FakeUpstream.Reply(401, "{}");
            }
            if (context.Request.Headers.Authorization == "Bearer " + FakeUpstream.SessionPrefix + "2" && Interlocked.Increment(ref replays) == 3)
                threeReplays.TrySetResult();
            return FakeUpstream.DefaultReply(context.Request, number);
        };
        await using var host = await StdioProcess.StartAsync(api.Origin);
        await host.ConnectAsync();
        var cold = Enumerable.Range(0, 8).Select(_ => host.CallAsync("get_relation", """{"id":17}""").AsTask()).ToArray();
        await firstSession.Task.WaitAsync(host.Token);
        Assert.Equal(1, api.Sessions);
        Assert.Equal(0, api.Reads);
        releaseFirstSession.TrySetResult();
        foreach (var result in await Task.WhenAll(cold).WaitAsync(host.Token)) ExecutableTests.Success(result);
        Assert.Equal(1, api.Sessions);
        Volatile.Write(ref rejectOld, 1);
        var refresh = Enumerable.Range(0, 4).Select(_ => host.CallAsync("get_relation", """{"id":17}""").AsTask()).ToArray();
        await allOldReads.Task.WaitAsync(host.Token);
        releaseEarly401.TrySetResult();
        await secondSession.Task.WaitAsync(host.Token);
        Assert.Equal(2, api.Sessions);
        releaseSecondSession.TrySetResult();
        await threeReplays.Task.WaitAsync(host.Token); // New token is installed before the fourth stale failure arrives.
        releaseLate401.TrySetResult();
        foreach (var result in await Task.WhenAll(refresh).WaitAsync(host.Token)) ExecutableTests.Success(result);
        Assert.Equal(2, api.Sessions);
        Assert.Equal(16, api.Reads); // Eight cold reads + four old-token failures + four replays.
        Assert.Equal(4, replays);
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [Fact]
    public async Task ConcurrentCallsAfterExpiryShareOneRefresh()
    {
        await using var api = await FakeUpstream.StartAsync();
        var refreshing = Signal(); var release = Signal();
        api.Respond = async (context, number, token) =>
        {
            if (number == 1) return FakeUpstream.Session(1, expiresIn: 2);
            if (number == 2) { refreshing.TrySetResult(); await release.Task.WaitAsync(token); return FakeUpstream.Session(2); }
            return FakeUpstream.DefaultReply(context.Request, number);
        };
        await using var host = await StdioProcess.StartAsync(api.Origin);
        await host.ConnectAsync();
        ExecutableTests.Success(await host.CallAsync("get_relation", """{"id":17}"""));
        // The actual executable uses System time. Unit tests cover boundaries with FakeTimeProvider.
        await Task.Delay(TimeSpan.FromSeconds(2.1), host.Token);
        var reads = Enumerable.Range(0, 8).Select(_ => host.CallAsync("get_relation", """{"id":17}""").AsTask()).ToArray();
        await refreshing.Task.WaitAsync(host.Token);
        Assert.Equal(2, api.Sessions);
        Assert.Equal(1, api.Reads);
        release.TrySetResult();
        foreach (var result in await Task.WhenAll(reads).WaitAsync(host.Token)) ExecutableTests.Success(result);
        Assert.Equal(2, api.Sessions);
        Assert.Equal(9, api.Reads);
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [Fact]
    public async Task LocalClientCancellationPlusProtocolNotificationAbortsActualHttpRequest()
    {
        await using var api = await FakeUpstream.StartAsync();
        var entered = Signal(); var aborted = Signal();
        api.Respond = async (context, number, token) =>
        {
            if (number > 0) return FakeUpstream.Session(number);
            if (context.Request.Path == "/v1/relation/17")
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { if (context.RequestAborted.IsCancellationRequested) aborted.TrySetResult(); }
            }
            return FakeUpstream.DefaultReply(context.Request, number);
        };
        await using var host = await StdioProcess.StartAsync(api.Origin);
        await host.ConnectAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(host.Token);
        var call = host.Client.SendRequestAsync(new JsonRpcRequest
        {
            Id = new RequestId(12344), Method = "tools/call", Params = JsonNode.Parse("""{"name":"get_relation","arguments":{"id":17}}"""),
        }, cancellation.Token);
        await entered.Task.WaitAsync(host.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        // SDK 2.3.0 local cancellation does not reliably send the remote notification.
        // Send it explicitly rather than equating a locally canceled task with remote abort.
        await host.Client.SendNotificationAsync("notifications/cancelled", new CancelledNotificationParams { RequestId = new RequestId(12344) }, cancellationToken: host.Token);
        await aborted.Task.WaitAsync(TimeSpan.FromSeconds(5), host.Token);
        Assert.Contains("notifications/cancelled", host.Sent);
        Assert.Equal(1, api.Reads);
        // A later call succeeds: cancellation did not stop the host or strand the operation permit.
        ExecutableTests.Success(await host.CallAsync("get_ledger_account", """{"id":7}"""));
        Assert.Equal(1, api.Sessions);
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [Fact]
    public async Task ExplicitProtocolCancellationMatchesRequestIdAndLeavesOtherCallsUsable()
    {
        await using var api = await FakeUpstream.StartAsync();
        var entered = Signal(); var aborted = Signal();
        api.Respond = async (context, number, token) =>
        {
            if (number > 0) return FakeUpstream.Session(number);
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { if (context.RequestAborted.IsCancellationRequested) aborted.TrySetResult(); }
            return new FakeUpstream.Reply(200, "{}");
        };
        await using var host = await StdioProcess.StartAsync(api.Origin);
        await host.ConnectAsync();
        var call = host.Client.SendRequestAsync(new JsonRpcRequest
        {
            Id = new RequestId(12345), Method = "tools/call", Params = JsonNode.Parse("""{"name":"get_relation","arguments":{"id":17}}"""),
        }, host.Token);
        await entered.Task.WaitAsync(host.Token);
        await host.Client.SendNotificationAsync("notifications/cancelled", new CancelledNotificationParams { RequestId = new RequestId(12345) }, cancellationToken: host.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        await aborted.Task.WaitAsync(TimeSpan.FromSeconds(5), host.Token);
        Assert.Equal(10, (await host.Client.ListToolsAsync(cancellationToken: host.Token)).Count);
        Assert.Equal(1, api.Reads);
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [Fact]
    public async Task EofDuringPendingHttpCancelsRequestAndExitsWithoutForcedKill()
    {
        await using var api = await FakeUpstream.StartAsync();
        var entered = Signal(); var aborted = Signal();
        api.Respond = async (context, number, token) =>
        {
            if (number > 0) return FakeUpstream.Session(number);
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { if (context.RequestAborted.IsCancellationRequested) aborted.TrySetResult(); }
            return new FakeUpstream.Reply(200, "{}");
        };
        await using var host = await StdioProcess.StartAsync(api.Origin);
        await host.ConnectAsync();
        var call = host.CallAsync("get_relation", """{"id":17}""").AsTask();
        await entered.Task.WaitAsync(host.Token);
        await host.CloseInputAndWaitAsync();
        await aborted.Task.WaitAsync(TimeSpan.FromSeconds(5), host.Token);
        try { await call; } catch (Exception) { /* Transport closure/cancellation is expected; process exit is asserted above. */ }
        host.AssertCleanOutput(); api.AssertHealthy();
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
