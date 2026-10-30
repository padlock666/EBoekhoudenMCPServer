# Independent implementation review — 2026-10-30

This review inspected the actual six-project solution, production code, test harnesses, dependency locks, CI, documentation and supplied OpenAPI. Earlier completion claims were checked against fresh execution. Scope remains ten tools over stdio, one credential, no database. All accounting tests used synthetic data and fake handlers or loopback HTTP. No live accounting request, deployment, package push or Git commit was performed.

## Confirmed defects fixed

All three findings are **P2 / medium** correctness defects. Regressions failed before each corresponding fix.

| Finding and impact | Fix and evidence |
| --- | --- |
| Default decimal parsing silently rounded excess precision and underflowed `1e-29` to zero. An upstream financial value could become a different successful balance. | [ExactDecimalConverter.cs](../../src/EBoekhouden.Api/Contracts/ExactDecimalConverter.cs) rejects values that cannot survive decimal conversion exactly. Numeric comparison uses .NET 10 JsonElement.DeepEquals, which compares decimal representations without rounding. Exact exponent values, extrema, nullable amounts and preserved scale remain supported. API regressions and published-host balance error/recovery tests pass. |
| HttpClient automatic response buffering threw before returning headers for oversized error bodies. The received 403 became a statusless network error; GETs were retried despite the permission denial. Broken error streams had the same classification problem. | [ResponseBuffer.cs](../../src/EBoekhouden.Api/Http/ResponseBuffer.cs), [ReadTransport.cs](../../src/EBoekhouden.Api/Http/ReadTransport.cs) and [SessionTokenProvider.cs](../../src/EBoekhouden.Api/Authentication/SessionTokenProvider.cs) receive headers first and buffer under existing deadlines. Oversized or HTTP/I/O-failed diagnostic bodies are disposed and discarded; status determines the existing retry policy. Error caps are 64 KiB for reads and 16 KiB for sessions; successful read cap remains 16 MiB. Caller cancellation still wins. API and real HTTP/subprocess regressions verify status, disposal and request counts. |
| Mutation type DTOs rejected the OpenAPI string enum representation (`"2"`), accepting only the numeric representation used in examples. Schema-conforming mutation list/detail responses failed. | [ReadDtos.cs](../../src/EBoekhouden.Api/Contracts/ReadDtos.cs) permits int32 numeric strings only on the two mutation Type properties. Numeric examples and future numeric codes still work; IDs, money and MCP request parsing remain strict. Both endpoints and numeric MCP output are tested. The schema/example conflict is recorded in [API_COMPATIBILITY.md](API_COMPATIBILITY.md). |

Regression sources: [ReviewRegressionTests.cs](../../tests/EBoekhouden.Api.Tests/ReviewRegressionTests.cs) and [ExecutableTests.cs](../../tests/EBoekhouden.IntegrationTests/ExecutableTests.cs). No package pins, lock files, MCP schemas or OpenAPI content changed.

The exact-number comparison was checked against [the .NET 10 documentation](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonelement.deepequals?view=net-10.0) and [runtime v10.0.13 source](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Text.Json/src/System/Text/Json/Document/JsonElement.cs). Official MCP SDK v2.3.0 source was inspected for tool creation/structured output, explicit DI primitives and single-session lifecycle; existing wire tests exercise those actual APIs.

## Review coverage and evidence

| Area | Result |
| --- | --- |
| Solution/dependencies | Conventional .sln contains six real SDK projects targeting net10.0; exact SDK 10.0.113, central stable package pins and six locks. Production dependency graphs contain no test packages. Locked restore and Release build pass with zero warnings/errors. NuGet direct/transitive vulnerability query reported no advisories for any of the six projects at review time; this is not a guarantee against unknown vulnerabilities. |
| Tools and schemas | Exactly ten explicitly registered tools, with read-only annotations. SDK stdio tools/list matches the checked-in snapshot. Independent Draft202012Validator checks accept all 20 current input/output schemas, ten README arguments and ten freshly mapped fixture outputs. Cross-field range ordering is enforced at runtime. |
| Upstream contracts | All 13 resource/nested DTO property sets match relevant OpenAPI property sets. Required minimum invariants remain an explicit local policy. Eleven routes, filter capabilities, one-time escaping, default 50/max 2000 and balance query mappings agree with the specification. |
| Dispatch security | Closed request factories; innermost method, exact path, scheme/host/port and header/body restrictions; redirects/cookies disabled. Only internal session POST is permitted. Negative guard tests and actual redirect-to-second-loopback tests pass. No invoice PDF URL is fetched. |
| Authentication/races | Lazy cache, semaphore recheck, expiry skew, reference-identity invalidation, canceled waiter/leader recovery and concurrent refresh verified. Old 401 cannot discard a newer lease, even with identical token text. |
| Retry/concurrency | Single shared GET attempt budget includes authentication replay; no session POST retry. Mixed status/network/timeout sequences, Retry-After, queue cancellation, circuit generations and operation deadlines pass. Body buffering remains within existing attempt and operation budgets after the fix. |
| Financial semantics | Decimal internally, exact conversion and invariant MCP strings; DateOnly without timezone conversion; nullable absent money/quantity retained. Quantity and deprecated amount remain independent. Invoice/mutation identities remain separate, mutation output int64 strings, detail input int32. Counts are page counts; full pages provide candidates only. No total/sort/payment inference/aggregation introduced. |
| Malformed responses/errors | Missing essentials, duplicate/case-alias properties, malformed/null bodies, null elements and inconsistent counts fail safely. Additive fields and unknown VAT/category codes survive. Errors use fixed messages and bounded credential/query-echo-filtered metadata; no raw body/inner exception escapes. |
| Credentials and stdio | Isolated synthetic child environment and secret store. Startup is network-free; error stdout empty, static stderr. Trace configuration cannot enable SDK payload logs. Every captured stdout line is JSON-RPC. Initialization and calls work against the actual published DLL. |
| Cancellation/cleanup | Explicit protocol cancellation aborts real HTTP; EOF cancels pending work and exits normally. Failure-path disposal reaps child processes and makes the port reusable. Existing SDK local-cancellation limitation remains below. |

