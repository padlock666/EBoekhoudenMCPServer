using System.Net;
using System.Net.Http.Headers;
using EBoekhouden.Api.Authentication;
using EBoekhouden.Api.Configuration;
using Microsoft.Extensions.Options;

namespace EBoekhouden.Api.Http;

public sealed class ReadTransport : IDisposable
{
    private readonly IHttpClientFactory _factory;
    private readonly SessionTokenProvider _tokens;
    private readonly EBoekhoudenOptions _settings;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _concurrency;
    private readonly ReadCircuit _circuit;

    internal ReadTransport(IHttpClientFactory factory, SessionTokenProvider tokens,
        IOptions<EBoekhoudenOptions> options, TimeProvider time)
    {
        _factory = factory;
        _tokens = tokens;
        _settings = options.Value;
        _time = time;
        _concurrency = new(_settings.MaxConcurrentReads, _settings.MaxConcurrentReads);
        _circuit = new(time, _settings.CircuitFailureThreshold, _settings.CircuitBreakDuration);
    }

    // Content is fully buffered under the timeout and concurrency budget. Caller owns the response.
    public async Task<HttpResponseMessage> SendAsync(ReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(_settings.OperationTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var entered = false;
        try
        {
            await _concurrency.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            return await SendCoreAsync(request, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ApiTransportException(TransportFailure.Timeout); }
        catch (OperationCanceledException)
        { throw new OperationCanceledException("e-Boekhouden read canceled.", cancellationToken); }
        finally { if (entered) _concurrency.Release(); }
    }

    private async Task<HttpResponseMessage> SendCoreAsync(ReadRequest read, CancellationToken cancellationToken)
    {
        using var client = _factory.CreateClient(ApiServiceCollectionExtensions.ReadClientName);
        var authenticationReplayed = false;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var permit = _circuit.Enter();
            var token = await _tokens.GetAsync(cancellationToken).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(_settings.AttemptTimeout, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var message = new HttpRequestMessage(HttpMethod.Get, read.PathAndQuery);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
                try { await ResponseBuffer.LoadAsync(response, client.MaxResponseContentBufferSize, linked.Token).ConfigureAwait(false); }
                catch { response.Dispose(); throw; }
                response.RequestMessage = message;
                if (linked.IsCancellationRequested)
                {
                    response.Dispose();
                    linked.Token.ThrowIfCancellationRequested();
                }
            }
            catch (HttpRequestException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                permit.Failure();
                if (attempt == _settings.MaxReadAttempts)
                    throw new ApiTransportException(timeout.IsCancellationRequested ? TransportFailure.Timeout : TransportFailure.Network);
                await DelayAsync(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                permit.Failure();
                if (attempt == _settings.MaxReadAttempts) throw new ApiTransportException(TransportFailure.Timeout);
                await DelayAsync(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                permit.Success();
                _tokens.Invalidate(token);
                if (!authenticationReplayed && attempt < _settings.MaxReadAttempts)
                {
                    authenticationReplayed = true;
                    response.Dispose();
                    continue;
                }
                return response;
            }
            if (!IsTransient(response.StatusCode))
            {
                permit.Success();
                return response;
            }

            permit.Failure();
            var delay = RetryDelay(response, attempt);
            if (attempt == _settings.MaxReadAttempts || delay > _settings.MaxRetryDelay) return response;
            response.Dispose();
            await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(Math.Min(_settings.MaxRetryDelay.TotalMilliseconds,
        _settings.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1) * (0.5 + Random.Shared.NextDouble())));

    private TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var backoff = Backoff(attempt);
        var retryAfter = response.Headers.RetryAfter;
        var server = retryAfter?.Delta ?? (retryAfter?.Date - _time.GetUtcNow());
        return server is { } delay && delay > backoff ? delay : backoff;
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, _time, cancellationToken);
    private static bool IsTransient(HttpStatusCode status) => status is HttpStatusCode.RequestTimeout or
        HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    public void Dispose() => _concurrency.Dispose();
}
