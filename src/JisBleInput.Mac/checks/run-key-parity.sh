#!/bin/zsh
set -eu
if [[ $# -gt 1 ]]; then
    print -u2 "Usage: zsh checks/run-key-parity.sh [/path/to/JisBleInput.Core]"
    exit 2
fi
core=${1:-}
if [[ -n "$core" ]]; then core="${core:A}"; fi
cd -- "${0:A:h}/.."
core=${core:-"$PWD/../JisBleInput.Core"}
check_output="$PWD/checks/.build/key-parity"
mkdir -p "$check_output"
(
    export DOTNET_CLI_HOME="$check_output" DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_CLI_TELEMETRY_OPTOUT=1
    dotnet build checks/windows/KeyParity.csproj -o "$check_output/windows" \
        "-p:BaseIntermediateOutputPath=$check_output/obj/" "-p:WindowsCore=$core"
    dotnet "$check_output/windows/KeyParity.dll" > "$check_output/windows.txt"
)
swiftc -warnings-as-errors -module-cache-path "$PWD/.module-cache" \
    AsciiKeys.swift SenderInput.swift \
    checks/mac/main.swift -o "$check_output/key-parity"
"$check_output/key-parity" > "$check_output/mac.txt"
diff -u "$check_output/windows.txt" "$check_output/mac.txt"
wc -l < "$check_output/mac.txt"
