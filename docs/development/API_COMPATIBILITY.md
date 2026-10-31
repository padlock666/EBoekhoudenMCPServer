# API compatibility and verification notes

Source: repository file `e-boekhouden-api-v1.yaml`. Initial review: 2026-10-29. No live API requests have been made during planning or implementation.

## Known constraints and ambiguities

| Topic | Evidence or constraint | Implementation direction | Verification |
| --- | --- | --- | --- |
| Authorization header | Quick-start prose uses the token directly; securitySchemes gives `Bearer SESSION_TOKEN` | Implemented per-request Bearer construction in ReadTransport; session POST has no Authorization header | Offline header tests pass; conflicting bare-token prose remains live-unverified |
| JSON casing | Schemas use camelCase; examples use PascalCase | Use deliberate case-insensitive upstream deserialization and stable MCP output casing | Case-insensitive API / strict camelCase MCP tests pass; live pending |
| Session creation | POST /v1/session requires accessToken and source; response has token and expiresIn | Internal-only, lazy serialized acquisition; conservative lifetime from request start, early refresh, lease-identity invalidation; no POST retries | Offline reuse/concurrency/expiry/failure/cancellation and stale-response tests pass |
| Source identifier | Maximum length 10 with a documented pattern | Validate the exact pattern and keyring attributes at startup without network access, using static validation errors | Offline startup validation tests pass |
| Relation search | Returns id, type, code only | Separate summary/detail methods and DTOs; never fabricate names or fetch details automatically | Exact synthetic list/detail request and typed-result tests pass |
| Invoice versus mutation | Invoicing and accounting are separate resources | Separate typed methods/DTOs; no payment inference from invoice detail | Exact synthetic endpoint tests pass |
| Outstanding balances | Endpoint requires credDeb C or D and returns mutationId; example contains deprecated null fields | Preserve mutationId and nullable money/deprecated fields; no invented relation/date filters or invoiceId | Exact synthetic request/result and absent-money tests pass |
| Pagination | Count is number retrieved, not total; limit maximum 2000, upstream default 100 | Send explicit MCP default 50; return count and candidate next offset; validate count against items, never report total matches | Offline count, bounds and overflow tests pass |
| Filter operators | One operator per field; range inclusive; plain parameters do not accept filters | Typed filters use value/to for range; encoder escapes brackets, commas and raw values once | Offline operator, invalid range and encoding tests pass |
| Ledger balances | Supplying from produces period movement rather than actual balance; routes are unpaginated | Explicit as_of (to only) versus period_change (from/to); echo requested scope and dates, no computed aggregate | Offline query/result mode tests pass |
| Mutation type representation | MutationType schema is a string enum, while mutation list/detail examples use numeric Type values | Accept int32 JSON numbers or int32 numeric strings only on mutation Type; keep MCP output numeric and preserve future numeric codes | Phase 8 regressions and published-host list/detail tests pass; actual live representation unverified |
| Identifier widths | `/v1/mutation` id filter is int64, list id is int32; detail path int32 but response id int64; outstanding mutationId int32 | Widen mutation DTO identities to long and MCP output identities to decimal strings. Keep detail input int32 per path; int64 search input remains supported. Other IDs stay int32; no invoice/mutation conversion | Offline width/mapping tests pass; wider detail paths remain unsupported/unverified |
| VAT/category codes | Introductory VAT tables and schema enum differ in coverage; response codes can evolve | Preserve VAT and category codes as strings; do not reject unknown response codes | Offline unknown-code tests pass; live pending |
| Quantity | Invoice item amount is deprecated in favor of quantity, with quantity schema default 0 | Preserve quantity and deprecated amount independently as nullable decimals, mapped to MCP strings. No fallback or default-zero substitution when quantity absent | Offline tests cover absent and differing values; live pending |
| Error shapes | Error schema errors is a string array, but examples use a dictionary; status/body casing can vary | Actual HTTP status is authoritative; ignore unsafe free text/collection shape; suppress upstream code/trace | Synthetic array/dictionary/security, malformed-body, duplicate-metadata, redaction and session tests pass; live pending |
| Administration scope | Discovery routes exist, but no explicit switching route in this spec | Bind to configured credential; do not invent administrationId support | Spec-reviewed |
| Reports | No dedicated aging, P&L, or VAT-summary routes in this spec | Defer derived reporting until rules/completeness are defined | Out of initial scope |

## Phase 2 response policy findings

