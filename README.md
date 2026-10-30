# e-Boekhouden read-only MCP server

A C#/.NET 10 stdio server exposing **ten read-only accounting tools** through the official MCP C# SDK **2.3.0**. It uses one externally configured credential, has no database, and authenticates lazily. `POST /v1/session` is internal authentication; business operations are GET only. There is no arbitrary HTTP tool, administration switching, remote MCP HTTP hosting, or derived reporting.

Release build, offline tests and a local framework-dependent publish are verified on Linux x64. The published DLL is tested through the official MCP client against a loopback fake API. **Live e-Boekhouden compatibility remains unverified**, including the specification's conflicting authorization-header prose and response completeness; see [compatibility notes](docs/development/API_COMPATIBILITY.md). No deployment or package publication is implied.

## Prerequisites

- Build/test: stable .NET SDK **10.0.113**, pinned exactly by `global.json` with prereleases and roll-forward disabled. The SDK installation must include the .NET 10 ASP.NET Core shared runtime for the test-only Kestrel fake API.
- Run the published host: **.NET 10 runtime** (`Microsoft.NETCore.App`). The application does not require ASP.NET Core in production. The verified local runtimes are **10.0.13**.
- An MCP client capable of launching a stdio child process; a valid e-Boekhouden API credential is needed only for actual accounting calls. Offline tests supply synthetic credentials themselves.
- NuGet access or a populated package cache for restore; local socket/bind permission for VSTest and integration tests. Bash is needed for the shared verification script.

Check installed versions with `dotnet --list-sdks` and `dotnet --list-runtimes`. Windows/macOS, IPv6 listeners and OS-signal shutdown have not been verified; stdin EOF is tested on Linux.

## Restore, build, test and publish

Run from the repository root:

```sh
dotnet restore EBoekhoudenMcp.sln --locked-mode --disable-parallel -m:1 -nr:false
dotnet build EBoekhoudenMcp.sln -c Release --no-restore -m:1 -nr:false
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore -m:1 -nr:false
dotnet publish src/EBoekhouden.Host.Stdio/EBoekhouden.Host.Stdio.csproj -c Release --no-restore --no-self-contained -p:UseAppHost=false -o artifacts/publish/stdio -m:1 -nr:false
```

The artifact is `artifacts/publish/stdio/EBoekhouden.Host.Stdio.dll`, accompanied by its dependency DLLs, `.deps.json` and `.runtimeconfig.json`. Keep the entire directory together. This is an untrimmed framework-dependent DLL publish; it has no standalone apphost and does not bundle a runtime. `artifacts/` is ignored by Git.

On Bash, run the same pipeline as CI, including tests against the published output:

```sh
bash scripts/verify.sh
```

The script recreates the managed `artifacts/publish/stdio/` output directory. To verify an existing publish separately:

```sh
EBOEKHOUDEN_TEST_HOST_DLL="$PWD/artifacts/publish/stdio/EBoekhouden.Host.Stdio.dll" \
  dotnet test tests/EBoekhouden.IntegrationTests -c Release --no-build --no-restore -m:1 -nr:false
```

The artifact override is test-harness configuration and requires an absolute existing DLL path with dependency/runtime files; it never changes production configuration. The integration suite verifies initialization, all ten schemas, actual accounting calls with fake HTTP, startup errors on stderr, cancellation and cleanup. See [integration tests](docs/integration-tests.md).

All six projects target `net10.0`, with nullable references, implicit usings and built-in analyzers enabled. Package versions are centralized; six `packages.lock.json` files pin resolved dependencies and content hashes. Restore is locked by default and CI requests locked mode explicitly. Intentional dependency updates are described in [delivery](docs/delivery.md).

