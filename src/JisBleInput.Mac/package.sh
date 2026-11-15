#!/bin/zsh
set -eu

cd -- "${0:A:h}"
app="JIS-BLE-Input.app"
version=$(plutil -extract CFBundleShortVersionString raw "$app/Contents/Info.plist")
archive="JIS-BLE-Input-${version}-macOS-$(uname -m).zip"
codesign --verify --strict "$app"
mkdir -p dist
ditto -c -k --keepParent "$app" "dist/$archive"
cd dist
shasum -a 256 "$archive" > "$archive.sha256"
shasum -a 256 -c "$archive.sha256"
