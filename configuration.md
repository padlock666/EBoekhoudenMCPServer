# Configuring the e-Boekhouden MCP server

This guide covers local setup, credentials, Codex MCP registration and a local desktop plugin. See [usage.md](usage.md) for tool examples and accounting limits. Examples use placeholder paths and credentials; replace them locally.

The server runs as a child process over **stdio**. It serves one configured e-Boekhouden credential and administration scope. Its production upstream is fixed to `https://api.e-boekhouden.nl/`. It exposes ten read-only business tools; session creation is internal authentication. There is no listening MCP HTTP port or database.

## 1. Build and publish

Install the stable .NET SDK **10.0.114**, pinned by [global.json](global.json). Tests also require the .NET 10 ASP.NET Core shared runtime included in the verified SDK installation. Running the published server requires the .NET 10 `Microsoft.NETCore.App` runtime. The credential lookup requires Linux and GNOME Keyring. Current Linux x64 checks use runtime **10.0.14**. Native test prerequisites are listed in [Linux Secret Service](docs/linux-secret-service.md#verification).

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

## 2. Configure GNOME Keyring

The API key is retrieved through GNOME's libsecret library, using the `application` and `account` attributes. Run the server as your normal desktop user in the same Linux login session as GNOME Keyring. The MCP client must inherit that session's `DBUS_SESSION_BUS_ADDRESS`. A desktop unlock dialog may appear during the first accounting call.

### Install the prerequisites

On Ubuntu, Zorin OS or Debian:

```sh
sudo apt update
sudo apt install gnome-keyring libpam-gnome-keyring libsecret-1-0 libsecret-tools seahorse
```

`libsecret-tools` supplies the setup command `secret-tool`; the server calls libsecret directly. Native GLib/GIO/libsecret libraries must match the .NET process architecture. After installing desktop integration, log out and back in with your normal password.

Open **Passwords and Keys** (`seahorse`). Create a password-protected keyring if needed, set the intended keyring as the default, and unlock it. For automatic unlock, the Login keyring password should match your login password and GNOME Keyring PAM integration must be enabled. Automatic or fingerprint login may leave it locked; unlock it manually in that case.

### Add the API key with secret-tool

Run this command in a terminal inside your desktop session, as your normal user:

```sh
secret-tool store --label='e-Boekhouden MCP API key' \
  application EBoekhoudenMcp account api
```

Paste your **e-Boekhouden API key** at the hidden `Password:` prompt and press Enter. Enter the API key itself, without quotes or JSON. The `--label` value is a display name; lookup uses the exact, case-sensitive attribute values `EBoekhoudenMcp` and `api`. Keep that pair unique across your keyrings. Store the key interactively so it does not appear in command arguments or shell history.

The command stores the item in the default keyring. A matching item in that collection is updated when you repeat it, so the same command can rotate the key. The server retrieves it again when the next session authenticates; an existing cached session continues until refresh. Restarting the server forces a fresh authentication on the next accounting call.

### Create the nonsecret configuration

Copy [examples/config.example.json](examples/config.example.json) to `~/.config/eboekhouden-mcp/config.json`:

```sh
mkdir -p "$HOME/.config/eboekhouden-mcp"
cp examples/config.example.json "$HOME/.config/eboekhouden-mcp/config.json"
```

Its contents are:

```json
{
  "EBoekhouden": {
    "Source": "McpLocal",
    "Keyring": {
      "Application": "EBoekhoudenMcp",
      "Account": "api"
    }
  }
}
```

`Source` is the upstream integration identifier: 1–10 characters matching `^[\w_ ]{1,10}$`. `Keyring.Application` and `Keyring.Account` are nonsecret lookup attributes. Their defaults are shown above, so the `Keyring` object can be omitted when using those values. Custom values must be nonblank, contain no control characters, and be at most 4,096 characters each. Change them in both the store command and configuration to select a different item:

```sh
secret-tool store --label='e-Boekhouden MCP work API key' \
  application EBoekhoudenMcp account work
```

Set `Keyring.Account` to `work` for that item. Lookup searches across the user's keyrings; libsecret may select one if multiple items match. Attribute names and values are metadata, not access controls.

Launch with the **absolute** configuration path:

```sh
dotnet /ABSOLUTE/PATH/TO/EBoekhouden.Host.Stdio.dll --config /ABSOLUTE/PATH/TO/config.json
```

The configuration contains only nonsecret settings. It is loaded only when explicitly selected with `--config`, without a file watcher. Environment and launch arguments can override nonsecret settings, for example `EBoekhouden__Keyring__Account` and `EBoekhouden__Source`. Changes to settings require a process restart. Startup validates configuration locally; initialization and tool discovery do not retrieve the key or contact the accounting API.

The key is retrieved only for an internal HTTPS `POST /v1/session`, after connection/TLS validation, then its owned buffers are cleared. Session tokens are cached in memory and refreshed automatically. A missing/unavailable key, missing native libraries or a failed lookup gives a safe `CredentialUnavailable` tool error. Lookup cancellation and unlock prompts share `SessionTimeout` and `OperationTimeout`; increase these within their supported bounds if you need longer to unlock the keyring.

See [Linux Secret Service](docs/linux-secret-service.md) for native implementation details and isolated tests.

## 3. Connect directly to Codex

For a local Codex client, add the following to `~/.codex/config.toml`, merging it with existing settings. Replace both paths with absolute paths on your machine:

```toml
[mcp_servers.eboekhouden]
command = "/ABSOLUTE/PATH/TO/dotnet"
args = ["/ABSOLUTE/PATH/TO/EBoekhoudenMCPServer/artifacts/publish/stdio/EBoekhouden.Host.Stdio.dll", "--config", "/ABSOLUTE/PATH/TO/config.json"]
env_vars = ["DBUS_SESSION_BUS_ADDRESS"]
startup_timeout_sec = 10
tool_timeout_sec = 60
```

The client must run in the same desktop user/session as GNOME Keyring. The `env_vars` entry forwards its `DBUS_SESSION_BUS_ADDRESS` to the child process. The `command`/`args` format follows the [official Codex MCP documentation](https://learn.chatgpt.com/docs/extend/mcp?surface=cli). Restart the client after changing registration.

Alternatively, register the same server through the CLI:

```sh
codex mcp add eboekhouden -- /ABSOLUTE/PATH/TO/dotnet /ABSOLUTE/PATH/TO/EBoekhoudenMCPServer/artifacts/publish/stdio/EBoekhouden.Host.Stdio.dll --config /ABSOLUTE/PATH/TO/config.json
```

After CLI registration, add `env_vars = ["DBUS_SESSION_BUS_ADDRESS"]` to its generated `[mcp_servers.eboekhouden]` table. Choose either registration method for the same name. The command writes local client configuration; it does not create an e-Boekhouden session by itself. Check registration with `codex mcp list`, restart the client, and use `/mcp` in the Codex TUI to inspect the active server. Registration is not proof that authentication to the accounting service succeeds. These settings follow the [official Codex MCP documentation](https://learn.chatgpt.com/docs/extend/mcp?surface=cli); CLI syntax was also checked against the installed client’s help.

There is no MCP OAuth login in this executable. `codex mcp login` and HTTP bearer-token client settings do not configure its upstream API credential.

## 4. Package it as a local ChatGPT/Codex desktop plugin

For a plugin entry in a supported local desktop client, prepare this directory outside the repository:

```text
eboekhouden/
  plugin.json
  mcp.json
  server/
    start.sh
    EBoekhouden.Host.Stdio.dll
    ...all remaining published files...
```

Copy the complete published output into `server/`. The following Linux example uses the nonsecret configuration above and GNOME Keyring in the desktop session.

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

`server/start.sh` (replace both absolute paths):

```sh
#!/bin/sh
set -eu
exec "/ABSOLUTE/PATH/TO/dotnet" "$(dirname -- "$0")/EBoekhouden.Host.Stdio.dll" --config "/ABSOLUTE/PATH/TO/config.json"
```

The wrapper must produce no stdout except the server protocol. `${PLUGIN_ROOT}` is expanded by the plugin host; the shell resolves the DLL beside the wrapper. Keep the nonsecret configuration on the machine running the plugin. Launch the desktop client in the same login session as GNOME Keyring.

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

Clients using a `mcpServers` JSON format can adapt this example:

```json
{
  "mcpServers": {
    "eboekhouden": {
      "command": "/ABSOLUTE/PATH/TO/dotnet",
      "args": ["/ABSOLUTE/PATH/TO/EBoekhoudenMCPServer/artifacts/publish/stdio/EBoekhouden.Host.Stdio.dll", "--config", "/ABSOLUTE/PATH/TO/config.json"]
    }
  }
}
```

Client formats differ. Use that client’s Linux stdio launch settings and preserve the desktop session bus. A client must read stdout as MCP and capture diagnostics separately from stderr.

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
| Exit 1 with a static stderr message | Check the absolute `--config` path, valid JSON, keyring attribute values, Source length/pattern, duration formats and test-origin overrides. The message deliberately omits configuration values. |
| CredentialUnavailable | Check libsecret installation, the same desktop user/session, exact application/account attributes and unlock status in Passwords and Keys. |
| Missing SDK/runtime or assembly | Check the exact SDK for builds, .NET 10 runtime for execution, absolute paths and complete publish directory. |
| Keyring unavailable in a desktop client | Preserve DBUS_SESSION_BUS_ADDRESS from the desktop login session and run as the same user. Check busctl --user status org.freedesktop.secrets. |
| Tools missing in Codex | Check registration, plugin enabled state, duplicate registrations and client restart. Check stderr separately. |
| HTTP 401/403 on an accounting call | Check credential validity and upstream permissions. Live header compatibility is still unverified; do not paste tokens or financial bodies into diagnostics. |
| Local cancellation leaves a request running | The client must send MCP notifications/cancelled with the matching request ID. The pinned SDK’s local-task forwarding limitation is documented in [REVIEW.md](docs/development/REVIEW.md). |

Documentation checked on **2026-10-31** against the actual server settings, official OpenAI documentation and installed Codex CLI help. Direct Codex registration and desktop plugin installation were not executed. Server/published-artifact behavior has offline synthetic test evidence; actual e-Boekhouden compatibility remains [unverified](docs/development/API_COMPATIBILITY.md).
