# Authentication and HTTP infrastructure

Phase 3 implements reusable API infrastructure. The [phase 4 typed client](api-client.md) consumes `ReadTransport` for typed responses and normalized errors; the [phase 5 runtime](mcp-runtime.md) wires the Generic Host and stdio transport. No live API behavior has been verified.

## Registration and external configuration

Register `services.AddEBoekhoudenApi(configuration)` once. This binds the `EBoekhouden` section, registers an `IValidateOptions<EBoekhoudenOptions>` validator with `ValidateOnStart`, and creates separate named `IHttpClientFactory` clients, `EBoekhouden.Session` and `EBoekhouden.Read`. The Generic Host runs this validation at startup without a network call. The offline tests invoke the same `IStartupValidator` hook directly. Merely resolving the token provider or transport never acquires a session.

The session-token provider and read transport are singletons for one configured credential. They hold the factory, not a transient client captured by a singleton. Each operation creates and disposes a short-lived factory client; the factory owns handler pooling. Configuration changes require a restart; credential rotation is not a runtime administration-switch feature.

The host supplies its configuration from the normal environment/development-secret providers. Keep the credential outside tracked appsettings files. Environment keys are `EBoekhouden__AccessToken` and `EBoekhouden__Source`. The host project includes a development `UserSecretsId`; local secret setup can use:

```sh
dotnet user-secrets set --project src/EBoekhouden.Host.Stdio EBoekhouden:AccessToken "<API_TOKEN>"
dotnet user-secrets set --project src/EBoekhouden.Host.Stdio EBoekhouden:Source "McpLocal"
```

These commands are setup instructions, not commands executed during development. The executable loads user secrets when launched with `DOTNET_ENVIRONMENT=Development`; environment variables are also supported. Do not put real credentials into examples, tests, commits or handoff notes.

| Option | Default | Valid range/policy |
| --- | --- | --- |
| AccessToken | absent | required, non-whitespace; never echoed by validation |
| Source | absent | exact specification pattern `^[\w_ ]{1,10}$` |
| SessionTimeout | 10 seconds | positive, at most 2 minutes |
| AttemptTimeout | 10 seconds | positive, at most 2 minutes, no greater than OperationTimeout |
| OperationTimeout | 30 seconds | positive, at most 5 minutes |
| RefreshBeforeExpiry | 30 seconds | positive, at most 5 minutes; effective skew also capped at 10% of returned lifetime |
| RetryBaseDelay | 200 milliseconds | positive, no greater than MaxRetryDelay |
| MaxRetryDelay | 5 seconds | positive, at most 30 seconds |
| MaxReadAttempts | 3 | 1..5 physical GET attempts, including authentication replay |
| MaxConcurrentReads | 4 | 1..32 logical read operations, including waits/retries within each operation |
| CircuitFailureThreshold | 5 | 1..100 consecutive transient GET failures |
| CircuitBreakDuration | 30 seconds | positive, at most 5 minutes |

Configuration validation produces static messages containing setting names, never values or credentials. `TimeProvider.System` is registered only if no provider was supplied; tests inject Microsoft's `FakeTimeProvider`.

## Sessions

On the first read, the internal session client sends `POST /v1/session` with only `accessToken` and `source` in a JSON body. It sends no session Authorization header. No application retry, resilience handler, or authentication replay wraps this POST. A failed POST stops the current operation; a later independent caller may try again.

Response `token` and `expiresIn` are required and support the upstream casing policy. `expiresIn` must be positive; a token must be a nonempty valid Bearer credential (RFC 6750 character syntax). Malformed bodies, missing/null fields, invalid lifetime, and invalid header values fail without exposing the body or underlying exception. Session response buffering is capped at 16 KiB.

The cache measures expiry conservatively from the request start using injected UTC time. Refresh occurs before expiry, using the smaller of the configured skew and 10% of the lifetime so short-lived tokens remain useful. Responses already beyond their refresh time are rejected. A cancellation-aware semaphore serializes acquisition and refresh; validity is checked again after taking it. All failure and cancellation paths release the semaphore. Each waiter has its own cancellation; canceling a waiter does not cancel the current refresh. Canceling the refresh owner aborts that POST, and the next caller can recover.

Every cached token is a distinct lease object, even if the upstream reuses the same token string. Invalidation is an atomic compare-and-clear against that lease. An old failed GET cannot clear a newer lease that another caller has already refreshed.

Every GET receives `Authorization: Bearer SESSION_TOKEN` on its own new request. This follows the specification's securitySchemes example. The quick-start prose instead shows the bare token; the contradiction remains unverified and isolated in `ReadTransport` header construction. No default authorization headers or shared cookies are used. A 401 permits at most one invalidation/replay per read operation, provided an attempt remains; the second 401 is returned as an error response for phase 4 normalization. A 403 does not refresh or retry. Final 401 responses invalidate only the failed lease so a later independent read will not reuse a known-failed token.

## Retry, timeout and concurrency bounds

There is exactly one application retry owner: `ReadTransport`. No retry or authentication handler is installed in the factory pipeline. The read loop creates a new `HttpRequestMessage` for every GET and disposes every discarded response. Caller owns the final buffered `HttpResponseMessage`; phase 4 must dispose it after mapping its content/error.

