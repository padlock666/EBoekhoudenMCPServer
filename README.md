# e-Boekhouden read-only MCP server

C#/.NET 10 implementation of a read-only Model Context Protocol server for the supplied [e-Boekhouden OpenAPI specification](e-boekhouden-api-v1.yaml).

**Current state: phase 6 executable and protocol integration tests.** Exactly ten read-only MCP tools are registered through the official C# SDK 2.3.0, backed by eleven typed GET operations. The Generic Host validates external configuration at startup and authenticates lazily. Diagnostics go to stderr; stdout is reserved for MCP protocol traffic.

## Build and test

Install the stable .NET SDK **10.0.113**, pinned exactly in `global.json`. All projects inherit `net10.0`, nullable reference types, implicit usings, and the SDK's built-in analyzers from `Directory.Build.props`. Package versions are managed in `Directory.Packages.props`.

From the repository root:

```sh
dotnet restore EBoekhoudenMcp.sln
dotnet build EBoekhoudenMcp.sln -c Release --no-restore
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore --list-tests --logger "console;verbosity=normal"
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore
```

Tests use **xunit.v3 3.2.2**, **xunit.runner.visualstudio 3.1.5**, and **Microsoft.NET.Test.Sdk 18.0.1**. `global.json` explicitly selects **VSTest** for `dotnet test`; do not use Microsoft.Testing.Platform-only CLI options. xUnit v3 test projects are executables with generated entry points. API and MCP tests reference their respective libraries; integration tests build the host and use the official MCP client SDK against its actual child process. Their test-only ASP.NET Core framework reference supplies the loopback Kestrel fake upstream.

API and MCP tests cover filters, encoding, contracts, validation, precision, dates, pagination, token races, cancellation, retries, concurrency, HTTP restrictions, exact endpoint requests/responses, nullable fields and safe error normalization with synthetic offline data. The integration project tests actual subprocess stdio with a loopback fake HTTP server, including schemas, results/errors, token refresh races, cancellation and cleanup. Default tests must never call the live API. The [tool contracts](docs/tool-contracts.md) document the ten callable tools, and [reviewed schemas](docs/tool-schemas.json) are checked against actual SDK `tools/list` output. Tool tests use the SDK client with fake upstream handlers; the subprocess suite also checks the real executable. See [integration tests](docs/integration-tests.md) for isolation, test configuration and platform requirements.

Restore needs access to NuGet (or a populated package cache). Restricted environments may need a writable `DOTNET_CLI_HOME` and `NUGET_PACKAGES`, plus `-m:1 -nr:false` for MSBuild. VSTest needs local socket access even for offline discovery. See [STATUS.md](docs/development/STATUS.md) for exact verified commands and environment limitations.

## Run with an MCP client

Build Release first, then have the client launch the DLL directly. This avoids build output on the protocol stream. Replace every placeholder in this example locally; it contains no usable credential:

```json
{
  "mcpServers": {
    "eboekhouden": {
      "command": "dotnet",
      "args": ["/ABSOLUTE/PATH/TO/src/EBoekhouden.Host.Stdio/bin/Release/net10.0/EBoekhouden.Host.Stdio.dll"],
      "env": {
        "EBoekhouden__AccessToken": "<API_TOKEN>",
        "EBoekhouden__Source": "McpLocal"
      }
    }
  }
}
```

Use environment variables supplied by your client/secret manager or local .NET user secrets. For user secrets, configure the host project as described in [HTTP infrastructure](docs/http-infrastructure.md) and set `DOTNET_ENVIRONMENT=Development` in the launching environment. Keep real credentials out of tracked client configurations and command-line arguments. The source must match the documented 1–10 character pattern.

Startup and tool discovery do not request a session; the first valid accounting call does. Missing/invalid configuration exits with code 1 and a static stderr diagnostic. Closing stdin or sending the normal host termination signal shuts down the single stdio session. Host shutdown is bounded to ten seconds. SDK payload logging is disabled even when external configuration requests Trace, and HTTP factory loggers are removed. See [MCP runtime](docs/mcp-runtime.md) for validation, schemas, result/error formatting and offline evidence.

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
| [Integration tests](docs/integration-tests.md) | Real executable, loopback fake API, cancellation, capture and cleanup |
| [MCP runtime](docs/mcp-runtime.md) | Tool registration, schemas, errors, host lifecycle and logging |
| [Typed API client](docs/api-client.md) | Eleven GET methods, response validation/nullability, errors, and synthetic fixture evidence |

The stdio executable exposes ten accounting tools over stdio using one externally configured API credential and no database. Broader endpoint coverage and remote HTTP hosting are later milestones. Publishing and CI are phase 7 work; no publish or deployment is claimed.
