using System.Net;
using EBoekhouden.Api.Http;

namespace EBoekhouden.Api.Errors;

public enum ApiErrorKind
{
    Http,
    InvalidResponse,
    Transport,
}

// Only normalized metadata escapes this boundary. No raw body or inner exception is retained.
public sealed class ApiException : Exception
{
    public ApiErrorKind Kind { get; }
    public HttpStatusCode? StatusCode { get; }
    public string? Code { get; }
    public string? TraceId { get; }
    public TransportFailure? TransportFailure { get; }

    private ApiException(ApiErrorKind kind, string message, HttpStatusCode? statusCode,
        string? code = null, string? traceId = null, TransportFailure? transportFailure = null) : base(message)
    {
        Kind = kind;
        StatusCode = statusCode;
        Code = code;
        TraceId = traceId;
        TransportFailure = transportFailure;
    }

    internal static ApiException Http(HttpStatusCode status) =>
        new(ApiErrorKind.Http, status switch
        {
            HttpStatusCode.NotFound => "The requested e-Boekhouden resource was not found.",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "e-Boekhouden denied access to the requested resource.",
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "e-Boekhouden rejected the request.",
            HttpStatusCode.TooManyRequests => "The e-Boekhouden rate limit was reached.",
            >= HttpStatusCode.InternalServerError => "e-Boekhouden is temporarily unavailable.",
            >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest => "e-Boekhouden returned an unexpected redirect.",
            _ => "The e-Boekhouden request failed.",
        }, status);

    internal static ApiException InvalidResponse(HttpStatusCode status) =>
        new(ApiErrorKind.InvalidResponse, "e-Boekhouden returned an invalid response.", status);

    internal static ApiException Transport(TransportFailure failure, HttpStatusCode? status = null,
        string? code = null, string? traceId = null) =>
        new(ApiErrorKind.Transport, "The e-Boekhouden transport failed.", status, code, traceId, failure);
}
