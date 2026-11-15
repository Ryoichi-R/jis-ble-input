#!/bin/zsh
set -eu

cd -- "${0:A:h}"
app="JIS-BLE-Input.app"
flags=()
if [[ ${1:-} == "--check-ui" ]]; then
    app="checks/.build/$app"
    flags=(-D UI_TEST)
fi
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp Info.plist "$app/Contents/Info.plist"
cp ../../LICENSE "$app/Contents/Resources/LICENSE"
sips -s format icns AppIcon.png --out "$app/Contents/Resources/AppIcon.icns" >/dev/null
minimum=$(plutil -extract LSMinimumSystemVersion raw Info.plist)
swiftc -target "$(uname -m)-apple-macos${minimum}" -warnings-as-errors -module-cache-path "$PWD/.module-cache" "${flags[@]}" \
    AsciiKeys.swift SenderInput.swift ConnectionSafety.swift \
    ReceiverApproval.swift ReportMap.swift TextTemplates.swift SenderWindow.swift main.swift \
    -o "$app/Contents/MacOS/JisBleInput"
codesign --force --sign - "$app"
codesign --verify --strict "$app"
plutil -lint "$app/Contents/Info.plist"
