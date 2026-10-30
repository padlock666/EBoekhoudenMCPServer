using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;

namespace EBoekhouden.Host.Stdio;

// SDK 2.3.0 drains in-flight handlers before its single-session service stops the host.
// On EOF, cancel the host first so a pending HTTP operation cannot delay disconnection.
internal sealed class StdioDisconnectService(ITransport transport, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await transport.MessageReader.Completion.WaitAsync(stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        catch (Exception) { /* A failed transport also ends this single-session host. Never log its payload. */ }
        lifetime.StopApplication();
    }
}
