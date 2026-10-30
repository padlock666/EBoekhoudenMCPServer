using EBoekhouden.Api;
using EBoekhouden.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace EBoekhouden.Host.Stdio;

public static class HostComposition
{
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });
        builder.Services.PostConfigure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        // SDK Trace logs include complete protocol payloads. Configuration must not re-enable them.
        builder.Services.PostConfigure<LoggerFilterOptions>(options =>
        {
            options.Rules.Clear();
            options.MinLevel = LogLevel.Information;
            options.Rules.Add(new LoggerFilterRule(null, "ModelContextProtocol", LogLevel.None, null));
            // Generic Host startup errors can contain configuration binding values/inner exceptions.
            options.Rules.Add(new LoggerFilterRule(null, "Microsoft.Extensions.Hosting.Internal.Host", LogLevel.None, null));
        });
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(10));
        var testOrigin = builder.Configuration["EBoekhoudenTest:UpstreamOrigin"];
        if (builder.Environment.IsEnvironment("IntegrationTest"))
        {
            // Explicit, fail-closed executable test seam; the API registration also requires numeric loopback.
            if (!Uri.TryCreate(testOrigin, UriKind.Absolute, out var origin))
                throw new InvalidOperationException("IntegrationTest requires a loopback upstream origin.");
            builder.Services.AddEBoekhoudenApiForTesting(builder.Configuration, origin);
        }
        else
        {
            if (testOrigin is not null)
                throw new InvalidOperationException("Upstream origin override requires IntegrationTest.");
            builder.Services.AddEBoekhoudenApi(builder.Configuration);
        }
        builder.Services.AddEBoekhoudenMcp().WithStdioServerTransport();
        builder.Services.AddHostedService<StdioDisconnectService>();
        return builder;
    }
}
