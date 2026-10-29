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
    public string? Code { get; }
    public string? TraceId { get; }

    internal ApiTransportException(TransportFailure failure, HttpStatusCode? statusCode = null, string? code = null, string? traceId = null)
        : base("e-Boekhouden transport failed: " + failure + ".")
    {
        Failure = failure;
        StatusCode = statusCode;
        Code = code;
        TraceId = traceId;
    }
}
