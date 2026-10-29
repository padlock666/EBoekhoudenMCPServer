# Development status and handoff

Last updated: 2026-10-29.

## Current state

- Phase 1 scaffold is implemented: conventional solution, six SDK-style projects, shared build settings, central package versions, SDK pin, formatting and ignore rules.
- `e-boekhouden-api-v1.yaml` is the upstream contract source.
- All projects target `net10.0`. Release build succeeds with zero warnings/errors. The executable only reports the unfinished host on stderr and exits 1; no MCP transport or API operations exist yet.
- Phase 2 implements all core read DTOs, separate MCP request/result records, strict contract serialization, typed filters, endpoint query encoding, validation, and explicit mappings. See [tool-contracts.md](../tool-contracts.md).
- API and MCP projects now contain 88 passing offline synthetic tests. IntegrationTests remains intentionally empty until phase 6.
- The workspace was not a Git repository at initial inspection. Git initialization is not required to begin implementation and has not been performed.
- No live API behavior has been verified.

## Phase tracking

| Phase | Status | Evidence |
| --- | --- | --- |
| 1. Scaffold | Complete | Restore succeeded; six-project Release build: 0 warnings/errors; xUnit adapter discovery verified in all three empty test assemblies |
| 2. Contracts and filters | Complete | Release build: 0 warnings/errors; 48 API and 40 MCP tests pass; exact ten-tool contracts documented |
| 3. Authentication and HTTP | Not started | Design recorded only |
| 4. API client | Not started | Design recorded only |
| 5. MCP tools and host | Not started | Design recorded only |
| 6. Integration tests | Not started | Test requirements recorded only |
| 7. Delivery and CI | Not started | Requirements recorded only |
| 8. Independent review | Not started | Review prompt available |

## Next action

Implement phase 3 from [PROMPTS.md](PROMPTS.md): separate session/read HTTP infrastructure, validated credential/origin settings, synchronized token lifecycle with stale-token protection, read-only dispatch enforcement, bounded resilience, safe logging and offline cancellation/concurrency tests. Select/pin HTTP, resilience and options packages when introduced. Do not expose tools or make live API requests.

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
