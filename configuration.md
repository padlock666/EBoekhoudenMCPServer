# Configuring the e-Boekhouden MCP server

This guide covers local setup, credentials, Codex MCP registration and a local desktop plugin. See [usage.md](usage.md) for tool examples and accounting limits. Examples use placeholder paths and credentials; replace them locally.

The server runs as a child process over **stdio**. It serves one configured e-Boekhouden credential and administration scope. Its production upstream is fixed to `https://api.e-boekhouden.nl/`. It exposes ten read-only business tools; session creation is internal authentication. There is no listening MCP HTTP port or database.

## 1. Build and publish

Install the stable .NET SDK **10.0.113**, pinned by [global.json](global.json). Tests also require the .NET 10 ASP.NET Core shared runtime included in the verified SDK installation. Running the published server requires the .NET 10 `Microsoft.NETCore.App` runtime. Linux x64 with runtime **10.0.13** has been tested; Windows/macOS setup has not been verified.

From the repository root:

```sh
dotnet --list-sdks
dotnet --list-runtimes
dotnet restore EBoekhoudenMcp.sln --locked-mode --disable-parallel -m:1 -nr:false
dotnet build EBoekhoudenMcp.sln -c Release --no-restore -m:1 -nr:false
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore -m:1 -nr:false
dotnet publish src/EBoekhouden.Host.Stdio/EBoekhouden.Host.Stdio.csproj -c Release --no-restore --no-self-contained -p:UseAppHost=false -o artifacts/publish/stdio -m:1 -nr:false
```

The result is `artifacts/publish/stdio/EBoekhouden.Host.Stdio.dll`. Keep the entire published directory together, including dependency DLLs, `.deps.json` and `.runtimeconfig.json`. Use absolute paths in client configuration. The publish is framework-dependent, untrimmed, and has no standalone apphost. The `artifacts/` directory is ignored by Git.

`bash scripts/verify.sh` runs the same restore/build/test/publish sequence and tests the published host with synthetic loopback infrastructure. It recreates the generated publish directory. See [delivery.md](docs/delivery.md).

## 2. Supply credentials

| Setting | Environment variable | Requirement |
| --- | --- | --- |
| AccessToken | `EBoekhouden__AccessToken` | Your e-Boekhouden API credential; required, non-whitespace |
| Source | `EBoekhouden__Source` | Integration identifier, 1–10 characters matching `^[\w_ ]{1,10}$`; for example `McpLocal` |

### Option A: environment variables

Supply these through the process environment or your client’s secret manager. For a Bash terminal:

```sh
export EBoekhouden__AccessToken="<API_TOKEN>"
export EBoekhouden__Source="McpLocal"
```

A desktop application may not inherit variables exported in a separate terminal. Configure its launch environment or use development user secrets below. The server does not automatically load `.env` files.

### Option B: .NET development user secrets

From the repository root, using the OS account that will run the server:

```sh
dotnet user-secrets set --project src/EBoekhouden.Host.Stdio EBoekhouden:AccessToken "<API_TOKEN>"
dotnet user-secrets set --project src/EBoekhouden.Host.Stdio EBoekhouden:Source "McpLocal"
```

Launch the published server with `DOTNET_ENVIRONMENT=Development`. Its compiled UserSecretsId connects it to that store even when launched outside the source directory. Environment variables override user secrets; use one credential source deliberately to avoid stale overrides. Secret-provider loading has been tested with an isolated synthetic store.

User secrets are a development convenience: they store values in a JSON file outside the repository and **do not encrypt them**. A literal credential in client JSON/TOML or a shell profile is also stored as text. Protect those files with OS permissions and keep real values out of version control and shell history. The placeholder commands above show configuration syntax, not a secure secret-entry mechanism.

The server keeps the configured API credential in process memory and sends it only in the internal HTTPS `POST /v1/session` body. Short-lived session tokens are cached in memory and refreshed automatically. The server does not write either token to a database or its own credential file, and does not log them. Rotate the external credential and restart the server to apply a change. One credential does not provide tool-level isolation between people sharing the same process.

## 3. Connect directly to Codex