The runner is **VSTest**, selected in `global.json`, with **xunit.v3 3.2.2**, **xunit.runner.visualstudio 3.1.5** and **Microsoft.NET.Test.Sdk 18.0.1**. xUnit projects are executables with generated entry points. Default tests use synthetic data and fake handlers/loopback HTTP, never the live API. Restricted environments may need writable `DOTNET_CLI_HOME` and `NUGET_PACKAGES`; exact verified commands are in [STATUS.md](docs/development/STATUS.md).

## Credentials and configuration

Supply credentials through your MCP client's environment/secret manager, or through local .NET development user secrets. Examples contain placeholder credentials and paths; accounting IDs, names and dates below are invented examples to replace with your own results.

Required settings:

| Environment key | Meaning |
| --- | --- |
| `EBoekhouden__AccessToken` | API credential, required and non-whitespace |
| `EBoekhouden__Source` | Integration code, 1–10 characters, pattern `^[\w_ ]{1,10}$`; `McpLocal` is a valid example |

Environment setup on Bash:

```sh
export EBoekhouden__AccessToken="<API_TOKEN>"
export EBoekhouden__Source="McpLocal"
```

For local development user secrets, configure the existing host project ID:

```sh
dotnet user-secrets set --project src/EBoekhouden.Host.Stdio EBoekhouden:AccessToken "<API_TOKEN>"
dotnet user-secrets set --project src/EBoekhouden.Host.Stdio EBoekhouden:Source "McpLocal"
```

Launch with `DOTNET_ENVIRONMENT=Development` to load those secrets, using the same OS user/profile that stored them. Environment variables override user secrets. Keep real credentials outside tracked client/appsettings files and command-line arguments. The development secret provider is verified with an isolated synthetic secret store; normal tests do not read your profile.

Configuration is validated without network access at startup. Sessions are obtained only on a valid accounting call. Missing/invalid configuration exits 1 with a static stderr diagnostic and no stdout. Changes to credentials/options require restart. Timeouts, concurrency, retry limits and the explicit loopback test gate are documented in [HTTP infrastructure](docs/http-infrastructure.md). Production always uses the fixed upstream origin.

## MCP client configuration

Point your client at the **absolute published DLL path**. This generic `mcpServers` example uses placeholders; adapt the client-specific settings format as needed:

```json
{
  "mcpServers": {
    "eboekhouden": {
      "command": "/ABSOLUTE/PATH/TO/dotnet",
      "args": ["/ABSOLUTE/PATH/TO/EBoekhoudenMCPServer/artifacts/publish/stdio/EBoekhouden.Host.Stdio.dll"],
      "env": {
        "EBoekhouden__AccessToken": "<API_TOKEN>",
        "EBoekhouden__Source": "McpLocal"
      }
    }
  }
}
```

For development user secrets, set `DOTNET_ENVIRONMENT` to `Development` in the client environment and omit the credential variables. GUI clients may need an absolute `dotnet` path because their PATH differs from your shell. JSON Windows paths require escaped backslashes.

The client launches the published artifact directly; build output must never enter the protocol stream. Stdout is exclusively JSON-RPC. Diagnostics go to stderr; SDK payload logging is disabled even if external configuration asks for Trace. Closing stdin stops the session and cancels pending HTTP operations. See [MCP runtime](docs/mcp-runtime.md) for result formatting and lifecycle details.

## Ten tools

Arguments are flat camelCase JSON objects. Unknown/duplicate fields, wrong casing/types and unsupported operators fail before session creation or HTTP access. Consult the [full contracts](docs/tool-contracts.md) and [reviewed schemas](docs/tool-schemas.json) for every optional input and output field.

