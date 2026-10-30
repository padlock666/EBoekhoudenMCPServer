#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
unset EBOEKHOUDEN_TEST_HOST_DLL

dotnet restore EBoekhoudenMcp.sln --locked-mode --disable-parallel -m:1 -nr:false
dotnet build EBoekhoudenMcp.sln -c Release --no-restore -m:1 -nr:false
dotnet test EBoekhoudenMcp.sln -c Release --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal'

# This directory is reserved for generated publish output; rebuild it without stale files.
publish_dir="$PWD/artifacts/publish/stdio"
rm -rf -- "$publish_dir"
dotnet publish src/EBoekhouden.Host.Stdio/EBoekhouden.Host.Stdio.csproj -c Release --no-restore --no-self-contained -p:UseAppHost=false -o "$publish_dir" -m:1 -nr:false

# Reuse the same SDK/fake-HTTP tests against the exact published artifact, not the build copy.
EBOEKHOUDEN_TEST_HOST_DLL="$publish_dir/EBoekhouden.Host.Stdio.dll"
export EBOEKHOUDEN_TEST_HOST_DLL
dotnet test tests/EBoekhouden.IntegrationTests -c Release --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal'
