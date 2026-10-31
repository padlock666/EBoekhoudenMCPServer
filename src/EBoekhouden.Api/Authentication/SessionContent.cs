using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace EBoekhouden.Api.Authentication;

// HttpClient invokes this only after connecting and validating TLS. Never prebuffer this content.
// A session request cannot replay or serialize twice, including a transport-internal replay.
internal sealed class SessionContent : HttpContent
{
    private readonly IPasswordSource _passwords;
    private readonly string _source;
    private readonly CancellationToken _operation;
    private int _serialized;
    internal Exception? Failure { get; private set; }

    internal SessionContent(IPasswordSource passwords, string source, CancellationToken operation)
    {
        _passwords = passwords; _source = source; _operation = operation;
        Headers.ContentType = new MediaTypeHeaderValue("application/json");
    }

    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, _operation);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_operation, cancellationToken);
        using var body = new MemoryStream();
        try
        {
            if (Interlocked.Exchange(ref _serialized, 1) != 0) throw Password.Unavailable();
            Password password;
            try { password = await _passwords.GetAsync(linked.Token).ConfigureAwait(false); }
            catch (Exception) when (!linked.IsCancellationRequested)
            {
                Failure = Password.Unavailable();
                throw Failure;
            }
            using (password)
            {
                linked.Token.ThrowIfCancellationRequested();
                using var writer = new Utf8JsonWriter(body);
                writer.WriteStartObject();
                writer.WriteString("accessToken", password.Bytes);
                writer.WriteString("source", _source);
                writer.WriteEndObject();
                writer.Flush();
            }
            await stream.WriteAsync(body.GetBuffer().AsMemory(0, checked((int)body.Length)), linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Failure = new OperationCanceledException("Credential operation canceled.", linked.Token);
            throw Failure;
        }
        finally { CryptographicOperations.ZeroMemory(body.GetBuffer()); }
    }
}
