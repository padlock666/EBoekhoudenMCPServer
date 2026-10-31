# GNOME Keyring implementation

The server retrieves its API key using the same libsecret application/account lookup as the GnomeKeyringTest demo. See [configuration.md](../configuration.md#2-configure-gnome-keyring) for prerequisite installation, desktop setup, the exact `secret-tool store` command and client examples.

## Lookup and lifetime

Production registers `KeyringPasswordSource` as the only `IPasswordSource`. The nonsecret options are `EBoekhouden:Keyring:Application` (default `EBoekhoudenMcp`) and `EBoekhouden:Keyring:Account` (default `api`). A worker calls `secret_password_lookupv_nonpageable_sync` with a null schema and a GLib-owned hash table containing those two attributes. This matches items provisioned with `secret-tool`.

Lookup searches across the user's collections. Keep the attribute pair unique: libsecret may choose one of multiple matches. It can unlock a locked item through a desktop prompt. The process must run as the same desktop user, with access to the desktop session bus and GNOME Keyring. The server does not independently verify the provider's process identity. Attributes are public metadata rather than access controls.

A `GCancellable` forwards cancellation and the session/operation deadlines to the native lookup. Its cancellation registration is disposed before freeing the native object. SafeHandles own the hash table, cancellable and password; `secret_password_free` clears and releases the native allocation even on failure. An owned managed UTF-8 copy crosses the existing asynchronous credential boundary, is validated, and is cleared after JSON serialization. Temporary JSON buffers are also cleared. Native/HTTP internals may make copies; complete process-memory erasure is not claimed.

The API key is a text password, not binary data: 1–4,096 UTF-8 bytes, nonblank and without control characters. Spaces are preserved. The native API returns NUL-terminated text; embedded NUL values are unsupported. No managed API-key string is created. Native load, lookup and validation failures become a fixed `CredentialUnavailable` error without raw errors or secret values.

Lookup occurs only when session request content is serialized after connection/TLS validation. Tool discovery and startup validation do not access the keyring. Each session POST retrieves afresh, so rotation takes effect on the next authentication; normal GETs reuse cached session tokens. Session POSTs are never retried. Upstream error code/trace are suppressed to prevent reflection of a released key. The fixed HTTP origin, read-only tools and bounded retry policies are unchanged.

## Verification

The standard Linux executable tests need libsecret/GLib/GIO, `dbus-daemon`, Python 3 with GI/GIO, and the pinned .NET SDK. On Ubuntu/Debian:

```sh
sudo apt install libsecret-1-0 dbus python3-gi gir1.2-glib-2.0
bash scripts/verify.sh
```

Each executable test owns a non-activating private D-Bus session, synthetic Secret Service, temporary HOME/XDG directories and subprocesses. Test environments never inherit the desktop bus or personal keyring. The actual host, native adapter, session JSON, fake HTTP, SDK transport and output capture run together. API/MCP unit tests inject synthetic UTF-8 credentials in code.

Tests cover default/custom attributes, Unicode and exact spaces, missing/unavailable/inaccessible/invalid secrets, rotation, session reuse, reflection safety, configuration rejection, native cancellation/deadlines and refresh recovery. See [integration tests](integration-tests.md) and [STATUS.md](development/STATUS.md) for executed results.

An optional additional test starts its own real GNOME Keyring daemon, provisions a disposable API key using `secret-tool store`, and authenticates through the actual host. It requires `gnome-keyring` and `libsecret-tools` and is skipped by default:

```sh
EBOEKHOUDEN_TEST_REAL_GNOME=1 dotnet test tests/EBoekhouden.IntegrationTests -c Release --no-build --no-restore -m:1 -nr:false --filter FullyQualifiedName~IsolatedGnomeTests
```

This uses only its private bus/keyring and synthetic HTTP. It does not configure a personal keyring or call e-Boekhouden.

Native API ownership and semantics were checked against [GNOME's lookup documentation](https://gnome.pages.gitlab.gnome.org/libsecret/func.password_lookupv_nonpageable_sync.html), [password freeing](https://gnome.pages.gitlab.gnome.org/libsecret/func.password_free.html), and the [secret-tool manual](https://manpages.debian.org/testing/libsecret-tools/secret-tool.1.en.html). The MCP SDK remains pinned to 2.3.0; its [tagged getting-started documentation](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.3.0/docs/concepts/getting-started.md) was rechecked. No managed packages were added.
