using Xunit;

namespace EBoekhouden.IntegrationTests.SecretService;

public sealed class IsolatedGnomeTests
{
    public static bool RunIsolatedGnome => OperatingSystem.IsLinux() &&
        Environment.GetEnvironmentVariable("EBOEKHOUDEN_TEST_REAL_GNOME") == "1";

    [Fact(Skip = "Set EBOEKHOUDEN_TEST_REAL_GNOME=1 for the isolated GNOME test.", SkipUnless = nameof(RunIsolatedGnome))]
    public async Task ActualGnomeDaemonInPrivateHomeAndBusAuthenticatesTheSyntheticCredential()
    {
        await using var bus = await PrivateSecretService.StartAsync(fake: false);
        using var daemon = bus.Start("/usr/bin/gnome-keyring-daemon", "--foreground", "--components=secrets", "--unlock",
            "--control-directory=" + bus.DirectoryPath + "/control");
        var stdout = daemon.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = daemon.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await daemon.StandardInput.WriteAsync("synthetic-private-keyring-unlock\n");
            daemon.StandardInput.Close();
            using (var ready = bus.Start("/usr/bin/gdbus", "wait", "--session", "--timeout", "5", "org.freedesktop.secrets"))
            {
                await ready.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
                Assert.Equal(0, ready.ExitCode);
            }
            using var provision = bus.Start("/usr/bin/secret-tool", "store", "--label=SYNTHETIC MCP TEST ONLY", "application", "EBoekhoudenMcp", "account", "api");
            await provision.StandardInput.WriteAsync(FakeUpstream.Credential);
            provision.StandardInput.Close();
            var provisionOut = provision.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var provisionErr = provision.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            try { await provision.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
            finally
            {
                if (!provision.HasExited) provision.Kill(entireProcessTree: true);
                await provision.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            Assert.True(provision.ExitCode == 0, await provisionErr);
            Assert.Equal("", (await provisionOut).Trim());
            Assert.Equal("", await provisionErr);
            await using var api = await FakeUpstream.StartAsync();
            await using var host = await StdioProcess.StartAsync(api.Origin, bus.Environment);
            await host.ConnectAsync();
            ExecutableTests.Success(await host.CallAsync("get_relation", """{"id":17}"""));
            Assert.Equal(1, api.Sessions);
            await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
        }
        finally
        {
            // Stop only the process started by this test, before its bus/home are removed.
            if (!daemon.HasExited) daemon.Kill(entireProcessTree: true);
            await daemon.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.DoesNotContain(FakeUpstream.Credential, await stdout);
            Assert.DoesNotContain(FakeUpstream.Credential, await stderr);
        }
    }
}
