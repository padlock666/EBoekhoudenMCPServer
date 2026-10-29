# Architecture and interface decisions

Status: phases 1–4 are implemented: scaffold, contracts/mappings/filters, authentication/HTTP infrastructure, and typed read-only client with normalized errors. Tools and executable hosting below remain proposed. Consult [STATUS.md](STATUS.md) for evidence and [tool-contracts.md](../tool-contracts.md) for the exact implemented contracts.

## Solution layout

```text
EBoekhoudenMcp.sln
global.json
Directory.Build.props
Directory.Packages.props
.editorconfig
.gitignore
README.md
AGENTS.md
e-boekhouden-api-v1.yaml
docs/
  development/                 # Existing plan, prompts, decisions, handoff
  tool-contracts.md            # Exact phase 2 contract and field reference
src/
  EBoekhouden.Api/
    EBoekhouden.Api.csproj
    Authentication/
    Clients/
    Contracts/
    Filtering/
    Errors/
    Configuration/
  EBoekhouden.Mcp/
    EBoekhouden.Mcp.csproj
    Tools/
    Contracts/
    Mapping/
    Validation/
  EBoekhouden.Host.Stdio/
    EBoekhouden.Host.Stdio.csproj
    Program.cs
    appsettings.json
tests/
  EBoekhouden.Api.Tests/
    EBoekhouden.Api.Tests.csproj
  EBoekhouden.Mcp.Tests/
    EBoekhouden.Mcp.Tests.csproj
  EBoekhouden.IntegrationTests/
    EBoekhouden.IntegrationTests.csproj
```

Folders are organizational guidance, not a requirement to create empty directories or unnecessary classes.

## Responsibilities and dependencies

`Host -> Mcp -> Api -> e-Boekhouden`

- **Api:** HTTP transport, upstream DTOs, query encoding, token lifecycle, configuration, and upstream errors. No MCP dependency.
- **Mcp:** explicit tool contracts, validation, mappings, descriptions, schemas, and tool error translation.
- **Host:** composition root, configuration, transport, logging, process lifetime, and shutdown.
- **Tests:** API behavior with fake handlers, MCP behavior with fakes, and executable integration with a loopback fake service.

Tools initially call the typed read client directly. Add query services only when operations genuinely combine endpoints. No database is required. Do not cache financial reads initially; only session tokens need caching.

## Packages and configuration

| Concern | Choice |
| --- | --- |
| MCP and stdio | `ModelContextProtocol` |
| Host lifecycle and DI | `Microsoft.Extensions.Hosting` |
| Typed HTTP clients | `Microsoft.Extensions.Http` |
| HTTP resilience | One explicit bounded read loop with authentication replay and shared circuit breaker |
| JSON | Built-in `System.Text.Json` |
| Options validation | `Microsoft.Extensions.Options.ConfigurationExtensions` with custom `IValidateOptions` and `ValidateOnStart` |
| Tests | `xunit.v3` and a compatible runner configuration |
| Later HTTP transport | `ModelContextProtocol.AspNetCore` |

Pin verified stable versions in `Directory.Packages.props`, SDK in `global.json`, and package resolution in lock files in phase 7. Do not assume package major versions all equal the target framework version.

### Phase 1 package and runner decisions

- SDK: installed stable **10.0.113**, exact pin with prereleases disabled; all six projects inherit `net10.0`.
- `ModelContextProtocol` **2.3.0** in Mcp and IntegrationTests; the host receives it through Mcp. This is the official package for stdio with hosting/DI, and supports `net10.0`.
- `Microsoft.Extensions.Hosting` **10.0.13** in the executable, compatible with the MCP package's Extensions dependency minimums. Phase 3 adds Http and Options.ConfigurationExtensions 10.0.13; resilience uses a single explicit loop and custom options validation. No HTTP hosting package is included.
- `xunit.v3` **3.2.2**, `xunit.runner.visualstudio` **3.1.5**, and `Microsoft.NET.Test.Sdk` **18.0.1** in each test project. VSTest is explicitly selected in `global.json`; test projects use `OutputType=Exe`, `IsTestProject=true`, and `IsPackable=false`. The adapter is private to each test project. No test-only dependency is referenced by production projects.
- Built-in .NET 10 analyzers and nullable checking are enabled centrally. CA2016 warns when cancellation tokens are not forwarded. No separate analyzer framework is added.
- The host currently exits with a static stderr diagnostic and status 1; phase 5 supplies the real composition root. There are no placeholder API/tool classes or dummy tests.

