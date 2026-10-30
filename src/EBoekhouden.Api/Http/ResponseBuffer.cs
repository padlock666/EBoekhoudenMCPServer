namespace EBoekhouden.Api.Http;

internal static class ResponseBuffer
{
    // Called after headers arrive, inside the existing timeout/concurrency budget.
    // Unreadable diagnostic bodies must not erase a status or turn a denial into a network retry.
    public static async Task LoadAsync(HttpResponseMessage response, long successLimit, CancellationToken cancellationToken)
    {
        var limit = response.IsSuccessStatusCode ? successLimit : Math.Min(successLimit, 64 * 1024);
        try
        {
            await response.Content.LoadIntoBufferAsync(limit, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (!response.IsSuccessStatusCode && error is HttpRequestException or IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            response.Content.Dispose();
            response.Content = new ByteArrayContent([]);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
