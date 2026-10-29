using System.Net;

namespace EBoekhouden.Api.Http;

public enum TransportFailure
{
    SessionRejected,
    InvalidSession,
    Network,
    Timeout,
    CircuitOpen,
    RequestRejected,
}

// No upstream messages, inner exceptions, response bodies, URIs, or credentials.
public sealed class ApiTransportException : Exception
{
    public TransportFailure Failure { get; }
    public HttpStatusCode? StatusCode { get; }

    internal ApiTransportException(TransportFailure failure, HttpStatusCode? statusCode = null)
        : base("e-Boekhouden transport failed: " + failure + ".")
    {
        Failure = failure;
        StatusCode = statusCode;
    }
}
