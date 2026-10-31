namespace EBoekhouden.Api.Authentication;

/// <summary>Consumes borrowed UTF-8 password bytes synchronously, without creating a managed string.</summary>
internal delegate void PasswordConsumer(ReadOnlySpan<byte> passwordUtf8);

internal static class KeyringPassword
{
    /// <summary>
    /// Finds a text password by its application/account attributes and invokes the consumer.
    /// Returns false if no password is retrievable. Searches across the user's collections.
    /// The consumer must not retain the native bytes beyond the callback.
    /// </summary>
    public static unsafe bool TryUsePassword(string application, string account,
        PasswordConsumer consumer, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("GNOME Keyring requires Linux.");
        ValidateAttribute(application, nameof(application));
        ValidateAttribute(account, nameof(account));
        ArgumentNullException.ThrowIfNull(consumer);
        cancellationToken.ThrowIfCancellationRequested();

        using var attributes = new NativeAttributes(application, account);
        using var cancellable = new CancellableHandle();
        // Dispose the registration before freeing GCancellable, including if cancellation races
        // with lookup completion. GIO explicitly permits cancellation from another thread.
        using var registration = cancellationToken.Register(() =>
            KeyringNative.g_cancellable_cancel(cancellable.DangerousGetHandle()));

        // The non-variadic 'v' API accepts a GHashTable, which is safe to call from P/Invoke.
        // A null schema intentionally matches attributes provisioned by secret-tool.
        using var password = KeyringNative.secret_password_lookupv_nonpageable_sync(
            0, attributes, cancellable, out var error);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (error != 0)
                throw new SecretServiceException();
            if (password.IsInvalid)
                return false;

            // Text passwords are NUL-terminated UTF-8. No Trim, decoding to string, or logging.
            // This span borrows the libsecret allocation; SafeHandle keeps it alive until return.
            var pointer = (byte*)password.DangerousGetHandle();
            var length = 0;
            while (length <= 4096 && pointer[length] != 0) length++;
            if (length > 4096) throw Password.Unavailable();
            var bytes = new ReadOnlySpan<byte>(pointer, length);
            consumer(bytes);
            GC.KeepAlive(password);
            return true;
        }
        finally
        {
            if (error != 0)
                KeyringNative.g_error_free(error);
            // Disposing password calls secret_password_free, including when consumer throws.
        }
    }

    internal static void ValidateAttribute(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096 || value.Any(char.IsControl))
            throw new ArgumentException("Lookup attributes must be non-empty strings without control characters (maximum 4096 characters).", name);
    }
}

internal sealed class SecretServiceException : Exception
{
    public SecretServiceException() : base("The desktop Secret Service could not retrieve the password.") { }
}