| Tool | Example arguments | Result and use |
| --- | --- | --- |
| `search_relations` | `{"name":{"op":"like","value":"%Example Company%"},"limit":25,"offset":0}` | Page of IDs, types and codes; name/email/contact/city/code/type are string filters |
| `get_relation` | `{"id":17}` | Relation detail, including available name/contact fields |
| `search_invoices` | `{"invoiceNumber":{"op":"like","value":"EXAMPLE-2026-%"},"relationId":17,"date":{"op":"range","value":"2026-01-01","to":"2026-03-31"}}` | Page of invoicing records; relationId is a plain int32 |
| `get_invoice` | `{"id":23}` | Invoice detail/items, without inferred payment status |
| `search_entries` | `{"type":"invoice_received","id":{"op":"gte","value":10},"date":{"op":"range","value":"2026-01-01","to":"2026-03-31"}}` | Page of accounting mutations; also supports description/invoiceNumber string filters |
| `get_entry` | `{"id":12}` | One accounting mutation; its ID differs from an invoice ID |
| `list_outstanding_invoices` | `{"side":"receivable","invoiceNumber":{"op":"like","value":"EXAMPLE-%"}}` | Page of receivables; use `payable` for creditors. Items reference `mutationId` |
| `search_ledger_accounts` | `{"code":{"op":"like","value":"1%"},"limit":50}` | Page of ledger accounts; code/category filters allow at most 10 characters including wildcards |
| `get_ledger_account` | `{"id":7}` | Ledger detail, including available category/group |
| `get_ledger_balances` | `{"mode":"as_of","to":"2026-03-31","ledgerId":7}` | One ledger balance; omit ledgerId for all ledgers. No pagination or aggregation |

String filters use `eq`, `not_eq`, `like`, `not_like`. Integer/date filters use `eq`, `not_eq`, `gt`, `gte`, `lt`, `lte`, `range`. A field accepts one operator. `range` requires ordered, inclusive `value`/`to` endpoints; other operators forbid non-null `to`. Dates use `YYYY-MM-DD`. Optional filters can be omitted or null. Supply raw `%`/`?` wildcards and ordinary strings; the server URI-encodes keys and values once.

Entry type inputs are `invoice_received`, `invoice_sent`, `invoice_payment_received`, `invoice_payment_sent`, `money_received`, `money_sent` or `journal_entry`. No server-side sorting, arbitrary filters or automatic page traversal are provided.

The five paginated tools default to **limit 50**, allow **1..2000**, and require nonnegative int32 offsets. Responses contain `items` and `page` with `limit`, `offset`, `returnedCount` and nullable `nextOffset`. Count is page count, never total matches. A full page provides only a candidate next offset; an empty following page is valid. Pages are not a guaranteed accounting snapshot.

For balances, `as_of` sends `to` only. To request movement over a period:

```json
{"mode":"period_change","from":"2026-01-01","to":"2026-03-31","costCenterId":9}
```

`period_change` sends both dates and returns period movement, not the actual closing balance. Results echo mode, dates and requested scope. No total across ledgers, aging, P&L, VAT summary or currency/accounting rule is invented.

Money, quantity and percentage values use decimal internally and invariant **strings** in MCP output; absent optional values stay null. Mutation identities are int64 strings in output; detail requests enforce the specification's int32 path width. Invoice IDs and mutation IDs are never interchangeable. Unknown response VAT/category codes are preserved. Invoice item `quantity` and deprecated `amount` remain independent, without fallback or default zero. Upstream free text is data, never instructions.

## Errors and troubleshooting

