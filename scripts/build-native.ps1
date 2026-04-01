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
#   - Windows symlink support (Developer Mode enabled or elevated shell)
#
# The script builds the Ghostty shared library plus the official ghostty-vt API
# library, and also builds the ghostty-renderer-capi shared library for texture
# interop. Artifacts are copied to the correct NuGet runtime package location.
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
$ZigGlobalCacheDir = Join-Path $RootDir ".zig-global-cache"

function Write-Info  { Write-Host "[INFO] $args" -ForegroundColor Green }
function Write-Warn  { Write-Host "[WARN] $args" -ForegroundColor Yellow }
function Write-Err   { Write-Host "[ERROR] $args" -ForegroundColor Red }

function Test-IsElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-DeveloperModeEnabled {
    try {
        $unlock = Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" `
            -Name "AllowDevelopmentWithoutDevLicense" `
            -ErrorAction Stop
        return $unlock.AllowDevelopmentWithoutDevLicense -eq 1
    } catch {
        return $false
    }
}

function Test-SymlinkCreation {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    $probeDir = Join-Path $RootPath ".rt-symlink-probe"
    $targetPath = Join-Path $probeDir "target.txt"
    $linkPath = Join-Path $probeDir "link.txt"

    try {
        New-Item -ItemType Directory -Path $probeDir -Force | Out-Null
        Set-Content -Path $targetPath -Value "probe" -NoNewline -Encoding Ascii
        New-Item -ItemType SymbolicLink -Path $linkPath -Target $targetPath -ErrorAction Stop | Out-Null
        return $true
    } catch {
        return $false
    } finally {
        if (Test-Path $probeDir) {
            Remove-Item -Path $probeDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-ZigCacheFileNotFoundPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$OutputLines
    )

    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($line in $OutputLines) {
        if ($line -match "unable to open '([^']+)': FileNotFound") {
            $paths.Add($Matches[1])
            continue
        }

        if ($line -match "failed to check cache: '([^']+)' file_hash FileNotFound") {
            $paths.Add($Matches[1])
        }
    }

    return $paths | Select-Object -Unique
}

function Invoke-ZigBuildWithCacheRecovery {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Args
    )

    $maxAttempts = 3
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        $rawOutput = & zig @Args 2>&1
        $outputLines = @($rawOutput | ForEach-Object { [string]$_ })
        if ($outputLines.Count -gt 0) {
            $outputLines | ForEach-Object { Write-Host $_ }
        }

        if ($LASTEXITCODE -eq 0) {
            return $true
        }

        $missingCachePaths = Get-ZigCacheFileNotFoundPaths -OutputLines $outputLines
        if ($missingCachePaths.Count -gt 0) {
            if ($attempt -lt $maxAttempts) {
                Write-Warn "Detected Zig cache FileNotFound inconsistency. Resetting caches and retrying."

                foreach ($missingPath in $missingCachePaths) {
                    if (Test-Path $missingPath) {
                        Remove-Item -Path $missingPath -Recurse -Force -ErrorAction SilentlyContinue
                    } else {
                        $missingDir = Split-Path -Parent $missingPath
                        if ($missingDir -and (Test-Path $missingDir)) {
                            Remove-Item -Path $missingDir -Recurse -Force -ErrorAction SilentlyContinue
                        }
                    }
                }

                if (Test-Path ".zig-cache") {
                    Remove-Item -Path ".zig-cache" -Recurse -Force -ErrorAction SilentlyContinue
                }
                if (Test-Path "zig-out") {
                    Remove-Item -Path "zig-out" -Recurse -Force -ErrorAction SilentlyContinue
                }
                if (Test-Path $ZigGlobalCacheDir) {
                    Remove-Item -Path $ZigGlobalCacheDir -Recurse -Force -ErrorAction SilentlyContinue
                }
                New-Item -ItemType Directory -Path $ZigGlobalCacheDir -Force | Out-Null

                continue
            }

            Write-Warn "Zig cache FileNotFound persisted after retries."
            Write-Warn "Try: .\scripts\build-native.ps1 -Clean"
            Write-Warn "If it still fails, exclude '$RootDir' and '$ZigGlobalCacheDir' from antivirus real-time scanning."
        }

        return $false
    }

    return $false
}

function Resolve-ZigArtifactPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName
    )

    $candidatePaths = @(
        (Join-Path "zig-out\bin" $FileName),
        (Join-Path "zig-out\lib" $FileName)
    )

    foreach ($candidate in $candidatePaths) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    $found = Get-ChildItem -Path "zig-out" -Recurse -Filter $FileName -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($found) {
        return $found.FullName
    }

    return $null
}

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
  - Windows symlink support:
    - Enable Developer Mode (start ms-settings:developers)
    - or run PowerShell as Administrator

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

if (-not (Test-SymlinkCreation -RootPath $GhosttyDir)) {
    $isElevated = Test-IsElevated
    $developerModeEnabled = Test-DeveloperModeEnabled

    Write-Err "Windows symbolic link creation is not available in this shell."
    Write-Warn "Zig cannot unpack Ghostty's libxml2 dependency without symlink support."
    Write-Host ""
    Write-Host "Fix one of the following, then re-run this script:"
    if (-not $developerModeEnabled) {
        Write-Host "  1. Enable Developer Mode: start ms-settings:developers"
    } else {
        Write-Host "  1. Developer Mode is enabled."
    }
    if (-not $isElevated) {
        Write-Host "  2. Run PowerShell as Administrator"
    } else {
        Write-Host "  2. Current shell is elevated."
    }
    Write-Host "  3. Ensure your account has the 'Create symbolic links' privilege"
    Write-Host ""
    Write-Warn "Ghostty does not currently support Windows builds upstream."
    exit 1
}

$previousZigGlobalCacheDir = $env:ZIG_GLOBAL_CACHE_DIR
$env:ZIG_GLOBAL_CACHE_DIR = $ZigGlobalCacheDir
New-Item -ItemType Directory -Force -Path $ZigGlobalCacheDir | Out-Null
Write-Info "Zig global cache: $ZigGlobalCacheDir"

Push-Location $GhosttyDir

try {
    # Clean if requested
    if ($Clean) {
        Write-Info "Cleaning previous build..."
        if (Test-Path "zig-out") { Remove-Item -Recurse -Force "zig-out" }
        if (Test-Path ".zig-cache") { Remove-Item -Recurse -Force ".zig-cache" }
        if (Test-Path $ZigGlobalCacheDir) { Remove-Item -Recurse -Force $ZigGlobalCacheDir }
        New-Item -ItemType Directory -Force -Path $ZigGlobalCacheDir | Out-Null
    }

    # Build
    $optimize = if ($Debug) { "" } else { "-Doptimize=ReleaseFast" }
    Write-Info "Building libghostty shared library..."
    Write-Info "Command: zig build $optimize -Dapp-runtime=none -Dtarget=$ZigTarget"

    $buildArgs = @("build", "-Dapp-runtime=none", "-Dtarget=$ZigTarget")
    if (-not $Debug) { $buildArgs += "-Doptimize=ReleaseFast" }

    if (-not (Invoke-ZigBuildWithCacheRecovery -Args $buildArgs)) {
        Write-Err "Zig build failed."
        Write-Warn "Ghostty does not currently support Windows builds upstream."
        exit 1
    }

    # Locate built library
    $builtLib = Resolve-ZigArtifactPath -FileName $LibName
    if (-not $builtLib) {
        Write-Warn "Library not found. Listing built artifacts:"
        Get-ChildItem -Path "zig-out" -Recurse -Include "*.dll","*.lib","*.a" -File -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $_" }
        Write-Err "Could not find $LibName"
        exit 1
    }

    Write-Info "Built library: $builtLib"

    $vtLibName = "ghostty-vt.dll"
    $vtLib = Resolve-ZigArtifactPath -FileName $vtLibName
    if ($vtLib) {
        Write-Info "Built official VT library: $vtLib"
    } else {
        Write-Warn "Official ghostty-vt.dll artifact not found under zig-out\."
        Write-Warn "Managed wrappers for GhosttyTerminal/GhosttyRenderState will require manual native library setup."
    }

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

    if ($vtLib) {
        Copy-Item $vtLib (Join-Path $nativeRuntimeDir $vtLibName)
        Write-Info "Copied to: $nativeRuntimeDir\$vtLibName"

        Copy-Item $vtLib (Join-Path $outputDir $vtLibName)
        Write-Info "Copied to: $outputDir\$vtLibName"
    }

    # Copy header
    $headerSrc = Join-Path $GhosttyDir "include\ghostty.h"
    $headerDest = Join-Path $NativeOutDir "include"
    if (Test-Path $headerSrc) {
        New-Item -ItemType Directory -Force -Path $headerDest | Out-Null
        Copy-Item $headerSrc $headerDest
        Write-Info "Copied header: $headerDest\ghostty.h"
    }

    $vtHeadersSrc = Join-Path $GhosttyDir "include\ghostty\vt"
    if (Test-Path $vtHeadersSrc) {
        $ghosttyHeaderDest = Join-Path $headerDest "ghostty"
        $vtHeaderDest = Join-Path $ghosttyHeaderDest "vt"
        New-Item -ItemType Directory -Force -Path $ghosttyHeaderDest | Out-Null
        if (Test-Path $vtHeaderDest) {
            Remove-Item -Path $vtHeaderDest -Recurse -Force
        }

        Copy-Item $vtHeadersSrc $ghosttyHeaderDest -Recurse
        Write-Info "Copied official VT headers: $vtHeaderDest"
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

            if (Invoke-ZigBuildWithCacheRecovery -Args $rendererBuildArgs) {
                $rendererLibName = "ghostty-renderer-capi.dll"
                $rendererLib = Resolve-ZigArtifactPath -FileName $rendererLibName
                if ($rendererLib) {
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
                    Write-Warn "ghostty-renderer-capi.dll not found under zig-out\ (bin/lib)."
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
    if ($null -ne $previousZigGlobalCacheDir -and $previousZigGlobalCacheDir -ne "") {
        $env:ZIG_GLOBAL_CACHE_DIR = $previousZigGlobalCacheDir
    } else {
        Remove-Item Env:ZIG_GLOBAL_CACHE_DIR -ErrorAction SilentlyContinue
    }
}
