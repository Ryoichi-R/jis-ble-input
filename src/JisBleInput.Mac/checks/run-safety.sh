#!/bin/zsh
set -eu
cd -- "${0:A:h}/.."
mkdir -p checks/.build
swiftc -warnings-as-errors -module-cache-path "$PWD/.module-cache" \
    AsciiKeys.swift SenderInput.swift ConnectionSafety.swift \
    TextTemplates.swift checks/safety/main.swift -o checks/.build/safety-check
checks/.build/safety-check
