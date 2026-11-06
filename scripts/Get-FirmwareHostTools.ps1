#requires -Version 7.4
# Host tools are separate from the firmware's hash-bound target toolchain.json.
function Get-FirmwareHostTools {
    param([string]$Architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString())
    switch ($Architecture) {
        'X64' {
            return [pscustomobject]@{
                Folder = 'llvm-23.1.2-x64-windows'
                Archive = 'clang+llvm-23.1.2-x86_64-pc-windows-msvc.tar.xz'
                Prefix = 'clang+llvm-23.1.2-x86_64-pc-windows-msvc'
                Sha256 = '8FB91CDC44FCBBDCF6B3FFD0A1F9859ABD14A3C3AAE4423C2B6D4A4F90BF0095'
                ProfileRuntime = 'clang_rt.profile-x86_64.lib'
                Compression = 'xz'
            }
        }
        'Arm64' {
            return [pscustomobject]@{
                Folder = 'llvm-23.1.2-aarch64-windows'
                Archive = 'clang+llvm-23.1.2-aarch64-pc-windows-msvc.tar.zst'
                Prefix = 'clang+llvm-23.1.2-aarch64-pc-windows-msvc'
                Sha256 = '5A1B014065E0828786D503DE408BFF6C0EE57D4E7AD70086E57FA729E14BB76C'
                ProfileRuntime = 'clang_rt.profile-aarch64.lib'
                Compression = 'zstd'
            }
        }
        default { throw "FIRMWARE_HOST_UNSUPPORTED: $Architecture (Windows x64 or ARM64 required)." }
    }
}
