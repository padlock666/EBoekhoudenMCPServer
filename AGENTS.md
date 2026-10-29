# Instructions for AI-assisted development

## Start here

This repository starts with `e-boekhouden-api-v1.yaml` and planning documents. The intended product is a read-only e-Boekhouden MCP server in C#/.NET 10.

Before implementation, read:

1. `docs/development/STATUS.md` for actual progress and handoff notes.
2. `docs/development/IMPLEMENTATION_PLAN.md` for scope, phases, and acceptance criteria.
3. `docs/development/ARCHITECTURE.md` for design decisions and tool contracts.
4. `docs/development/API_COMPATIBILITY.md` for known specification ambiguities.
5. The relevant phase prompt in `docs/development/PROMPTS.md`.
6. The relevant sections of `e-boekhouden-api-v1.yaml`.

The user's current instructions take precedence. Documentation describes intended work; it is not evidence that the work exists. Implement the requested phase and necessary dependencies, not every future phase automatically.

## Implementation rules

- Create real SDK-style project files and `EBoekhoudenMcp.sln`; target `net10.0`.
- Use stable compatible packages, pin versions centrally, and verify the official MCP SDK documentation for those versions.
- Preserve the supplied OpenAPI specification. Do not invent upstream routes, filters, sorting, totals, administration switching, or accounting rules.
- Keep API transport and DTOs separate from MCP tool contracts. Use explicit mappings.
- Business operations are read-only. `POST /v1/session` is an internal authentication exception, never an exposed tool. Do not expose an arbitrary HTTP request tool.
- Use `decimal` for financial arithmetic, `DateOnly` for accounting dates, and cancellation tokens throughout asynchronous operations.
- Keep stdout exclusively for MCP protocol traffic. Send diagnostics to stderr.
- Keep credentials outside tracked files. Never log tokens, request/response bodies, or sensitive query values.
- Default tests must be offline and use synthetic credentials and data.
- Do not call the live API, deploy, or publish packages unless the user authorizes that work.
- Do not introduce a database, mediator framework, agent framework, or speculative abstraction without a demonstrated need.
- Preserve existing user changes. Do not initialize Git or make commits unless requested.

## Validation and handoff

- Run the relevant build and meaningful tests; fix failures before marking a phase complete.
- Report execution blockers honestly. Never describe unrun checks as passing.
- Update `docs/development/STATUS.md` after a phase or substantial partial implementation. Record exact commands, outcomes, blockers, and the next concrete action.
- Record newly discovered API ambiguities in `docs/development/API_COMPATIBILITY.md`.
- Update architectural and tool documentation when implemented behavior changes.
- Do not place secrets or actual financial records into handoff notes or fixtures.
