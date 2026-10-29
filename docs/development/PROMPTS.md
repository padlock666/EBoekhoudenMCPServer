# Copyable implementation prompts

Select the model and effort in your coding environment. Use one phase per session or turn. Read [STATUS.md](STATUS.md) first; do not redo completed work. For a partial phase, instruct the AI to continue from the existing implementation.

## Shared instructions

Paste this before each phase prompt in a new session:

```text
You are implementing a production-quality read-only e-Boekhouden MCP server
in C#/.NET 10. Read AGENTS.md, docs/development/STATUS.md, the implementation
plan, architecture, compatibility notes, existing code, and the relevant
sections of e-boekhouden-api-v1.yaml before changing anything.

Implement the requested phase in the filesystem. Produce real SDK-style
.csproj files and EBoekhoudenMcp.sln, not merely example snippets.

Use stable compatible packages and the official MCP C# SDK. Verify APIs
against the pinned SDK documentation instead of guessing methods or
attributes. Preserve the OpenAPI file and existing user changes.

Business operations must be read-only; POST /v1/session is internal-only
authentication. Do not expose arbitrary HTTP requests. Do not invent API
filters, routes, totals, sorting, or accounting rules.

Keep API and MCP contracts separate. Use decimal for financial arithmetic,
DateOnly for accounting dates, and cancellation tokens throughout.
Keep stdout exclusively for protocol traffic. Never log secrets or raw
financial request/response data. Default tests are offline and synthetic.
Do not deploy, publish packages, or call the live API.

Complete the requested phase and its necessary dependencies. Run meaningful
checks and fix failures. Report actual results, including environment
blockers and unrun checks. Update STATUS.md and relevant design notes with
a concise handoff. Do not mark a phase complete without its evidence.
```

## Phase 1 — Scaffold (GPT-6.1 Sol, medium)

```text
Implement phase 1 of docs/development/IMPLEMENTATION_PLAN.md.

Create EBoekhoudenMcp.sln and these net10.0 projects:
- src/EBoekhouden.Api: class library
- src/EBoekhouden.Mcp: class library referencing Api
- src/EBoekhouden.Host.Stdio: executable referencing Mcp
- tests/EBoekhouden.Api.Tests
- tests/EBoekhouden.Mcp.Tests
- tests/EBoekhouden.IntegrationTests

Use an explicit conventional .sln format. Configure appropriate test
references, xUnit v3, and a compatible runner; document dotnet test usage.

Add global.json for an installed stable .NET 10 SDK, central package
management, nullable reference types, implicit usings, built-in analyzers,
.editorconfig, and .gitignore. Pin stable compatible package versions.
If .NET 10 is unavailable, report the blocker instead of targeting an
older framework or claiming the build passed.

Preserve and refine existing development documents. Scope is ten tools
over stdio, one credential, no database. Record concrete package and
runner choices. Do not introduce placeholder production classes or
meaningless tests merely to make discovery green.

Restore, build Release, verify test runner setup, and update STATUS.md.
```

## Phase 2 — Contracts (GPT-6.1 Sol, high)

```text
Implement phase 2: contracts, filters, and validation.

Read the specification sections needed for the ten core tools. Create API
DTOs and separate MCP request/result records. Create docs/tool-contracts.md
with exact names, arguments, output shapes, endpoint mappings, and limits.

Implement typed string/date/integer filters and a shared query encoder.
Permit only supported operators per parameter and one operator per field.
Encode wildcards and bracket syntax correctly without double encoding.

Use default limit 50, maximum 2000, nonnegative offsets. Treat API count as
page count, never total matches. Full pages yield only candidate next
offsets. Do not invent server-side sorting or extra filtering.

Encode balance modes explicitly: as_of sends to only; period_change sends
from and to. Return the mode and dates. Keep invoice and mutation IDs
distinct and document handling of inconsistent identifier widths.

Choose and document MCP monetary serialization with decimal internally.
Preserve unknown response VAT/category codes where enums are incomplete.
Document quantity versus deprecated amount handling.

Test valid and invalid filters, ranges, escaping, date serialization,
monetary precision, casing, required fields, and balance query mapping.
Build, run relevant tests, and update STATUS.md.
```

## Phase 3 — Authentication and HTTP (GPT-6.1 Sol, high)

```text
Implement phase 3: authentication and HTTP infrastructure.

Create separate session and read clients using IHttpClientFactory.
Implement a session-token provider for one configured credential:
- POST /v1/session with accessToken and source
- cache token and expiry using injected TimeProvider
- refresh shortly before expiry
- synchronize concurrent refreshes and recheck cached state under lock
- release synchronization on failure and cancellation
- do not let an old failed request invalidate a newer token

Follow the securitySchemes Bearer example as the initial documented header
assumption and retain the conflicting prose as unverified compatibility.
Attach authorization per request. Allow at most one authentication
refresh/replay per GET and do not endlessly refresh on permission denials.

Configure bounded transient retries, timeouts, and concurrency limits.
Bound total attempts across retry layers; recreate requests safely.
Disable automatic session POST retries.

Enforce an exact method/route/origin allowlist before dispatch and disable
redirects. No user-supplied URL or path. Keep test overrides explicit.

Validate configuration at startup without network access; obtain tokens
lazily. Credentials come from environment variables or user secrets.
Do not log secrets, bodies, or sensitive query values.

Use fake handlers and controllable time to test reuse, concurrent refresh,
expiry, stale-token invalidation, failure recovery, cancellation, request
restrictions, and retry budgets. Build/test and update STATUS.md.
```