For a local Codex client, add the following to `~/.codex/config.toml`, merging it with existing settings. This example forwards the credential from the environment instead of writing its value into TOML:

```toml
[mcp_servers.eboekhouden]
command = "/ABSOLUTE/PATH/TO/dotnet"
args = ["/ABSOLUTE/PATH/TO/EBoekhoudenMCPServer/artifacts/publish/stdio/EBoekhouden.Host.Stdio.dll"]
env_vars = ["EBoekhouden__AccessToken"]
startup_timeout_sec = 10
tool_timeout_sec = 60

[mcp_servers.eboekhouden.env]
EBoekhouden__Source = "McpLocal"
```

For user secrets, replace `env_vars` with no entry and set `DOTNET_ENVIRONMENT = "Development"` inside the `.env` table. Remove credential environment overrides if you want the secret store to supply both required settings.

Alternatively, register the user-secret variant through the CLI:

```sh
codex mcp add eboekhouden --env DOTNET_ENVIRONMENT=Development -- /ABSOLUTE/PATH/TO/dotnet /ABSOLUTE/PATH/TO/EBoekhoudenMCPServer/artifacts/publish/stdio/EBoekhouden.Host.Stdio.dll
```

Choose either registration method for the same name. The command writes local client configuration; it does not create an e-Boekhouden session by itself. Check registration with `codex mcp list`, restart the client, and use `/mcp` in the Codex TUI to inspect the active server. Registration is not proof that authentication to the accounting service succeeds. These settings follow the [official Codex MCP documentation](https://learn.chatgpt.com/docs/extend/mcp?surface=cli); CLI syntax was also checked against the installed client’s help.

There is no MCP OAuth login in this executable. `codex mcp login` and HTTP bearer-token client settings do not configure its upstream API credential.

## 4. Package it as a local ChatGPT/Codex desktop plugin

For a plugin entry in a supported local desktop client, prepare this directory outside tracked secret files:

```text
eboekhouden/
  plugin.json
  mcp.json
  server/
    start.sh
    EBoekhouden.Host.Stdio.dll
    ...all remaining published files...
```

Copy the complete published output into `server/`. The following Linux example uses the same user-secret store as step 2; it contains no API key.

`plugin.json`:

```json
{
  "$schema": "https://agent-plugins.org/schemas/1.0.0/plugin.schema.json",
  "name": "eboekhouden",
  "version": "0.1.0",
  "description": "Read-only e-Boekhouden accounting tools",
  "extensions": {
    "com.openai": {
      "requires_local_executor": true
    }
  }
}
```

`mcp.json`:

```json
{
  "$schema": "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json",
  "mcpServers": {
    "eboekhouden": {
      "type": "stdio",
      "command": "/bin/sh",
      "args": ["${PLUGIN_ROOT}/server/start.sh"]
    }
  }
}
```

`server/start.sh` (replace the absolute dotnet path):

```sh
#!/bin/sh
set -eu
export DOTNET_ENVIRONMENT=Development
exec "/ABSOLUTE/PATH/TO/dotnet" "$(dirname -- "$0")/EBoekhouden.Host.Stdio.dll"
```

The wrapper must produce no stdout except the server protocol. `${PLUGIN_ROOT}` is expanded by the plugin host; the shell resolves the DLL beside the wrapper. Keep the credential in the external user-secret store on the machine running the plugin.

For a personal marketplace, put this folder at `~/.codex/plugins/eboekhouden`. Merge this entry into `~/.agents/plugins/marketplace.json`, preserving other plugins:

```json
{
  "name": "local-accounting",
  "plugins": [
    {
      "name": "eboekhouden",
      "source": { "source": "local", "path": "./.codex/plugins/eboekhouden" },
      "policy": { "installation": "AVAILABLE", "authentication": "ON_INSTALL" },
      "category": "Productivity"
    }
  ]
}
```

Restart the ChatGPT desktop app, find the personal plugin and install/enable it. Avoid enabling a duplicate direct MCP registration. For CLI plugin management, inspect `codex plugin --help`; clients with the checked syntax support `codex plugin add eboekhouden@local-accounting`. See the official [plugin packaging](https://developers.openai.com/plugins/build/plugins) and [desktop-only MCP](https://developers.openai.com/plugins/deploy/desktop-only) guidance. Installation is account/client dependent and has not been performed for this repository.

This recipe packages the local executable. A browser/cloud custom-MCP URL form cannot launch this DLL on your computer: the repository has no remote HTTP transport. A remote ChatGPT connection would require a separately implemented and secured HTTP host. Public plugin submission and deployment are outside this guide.

## 5. Other stdio MCP clients

Clients using a `mcpServers` JSON format can adapt this user-secret example:

```json
{
  "mcpServers": {
    "eboekhouden": {
      "command": "/ABSOLUTE/PATH/TO/dotnet",
      "args": ["/ABSOLUTE/PATH/TO/EBoekhoudenMCPServer/artifacts/publish/stdio/EBoekhouden.Host.Stdio.dll"],
      "env": { "DOTNET_ENVIRONMENT": "Development" }
    }
  }
}
```

Client formats differ. Use that client’s stdio launch settings; JSON Windows paths need escaped backslashes. A client must read stdout as MCP and capture diagnostics separately from stderr.

## Optional runtime settings

All options belong to the `EBoekhouden` configuration section; environment names use `EBoekhouden__` plus the option name. Duration values use TimeSpan strings, such as `00:00:30`.

| Option | Default | Validation |
| --- | --- | --- |
| SessionTimeout | `00:00:10` | Positive, at most 2 minutes |
| AttemptTimeout | `00:00:10` | Positive, at most 2 minutes; no greater than OperationTimeout |
| OperationTimeout | `00:00:30` | Positive, at most 5 minutes; includes queueing, session acquisition, retries and parsing |
| RefreshBeforeExpiry | `00:00:30` | Positive, at most 5 minutes; effective margin capped at 10% of session lifetime |
| RetryBaseDelay | `00:00:00.200` | Positive, at most 30 seconds; no greater than MaxRetryDelay |
| MaxRetryDelay | `00:00:05` | Positive, at most 30 seconds |
| MaxReadAttempts | `3` | 1–5 GET attempts, including the one possible authentication replay |
| MaxConcurrentReads | `4` | 1–32 logical read operations |
| CircuitFailureThreshold | `5` | 1–100 consecutive transient failures |
| CircuitBreakDuration | `00:00:30` | Positive, at most 5 minutes |

Configuration is validated at startup without HTTP. Changes require a process restart. Do not set `EBoekhoudenTest__UpstreamOrigin` or `DOTNET_ENVIRONMENT=IntegrationTest` for normal use; these are an explicit loopback-only test seam. See [HTTP infrastructure](docs/http-infrastructure.md) for exact policies.

## Check setup and troubleshoot

Start with tool discovery: ask the client to list this server’s tools. Discovery and initialization are network-free; a valid accounting tool call obtains a session and accesses the configured administration.

| Symptom | Action |
| --- | --- |
| Exit 1 with a static stderr message | Check the required settings, source length/pattern, duration formats and test-origin overrides. The message deliberately omits configuration values. |
| Missing SDK/runtime or assembly | Check the exact SDK for builds, .NET 10 runtime for execution, absolute paths and complete publish directory. |
| Credential missing in a desktop client | Check its launch environment or Development/user-secret setup under the same OS account; restart after changes. |
| Tools missing in Codex | Check registration, plugin enabled state, duplicate registrations and client restart. Check stderr separately. |
| HTTP 401/403 on an accounting call | Check credential validity and upstream permissions. Live header compatibility is still unverified; do not paste tokens or financial bodies into diagnostics. |
| Local cancellation leaves a request running | The client must send MCP notifications/cancelled with the matching request ID. The pinned SDK’s local-task forwarding limitation is documented in [REVIEW.md](docs/development/REVIEW.md). |

Documentation checked on **2026-10-31** against the actual server settings, official OpenAI documentation and installed Codex CLI help. Direct Codex registration and desktop plugin installation were not executed. Server/published-artifact behavior has offline synthetic test evidence; actual e-Boekhouden compatibility remains [unverified](docs/development/API_COMPATIBILITY.md).
