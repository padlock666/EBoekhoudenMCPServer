using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using EBoekhouden.Api;
using EBoekhouden.Api.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EBoekhouden.Mcp.Tests;

internal sealed class ProtocolHarness : IAsyncDisposable
{
    private readonly Pipe _requests = new();
    private readonly Pipe _responses = new();
    private readonly CancellationTokenSource _shutdown = CancellationTokenSource.CreateLinkedTokenSource(Xunit.TestContext.Current.CancellationToken);
    private readonly ServiceProvider _services;
    private readonly Task _run;
    private int _sessions;
    private int _reads;
    public int Sessions => Volatile.Read(ref _sessions);
    public int Reads => Volatile.Read(ref _reads);
    public ConcurrentQueue<string> Requests { get; } = new();
    public McpServer Server { get; }
    public McpClient Client { get; private set; } = null!;
    public IServiceProvider Services => _services;

    private ProtocolHarness(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? read)
    {
        _shutdown.CancelAfter(TimeSpan.FromSeconds(20));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPasswordSource, SyntheticPasswordSource>();
        services.AddEBoekhoudenApiForTesting(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["EBoekhouden:Source"] = "Tests" }).Build(), new Uri("http://127.0.0.1:12345/"));
        services.AddHttpClient(ApiServiceCollectionExtensions.SessionClientName).ConfigurePrimaryHttpMessageHandler(() => new Handler((_, _) =>
        {
            Interlocked.Increment(ref _sessions);
            return Task.FromResult(Json("""{"token":"synthetic-session","expiresIn":3600}"""));
        }));
        services.AddHttpClient(ApiServiceCollectionExtensions.ReadClientName).ConfigurePrimaryHttpMessageHandler(() => new Handler((request, token) =>
        {
            Interlocked.Increment(ref _reads);
            Requests.Enqueue(request.RequestUri!.PathAndQuery);
            return read is null ? Task.FromResult(DefaultResponse(request)) : read(request, token);
        }));
        services.AddEBoekhoudenMcp().WithStreamServerTransport(_requests.Reader.AsStream(), _responses.Writer.AsStream());
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Server = _services.GetRequiredService<McpServer>();
        _run = Server.RunAsync(_shutdown.Token);
    }

    public static async Task<ProtocolHarness> StartAsync(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? read = null)
    {
        var harness = new ProtocolHarness(read);
        try
        {
            harness.Client = await McpClient.CreateAsync(new StreamClientTransport(harness._requests.Writer.AsStream(), harness._responses.Reader.AsStream()),
                cancellationToken: harness._shutdown.Token);
            return harness;
        }
        catch { await harness.DisposeAsync(); throw; }
    }

    public static HttpResponseMessage Json(string text, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(text, System.Text.Encoding.UTF8, "application/json") };
    private static HttpResponseMessage DefaultResponse(HttpRequestMessage request)
    {
        var fixture = request.RequestUri!.AbsolutePath switch
        {
            "/v1/relation" => "relations-list", "/v1/relation/17" => "relation-detail",
            "/v1/invoice" => "invoices-list", "/v1/invoice/23" => "invoice-detail",
            "/v1/mutation" => "mutations-list", "/v1/mutation/12" => "mutation-detail",
            "/v1/mutation/invoice/outstanding" => "outstanding-list",
            "/v1/ledger" => "ledgers-list", "/v1/ledger/7" => "ledger-detail",
            "/v1/ledger/balances" => "balances-all", "/v1/ledger/7/balance" => "balance-single",
            _ => throw new InvalidOperationException("Unexpected synthetic route."),
        };
        using var stream = typeof(ProtocolHarness).Assembly.GetManifestResourceStream("EBoekhouden.Mcp.Tests.Fixtures." + fixture + ".synthetic.json")!;
        using var reader = new StreamReader(stream);
        return Json(reader.ReadToEnd());
    }

    public async ValueTask DisposeAsync()
    {
        if (Client is not null) await Client.DisposeAsync();
        await _shutdown.CancelAsync();
        try { await _run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { }
        await _services.DisposeAsync();
        _shutdown.Dispose();
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}

internal sealed class SyntheticPasswordSource : IPasswordSource
{
    public ValueTask<Password> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Password.FromUtf8("synthetic-api-credential"u8));
    }
}
