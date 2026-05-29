---
title: Windows x64 Native Compatibility
---

# Windows x64 Native Compatibility

RoyalTerminal's Windows x64 native package must run on the baseline x64
instruction set. That includes older x64 CPUs, virtual machines where the
hypervisor masks AVX, and Windows ARM64 x64 emulation.

## Compatibility Goal

The Windows x64 native DLLs are distributed as general-purpose runtime assets.
They must not assume AVX, AVX2, FMA, BMI, or AVX-512 at process startup.

This matters because a machine can be x64-compatible while still not exposing
AVX to the process. Common examples include older x64 hardware, virtual
machines with restricted CPU feature masks, and x64 emulation on Windows ARM64.
If startup code contains an unsupported instruction, Windows terminates the
process before RoyalTerminal can fall back to another terminal engine.

## Compatibility Model

Ghostty exposes `-Dsimd=false` in its Zig build. Its CMake integration documents
that downstream builds can pass `ZIG_FLAGS -Dsimd=false`, and the Ghostty build
graph only links simdutf, Highway, and Ghostty's SIMD C++ files when `simd` is
enabled.

The safe model for optimized native code is runtime dispatch: baseline startup
code loads everywhere, checks CPU capabilities, and only then calls specialized
AVX or AVX2 paths. Windows Terminal follows that model for AVX2 hot paths by
gating them behind runtime CPU feature checks.

RoyalTerminal currently publishes a single Windows x64 native `ghostty-vt.dll`.
Until the VT DLL can cleanly separate baseline startup code from
runtime-dispatched optimized code, RoyalTerminal publishes the scalar
compatibility build for Windows x64.

## Build Policy

Windows x64 release artifacts use:

```text
-Dtarget=x86_64-windows-msvc
-Dcpu=x86_64-vzeroupper
-Dsimd=false
```

The CPU value keeps Zig on the baseline x64 model and removes the `vzeroupper`
feature. `-Dsimd=false` prevents Ghostty's SIMD bundle from adding simdutf,
Highway, and AVX-capable C++ objects to the compatibility DLL.

The renderer interop DLL also uses:

```text
-Dtarget=x86_64-windows-msvc
-Dcpu=x86_64-vzeroupper
```

It does not use Ghostty's `simd` option.

## Build Commands

Build the Windows x64 native artifacts:

```powershell
.\scripts\build-native.ps1 -Arch x64 -Release
```

Direct Ghostty build from `external/ghostty`:

```powershell
zig build -Doptimize=ReleaseFast -Dapp-runtime=none -Dtarget=x86_64-windows-msvc -Dcpu=x86_64-vzeroupper -Dsimd=false
```

Verify a DLL with disassembly:

```powershell
.\scripts\verify-windows-x64-no-avx.ps1 -Path .\src\RoyalTerminal.GhosttySharp.Native.Win64\runtimes\win-x64\native\ghostty-vt.dll
```

The verification fails when it finds AVX/VEX mnemonics or `ymm`/`zmm`
registers. The script requires `llvm-objdump` on `PATH`, in a standard LLVM
install location, or passed explicitly with `-ObjdumpPath`.

## Release Invariant

The release workflow disassembles Windows x64 native artifacts after building
them. A Windows x64 release must not publish if `ghostty-vt.dll` or
`ghostty-renderer-capi.dll` contains AVX/VEX instructions.

The verification checks the full DLL instead of a single exported function.
That prevents regressions where unsupported instructions move to another startup
or initialization path.
