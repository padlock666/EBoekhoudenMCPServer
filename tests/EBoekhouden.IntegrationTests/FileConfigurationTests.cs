using System.Text.Json.Nodes;
using Xunit;

namespace EBoekhouden.IntegrationTests;

public sealed class FileConfigurationTests
{
    [Fact]
    public async Task NonsecretConfigLoadsFromAbsolutePathInAnotherWorkingDirectory()
    {
        using var files = new ConfigFiles();
        await using var api = await FakeUpstream.StartAsync();
        await using var host = await StdioProcess.StartAsync(api.Origin,
            new Dictionary<string, string?> { ["EBoekhouden__Source"] = null }, ["--config", files.ConfigPath]);
        await host.ConnectAsync();
        Assert.Equal(10, (await host.Client.ListToolsAsync(cancellationToken: host.Token)).Count);
        Assert.Empty(api.Requests);
        ExecutableTests.Success(await host.CallAsync("get_relation", """{"id":17}"""));
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
        Assert.DoesNotContain(files.ConfigPath, host.Stderr);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnvironmentAndCommandLineOverrideNonsecretJsonSettings(bool commandLine)
    {
        using var files = new ConfigFiles();
        files.Document["EBoekhouden"]!["Source"] = "Invalid-source-value";
        files.Save();
        var environment = new Dictionary<string, string?> { ["EBoekhouden__Source"] = null };
        var arguments = new List<string> { "--config=" + files.ConfigPath };
        if (commandLine) arguments.Add("--EBoekhouden:Source=Tests");
        else environment["EBoekhouden__Source"] = "Tests";
        await using var api = await FakeUpstream.StartAsync();
        await using var host = await StdioProcess.StartAsync(api.Origin, environment, arguments);
        await host.ConnectAsync();
        ExecutableTests.Success(await host.CallAsync("get_relation", """{"id":17}"""));
        await host.CloseInputAndWaitAsync(); host.AssertCleanOutput(); api.AssertHealthy();
    }

    [Theory]
    [InlineData("missing-config")]
    [InlineData("relative-config")]
    [InlineData("malformed-json")]
    [InlineData("duplicate-field")]
    [InlineData("unknown-setting")]
    [InlineData("AccessToken")]
    [InlineData("PasswordFile")]
    [InlineData("PasswordSource")]
    [InlineData("null-keyring")]
    public async Task InvalidOrRemovedConfigurationFailsWithStaticStderrAndNoHttp(string scenario)
    {
        using var files = new ConfigFiles();
        var path = files.ConfigPath;
        switch (scenario)
        {
            case "missing-config": path += ".missing"; break;
            case "relative-config": path = "config.json"; break;
            case "malformed-json": File.WriteAllText(path, "{" + FakeUpstream.Credential); break;
            case "duplicate-field": File.WriteAllText(path, """{"EBoekhouden":{"Keyring":{"Account":"one","Account":"two"}}}"""); break;
            case "unknown-setting": files.Document["EBoekhouden"]!["Unknown"] = FakeUpstream.Credential; files.Save(); break;
            case "null-keyring": files.Document["EBoekhouden"]!["Keyring"] = null; files.Save(); break;
            default: files.Document["EBoekhouden"]![scenario] = FakeUpstream.Credential; files.Save(); break;
        }
        await using var api = await FakeUpstream.StartAsync();
        await using var host = await StdioProcess.StartAsync(api.Origin, arguments: ["--config", path]);
        await host.WaitForExitAsync();
        Assert.Equal(1, host.ExitCode);
        Assert.Equal("", host.Stdout);
        Assert.Equal("The e-Boekhouden MCP host could not start or continue. Check external configuration and transport setup.", host.Stderr.Trim());
        Assert.Empty(api.Requests); api.AssertHealthy();
    }

    private sealed class ConfigFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "eboekhouden config é " + Guid.NewGuid().ToString("N"));
        internal string ConfigPath => Path.Combine(_directory, "config.json");
        internal JsonObject Document { get; } = new()
        {
            ["EBoekhouden"] = new JsonObject
            {
                ["Source"] = "Tests",
                ["Keyring"] = new JsonObject { ["Application"] = "EBoekhoudenMcp", ["Account"] = "api" },
            },
        };
        internal ConfigFiles() { Directory.CreateDirectory(_directory); Save(); }
        internal void Save() => File.WriteAllText(ConfigPath, Document.ToJsonString());
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