- Most response schemas do not declare required properties. The implementation deliberately requires resource identities, invoice/mutation/outstanding accounting dates, page items/count, ledger balance code/type/balance, and the invoice item fields explicitly required by the specification. Other fields remain nullable. This is an offline minimum-invariant policy; authorized live verification may require a documented revision, not silent default values.
- Decimal JSON fields are modeled as nullable decimal when optional. MCP maps decimals to invariant strings, with no rounding or invented currency. Quoted upstream monetary strings are rejected until actual evidence justifies a compatibility converter. Out-of-range or inexact decimal values now fail deserialization, including excess precision and underflow; phase 8 corrected the built-in reader’s silent rounding. Precision remains bounded by .NET decimal.
- API DTOs accept additive fields and either casing. MCP requests use strict camelCase with duplicate/unknown fields rejected. Phase 5 verifies actual SDK schemas and strict pre-binding of arguments, including raw duplicate fields.
- Optional nested collections distinguish missing/null from empty; null collection elements fail result mapping. This prevents incomplete data from looking like a known-empty financial response.

## Phase 3 authentication assumptions

- `expiresIn` is treated as seconds and measured from the request start rather than response arrival, with an early refresh margin capped at 10% of lifetime. Invalid/nonpositive lifetimes or responses already beyond the refresh point are rejected without caching. These conservative timing choices are tested offline, not observed live.
- Session token strings must be valid RFC 6750 Bearer credentials. This prevents header injection and follows the securitySchemes example; the specification itself declares only string and does not define a token alphabet. If authorized live testing finds a different format/header requirement, revise the isolated handling with a regression test.
- Only HTTP 401 triggers one authentication replay. HTTP 403 is treated as a permission denial. Session HTTP failures, including 403 and transient errors, are never automatically retried. Actual upstream failure distinctions remain live-unverified.
- Production origin is fixed to the supplied server URL; no discovery or administration routes were added. Redirects are never followed. See [HTTP infrastructure](../http-infrastructure.md) for the exact allowlist and bounded retry policy.

## Phase 4 response and error findings

- Most GET properties remain optional at the schema level. Relation dates/flags, invoice/mutation monetary data and optional arrays remain nullable; only the existing documented minimum invariants are required. Type-dependent mutation-row descriptions include write validation text, which is not applied as new read-side accounting rules. Null nested elements fail at the API boundary.
- Invoice detail items have required description/vatCode/ledgerId within each item, but the containing items property has no required marker or minItems despite prose describing at least one item. Reads preserve absent/null/empty arrays independently, rather than inventing items or enforcing undocumented read completeness. Synthetic fixtures/tests cover this policy; live response completeness remains unverified.
- Only the documented HTTP 200 typed shape succeeds. Empty list envelopes succeed; empty/null/malformed bodies, inconsistent counts and unexpected 2xx statuses fail. A 404 is never converted into empty success. Duplicate JSON fields, including known property casing aliases, fail to prevent ambiguous identity/financial values; additive fields and either documented casing remain accepted.
- Code/traceId schema fields are unconstrained strings. The implementation suppresses them because the keyring credential is released after authentication and could be reflected upstream. HTTP status survives even when metadata/body cannot be read safely. All upstream title/message/errors/propertyName text is discarded.
- Session rejection uses the same metadata suppression; invalid session JSON/lifetime retains received HTTP status when known. No POST retry or additional GET retry layer was introduced. See [typed API client](../api-client.md) for error categories and cancellation/disposal behavior.

## Live smoke test, when authorized

Verify header format, actual list/detail casing and nullability, session expiry behavior, and representative error shapes with minimal read-only calls. Record sanitized observations, endpoint, date, and test result. Never commit tokens, identifiable financial records, or raw production responses.

Offline fixtures prove implementation behavior against fixtures, not actual service compatibility. Do not mark a live assumption verified merely because a synthetic test passes.

## New finding template

```text
Topic:
Specification location / endpoint:
Observed inconsistency:
Chosen handling:
Evidence (spec / synthetic test / authorized live test):
Remaining uncertainty:
Follow-up:
```

## Phase 5 MCP compatibility findings

- SDK 2.3.0 normally nests a request record under a method parameter and does not enforce every JSON Schema constraint when binding. A `DelegatingMcpServerTool` adapter exposes flat documented arguments, rejects unknown/duplicate/case-mismatched/null/invalid fields with `ContractJson.Options`, then delegates typed output formatting to the SDK. Cross-field ordering remains runtime validation.
- `UseStructuredContent=true` produces an object output schema, structured content and matching JSON text for successful typed records. Tool failures use safe JSON text with `isError=true` and omit success-shaped structured content. Optional success properties are present with explicit null; response VAT/category codes remain open strings.
- SDK Trace diagnostics can contain complete protocol payloads. The executable blocks SDK logging regardless of environment log-level settings and forces console diagnostics to stderr. HTTP factory loggers remain removed.
- Explicit `notifications/cancelled` messages are verified to cancel the upstream handler. An initial test that only canceled the SDK client's local call did not reliably forward cancellation in this harness; tests use an explicit request ID and protocol notification. Do not infer server cancellation from a locally canceled client task. Phase 6 reproduces the local-cancellation/remote-abort distinction across real subprocess/TCP boundaries; explicit notifications cancel the HTTP request and subsequent calls still work. No upstream/live compatibility claim is made.

