using System.Security.Cryptography;
using System.Text;
using EBoekhouden.Api.Http;

namespace EBoekhouden.Api.Authentication;

// One credential, acquired only when a session POST body is serialized after connection/TLS setup.
public interface IPasswordSource
{
    ValueTask<Password> GetAsync(CancellationToken cancellationToken);
}

public sealed class Password : IDisposable
{
    private byte[]? _bytes;
    internal ReadOnlySpan<byte> Bytes => _bytes ?? throw new ObjectDisposedException(nameof(Password));
    internal Password(byte[] ownedBytes) => _bytes = ownedBytes;

    // Useful for injected sources. The caller retains ownership of its input.
    public static Password FromUtf8(ReadOnlySpan<byte> value)
    {
        Validate(value);
        return new(value.ToArray());
    }

    internal static void Validate(ReadOnlySpan<byte> value)
    {
        if (value.Length is < 1 or > 4096) throw Unavailable();
        var remaining = value;
        var nonblank = false;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(remaining, out var rune, out var consumed) != System.Buffers.OperationStatus.Done ||
                Rune.IsControl(rune)) throw Unavailable();
            nonblank |= !Rune.IsWhiteSpace(rune);
            remaining = remaining[consumed..];
        }
        if (!nonblank) throw Unavailable();
    }

    internal static ApiTransportException Unavailable() => new(TransportFailure.CredentialUnavailable);

    public void Dispose()
    {
        var bytes = Interlocked.Exchange(ref _bytes, null);
        if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
    }
    public override string ToString() => nameof(Password);
}
