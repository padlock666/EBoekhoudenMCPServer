using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Xunit;

namespace EBoekhouden.IntegrationTests.SecretService;

public sealed class KeyringTests
{
    [LinuxTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AttributesSelectExactUtf8SecretAndCachedSessionAvoidsAnotherLookup(bool custom)
    {
        await using var service = await PrivateSecretService.StartAsync();
        var environment = service.Environment;
        if (custom)
        {
            var attributes = new { application = "SYNTHETIC app é", account = "SYNTHETIC account ü" };
            await service.CommandAsync(new { config = new { attributes } });
            environment["EBoekhouden__Keyring__Application"] = attributes.application;
            environment["EBoekhouden__Keyring__Account"] = attributes.account;
        }
        await using var api = await FakeUpstream.StartAsync();
        api.ExpectedCredential = " synthetic \\\" credential é ü ";
        await service.CommandAsync(new { config = new { secret = api.ExpectedCredential } });
        await using var host = await StdioProcess.StartAsync(api.Origin, environment);
        await host.ConnectAsync();
        Assert.Equal(10, (await host.Client.ListToolsAsync(cancellationToken: host.Token)).Count);
        lock (service.Events) Assert.DoesNotContain("search:SearchItems", service.Events);
        Assert.Empty(api.Requests);
        ExecutableTests.Success(await host.CallAsync("get_relation", """{"id":17}"""));
        await service.CommandAsync(new { config = new { scenario = "missing" } });
        ExecutableTests.Success(await host.CallAsync("get_relation", """{"id":17}"""));
        Assert.Equal(1, api.Sessions);
        lock (service.Events) Assert.Equal(1, service.Events.Count(e => e == "load:GetSecrets"));
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
        Assert.DoesNotContain(api.ExpectedCredential, host.Stdout + host.Stderr);
    }

    [LinuxTheory]
    [InlineData("missing")]
    [InlineData("locked")]
    [InlineData("denied")]
    [InlineData("denied_load")]
    [InlineData("deleted")]
    [InlineData("empty")]
    [InlineData("oversize")]
    [InlineData("invalid_utf8")]
    [InlineData("control")]
    [InlineData("newline")]
    [InlineData("blank")]
    public async Task UnavailableOrInvalidKeyringSecretFailsSafelyAndLaterLookupRecovers(string scenario)
    {
        await using var service = await PrivateSecretService.StartAsync();
        await service.CommandAsync(new { config = new { scenario } });
        await using var api = await FakeUpstream.StartAsync();
        await using var host = await StdioProcess.StartAsync(api.Origin, service.Environment);
        await host.ConnectAsync();
        var failure = ExecutableTests.Failure(await host.CallAsync("get_relation", """{"id":17}"""));
        Assert.Equal("CredentialUnavailable", failure.GetProperty("transportFailure").GetString());
        Assert.Equal(0, api.Sessions);
        Assert.Equal(0, api.Reads);
        await service.CommandAsync(new { config = new { scenario = "valid" } });
        ExecutableTests.Success(await host.CallAsync("get_relation", """{"id":17}"""));
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [LinuxFact]
    public async Task AnotherAccountDoesNotMatchAndUnavailableBusIsLazy()
    {
        await using var service = await PrivateSecretService.StartAsync();
        await using var api = await FakeUpstream.StartAsync();
        var environment = service.Environment;
        environment["EBoekhouden__Keyring__Account"] = "another-account";
        await using (var host = await StdioProcess.StartAsync(api.Origin, environment))
        {
            await host.ConnectAsync();
            var failure = ExecutableTests.Failure(await host.CallAsync("get_relation", """{"id":17}"""));
            Assert.Equal("CredentialUnavailable", failure.GetProperty("transportFailure").GetString());
            Assert.Empty(api.Requests);
            await host.CloseInputAndWaitAsync(); host.AssertCleanOutput();
        }
        environment = service.Environment;
        environment["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=" + service.DirectoryPath + "/missing-bus";
        await using (var host = await StdioProcess.StartAsync(api.Origin, environment))
        {
            await host.ConnectAsync();
            Assert.Equal(10, (await host.Client.ListToolsAsync(cancellationToken: host.Token)).Count);
            var failure = ExecutableTests.Failure(await host.CallAsync("get_relation", """{"id":17}"""));
            Assert.Equal("CredentialUnavailable", failure.GetProperty("transportFailure").GetString());
            Assert.Empty(api.Requests);
            await host.CloseInputAndWaitAsync(); host.AssertCleanOutput();
        }
    }

    [LinuxTheory]
    [InlineData("search")]
    [InlineData("load")]
    public async Task ProtocolCancellationAbortsNativeLookupAndReleasesTheRefreshGate(string stage)
    {
        await using var service = await PrivateSecretService.StartAsync();
        await service.CommandAsync(new { config = new { pause = stage } });
        await using var api = await FakeUpstream.StartAsync();
        await using var host = await StdioProcess.StartAsync(api.Origin, service.Environment);
        await host.ConnectAsync();
        var call = host.CallAsync("get_relation", """{"id":17}""").AsTask();
        await service.WaitAsync(StageEvent(stage));
        await CancelAsync(host);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5), host.Token));
        Assert.Equal(0, api.Sessions);
        await service.CommandAsync(new { release = true, config = new { pause = "" } });
        ExecutableTests.Success(await host.CallAsync("get_relation", """{"id":17}"""));
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [LinuxTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeDeadlineAndPendingShutdownDrainCanceledWork(bool operationDeadline)
    {
        await using var service = await PrivateSecretService.StartAsync();
        await service.CommandAsync(new { config = new { pause = "load" } });
        await using var api = await FakeUpstream.StartAsync();
        var environment = service.Environment;
        environment[operationDeadline ? "EBoekhouden__OperationTimeout" : "EBoekhouden__SessionTimeout"] = "00:00:01";
        if (operationDeadline) environment["EBoekhouden__AttemptTimeout"] = "00:00:01";
        await using var host = await StdioProcess.StartAsync(api.Origin, environment);
        await host.ConnectAsync();
        var failure = ExecutableTests.Failure(await host.CallAsync("get_relation", """{"id":17}"""));
        Assert.Equal("Timeout", failure.GetProperty("transportFailure").GetString());
        Assert.Equal(0, api.Sessions);
        // Late success is discarded; a later lookup does not inherit the canceled native state.
        await service.CommandAsync(new { release = true, config = new { pause = "" } });
        ExecutableTests.Success(await host.CallAsync("get_relation", """{"id":17}"""));
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [LinuxFact]
    public async Task CancellationWhileConnectingToAnUnresponsiveBusClosesTheNativeConnection()
    {
        await using var service = await PrivateSecretService.StartAsync(fake: false);
        await using var api = await FakeUpstream.StartAsync();
        var path = Path.Combine(service.DirectoryPath, "unresponsive");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path)); listener.Listen(1);
        var environment = service.Environment;
        environment["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=" + path;
        await using var host = await StdioProcess.StartAsync(api.Origin, environment);
        await host.ConnectAsync();
        var pending = host.CallAsync("get_relation", """{"id":17}""").AsTask();
        using var connected = await listener.AcceptAsync(host.Token);
        // Observe the native authentication handshake; never send a bus greeting.
        var buffer = new byte[4096];
        Assert.True(await connected.ReceiveAsync(buffer, SocketFlags.None, host.Token) > 0);
        await CancelAsync(host);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5), host.Token));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(host.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        Assert.Equal(0, await connected.ReceiveAsync(buffer, SocketFlags.None, deadline.Token));
        Assert.Equal(0, api.Sessions);
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [LinuxFact]
    public async Task CancellationDuringAuthenticationClearsTheGateAndTheNextSessionLooksUpAgain()
    {
        await using var service = await PrivateSecretService.StartAsync();
        await using var api = await FakeUpstream.StartAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Respond = async (context, session, token) =>
        {
            if (session == 1)
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { if (context.RequestAborted.IsCancellationRequested) aborted.TrySetResult(); }
            }
            return FakeUpstream.DefaultReply(context.Request, session);
        };
        await using var host = await StdioProcess.StartAsync(api.Origin, service.Environment);
        await host.ConnectAsync();
        var call = host.CallAsync("get_relation", """{"id":17}""").AsTask();
        await entered.Task.WaitAsync(host.Token);
        await CancelAsync(host);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5), host.Token));
        await aborted.Task.WaitAsync(TimeSpan.FromSeconds(5), host.Token);
        ExecutableTests.Success(await host.CallAsync("get_relation", """{"id":17}"""));
        lock (service.Events) Assert.Equal(2, service.Events.Count(e => e == "load:GetSecrets"));
        Assert.Equal(2, api.Sessions);
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [LinuxFact]
    public async Task RotationRetrievesAfreshAndRejectedSessionPostsAreNotRetried()
    {
        await using var service = await PrivateSecretService.StartAsync();
        await using var api = await FakeUpstream.StartAsync();
        var reject = false;
        api.Respond = (context, session, _) => Task.FromResult(session > 0
            ? reject ? new FakeUpstream.Reply(403, "{\"code\":\"SYNTHETIC_SECRET\",\"traceId\":\"synthetic-api-secret-PHASE6\"}") : FakeUpstream.Session(session)
            : reject ? new FakeUpstream.Reply(401, "{}") : FakeUpstream.DefaultReply(context.Request, session));
        await using var host = await StdioProcess.StartAsync(api.Origin, service.Environment);
        await host.ConnectAsync();
        ExecutableTests.Success(await host.CallAsync("get_relation", """{"id":17}"""));
        reject = true;
        api.ExpectedCredential = " synthetic rotated credential ";
        await service.CommandAsync(new { config = new { secret = api.ExpectedCredential } });
        var failure = ExecutableTests.Failure(await host.CallAsync("get_relation", """{"id":17}"""));
        Assert.Equal(403, failure.GetProperty("status").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, failure.GetProperty("code").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, failure.GetProperty("traceId").ValueKind);
        Assert.Equal(2, api.Sessions);
        reject = false;
        ExecutableTests.Success(await host.CallAsync("get_relation", """{"id":17}"""));
        Assert.Equal(3, api.Sessions);
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [LinuxTheory]
    [InlineData(1)]
    [InlineData(4096)]
    public async Task NativeByteBoundariesPreserveExactText(int bytes)
    {
        await using var service = await PrivateSecretService.StartAsync();
        await using var api = await FakeUpstream.StartAsync();
        api.ExpectedCredential = bytes == 1 ? "x" : " " + new string('é', 2047) + "z";
        await service.CommandAsync(new { config = new { secret = api.ExpectedCredential } });
        await using var host = await StdioProcess.StartAsync(api.Origin, service.Environment);
        await host.ConnectAsync();
        ExecutableTests.Success(await host.CallAsync("get_relation", """{"id":17}"""));
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [LinuxFact]
    public async Task ReadErrorsCannotReflectTheReleasedKeyringCredentialAsCodeOrTrace()
    {
        await using var service = await PrivateSecretService.StartAsync();
        await using var api = await FakeUpstream.StartAsync();
        api.ExpectedCredential = "SYNTHETIC_KEYRING_SECRET";
        await service.CommandAsync(new { config = new { secret = api.ExpectedCredential } });
        api.Respond = (_, session, _) => Task.FromResult(session > 0 ? FakeUpstream.Session(session)
            : new FakeUpstream.Reply(403, "{\"code\":\"SYNTHETIC_KEYRING_SECRET\",\"traceId\":\"SYNTHETIC_KEYRING_SECRET\"}"));
        await using var host = await StdioProcess.StartAsync(api.Origin, service.Environment);
        await host.ConnectAsync();
        var error = ExecutableTests.Failure(await host.CallAsync("get_relation", """{"id":17}"""));
        Assert.Equal(403, error.GetProperty("status").GetInt32());
        Assert.DoesNotContain(api.ExpectedCredential, error.GetRawText());
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [LinuxFact]
    public async Task MissingBusDoesNotBreakDiscoveryAndFailuresAreSanitized()
    {
        await using var service = await PrivateSecretService.StartAsync();
        await using var api = await FakeUpstream.StartAsync();
        var environment = service.Environment;
        environment["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=/nonexistent/synthetic-bus";
        await using var host = await StdioProcess.StartAsync(api.Origin, environment);
        await host.ConnectAsync();
        Assert.Equal(10, (await host.Client.ListToolsAsync(cancellationToken: host.Token)).Count);
        var failure = ExecutableTests.Failure(await host.CallAsync("get_relation", """{"id":17}"""));
        Assert.Equal("CredentialUnavailable", failure.GetProperty("transportFailure").GetString());
        Assert.DoesNotContain("synthetic-bus", failure.GetRawText());
        Assert.Equal(0, api.Sessions);
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [LinuxFact]
    public async Task TlsCertificateRejectionHappensBeforeAnySecretServiceLookup()
    {
        await using var service = await PrivateSecretService.StartAsync();
        await using var api = await FakeUpstream.StartAsync(rejectTls: true);
        await using var host = await StdioProcess.StartAsync(api.Origin, service.Environment);
        await host.ConnectAsync();
        var failure = ExecutableTests.Failure(await host.CallAsync("get_relation", """{"id":17}"""));
        Assert.Equal("Network", failure.GetProperty("transportFailure").GetString());
        lock (service.Events) Assert.DoesNotContain(service.Events, e => e.Contains(':'));
        Assert.Equal(0, api.Sessions);
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput();
    }

    private static async Task CancelAsync(StdioProcess host)
    {
        var id = host.Sent.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!.AsObject())
            .Last(node => node["method"]?.GetValue<string>() == "tools/call")["id"]!.DeepClone();
        await host.Client.SendMessageAsync(new JsonRpcNotification
        { Method = "notifications/cancelled", Params = new JsonObject { ["requestId"] = id } }, host.Token);
    }

    private static string StageEvent(string stage) => stage + ":" + (stage switch
    { "search" => "SearchItems", _ => "GetSecrets" });
}
