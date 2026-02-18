# build-native.ps1 — Build Ghostty native libraries from submodule (Windows)
#
# Usage:
#   .\scripts\build-native.ps1              # Build for current platform
#   .\scripts\build-native.ps1 -Arch arm64  # Build Windows arm64 artifacts
#   .\scripts\build-native.ps1 -Clean       # Clean build
#   .\scripts\build-native.ps1 -Help        # Show usage
#
# Prerequisites:
#   - Zig 0.15.2+ (https://ziglang.org/download/)
#   - Git submodule initialized: git submodule update --init
#
# The script builds libghostty, libghostty-terminal, and ghostty-renderer-capi as shared libraries
# and copies them to the correct NuGet runtime package location.
#
# NOTE: Ghostty currently does not officially support Windows builds.
# This script is provided for future compatibility.

param(
    [switch]$Clean,
    [switch]$Release,
    [switch]$Debug,
    [switch]$Static,
    [ValidateSet("x64", "arm64")]
    [string]$Arch = "x64",
    [switch]$Help
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$GhosttyDir = Join-Path $RootDir "external\ghostty"
$NativeOutDir = Join-Path $RootDir "native"

function Write-Info  { Write-Host "[INFO] $args" -ForegroundColor Green }
function Write-Warn  { Write-Host "[WARN] $args" -ForegroundColor Yellow }
function Write-Err   { Write-Host "[ERROR] $args" -ForegroundColor Red }

if ($Help) {
    Write-Host @"
Usage: .\scripts\build-native.ps1 [OPTIONS]

Build Ghostty native shared library from the git submodule.

Options:
  -Clean       Clean before building
  -Release     Build with ReleaseFast optimization (default)
  -Debug       Build with Debug optimization
  -Static      Also build static library
  -Arch        Target architecture: x64 or arm64 (default: x64)
  -Help        Show this help message

Prerequisites:
  - Zig 0.15.2+ must be in PATH
  - Git submodule must be initialized:
    git submodule update --init

NOTE: Ghostty does not currently support Windows builds upstream.
      This script is prepared for future compatibility.
"@
    exit 0
}

# Verify prerequisites
$zigPath = Get-Command zig -ErrorAction SilentlyContinue
if (-not $zigPath) {
    Write-Err "Zig not found in PATH."
    Write-Host ""
    Write-Host "Install Zig 0.15.2+:"
    Write-Host "  winget install zig.zig"
    Write-Host "  scoop install zig"
    Write-Host "  Manual: https://ziglang.org/download/"
    exit 1
}

$zigVersion = & zig version
Write-Info "Zig version: $zigVersion"

if (-not (Test-Path (Join-Path $GhosttyDir "build.zig"))) {
    Write-Err "Ghostty submodule not found at $GhosttyDir"
    Write-Host "Initialize with: git submodule update --init"
    exit 1
}

$RID = if ($Arch -eq "arm64") { "win-arm64" } else { "win-x64" }
$ZigTarget = if ($Arch -eq "arm64") { "aarch64-windows" } else { "x86_64-windows" }
$LibName = "ghostty.dll"

Write-Info "Platform: Windows ($RID)"
Write-Info "Target: $ZigTarget"
Write-Info "Library: $LibName"

Push-Location $GhosttyDir

try {
    # Clean if requested
    if ($Clean) {
        Write-Info "Cleaning previous build..."
        if (Test-Path "zig-out") { Remove-Item -Recurse -Force "zig-out" }
        if (Test-Path ".zig-cache") { Remove-Item -Recurse -Force ".zig-cache" }
    }

    # Build
    $optimize = if ($Debug) { "" } else { "-Doptimize=ReleaseFast" }
    Write-Info "Building libghostty shared library..."
    Write-Info "Command: zig build $optimize -Dapp-runtime=none -Dtarget=$ZigTarget"

    $buildArgs = @("build", "-Dapp-runtime=none", "-Dtarget=$ZigTarget")
    if (-not $Debug) { $buildArgs += "-Doptimize=ReleaseFast" }

    & zig @buildArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Err "Zig build failed."
        Write-Warn "Ghostty does not currently support Windows builds upstream."
        exit 1
    }

    # Locate built library
    $builtLib = Join-Path "zig-out\lib" $LibName
    if (-not (Test-Path $builtLib)) {
        $found = Get-ChildItem -Path "zig-out" -Recurse -Filter $LibName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            $builtLib = $found.FullName
        } else {
            Write-Warn "Library not found. Listing built artifacts:"
            Get-ChildItem -Path "zig-out" -Recurse -Include "*.dll","*.lib","*.a" -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $_" }
            Write-Err "Could not find $LibName"
            exit 1
        }
    }

    Write-Info "Built library: $builtLib"

    # Copy to NuGet directory
    $nativeRuntimeDir = Join-Path $RootDir "src\RoyalTerminal.GhosttySharp.Native.Win64\runtimes\$RID\native"
    New-Item -ItemType Directory -Force -Path $nativeRuntimeDir | Out-Null
    Copy-Item $builtLib $nativeRuntimeDir
    Write-Info "Copied to: $nativeRuntimeDir\$LibName"

    # Copy to output dir
    $outputDir = Join-Path $NativeOutDir $RID
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    Copy-Item $builtLib $outputDir
    Write-Info "Copied to: $outputDir\$LibName"

    # Copy header
    $headerSrc = Join-Path $GhosttyDir "include\ghostty.h"
    $headerDest = Join-Path $NativeOutDir "include"
    if (Test-Path $headerSrc) {
        New-Item -ItemType Directory -Force -Path $headerDest | Out-Null
        Copy-Item $headerSrc $headerDest
        Write-Info "Copied header: $headerDest\ghostty.h"
    }

    # ═══════════════════════════════════════════════════════════════════
    # Build libghostty-terminal (standalone terminal C API library)
    # ═══════════════════════════════════════════════════════════════════

    $terminalDir = Join-Path $RootDir "native\ghostty-terminal"

    if (Test-Path (Join-Path $terminalDir "build.zig")) {
        Write-Info ""
        Write-Info "Building libghostty-terminal..."

        Push-Location $terminalDir
        try {
            if ($Clean) {
                if (Test-Path "zig-out") { Remove-Item -Recurse -Force "zig-out" }
                if (Test-Path ".zig-cache") { Remove-Item -Recurse -Force ".zig-cache" }
            }

            $terminalBuildArgs = @("build", "-Dtarget=$ZigTarget")
            if (-not $Debug) { $terminalBuildArgs += "-Doptimize=ReleaseFast" }

            & zig @terminalBuildArgs
            if ($LASTEXITCODE -eq 0) {
                $terminalLibName = "ghostty-terminal.dll"
                $terminalLib = Join-Path "zig-out\lib" $terminalLibName
                if (Test-Path $terminalLib) {
                    Write-Info "Built: $terminalLib"

                    Copy-Item $terminalLib $nativeRuntimeDir
                    Write-Info "Copied to: $nativeRuntimeDir\$terminalLibName"

                    Copy-Item $terminalLib $outputDir
                    Write-Info "Copied to: $outputDir\$terminalLibName"

                    $terminalHeaderSrc = Join-Path $terminalDir "include\ghostty_terminal.h"
                    if (Test-Path $terminalHeaderSrc) {
                        Copy-Item $terminalHeaderSrc $headerDest
                        Write-Info "Copied header: $headerDest\ghostty_terminal.h"
                    }
                } else {
                    Write-Warn "ghostty-terminal.dll not found in zig-out\lib\"
                }
            } else {
                Write-Warn "libghostty-terminal build failed - skipping."
                Write-Warn "Native VT mode will not be available."
            }
        } finally {
            Pop-Location
        }
    } else {
        Write-Warn "native\ghostty-terminal not found - skipping libghostty-terminal build."
    }

    # ═══════════════════════════════════════════════════════════════════
    # Build ghostty-renderer-capi (renderer interop C API library)
    # ═══════════════════════════════════════════════════════════════════

    $rendererDir = Join-Path $RootDir "native\ghostty-renderer-capi"

    if (Test-Path (Join-Path $rendererDir "build.zig")) {
        Write-Info ""
        Write-Info "Building ghostty-renderer-capi..."

        Push-Location $rendererDir
        try {
            if ($Clean) {
                if (Test-Path "zig-out") { Remove-Item -Recurse -Force "zig-out" }
                if (Test-Path ".zig-cache") { Remove-Item -Recurse -Force ".zig-cache" }
            }

            $rendererBuildArgs = @("build", "-Dtarget=$ZigTarget")
            if (-not $Debug) { $rendererBuildArgs += "-Doptimize=ReleaseFast" }

            & zig @rendererBuildArgs
            if ($LASTEXITCODE -eq 0) {
                $rendererLibName = "ghostty-renderer-capi.dll"
                $rendererLib = Join-Path "zig-out\lib" $rendererLibName
                if (Test-Path $rendererLib) {
                    Write-Info "Built: $rendererLib"

                    Copy-Item $rendererLib $nativeRuntimeDir
                    Write-Info "Copied to: $nativeRuntimeDir\$rendererLibName"

                    Copy-Item $rendererLib $outputDir
                    Write-Info "Copied to: $outputDir\$rendererLibName"

                    $rendererHeaderSrc = Join-Path $rendererDir "include\ghostty_renderer.h"
                    if (Test-Path $rendererHeaderSrc) {
                        Copy-Item $rendererHeaderSrc $headerDest
                        Write-Info "Copied header: $headerDest\ghostty_renderer.h"
                    }
                } else {
                    Write-Warn "ghostty-renderer-capi.dll not found in zig-out\lib\"
                }
            } else {
                Write-Warn "ghostty-renderer-capi build failed - skipping."
                Write-Warn "Texture interop managed APIs will require manual native library setup."
            }
        } finally {
            Pop-Location
        }
    } else {
        Write-Warn "native\ghostty-renderer-capi not found - skipping ghostty-renderer-capi build."
    }

    Write-Info ""
    Write-Info "=== Build Complete ==="
    Write-Info "Library: $builtLib"
    Write-Info ""
    Write-Info "Next steps:"
    Write-Info "  1. Build the .NET solution: dotnet build"
    Write-Info "  2. Run the demo: dotnet run --project samples\RoyalTerminal.Demo"
    Write-Info "  3. Pack NuGet packages: dotnet pack -c Release"

} finally {
    Pop-Location
}
