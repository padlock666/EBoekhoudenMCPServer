# Implementation plan

## Objective and scope

Build a proper C#/.NET 10 solution implementing a read-only e-Boekhouden MCP server. The starting repository contains only the OpenAPI specification and these planning documents.

The initial release consists of ten core tools over stdio, one externally configured API credential, a typed HTTP client, offline tests, protocol integration tests, documentation, and a locally publishable executable. See [ARCHITECTURE.md](ARCHITECTURE.md) for the exact structure and tool mapping.

No live service verification is implied by offline tests. Remote HTTP hosting, additional endpoints, derived accounting reports, and package publication are outside this initial release.

## Model and reasoning effort

These are task-specific recommendations, not benchmark results. Select the model in the coding environment; merely including its name in a prompt does not switch models. Availability can vary.

| Phase | Suggested model | Effort |
| --- | --- | --- |
| 1. Scaffold | GPT-6.1 Sol | Medium |
| 2. Contracts and filters | GPT-6.1 Sol | High |
| 3. Authentication and HTTP | GPT-6.1 Sol | High |
| 4. Read-only API client | GPT-6.1 Sol | High |
| 5. MCP tools and host | GPT-6.1 Sol | Medium |
| 6. Protocol integration tests | GPT-6.1 Sol | High |
| 7. Delivery and documentation | GPT-6.1 Sol | Medium |
| 8. Independent review | GPT-6 Astra | High |

A simple default is GPT-6.1 Sol with high effort, lowering to medium for routine work. Reserve extra-high effort for a concrete unresolved problem. Recheck availability and recommendations when starting development.

Guidance consulted on 2026-10-29: [OpenAI model selection](https://developers.openai.com/api/docs/guides/model-selection) and [reasoning effort](https://developers.openai.com/api/docs/guides/reasoning).

## Workflow

Complete phases sequentially. Use the shared instructions and matching prompt in [PROMPTS.md](PROMPTS.md). Maintain a buildable solution after each implementation phase. Update [STATUS.md](STATUS.md) with actual results rather than copying acceptance criteria as claims.

### Phase 1: Scaffold the solution

Create the solution, six projects, project references, SDK pin, central package management, nullable settings, analyzers, formatting configuration, and ignore rules. Preserve these planning documents and refine them as needed.

Acceptance:

- A conventional `EBoekhoudenMcp.sln` references all six projects.
- All projects target `net10.0` and use appropriate SDK-style project files.
- SDK and stable compatible package versions are pinned.
- Restore and Release build succeed.
- The test runner is configured; no meaningless placeholder tests are added merely to obtain a green result.

### Phase 2: Contracts, filters, and validation

Create upstream DTOs and separate MCP request/result contracts. Specify the exact tool schemas in `docs/tool-contracts.md`. Implement typed filters and their query encoder.

Acceptance:

- Ten tools are documented with inputs, outputs, mappings, and limitations.
- Filters permit only supported operators and serialize without double encoding.
- Pagination distinguishes returned count from total matches.
- Balance versus period-change semantics are explicit.
- Date, monetary, identifier, and casing decisions have meaningful tests.

### Phase 3: Authentication and HTTP infrastructure

Build separate session and read HTTP clients, token caching, synchronized refresh, request restrictions, bounded resilience, validated configuration, and safe logging.

Acceptance:

- Offline tests prove token reuse, refresh concurrency, expiry, cancellation, failure recovery, and bounded retry behavior.
- An older failed request cannot invalidate a newer token.
- Method, route, and origin restrictions are enforced before dispatch; redirects are disabled.
- Session creation is the only non-GET request allowed.
- No credential or response-body logging occurs.

### Phase 4: Typed read-only API client

Implement every upstream endpoint needed by the core tools, along with normalized upstream errors and synthetic fixtures.

Acceptance:

- Exact request and response behavior is tested for each required endpoint.
- Search/detail differences and invoice/mutation identity remain intact.
- Malformed responses, missing essential fields, empty results, optional fields, and cancellation are handled deliberately.
- API tests and Release build pass.

### Phase 5: MCP tools and stdio host

Register exactly ten approved tools. Wire configuration, dependency injection, transport, structured results, error mapping, cancellation, and shutdown.

Acceptance:

- Generated tool schemas match the documented contracts.
- Invalid inputs fail before upstream requests.
- Read-only annotations accurately describe the exposed operations.
- Tool failures are errors rather than success-shaped empty results.
- Logs go to stderr and the host builds and runs with fake infrastructure.

### Phase 6: Protocol integration tests

Run the actual host as a subprocess and communicate through the official MCP C# client SDK. Use a loopback fake upstream with synthetic credentials.

Acceptance:

- Initialization, tool discovery, invocation, structured results, and failures are tested over stdio.
- Concurrency, pagination, balance interpretation, cancellation, and shutdown are covered.
- Invalid input causes no upstream traffic.
- Tests clean up processes and ports even on failure.
- Captured protocol output is clean and captured logs contain no secrets.
- The full suite runs offline.

### Phase 7: Delivery, documentation, and CI

Add verified user documentation, local Release publication, dependency locks, and CI matching local commands. CI provider is an implementation decision: use the repository's provider if established, otherwise document the choice before creating its configuration.

Acceptance:

- A fresh checkout can restore, build, test, and publish using documented commands.
- CI uses the pinned SDK and compatible test runner, with locked restore.
- Published executable/DLL launch instructions and MCP client examples are accurate.
- Configuration examples contain placeholders only.
- Offline evidence is distinguished from unverified live compatibility.

### Phase 8: Independent review

Use a fresh AI session to inspect the actual repository and specification. Fix confirmed defects with focused regression tests.

Acceptance:

- Method/origin enforcement, token races, retry budgets, precision, schemas, pagination, and protocol behavior have been reviewed.
- All fixes pass the verification workflow.
- Remaining findings have severity, file references, and an explicit impact.
- Live API assumptions remain identified rather than declared proven.

## Release definition of done

- Valid solution and project files exist, with all projects targeting .NET 10.
- Restore, Release build, offline tests, and local publish succeed.
- The published stdio application exposes exactly the ten documented tools.
- Session creation is the only permitted non-GET operation.
- Secrets remain external and are not leaked into logs or tool responses.
- Integration tests use the real executable and MCP transport.
- Documentation records limitations and unverified assumptions.

## Later milestones

1. With explicit authorization and credentials, run a small read-only live smoke test to verify authentication and response shapes. Store only sanitized findings.
2. Add products, product groups, cost centers, units, members, templates, and administration discovery using the same patterns.
3. Add `EBoekhouden.Host.Http` using `ModelContextProtocol.AspNetCore` and Streamable HTTP, with incoming authentication and credential-scoped session isolation.
4. Consider derived reports only after defining accounting rules, data completeness, and aggregation behavior.