## Remaining findings and verification limits

No unresolved high-severity server defect was identified. The following are retained explicitly; synthetic tests do not prove live compatibility.

| Severity/category | Reference | Impact and next action |
| --- | --- | --- |
| **P2 / medium — confirmed SDK interoperability limitation** | [ConcurrencyTests.cs](../../tests/EBoekhouden.IntegrationTests/ConcurrencyTests.cs), [MCP runtime cancellation](../mcp-runtime.md#errors-and-cancellation) | Canceling only the official SDK client's local task did not reliably send remote cancellation in this harness. HTTP can continue until an explicit notifications/cancelled, disconnect or operation deadline. Explicit notifications and subsequent server use pass. Clients must send cancellation notifications; investigate a narrowly scoped SDK reproduction before considering an upgrade. No server-side claim of automatic local-task forwarding is made. |
| **Medium compatibility risk — live behavior unverified, not a proven code defect** | [SessionTokenProvider.cs](../../src/EBoekhouden.Api/Authentication/SessionTokenProvider.cs), [ReadTransport.cs](../../src/EBoekhouden.Api/Http/ReadTransport.cs), [API_COMPATIBILITY.md](API_COMPATIBILITY.md) | Bearer versus bare-token prose, token alphabet/expiry behavior, actual required-field completeness, mutation type wire representation and wider mutation detail identifiers require authorized live observation. Current choices follow documented schemas/examples with explicit conservative policies. A failure of those assumptions could prevent authentication or reject a live response. No live test was authorized or run. |
| **P3 / low — delivery verification gap** | [verify.yml](../../.github/workflows/verify.yml), [delivery.md](../delivery.md), [StdioProcess.cs](../../tests/EBoekhouden.IntegrationTests/StdioProcess.cs) | The shared CI script passed locally on Linux x64; hosted checkout/setup/cache execution, Windows/macOS, IPv6 listeners and OS-signal shutdown were not run. There is no evidence of a defect, but portability/hosted operation must not be called verified. Run the hosted workflow when available; add platform checks only when those platforms enter release scope. |

## Executed verification

Environment: Linux x64, stable SDK **10.0.113**, runtime **10.0.13**, VSTest **18.0.2**, xUnit adapter **3.1.5**. Environment variables below select writable sandbox caches.

```sh
export DOTNET_CLI_HOME=/tmp/eboekhouden-dotnet
export NUGET_PACKAGES=/tmp/eboekhouden-nuget
bash scripts/verify.sh
dotnet list EBoekhoudenMcp.sln package --vulnerable --include-transitive --no-restore --format json
git diff --check
sha256sum e-boekhouden-api-v1.yaml
```

The baseline shared pipeline passed **438** tests plus **34** published-artifact cases. New defect regressions then failed as expected (five decimal/error-buffer cases; two mutation-type cases). After fixes, the final pipeline exited **0**: locked restore, Release build **0 warnings / 0 errors**, **340 API + 86 MCP + 39 integration = 465 unique tests passed**, zero failed/skipped. Local publish succeeded; all **39 integration cases passed again against the published DLL** under `artifacts/publish/stdio/`. This rerun is additional artifact evidence, not 39 additional unique tests. Dependency audit exited 0 with no reported vulnerabilities. Test execution used local sockets only; restore/audit used public NuGet metadata. Sandbox network/local-socket permission was needed and granted; no execution blocker remains for these checks.

An inspected temporary exporter (`/tmp/eboekhouden-schema-probe`) was rebuilt with current project references and mapped synthetic fixtures without API invocation. Python jsonschema 4.23.0 checked the resulting current schemas/outputs and README arguments; all passed. The OpenAPI SHA256 remains `b883304a41219a15395fde37110db3a6ea3c0d82a467e7c4123abb0e04b72ca9`.

The local offline review is complete. This does not certify hosted CI, other platforms or live service compatibility. Next action: review these focused changes and run hosted CI when available; live smoke testing remains a separate explicitly authorized task.
