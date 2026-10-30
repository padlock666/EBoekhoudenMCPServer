using EBoekhouden.Host.Stdio;
using Microsoft.Extensions.Hosting;

try
{
    using var host = HostComposition.CreateBuilder(args).Build();
    await host.RunAsync().ConfigureAwait(false);
    return 0;
}
catch (OperationCanceledException) { return 0; }
catch (Exception)
{
    Console.Error.WriteLine("The e-Boekhouden MCP host could not start or continue. Check external configuration and transport setup.");
    return 1;
}
