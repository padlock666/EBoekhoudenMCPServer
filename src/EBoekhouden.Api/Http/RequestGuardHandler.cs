using System.Globalization;

namespace EBoekhouden.Api.Http;

internal sealed class RequestGuardHandler(Uri origin, bool session) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != origin.Scheme ||
            !string.Equals(uri.IdnHost, origin.IdnHost, StringComparison.OrdinalIgnoreCase) || uri.Port != origin.Port ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ApiTransportException(TransportFailure.RequestRejected);

        // Uri normalizes unreserved escapes and dot segments. Reject alternate path spellings too.
        var rawTarget = uri.OriginalString.Split('?', 2)[0];
        if (rawTarget.Contains('%') || rawTarget.Contains('\\') ||
            rawTarget.Contains("/./", StringComparison.Ordinal) || rawTarget.Contains("/../", StringComparison.Ordinal))
            throw new ApiTransportException(TransportFailure.RequestRejected);

        var allowed = session
            ? request.Method == HttpMethod.Post && uri.AbsolutePath == "/v1/session" && uri.Query.Length == 0 &&
                request.Content is not null && request.Headers.Authorization is null
            : request.Method == HttpMethod.Get && request.Content is null && IsReadPath(uri.AbsolutePath) &&
                request.Headers.Authorization is { Scheme: "Bearer", Parameter.Length: > 0 };
        if (!allowed) throw new ApiTransportException(TransportFailure.RequestRejected);
        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsReadPath(string path)
    {
        if (path is "/v1/relation" or "/v1/invoice" or "/v1/mutation" or
            "/v1/mutation/invoice/outstanding" or "/v1/ledger" or "/v1/ledger/balances") return true;
        var parts = path.Split('/');
        if (parts.Length is not (4 or 5) || parts[1] != "v1" ||
            parts[2] is not ("relation" or "invoice" or "mutation" or "ledger")) return false;
        if (!int.TryParse(parts[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id) ||
            id.ToString(CultureInfo.InvariantCulture) != parts[3]) return false;
        return parts.Length == 4 || (parts[2] == "ledger" && parts[4] == "balance");
    }
}
