using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EBoekhouden.IntegrationTests;

// Real loopback HTTP, entirely invented data. No request/body is written to test output.
internal sealed class FakeUpstream : IAsyncDisposable
{
    internal const string Credential = "synthetic-api-secret-PHASE6";
    internal const string SessionPrefix = "synthetic-session-secret-";
    internal const string PrivateData = "synthetic-private-financial-data";
    private readonly WebApplication _app;
    private X509Certificate2? _certificate;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentQueue<Exception> _failures = new();
    private int _sessions;
    internal Uri Origin { get; private set; } = null!;
    internal ConcurrentQueue<SeenRequest> Requests { get; } = new();
    internal int Sessions => Volatile.Read(ref _sessions);
    internal string ExpectedCredential { get; set; } = Credential;
    internal int Reads => Requests.Count(r => r.Method == "GET");
    internal Func<HttpContext, int, CancellationToken, Task<Reply>>? Respond { get; set; }

    private FakeUpstream(WebApplication app) => _app = app;

    internal static async Task<FakeUpstream> StartAsync(bool rejectTls = false)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "IntegrationTest", Args = [] });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        using var key = rejectTls ? RSA.Create(2048) : null;
        var certificate = key is null ? null : new CertificateRequest("CN=synthetic-untrusted", key, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0, listen =>
        {
            if (certificate is not null) listen.UseHttps(certificate);
        }));
        var fake = new FakeUpstream(builder.Build());
        fake._certificate = certificate;
        fake._app.Run(fake.HandleAsync);
        try
        {
            await fake._app.StartAsync(Xunit.TestContext.Current.CancellationToken);
            fake.Origin = new Uri(fake._app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            return fake;
        }
        catch { await fake.DisposeAsync(); throw; }
    }

    private async Task HandleAsync(HttpContext context)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _shutdown.Token);
        var token = cancellation.Token;
        var request = context.Request;
        var sessionNumber = 0;
        try
        {
            if (request.Method == "POST" && request.Path == "/v1/session")
            {
                using var body = await JsonDocument.ParseAsync(request.Body, cancellationToken: token);
                if (body.RootElement.GetProperty("accessToken").GetString() != ExpectedCredential ||
                    body.RootElement.GetProperty("source").GetString() != "Tests" || request.Headers.Authorization.Count != 0)
                    throw new InvalidDataException("Incorrect synthetic session request.");
                sessionNumber = Interlocked.Increment(ref _sessions);
            }
            else if (request.Method != "GET" || !request.Headers.Authorization.ToString().StartsWith("Bearer " + SessionPrefix, StringComparison.Ordinal))
                throw new InvalidDataException("Unexpected method or authorization at the fake upstream.");
            Requests.Enqueue(new SeenRequest(request.Method, context.Features.Get<IHttpRequestFeature>()!.RawTarget!, request.Headers.Authorization.ToString()));
            var reply = Respond is { } respond ? await respond(context, sessionNumber, token) : DefaultReply(request, sessionNumber);
            context.Response.StatusCode = reply.Status;
            context.Response.ContentType = "application/json";
            if (reply.Location is not null) context.Response.Headers.Location = reply.Location;
            await context.Response.WriteAsync(reply.Json, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            _failures.Enqueue(error);
            context.Abort();
        }
    }

    internal static Reply Session(int number, int expiresIn = 3600) => new(200,
        JsonSerializer.Serialize(new { token = SessionPrefix + number, expiresIn }));

    internal static Reply DefaultReply(HttpRequest request, int sessionNumber)
    {
        if (sessionNumber > 0) return Session(sessionNumber);
        var fixture = request.Path.Value switch
        {
            "/v1/relation" => "relations-list", "/v1/relation/17" => "relation-detail",
            "/v1/invoice" => "invoices-list", "/v1/invoice/23" => "invoice-detail",
            "/v1/mutation" => "mutations-list", "/v1/mutation/12" => "mutation-detail",
            "/v1/mutation/invoice/outstanding" => "outstanding-list",
            "/v1/ledger" => "ledgers-list", "/v1/ledger/7" => "ledger-detail",
            "/v1/ledger/balances" => "balances-all", "/v1/ledger/7/balance" => "balance-single",
            _ => throw new InvalidDataException("Unapproved route at fake upstream."),
        };
        using var stream = typeof(FakeUpstream).Assembly.GetManifestResourceStream("EBoekhouden.IntegrationTests.Fixtures." + fixture + ".synthetic.json")!;
        using var reader = new StreamReader(stream);
        return new Reply(200, reader.ReadToEnd());
    }

    internal void AssertHealthy() => Xunit.Assert.Empty(_failures);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _shutdown.CancelAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _app.StopAsync(timeout.Token);
        }
        finally { await _app.DisposeAsync(); _certificate?.Dispose(); _shutdown.Dispose(); }
    }

    internal sealed record SeenRequest(string Method, string Target, string Authorization);
    internal sealed record Reply(int Status, string Json, string? Location = null);
}
