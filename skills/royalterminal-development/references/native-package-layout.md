# Native Package Layout

## Table Of Contents

- [Native Package Projects](#native-package-projects)
- [Runtime Asset Layout](#runtime-asset-layout)
- [Build-Transitive Copy Targets](#build-transitive-copy-targets)
- [RID And Architecture Selection](#rid-and-architecture-selection)
- [Packaging Validation](#packaging-validation)

## Native Package Projects

Native-only package projects:
- `src/RoyalTerminal.GhosttySharp.Native.OSX/RoyalTerminal.GhosttySharp.Native.OSX.csproj`
- `src/RoyalTerminal.GhosttySharp.Native.Linux64/RoyalTerminal.GhosttySharp.Native.Linux64.csproj`
- `src/RoyalTerminal.GhosttySharp.Native.Win64/RoyalTerminal.GhosttySharp.Native.Win64.csproj`

Shared project characteristics:
- `IncludeBuildOutput=false` (no managed assembly)
- `SuppressDependenciesWhenPacking=true`
- runtime-native assets are packed from `runtimes/<rid>/native/**`

## Runtime Asset Layout

Expected layout in package and project:
- `runtimes/osx-arm64/native/*`
- `runtimes/osx-x64/native/*`
- `runtimes/linux-x64/native/*`
- `runtimes/linux-arm64/native/*`
- `runtimes/win-x64/native/*`
- `runtimes/win-arm64/native/*`

These runtime folders must contain all required native libraries for that RID.

## Build-Transitive Copy Targets

Targets files:
- `src/RoyalTerminal.GhosttySharp.Native.OSX/buildTransitive/RoyalTerminal.GhosttySharp.Native.OSX.targets`
- `src/RoyalTerminal.GhosttySharp.Native.Linux64/buildTransitive/RoyalTerminal.GhosttySharp.Native.Linux64.targets`
- `src/RoyalTerminal.GhosttySharp.Native.Win64/buildTransitive/RoyalTerminal.GhosttySharp.Native.Win64.targets`

Behavior:
- select native files by explicit `RuntimeIdentifier`, or
- if RID is empty, select by host process architecture
- copy selected native files to output with:
  - `CopyToOutputDirectory="PreserveNewest"`
  - linked flat filename in output root

## RID And Architecture Selection

Selection conditions in targets are OS-gated and arch-aware:
- macOS: choose `osx-arm64` or `osx-x64`
- Linux: choose `linux-arm64` or `linux-x64`
- Windows: choose `win-arm64` or `win-x64`

Practical guidance:
- explicitly set `RuntimeIdentifier` for deterministic packaging and publish output
- when RID is omitted, ensure host architecture matches desired output architecture

## Packaging Validation

Validate runtime asset packaging with:
```bash
dotnet pack src/RoyalTerminal.GhosttySharp.Native.OSX/RoyalTerminal.GhosttySharp.Native.OSX.csproj -c Release
```
(Repeat for Linux64/Win64 packages.)

Then inspect package contents:
```bash
unzip -l path/to/*.nupkg | rg "runtimes/.*/native/"
```

Confirm consuming app output contains copied native files after restore/build.

## Code Examples

### Pack native runtime package

```bash
dotnet pack src/RoyalTerminal.GhosttySharp.Native.OSX/RoyalTerminal.GhosttySharp.Native.OSX.csproj -c Release
```

### Inspect package runtime assets

```bash
PKG="src/RoyalTerminal.GhosttySharp.Native.OSX/bin/Release/*.nupkg"
unzip -l $PKG | rg "runtimes/.*/native/"
```

### Force RID during app build

```bash
dotnet build samples/RoyalTerminal.Demo/RoyalTerminal.Demo.csproj -c Release -r osx-arm64
```