## Phase 6 executable findings

- The actual executable is verified with SDK 2.3.0 using initialize revisions 2025-06-18 and 2025-11-25 and the 2026-07-28 discovery revision. All ten reviewed schemas match the legacy initialization path; successful structured results/text and normalized failures cross actual stdio and loopback HTTP. This verifies local protocol compatibility, not the live accounting service.
- SDK 2.3.0 waits for in-flight handlers before its single-session hosted service requests application shutdown. Without a host disconnect observer, stdin EOF during an outstanding HTTP call waited for the operation deadline. StdioDisconnectService now observes transport reader completion and stops Generic Host, canceling the pending request. The subprocess regression asserts HTTP abort and normal process exit within five seconds.
- SDK client local token cancellation again completed locally without a timely remote HTTP abort in this environment. Tests therefore send an explicit notifications/cancelled with a known request ID and independently verify TCP request abort and continued server use. No SDK package change or undocumented client API is introduced, and local task cancellation alone is not reported as end-to-end cancellation evidence.
- The executable test origin requires IntegrationTest plus a numeric loopback origin. Production rejects the override; IntegrationTest without a valid origin fails closed. No method/path guard, redirect policy or authentication header assumption is relaxed. Live Bearer/header/response assumptions remain unverified.

## Phase 7 delivery evidence

- Local untrimmed framework-dependent DLL output is verified through the official SDK and fake HTTP integration suite, including all ten tools/schemas, initialization, structured results, safe failures and cleanup. The published runtime configuration requires Microsoft.NETCore.App only; test-only ASP.NET/xUnit assemblies are not shipped. This is Linux x64 offline evidence, not live service verification.
- Six lock files preserve resolved package versions/content hashes; a copied central-pin change is rejected by locked restore (NU1004). No SDK/package pin was upgraded. GitHub Actions configuration is checked locally via its shared script and YAML inspection; no hosted run is claimed.
- The existing Bearer/bare-token prose conflict, live response completeness/required fields, expiry/header assumptions, inconsistent mutation identifier widths and SDK client local-cancellation forwarding remain open compatibility limitations. Publishing does not resolve them. Independent review is phase 8; Windows/macOS, self-contained/RID-specific artifacts and OS-signal behavior remain unverified.

## Phase 8 review findings

- The independent specification audit found the previously undocumented MutationType conflict: the component schema declares string enum values, whereas GET examples contain numbers. Property-local numeric-string reading supports both without relaxing identity, financial or MCP input validation. Output remains a nullable numeric code. This is evidence from the supplied specification and synthetic tests, not live observation.
- Financial JSON numbers must be exactly representable by decimal. The default .NET reader silently rounded some excess precision and underflowed small numbers to zero; the exact converter now rejects changed values as InvalidResponse. Exactly representable exponent notation and redundant trailing zeros are accepted. This defines an explicit local capacity limit rather than an accounting rounding rule.
- Oversized or HTTP/I/O-failed error bodies no longer erase the received HTTP status or cause extra network retries for permission denials. Headers are received first; bounded diagnostic bodies are buffered under existing cancellation/deadlines and discarded on size/read failure. No retry layer or route was added.
- See [REVIEW.md](REVIEW.md) for three fixed P2 defects, 465 passing offline tests, 39 published-artifact cases and remaining findings. Existing live header/expiry/completeness/identifier assumptions, SDK local-cancellation forwarding and unrun hosted/platform checks remain open.

## GNOME Keyring authentication

- The API key is retrieved by application/account attributes through libsecret, using the GnomeKeyringTest demo's native lookup and SafeHandle ownership. This changes local credential acquisition only: upstream routes, session payload fields, Bearer construction, tools and schemas are unchanged. Lookup may display a desktop unlock prompt. See [Linux Secret Service](../linux-secret-service.md).
- Session content is deferred and one-shot so key retrieval follows TLS identity validation. HTTP/1.1 uses chunked request framing. Synthetic Kestrel tests accept the exact payload; live upstream acceptance of this framing remains unverified and requires separate authorization.
- Session/read errors preserve HTTP status and fixed messages but suppress upstream code/trace, because no API key remains available to filter reflected values. No new upstream API ambiguity was discovered.
