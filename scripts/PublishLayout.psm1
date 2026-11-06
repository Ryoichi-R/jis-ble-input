Set-StrictMode -Version Latest

function Get-PublishedRuntime {
    param([Parameter(Mandatory)][string]$ExePath, [string]$BundleEntryHash)
    $stream = [IO.File]::OpenRead($ExePath)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        $prefix = $reader.ReadBytes([int][Math]::Min($stream.Length, 16MB))
        $signature = [Convert]::FromHexString('8B1202B96A612038727B930214D7A03213F5B9E6EFAE3318EE3B2DCE24B36AAE')
        $marker = [Text.Encoding]::Latin1.GetString($prefix).IndexOf([Text.Encoding]::Latin1.GetString($signature), [StringComparison]::Ordinal)
        if ($marker -lt 8) { throw 'BUNDLE_MARKER_MISSING' }
        $stream.Position = [BitConverter]::ToInt64($prefix, $marker - 8)
        $major = $reader.ReadUInt32()
        $null = $reader.ReadUInt32()
        $count = $reader.ReadInt32()
        if ($major -ne 6 -or $count -lt 1 -or $count -gt 10000) { throw 'BUNDLE_HEADER_INVALID' }
        $null = $reader.ReadString()
        $stream.Position += 40
        for ($i = 0; $i -lt $count; $i++) {
            $offset = $reader.ReadInt64()
            $size = $reader.ReadInt64()
            $compressed = $reader.ReadInt64()
            $null = $reader.ReadByte()
            $name = $reader.ReadString()
            if ($BundleEntryHash -and $name -eq $BundleEntryHash) {
                if ($compressed -ne 0 -or $size -gt 16MB -or $size -lt 1 -or $offset -lt 0 -or $offset + $size -gt $stream.Length) { throw 'BUNDLE_ENTRY_INVALID' }
                $stream.Position = $offset
                return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($reader.ReadBytes([int]$size)))
            }
            if (-not $BundleEntryHash -and $name -eq 'JisBleInput.runtimeconfig.json') {
                if ($compressed -ne 0 -or $size -gt 1MB -or $size -lt 1 -or $offset -lt 0 -or $offset + $size -gt $stream.Length) {
                    throw 'BUNDLE_CONFIG_INVALID'
                }
                $stream.Position = $offset
                return [Text.Encoding]::UTF8.GetString($reader.ReadBytes([int]$size)) | ConvertFrom-Json
            }
        }
        throw 'BUNDLE_CONFIG_MISSING'
    } finally { $reader.Dispose() }
}

