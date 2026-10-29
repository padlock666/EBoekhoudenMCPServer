# e-Boekhouden read-only MCP server

C#/.NET 10 implementation of a read-only Model Context Protocol server for the supplied [e-Boekhouden OpenAPI specification](e-boekhouden-api-v1.yaml).

**Current state: phase 4 typed read-only API client.** The solution includes all eleven required GET operations, normalized errors, contracts/filters, authentication, restricted HTTP infrastructure, and synthetic offline tests. MCP tools and executable transport wiring are not implemented yet. The executable reports this on stderr and exits with code 1 without writing stdout or making network requests.

## Build and test

Install the stable .NET SDK **10.0.113**, pinned exactly in `global.json`. All projects inherit `net10.0`, nullable reference types, implicit usings, and the SDK's built-in analyzers from `Directory.Build.props`. Package versions are managed in `Directory.Packages.props`.

From the repository root:

```sh
dotnet restore EBoekhoudenMcp.sln
dotnet build EBoekhoudenMcp.sln -c Release --no-restore
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore --list-tests --logger "console;verbosity=normal"
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore
```

Tests use **xunit.v3 3.2.2**, **xunit.runner.visualstudio 3.1.5**, and **Microsoft.NET.Test.Sdk 18.0.1**. `global.json` explicitly selects **VSTest** for `dotnet test`; do not use Microsoft.Testing.Platform-only CLI options. xUnit v3 test projects are executables with generated entry points. API and MCP tests reference their respective libraries; integration tests reference the host and official MCP SDK for later subprocess tests.

API and MCP tests cover filters, encoding, contracts, validation, precision, dates, pagination, token races, cancellation, retries, concurrency, HTTP restrictions, exact endpoint requests/responses, nullable fields and safe error normalization with synthetic offline data. The integration project intentionally has no tests until phase 6 and reports no tests available. Default tests must never call the live API. The [tool contracts](docs/tool-contracts.md) document the ten planned tools and the implemented request/result shapes; no tools are callable yet.

Restore needs access to NuGet (or a populated package cache). Restricted environments may need a writable `DOTNET_CLI_HOME` and `NUGET_PACKAGES`, plus `-m:1 -nr:false` for MSBuild. VSTest needs local socket access even for offline discovery. See [STATUS.md](docs/development/STATUS.md) for exact verified commands and environment limitations.

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
| [Typed API client](docs/api-client.md) | Eleven GET methods, response validation/nullability, errors, and synthetic fixture evidence |

The initial release will expose ten accounting tools over stdio using one externally configured API credential and no database. Broader endpoint coverage and remote HTTP hosting are later milestones. During implementation, extend this README with verified publish, configuration, and client setup instructions.
