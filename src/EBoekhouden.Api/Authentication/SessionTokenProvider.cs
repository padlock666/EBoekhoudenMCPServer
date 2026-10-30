using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using EBoekhouden.Api.Configuration;
using EBoekhouden.Api.Contracts;
using EBoekhouden.Api.Errors;
using EBoekhouden.Api.Http;
using Microsoft.Extensions.Options;

namespace EBoekhouden.Api.Authentication;

internal sealed class SessionToken(string value, DateTimeOffset refreshAt)
{
    internal string Value { get; } = value;
    internal DateTimeOffset RefreshAt { get; } = refreshAt;
    public override string ToString() => nameof(SessionToken);
}

internal sealed partial class SessionTokenProvider(
    IHttpClientFactory factory, IOptions<EBoekhoudenOptions> options, TimeProvider time) : IDisposable
{
    private readonly EBoekhoudenOptions _settings = options.Value;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private SessionToken? _cached;

    public async Task<SessionToken> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var token = Volatile.Read(ref _cached);
        if (token is not null && time.GetUtcNow() < token.RefreshAt) return token;
        await _refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            token = Volatile.Read(ref _cached);
            if (token is not null && time.GetUtcNow() < token.RefreshAt) return token;
            token = await CreateAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _cached, token);
            return token;
        }
        finally { _refresh.Release(); }
    }

    public void Invalidate(SessionToken failed) => Interlocked.CompareExchange(ref _cached, null, failed);

    private async Task<SessionToken> CreateAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_settings.SessionTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var client = factory.CreateClient(ApiServiceCollectionExtensions.SessionClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/session")
        {
            Content = JsonContent.Create(new SessionRequest(_settings.AccessToken, _settings.Source), options: ApiJson.Options),
        };
        var started = time.GetUtcNow();
        HttpStatusCode? receivedStatus = null;
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            receivedStatus = response.StatusCode;
            await ResponseBuffer.LoadAsync(response, client.MaxResponseContentBufferSize, linked.Token).ConfigureAwait(false);
            response.RequestMessage = request;
            linked.Token.ThrowIfCancellationRequested();
            if (!response.IsSuccessStatusCode)
            {
                var metadata = await ErrorMetadata.ReadAsync(response, _settings.AccessToken, linked.Token).ConfigureAwait(false);
                throw new ApiTransportException(TransportFailure.SessionRejected, response.StatusCode, metadata.Code, metadata.TraceId);
            }
            var session = await response.Content.ReadFromJsonAsync<SessionResponse>(ApiJson.Options, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            if (session is null || session.ExpiresIn <= 0 || string.IsNullOrEmpty(session.Token) || !BearerToken().IsMatch(session.Token))
                throw new ApiTransportException(TransportFailure.InvalidSession, response.StatusCode);
            var lifetime = TimeSpan.FromSeconds(session.ExpiresIn);
            var skew = TimeSpan.FromTicks(Math.Min(_settings.RefreshBeforeExpiry.Ticks, lifetime.Ticks / 10));
            var refreshAt = started + lifetime - skew;
            if (time.GetUtcNow() >= refreshAt) throw new ApiTransportException(TransportFailure.InvalidSession, response.StatusCode);
            return new SessionToken(session.Token, refreshAt);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ApiTransportException(TransportFailure.Timeout); }
        catch (HttpRequestException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new ApiTransportException(timeout.IsCancellationRequested ? TransportFailure.Timeout : TransportFailure.Network);
        }
        catch (JsonException) { throw new ApiTransportException(TransportFailure.InvalidSession, receivedStatus); }
    }

    public void Dispose() => _refresh.Dispose();

    [GeneratedRegex(@"\A[A-Za-z0-9\-._~+/]+=*\z", RegexOptions.CultureInvariant)]
    private static partial Regex BearerToken();

    private sealed class SessionRequest(string accessToken, string source)
    {
        public string AccessToken { get; } = accessToken;
        public string Source { get; } = source;
    }

    private sealed class SessionResponse
    {
        public required string Token { get; init; }
        public required int ExpiresIn { get; init; }
    }
}