function Test-PublishLayout {
    param(
        [Parameter(Mandatory)][string]$PublishPath,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$RuntimeVersion
    )
    $required = @(
        'JisBleInput.exe', 'README.md', 'VALIDATION.md', 'licenses/windows-ble-hid-LICENSE.txt',
        'LICENSE', 'SECURITY.md', 'SUPPORT.md', 'THIRD-PARTY-NOTICES.md',
        'sbom.cdx.json',
        'licenses/dotnet-runtime/LICENSE.TXT', 'licenses/dotnet-runtime/THIRD-PARTY-NOTICES.TXT',
        'licenses/windows-desktop-runtime/LICENSE.TXT',
        'licenses/windows-desktop-runtime/THIRD-PARTY-NOTICES-WPF.TXT', 'licenses/windows-desktop-runtime/THIRD-PARTY-NOTICES-WINFORMS.TXT',
        'licenses/windows-desktop-runtime/sources.json',
        'licenses/dotnet-host/LICENSE.TXT', 'licenses/dotnet-host/THIRD-PARTY-NOTICES.TXT',
        'licenses/system-io-ports/LICENSE.TXT', 'licenses/system-io-ports/THIRD-PARTY-NOTICES.TXT',
        'licenses/system-io-ports/source.json',
        'docs/XIAO-PROTOCOL.md', 'firmware/README.md', 'firmware/compatibility.json', 'firmware/toolchain.json',
        'firmware/xiao-nrf52840.ino.uf2', 'firmware/xiao-nrf52840.ino.hex', 'firmware/xiao-nrf52840.ino.zip',
        'firmware/rebuild/Seeed_nRF52_Boards-1.1.13.tar.bz2', 'firmware/rebuild/linkable-objects.zip',
        'firmware/rebuild/jis-ble-input/LICENSE', 'firmware/rebuild/jis-ble-input/scripts/Get-FirmwareHostTools.ps1',
        'firmware/licenses/sdk/LICENSE', 'firmware/licenses/sdk/Nordic-SoftDevice-API.txt',
        'firmware/licenses/sdk/LGPL-2.1.txt', 'firmware/licenses/sdk/libraries/Adafruit_nRFCrypto/src/cortex-m4/license.txt',
        'firmware/licenses/sdk/libraries/Adafruit_nRFCrypto/src/nrf_cc310/license.txt',
        'samples/ascii-url.txt', 'samples/ascii-printable.txt', 'samples/multiline-crlf.txt',
        'samples/multiline-lf.txt', 'samples/AC-08-100.txt', 'samples/AC-08-1000.txt'
    )
    foreach ($relative in $required) {
        $path = Join-Path $PublishPath $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            throw "PUBLISH_FILE_MISSING: $relative"
        }
    }
    $exe = Join-Path $PublishPath 'JisBleInput.exe'
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion -ne $Version) { throw 'PUBLISH_VERSION_MISMATCH' }
    $compatibility = Get-Content -LiteralPath (Join-Path $PublishPath 'firmware/compatibility.json') -Raw | ConvertFrom-Json
    if ($compatibility.AppVersion -ne $Version -or $compatibility.Firmware -ne '0.1.0' -or $compatibility.Protocol -ne 1) { throw 'PUBLISH_FIRMWARE_VERSION_MISMATCH' }
    foreach ($name in @('xiao-nrf52840.ino.uf2', 'xiao-nrf52840.ino.hex', 'xiao-nrf52840.ino.zip')) {
        $entry = @($compatibility.Images | Where-Object Name -EQ $name)
        if ($entry.Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path $PublishPath "firmware/$name")).Hash -ne $entry[0].Sha256) { throw 'PUBLISH_FIRMWARE_HASH_MISMATCH' }
    }
    if ($compatibility.PSObject.Properties.Name -notcontains 'Fqbn' -or $compatibility.Fqbn -ne 'Seeeduino:nrf52:xiaonRF52840Sense:softdevice=s140v6,debug=l0') { throw 'PUBLISH_FIRMWARE_FQBN_MISMATCH' }
    $descriptorPath = Join-Path $PublishPath 'firmware/rebuild/jis-ble-input/contracts/xiao-protocol-v1/descriptor.hex'
    if (-not (Test-Path -LiteralPath $descriptorPath -PathType Leaf)) { throw 'PUBLISH_FILE_MISSING: firmware descriptor contract' }
    $descriptor = [Convert]::FromHexString([IO.File]::ReadAllText($descriptorPath).Trim())
    if ($compatibility.PSObject.Properties.Name -notcontains 'DescriptorSha256' -or
        $compatibility.DescriptorSha256 -ne [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($descriptor))) { throw 'PUBLISH_FIRMWARE_DESCRIPTOR_MISMATCH' }
    $noticeRoot = Join-Path $PublishPath 'licenses/windows-desktop-runtime'
    $notices = Get-Content -LiteralPath (Join-Path $noticeRoot 'sources.json') -Raw | ConvertFrom-Json
    if ($notices.runtimeVersion -ne $RuntimeVersion) { throw 'PUBLISH_NOTICE_VERSION_MISMATCH' }
    if (@($notices.sources).Count -ne 2) { throw 'PUBLISH_NOTICE_SOURCES_INVALID' }
    foreach ($source in $notices.sources) {
        if ($source.file -notin @('THIRD-PARTY-NOTICES-WPF.TXT', 'THIRD-PARTY-NOTICES-WINFORMS.TXT')) { throw 'PUBLISH_NOTICE_PATH_INVALID' }
        if ((Get-FileHash -LiteralPath (Join-Path $noticeRoot $source.file)).Hash -ne $source.sha256) { throw 'PUBLISH_NOTICE_HASH_MISMATCH' }
    }
    $stream = [IO.File]::OpenRead($exe)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        $stream.Position = 0x3C
        $pe = $reader.ReadInt32()
        $stream.Position = $pe
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0xAA64) { throw 'PUBLISH_NOT_ARM64' }
    } finally { $reader.Dispose() }
    $config = Get-PublishedRuntime -ExePath $exe
    $frameworks = @($config.runtimeOptions.includedFrameworks)
    foreach ($name in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
        $framework = @($frameworks | Where-Object name -EQ $name)
        if ($framework.Count -ne 1 -or $framework[0].version -ne $RuntimeVersion) { throw "PUBLISH_RUNTIME_MISMATCH: $name" }
    }
    $portsSource = Get-Content -LiteralPath (Join-Path $PublishPath 'licenses/system-io-ports/source.json') -Raw | ConvertFrom-Json
    if ($portsSource.Version -ne $RuntimeVersion -or (Get-PublishedRuntime -ExePath $exe -BundleEntryHash 'System.IO.Ports.dll') -ne $portsSource.WindowsAssemblySha256) { throw 'PUBLISH_PORTS_ASSEMBLY_MISMATCH' }
    [pscustomobject]@{ Version = $Version; RuntimeVersion = $RuntimeVersion; Sha256 = (Get-FileHash -LiteralPath $exe).Hash; RequiredFiles = $required.Count }
}

Export-ModuleMember -Function Get-PublishedRuntime, Test-PublishLayout
