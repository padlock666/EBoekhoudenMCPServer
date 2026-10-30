# Executable and protocol integration tests

The phase 6 suite launches the **actual compiled stdio host** as a child process and communicates through the official MCP C# client SDK **2.3.0**. A Kestrel server bound to `127.0.0.1:0` supplies synthetic HTTP responses. The OS assigns the port while binding; there is no reserve/release race. No test calls the live accounting API.

## Running

Use the pinned .NET SDK **10.0.113** and the existing VSTest/xUnit v3 runner. The integration project's test-only `Microsoft.AspNetCore.App` framework reference requires the .NET 10 ASP.NET Core shared runtime, supplied by the SDK installation used here. No new NuGet package or production framework reference is introduced.

```sh
dotnet restore EBoekhoudenMcp.sln
dotnet build EBoekhoudenMcp.sln -c Release --no-restore
dotnet test tests/EBoekhouden.IntegrationTests -c Release --no-build --no-restore
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore
```

Build first. The integration project copies the host's intact build output, including dependencies and runtime configuration, into its ignored `bin/.../StdioHost/` directory. Launching the host DLL from the test project's own output root would be incorrect: its ASP.NET framework reference can omit Extensions DLLs needed by the independently launched host. Tests run `dotnet <absolute host DLL>` directly, without restore/build output on the protocol stream. This is a build artifact test; publishing remains phase 7 work.

## Explicit test configuration

The executable accepts a test origin only when **both** of these external settings are present:

```text
DOTNET_ENVIRONMENT=IntegrationTest
EBoekhoudenTest__UpstreamOrigin=http://127.0.0.1:<OS_ASSIGNED_PORT>/
```

The API registration validates a numeric IPv4/IPv6 loopback HTTP(S) origin, root path, and no query, fragment or user information. DNS hostnames such as localhost are rejected. IntegrationTest without an origin fails at startup; supplying the override in Production/Development also fails. Normal production always uses the fixed upstream origin. No tool accepts this setting, and no route/method/header/redirect restriction is bypassed.

Each child receives a cleared environment with only necessary runtime/system paths and explicit synthetic options. It runs in a unique disposable empty directory, so user credentials, user secrets, appsettings and proxy configuration are not inherited. Synthetic session POST bodies and Bearer GET headers are checked by the fake server. Fixtures are linked from the clearly labeled invented API fixtures; no captured accounting data is used.

## Coverage

- Initialization, discovery, exactly ten reviewed wire schemas and annotations; initialize revisions 2025-06-18/2025-11-25 and the SDK's 2026-07-28 discovery revision.
- All ten tools and eleven GET routes, exact encoded requests, structured/text equality, decimal precision, optional quantities, unknown codes, invoice/mutation identity and lazy authentication.
- Invalid casing, types, operators, fields, limits, date/range/balance conditions and raw duplicate arguments cause **zero** session/GET requests.
- Local startup validation and test-origin restrictions; rejected/malformed sessions; bounded unauthorized replay, permission denial, upstream validation/malformed responses and sanitized tool errors. Session POST failures are not retried. Redirects to a second fake server are not followed.
- Concurrent cold calls share a single session; expired sessions share a refresh; a delayed old-token 401 cannot invalidate a newer session. Races use async gates. The expiry subprocess test has one 2.1-second wall-clock delay; detailed expiry boundaries remain covered with FakeTimeProvider in API tests.
- Pagination returns candidate offsets without invented totals or automatic traversal. All/single balance calls preserve mode/dates and actual upstream values without aggregation.
- Explicit cancellation notifications abort the actual TCP HTTP request. Local client cancellation is tested separately with an explicit subsequent notification; SDK 2.3.0 local cancellation alone did not reliably cause remote abort here. Subsequent tool calls remain usable.
- Closing stdin with idle or pending work exits normally. The pending-work regression verifies HTTP abort and exit within five seconds. Disposing after a protocol failure reaps the child and makes the fake server's port reusable.

## Output capture and cleanup

The harness owns Process.Start and connects SDK `StreamClientTransport` to redirected stdin and a stdout pipe. Separate pumps drain **all raw stdout and stderr** to separate bounded in-memory buffers, including output after client disconnection. It validates every stdout line as a JSON-RPC message independently of the SDK parser, which can ignore malformed lines. Trace is deliberately requested externally; stderr must contain diagnostics and must not contain synthetic credential, session, private-query or financial markers. Errors are checked for the same markers; successful protocol results are allowed to carry their requested financial data. Captures are not written to logs or committed artifacts.

Every test uses async disposal or try/finally. SDK client disposal, stdin closure and process waiting are bounded. A still-running child is killed with its process tree and reaped before handles/scratch directories are removed, including initialization/assertion failures. Kestrel cancellation releases gated handlers; stop/disposal releases listening sockets. The cleanup regression verifies a stopped process and immediate port reuse.

Execution was verified on Linux x64 with .NET runtime **10.0.13**. Windows/macOS, IPv6 listeners and OS-signal shutdown are not claimed tested; the suite uses IPv4 loopback and stdin EOF. Restricted runners need local bind/connect permission in addition to VSTest's local sockets. NuGet restore/audit metadata may require network or a populated cache; test execution itself only uses loopback. Exact commands/outcomes and resolved environment issues are in [STATUS.md](development/STATUS.md).
