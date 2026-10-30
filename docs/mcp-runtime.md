# MCP runtime

The phase 5 executable uses Generic Host and the official `ModelContextProtocol` **2.3.0** SDK over stdio. `HostComposition` registers the existing API services, startup options validation, `AccountingTools`, its explicit ten-entry catalog and the SDK single-session hosted service. There is one credential and no database. No tool exposes session creation or arbitrary HTTP access. Startup/discovery are network-free; authentication remains lazy.

## Contracts and SDK binding

The [tool contracts](tool-contracts.md) define arguments, endpoint mappings, output fields and accounting limits. [tool-schemas.json](tool-schemas.json) is the reviewed export of the actual SDK-created protocol tools, not a manually invented schema. The SDK-client `tools/list` test compares every published tool with this snapshot and checks exactly ten names and read-only annotations. Updating a contract requires reviewing and regenerating that snapshot.

The catalog creates SDK tools from ten typed delegates with `UseStructuredContent=true`. The SDK initially generates a nested `request` parameter; `ToolSchemas` flattens it and refines closed object shapes, field-specific operators, defaults/bounds, required range endpoints, conditional balance dates, string enums, ISO dates and decimal/identifier string patterns. Optional filters and dates explicitly support null where documented. Output properties are present even when null. No C# enum/union inference is assumed. Schema conditions cannot enforce ordered date/integer endpoints, so runtime validation additionally checks ordering.

`StrictContractTool` reads the original JSON arguments before dictionary binding can discard duplicate fields, deserializes with the strict MCP options, and passes the validated flat object to the SDK's nested typed handler. `RequestMapper` checks all supported filters, cross-field rules and limits before any API method is invoked. Unknown fields, wrong casing and unsupported operators fail without even requesting a session. Tools only validate, call the typed read client and invoke explicit result mappers.

Successful calls return both `structuredContent` and one text block containing the same JSON object through the SDK's typed-result formatting. Money, quantities and percentages remain invariant decimal strings; mutation IDs remain integer strings. API DTOs never become MCP contracts directly. Free text from the API is untrusted data, never instructions.

## Errors and cancellation

Execution failures have `isError=true`, no structured success result and one JSON text block:

```json
{"kind":"upstream_failure","message":"<fixed safe message>","status":403,"code":"EXAMPLE_001","traceId":"TRACE-123","transportFailure":null}
```

`kind` is `invalid_arguments`, `upstream_failure`, `invalid_response` or `internal_error`. Diagnostic fields may be null. Upstream status/code/trace come from the API layer's sanitizer; raw bodies, query values and arbitrary exception messages never escape. API authentication/transport/response failures remain errors, including 404 and malformed successful responses. An actual empty page is a normal success.

Cancellation tokens flow from the SDK handler through the tool and typed HTTP stack. Requested cancellation is rethrown for SDK protocol handling; it is not an empty page or generic failure. Tests verify an explicit `notifications/cancelled` message with a known request ID, not merely a locally canceled client task. See the [compatibility note](development/API_COMPATIBILITY.md#phase-5-mcp-compatibility-findings) about client-side forwarding observed during testing.

## Host lifecycle and diagnostics

Use the [README client example](../README.md#run-with-an-mcp-client) and [external options setup](http-infrastructure.md). Normal environment and development user-secret providers supply configuration. Validation occurs at startup without HTTP; malformed binding or missing options produce exit 1 with a static stderr message. Tokens are obtained only for a valid accounting call. `StdioDisconnectService` observes the SDK transport reader completion and stops Generic Host on EOF or transport failure, canceling pending HTTP operations. This avoids SDK 2.3.0 waiting for in-flight handlers before its single-session service stops the host. Generic Host also handles termination signals and has a ten-second shutdown timeout; the pending-call EOF integration test requires normal exit within five seconds.

All console logging is forced to stderr even if configuration requests another threshold. The host blocks `ModelContextProtocol` logs because SDK Trace can include financial arguments/results. It also blocks Generic Host internal startup exception logging because binding errors can include configuration values; the entry point emits a static diagnostic instead. HTTP factory logging remains disabled. The host neither configures telemetry exporters nor writes startup banners to stdout.

## Verification and version references

Offline tests exercise all ten tools and eleven underlying routes through the official SDK client, with real API/authentication infrastructure and synthetic fake primary handlers. They verify exact requests, typed mappings, structured/text equality, safe failures, validation before network, raw duplicate arguments, cancellation notifications, actual schemas, lazy Generic Host startup and shutdown, and protected logger settings. The [phase 6 subprocess suite](integration-tests.md) additionally uses the actual executable and real loopback HTTP with synthetic fixtures, including expiry/refresh races, cancellation and cleanup.

The implementation was checked against tagged official SDK documentation/source:

- [Getting started and stdio hosting](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.3.0/docs/concepts/getting-started.md)
- [Tools](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.3.0/docs/concepts/tools/tools.md)
- [Tool creation options and structured content](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.3.0/src/ModelContextProtocol.Core/Server/McpServerToolCreateOptions.cs)
- [DelegatingMcpServerTool](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.3.0/src/ModelContextProtocol.Core/Server/DelegatingMcpServerTool.cs)
- [Explicit primitive collection in DI](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.3.0/src/ModelContextProtocol/McpServerOptionsSetup.cs)

See [STATUS.md](development/STATUS.md) for executed commands, counts and limitations. No live API compatibility, deployment or publication is implied.