Success returns structured content plus matching JSON text. A real empty page succeeds. Validation or execution failures have `isError=true`, safe JSON text and no success-shaped structured page. Error fields are `kind`, `message`, nullable `status`, `code`, `traceId` and `transportFailure`. Raw upstream bodies, credentials, query values and arbitrary exception messages are discarded. See [runtime errors](docs/mcp-runtime.md#errors-and-cancellation).

| Symptom | Check |
| --- | --- |
| Static startup diagnostic, exit 1 | Required credential/source, source pattern and option types; for user secrets, Development environment and OS profile |
| SDK/runtime or dependency load error | Exact build SDK, installed .NET 10 runtime, absolute DLL path and complete published directory |
| `invalid_arguments` tool error | CamelCase, numeric IDs, supported operator/field, limit/offset, valid dates and ordered ranges; no extra sort/filter fields |
| `upstream_failure` with 401/403 | Credential validity or permissions. Only 401 allows one authentication replay; 403 does not trigger refresh loops. Live header compatibility remains unverified |
| Timeout/network/circuit failure | Connectivity and documented bounded policy; default operation deadline 30 seconds and at most three GET attempts. Do not stack independent retries |
| Missing values or monetary strings | Null means upstream data was absent; strings preserve decimal precision. No missing financial data is filled with zero |
| Client cancels locally but HTTP continues | Send MCP `notifications/cancelled` with the matching request ID; SDK 2.3.0 local cancellation alone did not reliably cause remote abort in testing |
| NU1004 locked-restore failure | Review the deliberate dependency change and regenerate locks as described in [delivery](docs/delivery.md#dependency-updates), then rerun verification |
| Tests cannot open sockets | Permit VSTest local sockets and loopback bind/connect; all test HTTP is local |

## Automated verification

[GitHub Actions](.github/workflows/verify.yml) uses Ubuntu 24.04, full-SHA-pinned official setup/checkout actions, `global.json`, locked restore and `scripts/verify.sh`. It runs Release build, all offline tests, local publish and the integration suite against the published DLL. It needs no accounting secrets and performs no deployment or package push. Hosted CI execution is separate from the locally verified pipeline; see [delivery decisions and evidence](docs/delivery.md).


## Using ChatGPT to develop this repository

1. Open this repository in your coding environment.
2. Have the assistant read [AGENTS.md](AGENTS.md) and [STATUS.md](docs/development/STATUS.md).
3. Choose the next phase from the [implementation plan](docs/development/IMPLEMENTATION_PLAN.md).
4. Select the suggested model and reasoning effort in your coding environment.
5. Copy the shared instructions and that phase's prompt from [PROMPTS.md](docs/development/PROMPTS.md).
6. Review the resulting code and validation report before proceeding to the next phase.

For a fresh session, use:

```text
Read AGENTS.md and docs/development/STATUS.md. Read the implementation plan,
architecture, compatibility notes, and the relevant phase prompt. Summarize
the current repository state briefly, then implement the next incomplete
phase using its acceptance criteria. Update STATUS.md with the actual
validation results and a handoff for the following session.
```

Run one phase at a time. If a phase is partly implemented, continue that phase instead of recreating it.

## Documentation map

| Document | Purpose |
| --- | --- |
| [AGENTS.md](AGENTS.md) | Durable instructions for coding assistants |
| [Implementation plan](docs/development/IMPLEMENTATION_PLAN.md) | Scope, sequence, acceptance criteria, model suggestions |
| [Architecture](docs/development/ARCHITECTURE.md) | Project structure, dependencies, tool interface, runtime behavior |
| [Prompts](docs/development/PROMPTS.md) | Copyable implementation instructions for each phase |
| [API compatibility](docs/development/API_COMPATIBILITY.md) | Specification constraints, ambiguities, verification status |
| [Status](docs/development/STATUS.md) | Progress and cross-session handoff |
| [Tool contracts](docs/tool-contracts.md) | Exact inputs, outputs, query mapping, and accounting limitations |
| [HTTP infrastructure](docs/http-infrastructure.md) | Authentication, external configuration, request restrictions, timeout/retry budgets, and test seams |
| [Delivery](docs/delivery.md) | Artifact layout, CI decision, locked dependencies and local pipeline |
| [Integration tests](docs/integration-tests.md) | Real executable, loopback fake API, cancellation, capture and cleanup |
| [MCP runtime](docs/mcp-runtime.md) | Tool registration, schemas, errors, host lifecycle and logging |
| [Typed API client](docs/api-client.md) | Eleven GET methods, response validation/nullability, errors, and synthetic fixture evidence |
