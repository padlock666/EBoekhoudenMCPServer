using System.IO.Pipelines;
using EBoekhouden.Api;
using EBoekhouden.Host.Stdio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace EBoekhouden.Mcp.Tests;

public sealed class HostTests
{
    [Fact]
    public async Task GenericHostStartsLazilyRunsToolsWithFakeHttpAndStops()
    {
        var input = new Pipe();
        var output = new Pipe();
        var builder = HostComposition.CreateBuilder([]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["EBoekhouden:AccessToken"] = "synthetic-credential", ["EBoekhouden:Source"] = "Tests" });
        builder.Services.RemoveAll<ITransport>();
        builder.Services.AddSingleton<ITransport>(new StreamServerTransport(input.Reader.AsStream(), output.Writer.AsStream()));
        var sends = 0;
        builder.Services.AddHttpClient(ApiServiceCollectionExtensions.SessionClientName).ConfigurePrimaryHttpMessageHandler(() => new Handler(request =>
        {
            Interlocked.Increment(ref sends);
            Assert.Equal("/v1/session", request.RequestUri!.AbsolutePath);
            return ProtocolHarness.Json("""{"token":"synthetic-session","expiresIn":3600}""");
        }));
        builder.Services.AddHttpClient(ApiServiceCollectionExtensions.ReadClientName).ConfigurePrimaryHttpMessageHandler(() => new Handler(request =>
        {
            Interlocked.Increment(ref sends);
            Assert.Equal("/v1/relation/17", request.RequestUri!.AbsolutePath);
            return ProtocolHarness.Json("""{"id":17,"code":"SYNTHETIC"}""");
        }));
        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        await using (var client = await McpClient.CreateAsync(new StreamClientTransport(input.Writer.AsStream(), output.Reader.AsStream()), cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal(10, (await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Count);
            Assert.Equal(0, sends);
            var result = await client.CallToolAsync("get_relation", ToolExecutionTests.Args("""{"id":17}"""), cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotEqual(true, result.IsError);
            Assert.Equal("SYNTHETIC", result.StructuredContent!.Value.GetProperty("code").GetString());
            Assert.Equal(2, sends);
        }
        await host.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartupRejectsMissingConfigurationWithoutHttpAndLoggingCannotExposePayloads()
    {
        var builder = HostComposition.CreateBuilder([]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EBoekhouden:AccessToken"] = "", ["EBoekhouden:Source"] = "",
            ["Logging:LogLevel:Default"] = "Trace", ["Logging:LogLevel:ModelContextProtocol"] = "Trace",
            ["Logging:Console:LogToStandardErrorThreshold"] = "None",
        });
        var sends = 0;
        foreach (var name in new[] { ApiServiceCollectionExtensions.SessionClientName, ApiServiceCollectionExtensions.ReadClientName })
            builder.Services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new Handler(_ =>
            { Interlocked.Increment(ref sends); throw new InvalidOperationException("Unexpected network access."); }));
        using var host = builder.Build();
        Assert.Equal(LogLevel.Trace, host.Services.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value.LogToStandardErrorThreshold);
        var factory = host.Services.GetRequiredService<ILoggerFactory>();
        Assert.False(factory.CreateLogger("ModelContextProtocol.Server").IsEnabled(LogLevel.Critical));
        Assert.False(factory.CreateLogger("Microsoft.Extensions.Hosting.Internal.Host").IsEnabled(LogLevel.Critical));
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, sends);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
