using System.Collections.Concurrent;
using System.Net;
using EBoekhouden.Api.Authentication;
using EBoekhouden.Api.Configuration;
using EBoekhouden.Api.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace EBoekhouden.Api.Tests;

internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);
    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        Requests.Enqueue(request);
        return send(request, cancellationToken);
    }
}

internal sealed class CapturedLogs : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Sink(Messages);
    public void Dispose() { }
    private sealed class Sink(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
    }
}

internal sealed class HttpHarness : IDisposable
{
    public const string Credential = "synthetic-api-credential";
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    public StubHandler Sessions { get; }
    public StubHandler Reads { get; }
    public CapturedLogs Logs { get; } = new();
    public ServiceProvider Services { get; }
    public SessionTokenProvider Tokens => Services.GetRequiredService<SessionTokenProvider>();
    public ReadTransport Transport => Services.GetRequiredService<ReadTransport>();

    public HttpHarness(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? session = null,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? read = null,
        Action<EBoekhoudenOptions>? configure = null)
    {
        Sessions = new(session ?? ((_, _) => Task.FromResult(Session())));
        Reads = new(read ?? ((_, _) => Task.FromResult(Response(HttpStatusCode.OK))));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Time);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        services.AddSingleton<IPasswordSource, SyntheticPasswordSource>();
        services.AddEBoekhoudenApiForTesting(Configuration(), new Uri("http://127.0.0.1:12345/"));
        if (configure is not null) services.Configure(configure);
        services.AddHttpClient(ApiServiceCollectionExtensions.SessionClientName).ConfigurePrimaryHttpMessageHandler(() => Sessions);
        services.AddHttpClient(ApiServiceCollectionExtensions.ReadClientName).ConfigurePrimaryHttpMessageHandler(() => Reads);
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public static IConfiguration Configuration(Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["EBoekhouden:Source"] = "Tests",
        };
        if (overrides is not null) foreach (var item in overrides) values[item.Key] = item.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    public static HttpResponseMessage Session(string token = "synthetic-session", int seconds = 3600) =>
        new(HttpStatusCode.OK) { Content = new StringContent($$"""{"token":"{{token}}","expiresIn":{{seconds}}}""") };
    public static HttpResponseMessage Response(HttpStatusCode status) => new(status) { Content = new StringContent("synthetic-body") };
    public static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Yield to asynchronous continuations between virtual-time advances; no real timeout is being tested here.
    public async Task<T> CompleteWithTimeAsync<T>(Task<T> task)
    {
        for (var i = 0; i < 100 && !task.IsCompleted; i++)
        {
            Time.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(1);
        }
        return await task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public void Dispose() => Services.Dispose();
}

internal sealed class SyntheticPasswordSource : IPasswordSource
{
    public ValueTask<Password> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Password.FromUtf8(System.Text.Encoding.UTF8.GetBytes(HttpHarness.Credential)));
    }
}
