using EBoekhouden.Api.Configuration;

namespace EBoekhouden.Api.Authentication;

internal sealed class KeyringPasswordSource(KeyringOptions selector) : IPasswordSource
{
    public async ValueTask<Password> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // libsecret's synchronous lookup may wait for a desktop unlock dialog.
            // Await native cleanup before releasing the session refresh gate.
            return await Task.Factory.StartNew(() => Lookup(cancellationToken), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default).ConfigureAwait(false);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Password.Unavailable();
        }
    }

    private Password Lookup(CancellationToken cancellationToken)
    {
        Password? result = null;
        try
        {
            if (!KeyringPassword.TryUsePassword(selector.Application, selector.Account,
                bytes => result = Password.FromUtf8(bytes), cancellationToken))
                throw Password.Unavailable();
            cancellationToken.ThrowIfCancellationRequested();
            return result!;
        }
        catch
        {
            result?.Dispose();
            throw;
        }
    }
}