The total GET budget includes transient retries and the single authentication replay. For example, 503 → 401 → 503 consumes the default three attempts and stops. Permission denials, validation errors, other permanent HTTP errors and all redirects are returned immediately. Only 408, 429, 500, 502, 503 and 504, network exceptions and per-attempt timeouts are retried. Exceptions exposed by the infrastructure carry a static failure kind and optional HTTP status, with no raw URI, response body or inner exception. Caller cancellation takes precedence over a racing network failure and preserves the caller's cancellation token.

Transient backoff is exponential with 0.5..1.5 jitter, capped at MaxRetryDelay. A valid Retry-After delta or HTTP date is a minimum wait; past dates fall back to backoff. If the server asks for longer than MaxRetryDelay, the current failure response is returned instead of retrying earlier than requested. Caller cancellation and the operation deadline interrupt backoff. An independent later operation is the caller's decision; no financial read cache or global Retry-After cache is introduced.

At most MaxReadAttempts GETs can occur per operation. Token acquisition is called at most once per GET attempt, so at most that many session POSTs can be initiated by that operation (normally one, or two after a 401). Natural token expiry during retries can require another session, without increasing the GET budget. Session acquisition across operations is globally serialized. There are no stacked retry layers multiplying these bounds.

OperationTimeout begins before waiting for a read concurrency permit and includes token-lock waiting, session creation, all attempts, body buffering and retry delays. AttemptTimeout covers each physical GET including body buffering; SessionTimeout covers the individual POST and session deserialization. Factory `HttpClient.Timeout` is infinite so these explicit budgets have one owner. Read response buffering is capped at 16 MiB. Headers-only returns do not escape the timeout. Timers and delays use the injected TimeProvider.

The shared concurrency semaphore is held for the entire logical operation; waiting callers can cancel or time out without consuming a permit. At most MaxConcurrentReads physical GETs plus one session POST can be active. Permits are released on all exit paths. Production socket connection limits are also configured; redirects and cookies are disabled.

The shared circuit breaker counts consecutive transient physical GET failures. Non-transient responses reset the failure count. Session failures and caller cancellation do not count against the read circuit. On reaching the threshold it rejects new attempts until CircuitBreakDuration passes; then exactly one probe is permitted. Successful probes close the circuit, failures reopen it, and canceled probes release the probe slot. A generation number prevents old completions from altering a newer circuit state. Circuit rejection may stop an operation before its attempt budget is exhausted.

## Request restrictions and test seams

`ReadRequest` has a private constructor and public factories only for the existing typed query types and int32 detail IDs. It accepts no caller URL, path, arbitrary query dictionary, method or body. No HTTP operation is exposed as an MCP tool.

An innermost request guard checks the final absolute URI's scheme, host and port, absence of user info/fragments, method and exact allowlisted path immediately before the primary handler. Encoded path spellings and dot-segment/backslash spellings are rejected, including escapes that .NET's Uri would normalize. Encoded query values remain valid data. Read requests require a Bearer header and no body. Session requests allow only POST to `/v1/session`, without query parameters or an Authorization header. No other method is permitted.

The read allowlist is exactly:

```text
GET /v1/relation
GET /v1/relation/{int32}
GET /v1/invoice
GET /v1/invoice/{int32}
GET /v1/mutation
GET /v1/mutation/{int32}
GET /v1/mutation/invoice/outstanding
GET /v1/ledger
GET /v1/ledger/{int32}
GET /v1/ledger/balances
GET /v1/ledger/{int32}/balance
```

Production origin is fixed to `https://api.e-boekhouden.nl/`. `AddEBoekhoudenApiForTesting` is an explicit alternate registration restricted to numeric IPv4/IPv6 loopback HTTP(S) origins with root path and no query, fragment, or user information. Offline unit tests invoke it in code and replace only primary handlers. The phase 6 executable seam requires **both** `DOTNET_ENVIRONMENT=IntegrationTest` and `EBoekhoudenTest__UpstreamOrigin` set to an accepted loopback origin. IntegrationTest without an origin, or an origin override in any other environment, fails at startup without network access. The production origin is never replaced by a general URL setting. The test seam retains the exact method/route/origin guards, redirect restrictions, factory, cache, timeout and retry code. See [integration tests](integration-tests.md).

## Diagnostics and dependencies

Both named clients use `RemoveAllLoggers` to remove HTTP factory loggers, which could otherwise expose financial query values. The infrastructure does not log request/response bodies, tokens, credentials, headers or queries, or attach low-level exceptions to errors. There are no console writes. Host diagnostics go to stderr; SDK payload logging is disabled independently of configuration. External trace exporters or custom handlers are not configured here.

Production additions are `Microsoft.Extensions.Http` **10.0.13** and `Microsoft.Extensions.Options.ConfigurationExtensions` **10.0.13**. Validation uses a custom IValidateOptions implementation rather than adding DataAnnotations. Retry/circuit policy is the small explicit loop above; `Microsoft.Extensions.Http.Resilience` is deliberately not installed because a second policy owner would obscure the shared authentication/transient attempt budget. The test-only `Microsoft.Extensions.TimeProvider.Testing` **10.0.0** provides deterministic virtual time. Versions are centrally pinned and verified by restore and compilation against net10.0.

Official references checked during implementation: [IHttpClientFactory](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory), [options validation and startup validation](https://learn.microsoft.com/en-us/dotnet/core/extensions/options), [RemoveAllLoggers](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.httpclientbuilderextensions.removeallloggers), and [FakeTimeProvider testing](https://learn.microsoft.com/en-us/dotnet/core/extensions/timeprovider-testing). MCP SDK pins and APIs are unchanged in this phase.
