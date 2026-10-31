# Executable and protocol integration tests

The phase 6 suite launches the **actual compiled stdio host** as a child process and communicates through the official MCP C# client SDK **2.3.0**. A Kestrel server bound to `127.0.0.1:0` supplies synthetic HTTP responses. The OS assigns the port while binding; there is no reserve/release race. No test calls the live accounting API.

## Running

Use the pinned .NET SDK **10.0.114** and the existing VSTest/xUnit v3 runner. The integration project's test-only `Microsoft.AspNetCore.App` framework reference requires the .NET 10 ASP.NET Core shared runtime, supplied by the SDK installation used here. No new NuGet package or production framework reference is introduced.

```sh
dotnet restore EBoekhoudenMcp.sln --locked-mode
dotnet build EBoekhoudenMcp.sln -c Release --no-restore
dotnet test tests/EBoekhouden.IntegrationTests -c Release --no-build --no-restore
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore
```

Build first. The integration project copies the host's intact build output, including dependencies and runtime configuration, into its ignored `bin/.../StdioHost/` directory. Launching the host DLL from the test project's own output root would be incorrect: its ASP.NET framework reference can omit Extensions DLLs needed by the independently launched host. Tests run `dotnet <absolute host DLL>` directly, without restore/build output on the protocol stream. By default this tests the build artifact. Phase 7 also runs the same suite against the published DLL: set `EBOEKHOUDEN_TEST_HOST_DLL` to an absolute existing DLL path with its `.deps.json` and `.runtimeconfig.json` beside it. An invalid explicit path fails without falling back to build output. `scripts/verify.sh` clears an inherited override for the build tests, publishes locally, then sets this variable for the artifact tests; see [delivery](delivery.md).

## Explicit test configuration

The executable accepts a test origin only when **both** of these external settings are present:

```text
DOTNET_ENVIRONMENT=IntegrationTest
EBoekhoudenTest__UpstreamOrigin=http://127.0.0.1:<OS_ASSIGNED_PORT>/
```

The API registration validates a numeric IPv4/IPv6 loopback HTTP(S) origin, root path, and no query, fragment or user information. DNS hostnames such as localhost are rejected. IntegrationTest without an origin fails at startup; supplying the override in Production/Development also fails. Normal production always uses the fixed upstream origin. No tool accepts this setting, and no route/method/header/redirect restriction is bypassed.

Each child receives a cleared environment, explicit synthetic settings, a private D-Bus address and temporary HOME/XDG directories. The harness owns the synthetic Secret Service and its non-activating bus for the lifetime of the host. Personal desktop services and configuration are never inherited. Synthetic session POST bodies and Bearer GET headers are checked by the fake server. Fixtures contain invented accounting data.

## Coverage

- Initialization, discovery, exactly ten reviewed wire schemas and annotations; initialize revisions 2025-06-18/2025-11-25 and the SDK's 2026-07-28 discovery revision.
- All ten tools and eleven GET routes, exact encoded requests, structured/text equality, decimal precision, rejection of inexact/underflowing balances, optional quantities, unknown codes, string/numeric upstream mutation types, invoice/mutation identity and lazy authentication. Oversized permission-denial bodies preserve status with no extra requests.
- Invalid casing, types, operators, fields, limits, date/range/balance conditions and raw duplicate arguments cause **zero** session/GET requests.
- Invalid keyring configuration with empty stdout/static stderr; local startup validation and test-origin restrictions; rejected/malformed sessions; bounded unauthorized replay, permission denial, upstream validation/malformed responses and sanitized tool errors. Session POST failures are not retried. Redirects to a second fake server are not followed.
- Nonsecret config.json loading by absolute path from another working directory, Unicode/spaces in paths, environment/CLI precedence, duplicate/malformed/unknown option rejection. Keyring default/custom attributes, token rotation, cached-session reuse and session/read reflection safety run against the build and published host.
- Concurrent cold calls share a single session; expired sessions share a refresh; a delayed old-token 401 cannot invalidate a newer session. Races use async gates. The expiry subprocess test has one 2.1-second wall-clock delay; detailed expiry boundaries remain covered with FakeTimeProvider in API tests.
- Pagination returns candidate offsets without invented totals or automatic traversal. All/single balance calls preserve mode/dates and actual upstream values without aggregation.
- Explicit cancellation notifications abort the actual TCP HTTP request. Local client cancellation is tested separately with an explicit subsequent notification; SDK 2.3.0 local cancellation alone did not reliably cause remote abort here. Subsequent tool calls remain usable.
- Closing stdin with idle or pending work exits normally. The pending-work regression verifies HTTP abort and exit within five seconds. Disposing after a protocol failure reaps the child and makes the fake server's port reusable.

## Output capture and cleanup

The harness owns Process.Start and connects SDK `StreamClientTransport` to redirected stdin and a stdout pipe. Separate pumps drain **all raw stdout and stderr** to separate bounded in-memory buffers, including output after client disconnection. It validates every stdout line as a JSON-RPC message independently of the SDK parser, which can ignore malformed lines. Trace is deliberately requested externally; stderr must contain diagnostics and must not contain synthetic credential, session, private-query or financial markers. Errors are checked for the same markers; successful protocol results are allowed to carry their requested financial data. Captures are not written to logs or committed artifacts.

Every test uses async disposal or try/finally. SDK client disposal, stdin closure and process waiting are bounded. A still-running child is killed with its process tree and reaped before handles/scratch directories are removed, including initialization/assertion failures. Kestrel cancellation releases gated handlers; stop/disposal releases listening sockets. The cleanup regression verifies a stopped process and immediate port reuse.

Execution was verified on Linux x64 with .NET runtime **10.0.14**. Windows/macOS, IPv6 listeners and OS-signal shutdown are not claimed tested; the suite uses IPv4 loopback and stdin EOF. Restricted runners need local bind/connect permission in addition to VSTest's local sockets. NuGet restore/audit metadata may require network or a populated cache; test execution itself only uses loopback. Exact commands/outcomes and resolved environment issues are in [STATUS.md](development/STATUS.md).

## Native credential tests

The executable suite uses Linux and the actual libsecret adapter with a synthetic Secret Service. It requires libsecret/GIO runtime libraries, dbus-daemon and Python GI/GIO. Each case owns its bus, HOME/XDG directories and subprocesses. Tests cover selection, lookup failures, byte/UTF-8 validation, cancellation, deadlines, rotation and refresh recovery. A separately gated case starts an isolated GNOME daemon and provisions a synthetic key with `secret-tool store`; default runs skip it. See [native prerequisites and test commands](linux-secret-service.md#verification). STATUS distinguishes executed fake-service/GNOME tests from live compatibility.
