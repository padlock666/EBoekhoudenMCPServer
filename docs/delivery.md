# Delivery and automated verification

## CI provider decision

No existing CI configuration was found. Phase 7 chooses **GitHub Actions**, using an **Ubuntu 24.04** job. This matches the Linux platform verified locally and supports the loopback HTTP/socket and subprocess tests. The workflow uses the SDK in `global.json`, its VSTest selection, central package pins and six project lock files. No accounting credential or live API is required. Creating this workflow does not run a hosted job or deploy anything.

## Artifact choice

The local artifact is a framework-dependent, untrimmed DLL publish under ignored `artifacts/publish/stdio/`, with `UseAppHost=false` and no runtime identifier. Launch it using an absolute DLL path with `dotnet`. Keep all dependency and runtime files beside it. This avoids OS-specific apphost/runtime-pack lock graphs; it does not bundle the .NET runtime. Production needs the .NET 10 runtime, while build/tests need the pinned SDK and the test-only ASP.NET Core shared runtime. Single-file, trimming, self-contained and NativeAOT publication are outside the verified configuration.

## Pipeline

`bash scripts/verify.sh` is the shared local/CI entry point. It clears any inherited test-artifact override, restores the solution in locked mode, builds Release, runs the full offline suite, recreates the generated publish directory, publishes locally, then reruns the executable integration suite with `EBOEKHOUDEN_TEST_HOST_DLL` set to the absolute published DLL. Each process receives only isolated synthetic configuration. Startup errors, user-secret loading, schemas, fake accounting calls, log isolation and shutdown are tested against that exact artifact. The script stops on any failed command.

The workflow uses official `actions/checkout` v6 at `d23441a48e516b6c34aea4fa41551a30e30af803` and `actions/setup-dotnet` v5 at `26b0ec14cb23fa6904739307f278c14f94c95bf1`. Their official tag refs and action metadata were checked. Setup reads `global.json` and caches packages using all six lock files; `global.json` selects SDK 10.0.113 and VSTest. Workflow permissions are contents-read only; checkout does not persist credentials. No accounting secret, deployment, package push or release upload is configured. Hosted execution has not been run from this workspace; the same pipeline commands are verified locally.

## Dependency updates

`Directory.Build.props` enables lock files and locked restore by default. Each SDK-style project has a `packages.lock.json` recording resolved direct/transitive versions and package content hashes. Normal restore must preserve these files and fail when requested dependencies change.

For a deliberate change, edit central pins or project references, then explicitly allow lock regeneration:

```sh
dotnet restore EBoekhoudenMcp.sln -p:RestoreLockedMode=false --force-evaluate --disable-parallel -m:1 -nr:false
```

Review all affected lock-file diffs, including transitive versions and hashes. Run `bash scripts/verify.sh` afterwards. Do not disable locked mode in normal build/CI to bypass a mismatch. An isolated negative control changing a copied central pin is expected to fail with NU1004; actual evidence is in [STATUS.md](development/STATUS.md).

## Verification scope

Local verification covers Linux x64, the pinned SDK, .NET 10.0.13, the portable DLL publish and synthetic loopback HTTP. Windows/macOS, RID-specific apphosts, self-contained output and hosted Actions infrastructure are not claimed tested. Existing API header/expiry/response assumptions and SDK local-cancellation behavior remain in [compatibility notes](development/API_COMPATIBILITY.md). The [phase 8 review](development/REVIEW.md) records verified local evidence and remaining compatibility limits.

Primary references checked during implementation: [NuGet lock files](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies), [dotnet publish](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish), [pinned checkout metadata](https://github.com/actions/checkout/blob/d23441a48e516b6c34aea4fa41551a30e30af803/action.yml) and [pinned setup-dotnet metadata](https://github.com/actions/setup-dotnet/blob/26b0ec14cb23fa6904739307f278c14f94c95bf1/action.yml).