Version-specific SDK verification: [v2.3.0 getting started](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.3.0/docs/concepts/getting-started.md) documents `AddMcpServer`, `WithStdioServerTransport`, and stderr console logging. These APIs are guidance for phase 5, not implemented behavior. Explicit approved tool registration still requires version-specific verification when implemented.

Runner guidance: [xUnit VSTest/MTP documentation](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform) explains the VSTest adapter path. Stable package metadata was checked for [xunit.v3 3.2.2](https://www.nuget.org/packages/xunit.v3/3.2.2), [adapter 3.1.5](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5), and [Test SDK 18.0.1](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.0.1). Restore, Release compilation, and adapter discovery verify the selected combination locally. Phase 2 adds meaningful offline contract tests; integration tests remain phase 6 work.

Use `ILogger<T>` initially. All stdio diagnostics must use stderr. OpenTelemetry is optional for later deployment; exporters must not corrupt stdout or expose financial data.

Credentials come from environment variables or development user secrets. Nonsecret settings may live in appsettings. Validate source constraints, credential presence, timeouts, and origins at startup without making a network call. Acquire sessions lazily.

## Core tool mapping

Search/list tools accept `limit` and `offset` only where the upstream supports pagination.

| Tool | Additional inputs | Upstream route |
| --- | --- | --- |
| `search_relations` | code, type, email, name, contact, city | `GET /v1/relation` |
| `get_relation` | id | `GET /v1/relation/{id}` |
| `search_invoices` | invoiceNumber, relationId, date | `GET /v1/invoice` |
| `get_invoice` | id | `GET /v1/invoice/{id}` |
| `search_entries` | type, id, description, invoiceNumber, date | `GET /v1/mutation` |
| `get_entry` | id | `GET /v1/mutation/{id}` |
| `list_outstanding_invoices` | side, invoiceNumber | `GET /v1/mutation/invoice/outstanding` |
| `search_ledger_accounts` | code, category | `GET /v1/ledger` |
| `get_ledger_account` | id | `GET /v1/ledger/{id}` |
| `get_ledger_balances` | optional ledgerId, mode, dates, optional costCenterId | `GET /v1/ledger/balances` or `GET /v1/ledger/{id}/balance` |

Map outstanding `side=receivable` to `credDeb=D`, and `side=payable` to `credDeb=C`.

Map descriptive entry types to API values: `invoice_received=1`, `invoice_sent=2`, `invoice_payment_received=3`, `invoice_payment_sent=4`, `money_received=5`, `money_sent=6`, `journal_entry=7`.

### Filters

Use typed filter objects, for example:

```json
{
  "name": { "op": "like", "value": "%Acme%" },
  "limit": 25,
  "offset": 0
}
```

```json
{
  "type": "invoice_received",
  "date": { "op": "range", "value": "2026-01-01", "to": "2026-03-31" },
  "limit": 50
}
```

- Filterable strings: eq, not_eq, like, not_like.
- Filterable integers/dates: eq, not_eq, gt, gte, lt, lte, range.
- Only one operator per field; range endpoints are inclusive.
- Range uses typed `value` and `to` endpoints, with non-null `to` forbidden for every other operator. This refines the unimplemented planning syntax so no scalar/array union is needed in tool schemas.
- Plain parameters such as invoice relationId accept direct values, not arbitrary filters.
- Validate supported fields per endpoint and encode bracket syntax centrally.

### Pagination

Default limit 50, maximum 2000, offset at least zero. Return `items` and `page` containing `limit`, `offset`, `returnedCount`, and nullable `nextOffset`. A full page yields a candidate next offset, not proof of additional matches. An incomplete page ends traversal. Never label upstream count as a total. Do not silently auto-fetch every page or claim a snapshot guarantee.

### Balances and financial values

- `mode=as_of` requires a date and sends only `to`.
- `mode=period_change` requires from/to and sends both.
- Return mode and requested dates with the result.
- Use `decimal` internally, `DateOnly` for accounting dates, `DateTimeOffset` for expiry, and injected `TimeProvider` for time-dependent behavior.
- MCP money, quantity and percentage values are invariant decimal strings, including original scale where decimal preserves it. Optional amounts remain null. Never use binary floating point for arithmetic.
- API mutation identities use `long`, and MCP mutation identities serialize as decimal strings. Invoice and other IDs remain `int`. Mutation search permits int64; mutation detail requests enforce the specified int32 path width. See compatibility notes for the upstream inconsistency.
- API JSON is case-insensitive with strict numeric types and required minimum invariants; MCP JSON is strict camelCase and rejects unknown/duplicate properties. API and MCP contracts are separate records with explicit mappings.
- Invoice item `quantity` and deprecated `amount` are preserved independently, with no fallback or default-zero substitution. Unknown VAT/category response codes remain strings.

## Authentication and transport

Use separate session and read clients from `IHttpClientFactory`. A singleton token provider serves the initial single configured credential. Do not capture a transient typed read client inside the singleton token provider; use a factory-created session client or an appropriate dedicated session dependency.

Cache token and expiry, refresh shortly before expiry, and coordinate refresh with cancellation-aware synchronization. Recheck state after acquiring the lock. Invalidate only the token that actually failed; do not discard a newer token after an older request returns an authentication failure.

Attach authorization per request. One authentication refresh/replay is permitted for a failed GET; permission denials do not trigger endless refresh. Bound total work across authentication and transient retry layers. Recreate requests safely for replay.

Configure timeouts, limited concurrency, transient retries with backoff/jitter, and circuit breaking. Honor Retry-After where applicable. Do not automatically retry the session POST. Avoid stacked independent retry policies that multiply requests unexpectedly.

Enforce exact approved methods, routes, and origin immediately before dispatch. Disable redirects. No arbitrary URL parameter. Loopback origin overrides are allowed only in explicit test configuration.

### Implemented phase 3 policy

See [HTTP infrastructure](../http-infrastructure.md) for exact registration, external configuration, defaults, limits, circuit policy, request allowlist, timeout ownership, and test seams. Production uses two named factory clients with redirects/cookies disabled and all HTTP factory loggers removed. Session POSTs have no retry policy; one shared read loop owns the complete GET attempt budget, including one optional 401 replay. Default budget is three GET attempts and a 30-second operation deadline including queue/token waits, buffering and backoff. Token refresh uses a cancellation-aware lock and atomic lease-identity invalidation.

The planned Http.Resilience/DataAnnotations dependencies were replaced by this explicit bounded policy and IValidateOptions validator to avoid multiple retry owners and keep configuration validation local. Production origin is fixed; the code-only test registration accepts numeric loopback origins only. ValidateOnStart is registered and tested through IStartupValidator; the executable composition root remains phase 5 work. Development user-secret metadata is present in the host project, with setup instructions in the infrastructure document.

### Implemented phase 4 client

`IEBoekhoudenReadClient` exposes exactly eleven typed GET operations for the ten tools. `EBoekhoudenReadClient` is registered as a singleton over the existing factory-backed ReadTransport. See [typed client](../api-client.md) for method/route/DTO mapping, the reviewed nullability policy, envelope/nested-element checks and error behavior. The API layer has no MCP dependency or arbitrary HTTP operation.

Only HTTP 200 typed responses succeed. A 404 is an error, an empty list envelope succeeds, and malformed/missing/ambiguous data fails. Unknown/additive fields are tolerated, but duplicate JSON properties are rejected. Optional values remain null. Every final response/stream is disposed. A linked typed-call deadline extends the transport budget through JSON parsing; caller cancellation stays cancellation.

ApiException uses fixed local messages, actual HTTP status and optional bounded/sanitized code/trace metadata. Error title/message/errors text and raw inner exceptions are discarded. Session errors use the same sanitizer; no extra retry layer was added. Phase 5 must translate ApiException into tool errors explicitly.

## MCP behavior

Explicitly register approved tool classes. Provide typed contracts, useful descriptions, read-only annotations, and output schemas. Inspect actual generated schemas with the pinned SDK. Keep protocol-specific result formatting and errors in the MCP layer.

Return structured results plus compatible text content as supported by the SDK. Invalid input must not reach the upstream service. API failures must not look like successful empty pages. Preserve useful error codes/status/trace IDs, while sanitizing potentially sensitive messages.

Descriptions, notes, and other upstream free text are data, never instructions for the server or caller.

## References

- [Official C# MCP SDK getting started](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/getting-started.md)
- [.NET HTTP resilience](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)
- [xUnit v3 getting started](https://xunit.net/docs/getting-started/v3/cmdline)

Verify version-specific details during implementation.
