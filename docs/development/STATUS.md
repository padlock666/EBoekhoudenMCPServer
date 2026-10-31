# Development status and handoff

Last updated: 2026-10-31.

## Current state

- Phase 1 scaffold is implemented: conventional solution, six SDK-style projects, shared build settings, central package versions, SDK pin, formatting and ignore rules.
- `e-boekhouden-api-v1.yaml` is the upstream contract source.
- All projects target `net10.0`. Release build succeeds with zero warnings/errors. Phase 5 wires the working Generic Host/stdin-stdout executable and exactly ten approved MCP tools.
- Phase 2 implements all core read DTOs, separate MCP request/result records, strict contract serialization, typed filters, endpoint query encoding, validation, and explicit mappings. See [tool-contracts.md](../tool-contracts.md).
- Phase 3 implements lazy single-credential session authentication, separate restricted factory clients, validated configuration/startup hook, shared attempt/concurrency/time budgets and circuit breaking. See [HTTP infrastructure](../http-infrastructure.md).
- Phase 4 implements IEBoekhoudenReadClient with all eleven typed GET operations, API-boundary response validation, disposal/cancellation and normalized safe errors. See [typed API client](../api-client.md).
- Current offline suite: **528 passing tests** (365 API, 87 MCP, 76 executable integration), with one optional GNOME test skipped by default. All 76 executable cases also pass against the local publish. The gated real-GNOME case passes separately against both build and publish. Earlier dated phase counts are historical.
- GNOME Keyring is the sole API-key source, using the GnomeKeyringTest demo's libsecret application/account lookup. See [configuration](../../configuration.md#2-configure-gnome-keyring) and [native implementation](../linux-secret-service.md).
- The workspace was not a Git repository at initial inspection. Git initialization is not required to begin implementation and has not been performed.
- No live API behavior has been verified.
- User guides: [configuration.md](../../configuration.md) covers credentials, Codex registration and a local desktop plugin; [usage.md](../../usage.md) covers all ten tools and accounting limits. Documentation checks are recorded below; client/plugin installation has not been performed.

## Phase tracking

| Phase | Status | Evidence |
| --- | --- | --- |
| 1. Scaffold | Complete | Restore succeeded; six-project Release build: 0 warnings/errors; xUnit adapter discovery verified in all three empty test assemblies |
| 2. Contracts and filters | Complete | Release build: 0 warnings/errors; 48 API and 40 MCP tests pass; exact ten-tool contracts documented |
| 3. Authentication and HTTP | Complete | Restore and Release build succeed, 0 warnings/errors; full offline suite: 171 API + 40 MCP tests pass |
| 4. API client | Complete | Eleven exact-request/typed-result endpoint fixtures; Release build: 0 warnings/errors; full offline suite: 318 API + 40 MCP tests pass |
| 5. MCP tools and host | Complete | Official SDK ten-tool wire schemas/structured results verified; Generic Host and executable smoke checks; Release build 0 warnings/errors; 318 API + 86 MCP tests pass |
| 6. Integration tests | Complete | Actual host/SDK stdio + loopback fake HTTP; 32 integration cases; full offline suite 436 passed; Release build 0 warnings/errors |
| 7. Delivery and CI | Complete | Six dependency locks; locked restore and clean Release build; 438 offline tests + 34 published-artifact cases pass; shared local/CI pipeline and GitHub Actions configuration |
| 8. Independent review | Complete for local offline scope | Three confirmed P2 defects fixed; locked restore, clean Release build, 465 offline tests + 39 published-artifact cases pass; remaining SDK/live/hosted/platform limits recorded in REVIEW.md |
| Follow-up: GNOME Keyring | Complete for local Linux scope | Locked restore/build/publish pass; 528 standard tests, 76 published cases; isolated real-GNOME passes for build and publish |

## Next action