## Phase 4 — API client (GPT-6.1 Sol, high)

```text
Implement phase 4: the typed read-only API client.

Implement relation list/detail, invoice list/detail, mutation list/detail,
outstanding invoices, ledger list/detail, all balances, and single balance.
Reuse prior authentication, filtering, and HTTP infrastructure.

Preserve search/detail differences. Relation search contains only id,
type, code. Outstanding items reference mutationId. Never infer invoice
payment status from invoice details or fabricate absent data.

Normalize upstream errors with useful status, code, and trace ID while
sanitizing messages and avoiding raw-body exposure. Handle malformed JSON,
missing essential fields, empty results, optional/additive fields, and
cancellation deliberately.

Add clearly labeled synthetic fixtures and tests asserting exact requests
and typed results for each endpoint. Review DTO nullability against the
spec rather than filling missing financial data with zero by default.

Run API tests and Release build. Update STATUS.md and compatibility notes.
```

## Phase 5 — MCP tools and host (GPT-6.1 Sol, medium)

```text
Implement phase 5: MCP tools and the stdio executable.

Expose exactly the ten core tools documented in ARCHITECTURE.md and
docs/tool-contracts.md using the official SDK. Register approved tool
classes explicitly. Provide concise descriptions, input/output schemas,
and read-only annotations.

Verify actual generated JSON schemas; do not assume C# union/nullability
or enum representations automatically produce the intended interface.

Keep tools thin: validate, invoke, map, translate errors. Invalid input
must fail before upstream access. Return structured results and compatible
text content through the pinned SDK. Execution failures must be tool
errors, not successful empty pages.

Wire Generic Host, DI, options, cancellation, shutdown, and stdio transport.
All logs must go to stderr. Configure credentials through external
configuration and document placeholder-only examples.

Add tool tests for mappings, validation, schema shape, structured output,
and failures. Build/test without live API access and update STATUS.md.
```

## Phase 6 — Integration tests (GPT-6.1 Sol, high)

```text
Implement phase 6: executable and protocol integration tests.

Launch the actual stdio host as a child process and connect using the
official MCP C# client SDK. Run a local fake upstream HTTP server with
synthetic credentials. Permit loopback origin override only in explicit
test configuration while retaining production restrictions.

Test initialization, tools/list, exactly ten expected schemas, successful
calls and structured results, invalid arguments causing zero upstream
requests, authentication/validation failures, concurrent session refresh,
pagination, balance interpretation, cancellation, and clean shutdown.

Capture stdout and stderr separately. Verify diagnostics do not corrupt
the protocol and synthetic secrets do not appear in logs or tool errors.
Ensure cleanup of subprocesses and ports on every failure path.

Run the full offline solution suite. Update STATUS.md with exact commands,
outcomes, and any platform-specific limitations.
```

## Phase 7 — Delivery (GPT-6.1 Sol, medium)

```text
Implement phase 7: packaging, documentation, and automated verification.

Expand README.md with verified prerequisites, configuration and secret
setup, restore/build/test/publish commands, and MCP client configuration
using absolute published executable or DLL paths. Preserve the development
documentation index and remove the planning-only claim when appropriate.

Document all ten tools with realistic arguments, operators, pagination,
accounting limits, error behavior, and troubleshooting. Use placeholders
only and distinguish offline validation from live compatibility.

Publish the host locally in Release to an ignored artifacts directory.
Verify the published artifact can initialize via an MCP client with fake
upstream infrastructure, and configuration errors go to stderr.

Add CI for restore, Release build, tests, and publish using the same pinned
SDK/runner as local development. Use the established CI provider, or
document the provider chosen if none exists. Add dependency lock files and
locked restore. Do not make Git commits, deploy, or publish packages.

Run the workflow locally where possible. Update STATUS.md and documentation
with actual evidence and remaining compatibility questions.
```

## Phase 8 — Independent review (GPT-6 Astra, high)

Use a fresh session for this phase.

```text
Review this .NET 10 read-only MCP server independently. Read AGENTS.md,
the actual solution/code/tests/docs, and the OpenAPI specification. Treat
prior completion claims as claims to verify.

Check solution completeness, dependencies, exact tool/schema agreement,
method/route/origin enforcement, redirects, token races, cancellation,
retry multiplication, credential isolation, monetary precision, dates,
pagination, invoice/mutation identity, balance semantics, malformed
responses, sanitized errors, stdio behavior, and subprocess cleanup.

Prioritize concrete correctness and security defects over style. Implement
focused fixes with regression tests for confirmed defects. Preserve scope.

Run restore, Release build, the complete offline tests, and local publish.
Verify published-artifact protocol behavior. Report remaining findings by
severity with file references and impacts. Distinguish proven behavior
from assumptions requiring authorized live verification.

Update STATUS.md with evidence and the next action. Do not mark release
criteria satisfied when a check is blocked or has not been run.
```
