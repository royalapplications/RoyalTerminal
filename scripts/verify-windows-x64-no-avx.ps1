# verify-windows-x64-no-avx.ps1 - Fail when Windows x64 native DLLs contain AVX/VEX instructions.

param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,

    [string]$ObjdumpPath
)

$ErrorActionPreference = "Stop"

function Write-Info {
    Write-Host "[INFO] $args" -ForegroundColor Green
}

function Write-Err {
    Write-Host "[ERROR] $args" -ForegroundColor Red
}

function Resolve-LlvmObjdump {
    param([string]$ExplicitPath)

    if ($ExplicitPath) {
        if (Test-Path $ExplicitPath) {
            return (Resolve-Path $ExplicitPath).Path
        }

        throw "Requested llvm-objdump path does not exist: $ExplicitPath"
    }

    $command = Get-Command llvm-objdump -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidatePaths = @(
        $(if ($env:ProgramFiles) { Join-Path $env:ProgramFiles "LLVM\bin\llvm-objdump.exe" }),
        $(if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} "LLVM\bin\llvm-objdump.exe" }),
        "/opt/homebrew/opt/llvm/bin/llvm-objdump",
        "/opt/homebrew/opt/llvm@20/bin/llvm-objdump",
        "/usr/local/opt/llvm/bin/llvm-objdump",
        "/usr/local/opt/llvm@20/bin/llvm-objdump",
        "/usr/bin/llvm-objdump"
    )

    foreach ($candidate in $candidatePaths) {
        if ($candidate -and (Test-Path $candidate)) {
            return (Resolve-Path $candidate).Path
        }
    }

    return $null
}

function Test-NativeImageForAvx {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ImagePath,

        [Parameter(Mandatory = $true)]
        [string]$Objdump
    )

    if (-not (Test-Path $ImagePath)) {
        throw "Native image does not exist: $ImagePath"
    }

    Write-Info "Checking $ImagePath for AVX/VEX instructions..."

    $disassembly = & $Objdump -d --no-show-raw-insn $ImagePath 2>&1
    if ($LASTEXITCODE -ne 0) {
        $disassembly | ForEach-Object { Write-Host $_ }
        throw "llvm-objdump failed for $ImagePath."
    }

    $simdRegisterPattern = '\b%?(?:ymm|zmm)[0-9]+\b'
    $matches = @(
        $disassembly |
            Where-Object {
                $isInstruction = $_ -match '^\s*[0-9a-fA-F]+:\s*(?<instruction>.*)$'
                if ($isInstruction) {
                    $instruction = $Matches["instruction"].Trim()
                    $parts = $instruction -split '\s+', 2
                    $mnemonic = $parts[0]
                    $operands = if ($parts.Count -gt 1) { $parts[1] } else { "" }
                    $operandsWithoutSymbols = $operands -replace '<[^>]*>', ''

                    ($mnemonic -match '^v[a-z][a-z0-9_.]*$') -or
                        ($operandsWithoutSymbols -match $simdRegisterPattern)
                } else {
                    $false
                }
            } |
            Select-Object -First 30
    )

    if ($matches.Count -gt 0) {
        Write-Err "Found AVX/VEX instructions in $ImagePath."
        $matches | ForEach-Object { Write-Host $_ }
        throw "Windows x64 baseline native artifacts must not contain AVX/VEX instructions."
    }

    Write-Info "No AVX/VEX instructions found in $ImagePath."
}

$objdump = Resolve-LlvmObjdump -ExplicitPath $ObjdumpPath
if (-not $objdump) {
    $message = "llvm-objdump was not found. Install LLVM or pass -ObjdumpPath to verify Windows x64 native artifacts."
    Write-Err $message
    exit 1
}

Write-Info "Using llvm-objdump: $objdump"

foreach ($image in $Path) {
    Test-NativeImageForAvx -ImagePath $image -Objdump $objdump
}