Follow [configuration.md](../../configuration.md#2-configure-gnome-keyring) to provision the API key in the intended desktop session, then register the published DLL with the absolute nonsecret configuration path. Hosted CI, personal client/keyring setup and live e-Boekhouden compatibility remain unverified.

## Phase 1 validation evidence

Validated on 2026-10-29 with installed stable SDK **10.0.113** and runtime **10.0.13** on Linux. Package pins: `ModelContextProtocol` 2.3.0, `Microsoft.Extensions.Hosting` 10.0.13, `xunit.v3` 3.2.2, `xunit.runner.visualstudio` 3.1.5, `Microsoft.NET.Test.Sdk` 18.0.1. See [ARCHITECTURE.md](ARCHITECTURE.md#phase-1-package-and-runner-decisions) for official version-specific SDK and runner sources.

Exact successful commands (temporary writable CLI/package directories were needed in this sandbox):

```sh
export DOTNET_CLI_HOME=/tmp/eboekhouden-dotnet
export NUGET_PACKAGES=/tmp/eboekhouden-nuget
dotnet restore EBoekhoudenMcp.sln --disable-parallel -m:1 -nr:false
dotnet build EBoekhoudenMcp.sln -c Release --no-restore -m:1 -nr:false
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore --list-tests -m:1 -nr:false --logger 'console;verbosity=normal'
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=normal'
```

- Restore: exit 0, all six projects restored/up-to-date; network-enabled execution was required for public NuGet downloads.
- Release build: exit 0, all six projects compiled, **0 warnings, 0 errors**.
- Discovery and test invocation: each exit 0; VSTest **18.0.2** loaded xUnit adapter **3.1.5** and discovered all three assemblies. Each reported **no tests available**, as expected for phase 1. This verifies runner setup, not application behavior.
- Host check: launched `dotnet src/EBoekhouden.Host.Stdio/bin/Release/net10.0/EBoekhouden.Host.Stdio.dll` using Python `subprocess.run(..., capture_output=True, text=True)`: exit 1, stdout 0 bytes, stderr contains the static not-implemented diagnostic. No network call occurs.
- `sha256sum e-boekhouden-api-v1.yaml`: `b883304a41219a15395fde37110db3a6ea3c0d82a467e7c4123abb0e04b72ca9`, unchanged from before scaffold creation.

Environment failures encountered and resolved:

- Initial `dotnet new sln --name EBoekhoudenMcp --format sln` failed because the default template cache was read-only. Re-ran with `DOTNET_CLI_HOME=/tmp/eboekhouden-dotnet`, then added all six projects using `dotnet sln EBoekhoudenMcp.sln add ...`.
- Default solution restore exited 1 without useful errors; diagnostic restore showed a failed project evaluation with zero reported errors. Serial MSBuild (`-m:1 -nr:false`) allowed restore to proceed.
- Restricted restore failed NU1301 (`api.nuget.org:443` permission denied; initial curl also could not resolve DNS). Network-enabled retry succeeded; no dependency substitutions were made.
- Restricted discovery printed `SocketException (13): Permission denied` despite exit 0. It is not counted as successful verification. Re-running discovery and test invocation with local socket permission loaded the adapter correctly in all assemblies.

No remaining phase 1 blocker. No live e-Boekhouden requests, deployment, package publication, Git initialization, or commits were performed. Behavioral tests, protocol integration, and publish checks were not run because their implementation belongs to later phases. Dependency lock files and CI remain phase 7 work. No new upstream specification ambiguity was discovered in this scaffold phase.

Files added: `EBoekhoudenMcp.sln`, six `.csproj` files, minimal `Program.cs`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`. Updated README, architecture decisions, and this status; existing plan, prompts, compatibility notes, and OpenAPI specification preserved.

## Phase 2 handoff — 2026-10-29

Implemented:

- All properties of the 13 relevant GET response DTOs (including nested invoice items, mutation rows/VAT); separate MCP records; explicit field-by-field mappings with no API contract leakage.
- String/date/int64 typed filters and endpoint-specific queries. One operator per field; ordered inclusive ranges use typed `value`/`to` endpoints. Shared encoder escapes raw keys and values once and adds only supported query parameters.
- Pagination default 50, limit 1..2000, nonnegative int32 offset. Page count must match returned items; full-page nextOffset is only a candidate, computed with int64 arithmetic. Balances have no pagination.
- `as_of` sends to only; `period_change` requires from/to. Results retain dates, mode and requested scope; no accounting totals are derived.
- Nullable upstream optional financial fields; strict required identities/dates/envelopes/balance essentials; case-insensitive upstream deserialization, strict camelCase MCP requests. Unknown response VAT/category/type codes preserved.
- Monetary/quantity/percentage MCP strings from decimal. Mutation identities use long internally and strings in MCP results; detail input remains int32 per the inconsistent spec. Quantity and deprecated amount remain independent, with no fallback.

Files: new API `Contracts/` and `Filtering/`; MCP `Contracts/`, `Mapping/`, `Validation/`; API and MCP test files; [tool-contracts.md](../tool-contracts.md). Updated README, architecture, compatibility notes and this handoff. Package pins and host entry point unchanged.

Exact verification commands:

```sh
export DOTNET_CLI_HOME=/tmp/eboekhouden-dotnet
export NUGET_PACKAGES=/tmp/eboekhouden-nuget
dotnet build EBoekhoudenMcp.sln -c Release --no-restore -m:1 -nr:false
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal'
sha256sum e-boekhouden-api-v1.yaml
```

Results: final Release build exit 0, **0 warnings / 0 errors**. Full offline test invocation exit 0: **48 API tests + 40 MCP tests passed, 0 failed, 0 skipped**. IntegrationTests reports no tests, as expected; no integration coverage claimed. VSTest required network-enabled execution for its local sockets, as in phase 1. Tests cover operators, invalid ranges and pagination, escaping (wildcards, Unicode, percent/injection characters), date parsing/formatting, required/null/malformed values, decimal precision/overflow and culture invariance, optional nested collection semantics, identity widths, unknown codes, side/type mappings, page counts and balance modes. No failures remain.

A read-only Python/PyYAML audit compared every DTO property set to the supplied GET response schema (all 13 matched). OpenAPI SHA256 remains `b883304a41219a15395fde37110db3a6ea3c0d82a467e7c4123abb0e04b72ca9`.

No new packages were introduced; build used the existing restored phase 1 graph, so a fresh restore was not needed for this phase. No HTTP clients, live API requests, deployments, publication, or Git operations. SDK-generated MCP schema inspection and protocol tests are unrun because tool/transport registration is phase 5 and integration infrastructure is phase 6. Contract JSON options and validation must be wired into the SDK argument/result path in phase 5 and verified against actual schemas then; no schema compatibility is claimed yet. See API_COMPATIBILITY.md for unverified upstream required-field and identifier-width assumptions.

No remaining phase 2 blocker. Next action is phase 3 authentication and HTTP infrastructure.

## Phase 3 handoff — 2026-10-29

Implemented:

- Separate named session/read clients through IHttpClientFactory, plus singleton token provider and read transport. No transient client captured by a singleton and no default Authorization headers.
- Internal-only POST /v1/session with accessToken/source, no retries; cache/early expiry using injected TimeProvider, cancellation-aware single refresh with double-check, finally-release on all failures/cancellations, atomic lease-identity invalidation (including identical token text).
- Per-request Bearer authorization; at most one 401 replay, none for 403. Authentication replay and transient retries share a single GET budget (default 3, configurable 1..5). Fresh request per attempt; discarded responses disposed. Retry-After honored or returned without retry if beyond allowed delay.
- Overall operation deadline (queue, token waits, session creation, attempts, buffering and backoff), per-attempt and per-session timeouts; shared logical-operation concurrency limit; jittered backoff and generation-safe circuit breaker. Cancellation wins racing network errors and releases resources.
- Closed ReadRequest factories, innermost method/path/origin guards, fixed production origin, redirects/cookies disabled. Code-only numeric loopback registration for tests, never a configuration flag. Both HTTP factory loggers removed; infrastructure emits no bodies/headers/queries/tokens or console output. Failures use static messages with no raw inner exceptions.
- Custom IValidateOptions with ValidateOnStart/IStartupValidator validates configuration locally, with lazy tokens. Generic Host composition remains phase 5; the existing executable is unchanged.

Packages: Microsoft.Extensions.Http **10.0.13** and Microsoft.Extensions.Options.ConfigurationExtensions **10.0.13** in Api; test-only Microsoft.Extensions.TimeProvider.Testing **10.0.0**. Official factory/options/FakeTimeProvider docs and package metadata were checked; restore and compilation verify these pins. MCP SDK APIs/pins unchanged. The planned Http.Resilience/DataAnnotations additions were replaced with one explicit retry/circuit owner and a custom validator; this keeps the shared auth/transient budget observable and avoids extra retry layers.

Files: Api registration, Configuration/, Authentication/, Http/, Properties/AssemblyInfo.cs; five new API test files with fake handlers/time/log capture; package/project references; [HTTP infrastructure](../http-infrastructure.md), README, architecture, compatibility and status updates. OpenAPI unchanged.

Exact successful commands (same writable sandbox CLI/package directories as earlier phases):

```sh
export DOTNET_CLI_HOME=/tmp/eboekhouden-dotnet
export NUGET_PACKAGES=/tmp/eboekhouden-nuget
dotnet restore EBoekhoudenMcp.sln --disable-parallel -m:1 -nr:false
dotnet build EBoekhoudenMcp.sln -c Release --no-restore -m:1 -nr:false
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal'
sha256sum e-boekhouden-api-v1.yaml
```

Outcomes: restore exit 0; final Release build exit 0, **0 warnings / 0 errors**; final full offline test command exit 0, **171 API + 40 MCP = 211 passed, 0 failed, 0 skipped**. The empty integration project reports no tests; no integration coverage claimed. Restore used approved network-enabled execution for public NuGet downloads; VSTest used local socket permission. The business API was never called. Timed tests use FakeTimeProvider, synthetic credentials/data and fake primary handlers; the actual guards/factory/token provider/retry code remain active.

A focused intermediate run found one failed encoded-path restriction test: Uri normalized an escaped digit before checking AbsolutePath. The guard now rejects encoded/dot-segment/backslash path spellings via OriginalString before dispatch, and that regression plus the full suite passes. No known failures remain.

OpenAPI SHA256 remains `b883304a41219a15395fde37110db3a6ea3c0d82a467e7c4123abb0e04b72ca9`. No credentials, response bodies or real financial records were added. No Git initialization/commits, deployment or package publication occurred.

Unrun: live authentication/header/expiry verification (not authorized), typed endpoint/error-fixture coverage (phase 4), actual executable startup/configuration and SDK protocol/schema checks (phase 5), subprocess integration (phase 6), publish/CI/locks (phase 7). Startup validation is verified through its IStartupValidator hook without network, not by claiming the scaffold executable is a functioning host. Bearer-vs-bare-token prose remains explicitly unverified in API_COMPATIBILITY.md.

No remaining phase 3 blocker. Next action is phase 4 typed read-only client and normalized errors.

## Phase 4 handoff — 2026-10-29

Implemented:

- IEBoekhoudenReadClient / EBoekhoudenReadClient: relation list/detail, invoice list/detail, mutation list/detail, outstanding, ledger list/detail, all balances and single balance. Registered as singleton over existing ReadTransport, with only approved ReadRequest/QueryEncoder routes and required cancellation tokens.
- Exact search/detail DTO separation, long mutation identities, DateOnly dates, nullable decimal money and optional arrays. No invoice payment inference, invoice/mutation conversion, absent-money zero defaults, PDF fetches or derived totals. Single/all balance scope validation occurs before authentication.
- Only HTTP 200 typed shapes succeed. API boundary rejects null/malformed/missing/duplicate fields, inconsistent page counts and null nested elements; empty envelopes succeed. Existing duplicate-property JSON checks now also apply upstream. Reviewed optionality against GET schemas and recorded invoice-items prose/required-marker ambiguity.
- ApiException has fixed safe messages, actual HTTP status, kind, suppressed upstream code/trace and transport reason. Error array/dictionary/security shapes, non-JSON/malformed bodies and mismatched body status normalize defensively. Free text/raw bodies/URIs/inner exceptions never escape. Error-body handling and reflection safety are documented.
- Session errors retain HTTP status and suppress upstream code/trace; invalid session payloads retain received status. No retry policy added. Final responses/streams are disposed; the typed-call deadline extends through parsing/validation and preserves caller cancellation.
- Fourteen clearly labeled embedded synthetic JSON fixtures (eleven GET responses plus three error shapes), fixture README and endpoint/validation/error test classes. Tests retain the actual factory/authentication/guards/transport with only fake primary handlers.

Files: new Api Clients/ and Errors/; ApiJson, DI registration, ReadTransport response provenance, session normalization and ApiTransportException metadata updates; API test resources/support/three test classes; [typed client](../api-client.md), README, tool-contract/infrastructure links, architecture, compatibility and status notes. No package, SDK or MCP contract change.

Exact final verification commands (same writable cache/CLI paths as previous phases):

```sh
export DOTNET_CLI_HOME=/tmp/eboekhouden-dotnet
export NUGET_PACKAGES=/tmp/eboekhouden-nuget
dotnet build EBoekhoudenMcp.sln -c Release --no-restore -m:1 -nr:false
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal'
sha256sum e-boekhouden-api-v1.yaml
```

Results: Release build exit 0, **0 warnings / 0 errors**. Full offline suite exit 0: **318 API + 40 MCP = 358 passed, 0 failed, 0 skipped**. IntegrationTests reports no tests, as expected. An earlier focused API run passed 284 tests before the final error-normalization cases were added; the final full run includes all 318 API tests. VSTest required approved local socket access; all upstream handlers were synthetic, with no live requests. No failures or remaining phase 4 blockers.

Package graph unchanged; no new restore was needed. The OpenAPI SHA256 remains `b883304a41219a15395fde37110db3a6ea3c0d82a467e7c4123abb0e04b72ca9`. No secrets/actual financial records, Git operations, deployment or publication were introduced.

Unrun: authorized live compatibility (not requested), SDK-generated schemas and actual stdio host/tool behavior (phase 5), subprocess integration (phase 6), publish/CI/locks (phase 7). API compatibility notes retain the unverified Bearer header, response required-field policy, mutation width conflict and diagnostic-format assumptions. Next action is phase 5; do not claim tools are callable until then.

## Phase 5 handoff — 2026-10-29

Implemented:

- Exactly ten explicit delegates on the approved AccountingTools class, registered as SDK primitives without assembly scanning. Thin validation/invocation/explicit mapping; all eleven GET paths covered. No new upstream filters/routes, mutable tools or session tool.
- Official MCP SDK 2.3.0 typed creation and structured output, flat strict input adapter, refined generated input/output schemas and read-only/non-destructive/idempotent/open-world annotations. Reviewed [schema snapshot](../tool-schemas.json) is compared with actual SDK client tools/list output. Runtime ordering checks supplement schemas.
- Success has structured content plus matching JSON text. Validation, API and unexpected execution failures have isError=true and safe JSON text, without a success-shaped structured page. Original raw arguments reject duplicate fields before dictionary binding. Genuine empty upstream pages remain successful.
- Generic Host composition, startup options validation, external nonsecret configuration, lazy authentication, stdio transport, cancellation and bounded shutdown. Console logging forced to stderr; SDK payload logs and Host internal exception logs blocked regardless of configuration. Static entry-point failure diagnostic. Production origin remains fixed.
- 46 new offline MCP/host cases (86 MCP total): all tool mappings/exact queries, balances, required/null/type/case/unknown/range/limit validation before any session/GET, raw duplicate arguments, schemas/annotations, output precision/text equality, real empty pages, normalized/malformed/unexpected failures, explicit protocol cancellation, fake-infrastructure Generic Host startup/call/shutdown and protected logger configuration.

Files: Mcp Tools/ and registration extension; HostComposition/Program; MCP test project references/resources plus ProtocolHarness, ToolExecutionTests, ToolSchemaTests and HostTests; [MCP runtime](../mcp-runtime.md), actual schema snapshot, README, tool-contract/infrastructure notes, architecture, compatibility and this status. Existing package pins and OpenAPI unchanged. No placeholder production classes or meaningless discovery tests.

SDK APIs were checked against official tagged v2.3.0 docs/source, including McpServerTool.Create/options, DelegatingMcpServerTool, typed structured formatting, explicit primitive collection and stream/stdio transports. No inferred SDK attributes/methods. No new product/test package dependency was added.

Exact final verification commands:

```sh
export DOTNET_CLI_HOME=/tmp/eboekhouden-dotnet
export NUGET_PACKAGES=/tmp/eboekhouden-nuget
dotnet restore EBoekhoudenMcp.sln --disable-parallel -m:1 -nr:false
dotnet build EBoekhoudenMcp.sln -c Release --no-restore -m:1 -nr:false
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal'
python3 /tmp/eboekhouden-phase5-smoke.py
sha256sum e-boekhouden-api-v1.yaml
```

Results: restore exit 0; Release build exit 0, **0 warnings / 0 errors**; full offline suite exit 0, **318 API + 86 MCP = 404 passed, 0 failed, 0 skipped**. IntegrationTests intentionally reports no tests; no phase 6 suite coverage claimed. Restore required approved public NuGet access; VSTest required approved local socket access. All upstream handlers in tests were synthetic. No live API calls.

Additional verification: a temporary external SDK-schema/mapped-fixture exporter produced ten actual tools and ten synthetic outputs. Independent Python jsonschema **4.23.0** Draft202012Validator checks accepted all **20 schemas**, all **10 mapped outputs**, and **10 valid inputs**; rejected **13 invalid inputs** (required values, casing, limits, enums, range and balance conditions, invalid dates). The audit used a /tmp-only uv target installation after system Python venv creation lacked ensurepip; no repository dependency was introduced. The temporary exporter initially had a restricted NuGet metadata warning, resolved by its restore; final exporter and solution builds were clean. The checked-in SDK-client snapshot/shape tests provide ongoing regression evidence.

The one-off subprocess smoke (`/tmp/eboekhouden-phase5-smoke.py`, synthetic environment, no valid business calls) passed: missing configuration exits 1 with empty stdout/static stderr; initialization, exactly ten listed tools and invalid-argument tool error work; every stdout line is protocol JSON; forced Trace settings do not emit synthetic credential/query/result markers to stderr; stdin EOF exits 0. Generic Host with actual API/auth stack and fake HTTP primary handlers is also covered by committed tests. This smoke does not replace phase 6 fake-upstream subprocess tests.

An initial SDK-client local-cancellation test timed out waiting for server-side cancellation, while an explicit notifications/cancelled message with a known request ID reaches the HTTP handler correctly. The committed test verifies the actual protocol notification and cancellation propagation. Client local-token forwarding remains a compatibility investigation for phase 6; do not equate local task cancellation with receipt of a remote notification. No current test failures or remaining phase 5 blockers.

OpenAPI SHA256 remains `b883304a41219a15395fde37110db3a6ea3c0d82a467e7c4123abb0e04b72ca9`. No real secrets/financial records, Git operations, deployment or publication. Unrun: live compatibility (not authorized), dedicated fake-upstream subprocess integration suite (phase 6), publishing/CI/lock files (phase 7). Next action: phase 6.

## Phase 6 handoff — 2026-10-30

Implemented:

- Actual compiled host child processes connected through official MCP SDK 2.3.0 StreamClientTransport, with a real Kestrel fake bound atomically to 127.0.0.1:0. Synthetic credentials/fixtures only; child environments are cleared and scratch working directories isolated. Test-only Microsoft.AspNetCore.App framework reference; no package pin or production framework change.
- Explicit executable gate: DOTNET_ENVIRONMENT=IntegrationTest plus EBoekhoudenTest__UpstreamOrigin, validated through the existing numeric-loopback-only API registration. Missing/invalid origin or an override outside IntegrationTest fails before network access. Production origin, route/method guards, auth and redirect restrictions remain intact.
- 32 integration cases: initialization/discovery across three protocol revisions, exactly ten snapshot schemas, all eleven routes/structured results, zero HTTP for invalid arguments, startup/session/upstream failures and redaction, cold/expired/concurrent refresh and late stale 401, pagination, both balance modes/scopes, redirects, cancellation, pending/idle EOF and cleanup after protocol failure. Raw stdout and stderr are captured separately and checked independently of SDK parsing.
- Fixed a real EOF shutdown regression: SDK 2.3.0 waits for handlers before its single-session hosted service stops the host. New StdioDisconnectService observes transport reader completion and stops Generic Host, canceling pending HTTP. The regression requires HTTP abort and normal exit within five seconds. Test cleanup has bounded waits, process-tree kill/reap fallback, socket disposal and scratch removal; a failure-path test verifies process exit and immediate port reuse.

Files: HostComposition and new StdioDisconnectService; integration project plus FakeUpstream, StdioProcess, ExecutableTests and ConcurrencyTests; [integration guide](../integration-tests.md), README, runtime/infrastructure/architecture/compatibility notes and status. OpenAPI and package pins unchanged.

Exact final successful commands:

```sh
export DOTNET_CLI_HOME=/tmp/eboekhouden-dotnet
export NUGET_PACKAGES=/tmp/eboekhouden-nuget
dotnet restore EBoekhoudenMcp.sln --force --no-http-cache --disable-parallel -m:1 -nr:false
dotnet build EBoekhoudenMcp.sln -c Release --no-restore -m:1 -nr:false
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal'
sha256sum e-boekhouden-api-v1.yaml
```

Results: restore exit 0 without warnings; final Release build exit 0, **0 warnings / 0 errors**; full suite exit 0, **318 API + 86 MCP + 32 integration = 436 passed, 0 failed, 0 skipped**. A preceding focused integration run passed 27 cases before the additional protocol/origin checks. Default test execution makes only loopback requests; no live API was contacted. OpenAPI SHA256 remains `b883304a41219a15395fde37110db3a6ea3c0d82a467e7c4123abb0e04b72ca9`.

Resolved implementation/environment issues: restricted restore cached NU1900 audit-metadata failures; approved forced restore without HTTP cache resolved them. VSTest and the fake HTTP server require approved local socket/bind access in this sandbox. The ASP.NET test framework reference pruned Extensions DLLs from the test output root, so launching that copy failed; the build now copies the host's intact own output into ignored StdioHost/ and launches it with its own deps/runtime files. An intermediate MSBuild metadata/path error was corrected with an explicit nonempty-directory guard. No build/test failures remain.

SDK client local cancellation again did not cause timely remote HTTP abort in the initial subprocess test. The suite explicitly sends notifications/cancelled with a known request ID and verifies TCP abort plus later successful calls; local task cancellation alone is not counted as remote cancellation evidence. This client compatibility limitation is documented; no SDK version/third-party code was changed. Server cancellation and EOF shutdown are verified.

Platform evidence: Linux x64, SDK 10.0.113, runtime 10.0.13. Tests need the ASP.NET Core 10 shared runtime (present here) and a resolvable dotnet host. Windows/macOS, IPv6 listeners and OS-signal shutdown were not run; EOF is the tested disconnect path. One subprocess expiry test waits 2.1 seconds on system time; race scheduling uses gates and API boundary timing remains covered with FakeTimeProvider. Captures remain in memory; no synthetic secrets are emitted into host logs/errors.

No phase 6 blocker. Unrun: publishing/CI/locks and published-artifact smoke (phase 7), independent review (phase 8), live compatibility (not authorized). No deployment, package publication, database or Git operations. Next action: phase 7.

## Phase 7 handoff — 2026-10-30

Implemented:

- Expanded README with verified SDK/runtime/runner requirements, locked restore/build/test/publish commands, local setup, absolute published-DLL MCP client configuration, all ten tool examples, operators/pagination, accounting/identity/precision limits, safe errors and troubleshooting. Development documentation index preserved; [delivery decisions](../delivery.md) and runtime/integration/architecture/compatibility notes updated.
- Six SDK-generated packages.lock.json files; RestorePackagesWithLockFile and RestoreLockedMode enabled centrally. Pins unchanged. Intentional updates require explicit unlocked regeneration and lock review.
- No prior CI configuration was present; documented GitHub Actions/Ubuntu 24.04 before adding .github/workflows/verify.yml. Official checkout v6 and setup-dotnet v5 tag SHAs/metadata verified and pinned; contents-read permission, no persisted checkout credential, SDK from global.json, VSTest and lock-keyed package cache. Shared scripts/verify.sh runs restore/build/tests/local publish and published-artifact tests. No deploy/push/upload step or accounting credential.
- Local ignored artifact at artifacts/publish/stdio/EBoekhouden.Host.Stdio.dll, framework-dependent, untrimmed, no RID/apphost, no bundled runtime. Runtime configuration requires Microsoft.NETCore.App only; no test assemblies/xUnit in publish output.
- Integration harness accepts an explicit absolute EBOEKHOUDEN_TEST_HOST_DLL with required companion files, failing instead of falling back. All existing integration cases also run against the exact publish, retaining synthetic loopback API, capture/redaction, cancellation and cleanup.

Commands actually executed:

```sh
export DOTNET_CLI_HOME=/tmp/eboekhouden-dotnet
export NUGET_PACKAGES=/tmp/eboekhouden-nuget
# Initial lock generation only:
dotnet restore EBoekhoudenMcp.sln -p:RestoreLockedMode=false --force-evaluate --disable-parallel -m:1 -nr:false
# Final pipeline, shared verbatim with CI:
bash scripts/verify.sh
bash -n scripts/verify.sh
sha256sum e-boekhouden-api-v1.yaml
```

The final script executes:

```sh
dotnet restore EBoekhoudenMcp.sln --locked-mode --disable-parallel -m:1 -nr:false
dotnet build EBoekhoudenMcp.sln -c Release --no-restore -m:1 -nr:false
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal'
dotnet publish src/EBoekhouden.Host.Stdio/EBoekhouden.Host.Stdio.csproj -c Release --no-restore --no-self-contained -p:UseAppHost=false -o "$PWD/artifacts/publish/stdio" -m:1 -nr:false
EBOEKHOUDEN_TEST_HOST_DLL="$PWD/artifacts/publish/stdio/EBoekhouden.Host.Stdio.dll" dotnet test tests/EBoekhouden.IntegrationTests -c Release --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal'
```

Results: lock generation and final pipeline each exit 0. Final locked restore succeeds; Release build **0 warnings / 0 errors**; full suite **318 API + 86 MCP + 34 integration = 438 passed, 0 failed, 0 skipped**; publish exit 0; **all 34 integration cases pass again against the published DLL**, including initialization, every tool/schema, fake accounting calls and Production configuration errors on stderr/empty stdout. Earlier pipeline with 33 integration cases also passed before the additional Production check. Six lock-file SHA256 values are unchanged by the final locked pipeline. No test failure remains.

Additional checks: bash syntax passes; workflow YAML parsed with PyYAML 6.0.1 and checked for event names, read-only permissions, Ubuntu runner, full action SHAs and the shared entry point. All ten README argument examples validate against reviewed SDK schemas using the existing /tmp jsonschema 4.23.0 audit installation. A /tmp-only copied graph with Microsoft.Extensions.Http changed from 10.0.13 to 10.0.12 failed locked restore as expected (exit 1, NU1004); the repository pin/locks were untouched. Audit metadata was disabled only for that isolated negative control; normal pipeline audit settings were retained. git check-ignore confirms the publish DLL is ignored. No integration scratch/isolated secret directories remain.

Environment/evidence limits: Linux x64, SDK 10.0.113, runtime 10.0.13. Restore/action metadata lookup used approved public network access; test execution required approved VSTest/loopback socket permission. No current environment blocker. GitHub hosted checkout/setup/cache/job execution has not been run; only the identical pipeline commands and workflow structure were verified locally. Windows/macOS, RID-specific/self-contained outputs and OS-signal shutdown remain unrun. No live accounting API calls, deployment, package publication, Git initialization/staging/commits or real secret/data files were introduced.

OpenAPI SHA256 remains `b883304a41219a15395fde37110db3a6ea3c0d82a467e7c4123abb0e04b72ca9`. Existing live auth/response/expiry assumptions, mutation-width mismatch and SDK local-cancellation caveat remain open. No remaining phase 7 blocker. Next action: phase 8 independent review in a fresh session; do not call the release independently reviewed yet.

## Handoff template

Append or summarize a handoff after each work session. Keep this file concise and current.

```text
Date:
Phase and status:
Implemented behavior:
Files changed:
Commands run and exact outcomes:
Checks not run and reason:
Decisions / compatibility findings:
Known failures or blockers:
Next concrete action:
```

## Phase 8 independent review handoff — 2026-10-30

Reviewed actual solution/projects, central dependencies and locks, API/authentication/HTTP/filter contracts, MCP mappings/schemas, host lifecycle, synthetic test harnesses, delivery workflow/docs and relevant OpenAPI operations/components. The initial working tree was clean. The OpenAPI and existing package/SDK/schema pins remain unchanged.

Confirmed and fixed three **P2 / medium** defects, each reproduced before its fix:

1. Built-in decimal deserialization silently rounded excess precision/underflow. ExactDecimalConverter now rejects changed financial values; exact exponents, decimal extrema, preserved scale and nullable fields remain supported.
2. Automatic HttpClient buffering lost received error status on oversized/broken diagnostic bodies and retried GET permission denials as network failures. Headers-first bounded buffering preserves status and discards unreadable diagnostics; disposal/cancellation and existing retry budgets remain enforced.
3. Mutation Type accepted only numeric examples, rejecting the specification’s string enum. Only the two upstream mutation Type properties now accept int32 strings as well as numbers; numeric MCP output and strict identity/financial/request parsing remain intact.

Added 22 API regression cases and five actual executable/protocol cases. Updated API/HTTP/tool/compatibility notes and the review report. No additional package, tool, route, database or accounting rule.

Exact main commands (with writable sandbox cache environment):

```sh
export DOTNET_CLI_HOME=/tmp/eboekhouden-dotnet
export NUGET_PACKAGES=/tmp/eboekhouden-nuget
bash scripts/verify.sh > /tmp/eboekhouden-review-baseline.log 2>&1
dotnet test tests/EBoekhouden.Api.Tests -c Release --no-restore -m:1 -nr:false --filter FullyQualifiedName~ReviewRegressionTests --logger 'console;verbosity=minimal'
dotnet test tests/EBoekhouden.Api.Tests -c Release --no-restore -m:1 -nr:false --filter FullyQualifiedName~MutationTypeAcceptsTheSchemaString --logger 'console;verbosity=minimal'
bash scripts/verify.sh > /tmp/eboekhouden-review-final.log 2>&1
dotnet list EBoekhoudenMcp.sln package --vulnerable --include-transitive --no-restore --format json
DOTNET_CLI_HOME=/tmp/eboekhouden-dotnet NUGET_PACKAGES=/tmp/eboekhouden-nuget dotnet run --project /tmp/eboekhouden-schema-probe -c Release --no-restore --no-launch-profile -p:BuildInParallel=false
git diff --check
sha256sum e-boekhouden-api-v1.yaml
```

Outcomes:

- Baseline workflow exit 0: 438 tests and 34 artifact cases pass, confirming the old suite did not detect the three findings.
- Pre-fix regression runs: expected exit 1; five decimal/buffer cases failed, six exact-decimal controls passed; two mutation-type cases failed. The first oversized fake reused disposed content on retry; it was corrected to create fresh content per attempt and rerun, confirming status loss without that harness error.
- Final workflow exit 0: locked restore succeeds; Release build **0 warnings / 0 errors**; **340 API + 86 MCP + 39 integration = 465 unique tests pass**, none failed/skipped. Release publish succeeds into ignored artifacts/publish/stdio; all **39 integration tests pass again against that published DLL**. Includes revised error buffering and decimal/type behavior, exact schemas, synthetic secrets, EOF/cancellation and subprocess cleanup.
- NuGet vulnerability query exit 0: no reported advisories for direct/transitive dependencies in the six projects at review time. Production graphs contain no test packages. Pins/locks unchanged.
- Independent static property comparison: all 13 resource/nested DTO field sets match OpenAPI. Current SDK/schema exporter rebuilt successfully, making no HTTP invocation. Python jsonschema 4.23.0 validates 20 schemas, ten fresh mapped synthetic outputs and ten README arguments; actual SDK schemas equal the reviewed snapshot.
- OpenAPI SHA256 unchanged: b883304a41219a15395fde37110db3a6ea3c0d82a467e7c4123abb0e04b72ca9. Final whitespace/document link/lock/publish checks pass.

Network/local-socket permission was granted for public NuGet metadata and VSTest/loopback checks; no current blocker for executed checks. No live API, deployment, package push or Git commit. Hosted Actions, Windows/macOS, IPv6 listener and OS-signal checks remain unrun. SDK client local-task cancellation alone remains an interoperability limitation; explicit notifications abort upstream HTTP. Live Bearer/token format/expiry, response completeness and identifier-width assumptions remain unverified. Full severities, impacts and file references are in [REVIEW.md](REVIEW.md). Phase 8 completes the local offline review, not blanket release certification across unrun environments.

## GNOME Keyring handoff — 2026-10-31

Implemented:

- Adapted `KeyringPassword` and the fixed-ABI SafeHandle wrapper from `/home/jimmy/Documents/Projects/GnomeKeyringTest/` without changing that project. Production registers only `KeyringPasswordSource`; its application/account attributes default to `EBoekhoudenMcp` / `api`.
- Lookup runs on a worker with native cancellation and owned-memory cleanup. Session content remains deferred until after connection/TLS setup, one-shot and buffer-clearing. Cached sessions, synchronized refresh, bounded retries and fixed read-only routes are retained. Upstream error code/trace are suppressed to prevent reflection of the released key.
- Configuration contains only nonsecret settings; an absolute `--config` selects JSON. README, configuration, examples, architecture, HTTP/API/runtime, compatibility, integration and delivery documentation describe the current implementation. The setup guide includes interactive `secret-tool store`, exact matching attributes, prerequisites, desktop unlock, custom accounts and rotation.
- Executable tests use the real adapter with owned synthetic Secret Services/private buses. An optional real-GNOME test uses its own daemon, bus and HOME/XDG directories and provisions with `secret-tool store`. No personal keyring or live API is used.

Exact final commands (writable CLI/package caches in this sandbox):

```sh
export DOTNET_CLI_HOME=/tmp/eboekhouden-dotnet
export NUGET_PACKAGES=/tmp/eboekhouden-nuget
bash scripts/verify.sh > /tmp/eboekhouden-keyring-verify.log 2>&1
EBOEKHOUDEN_TEST_REAL_GNOME=1 dotnet test tests/EBoekhouden.IntegrationTests -c Release --no-build --no-restore -m:1 -nr:false --filter FullyQualifiedName~IsolatedGnomeTests --logger 'console;verbosity=minimal'
EBOEKHOUDEN_TEST_REAL_GNOME=1 EBOEKHOUDEN_TEST_HOST_DLL="$PWD/artifacts/publish/stdio/EBoekhouden.Host.Stdio.dll" dotnet test tests/EBoekhouden.IntegrationTests -c Release --no-build --no-restore -m:1 -nr:false --filter FullyQualifiedName~IsolatedGnomeTests --logger 'console;verbosity=minimal'
git diff --check
git diff --exit-code -- Directory.Packages.props global.json ':(glob)**/packages.lock.json' docs/tool-schemas.json e-boekhouden-api-v1.yaml
sha256sum e-boekhouden-api-v1.yaml
```

Outcomes:

- Shared pipeline exit 0: locked restore succeeds; Release build **0 warnings / 0 errors**; **365 API + 87 MCP + 76 integration = 528 standard tests pass**. Local framework-dependent publish succeeds; **76 integration cases pass again against the published DLL**. The one optional real-GNOME test is skipped in each standard run and is not counted as passing there.
- Both explicit real-GNOME commands exit 0: **1 case passes, 0 failures/skips**, separately against build and published host. Provisioning uses the installed `secret-tool` on the test's private bus; no desktop keyring is inherited.
- JSON/TOML/shell syntax checks pass for **14 JSON, 1 TOML and 14 shell fences** in the user/native guides, plus the example JSON. Local documentation files and heading anchors resolve. A disposable `/tmp` Codex profile is inspected with `codex mcp get eboekhouden --json` (exit 0), confirming the launch arguments and `DBUS_SESSION_BUS_ADDRESS` allowlist; no server connection or personal client registration is made.
- Repository searches find no setup/documentation references to the superseded credential implementations. Central pins, six locks, reviewed tool schemas and OpenAPI are unchanged. OpenAPI SHA256 remains `b883304a41219a15395fde37110db3a6ea3c0d82a467e7c4123abb0e04b72ca9`. Whitespace checks pass.

Environment: Linux x64, SDK 10.0.114/runtime 10.0.14; libsecret/tools 0.21.4-1build3, GLib/GIO 2.80.0-6ubuntu3.10, dbus 1.14.10-4ubuntu4.1, GNOME Keyring 46.1-2ubuntu0.2, Python GI 3.48.2-1. Existing installed libraries/tools were used. Official GNOME lookup/freeing and secret-tool documentation, the MCP SDK 2.3.0 guide, and official OpenAI MCP environment forwarding documentation were rechecked. CI now installs the fake-service native prerequisites before its shared pipeline; hosted execution was not run here.

Resolved execution issues: the initial sandbox test invocation aborted because VSTest local sockets were denied; socket-capable execution passed. The first gated GNOME attempt raced daemon startup; an explicit bounded D-Bus name readiness wait fixed the fixture, and both final gated runs pass. No remaining execution blocker.

Limits: live authentication, deferred/chunked body compatibility, desktop unlock UI, hosted CI, other native versions/architectures and personal client/keyring setup are unverified. No real credential entry, financial data, live accounting request, deployment, package publication, package/plugin installation or Git commit occurred. Next concrete action is operator keyring setup using the documented `secret-tool store` command and client registration; live API checks require separate authorization.
