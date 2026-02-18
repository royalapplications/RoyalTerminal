# Modes Catalog

This catalog is the exhaustive enum/mode selector reference for repository scope (`src/` + `samples/`).

## Table Of Contents

- [Scope](#scope)
- [Primary Runtime Modes](#primary-runtime-modes)
- [Input And Interaction Modes](#input-and-interaction-modes)
- [Rendering And Interop Modes](#rendering-and-interop-modes)
- [Security And Secret Modes](#security-and-secret-modes)
- [Ghostty Native API Mode Families](#ghostty-native-api-mode-families)
- [Full Enum Inventory](#full-enum-inventory)
- [Primary Resolver Files](#primary-resolver-files)

## Scope

Included in this catalog:
- all mode-bearing enums used by runtime behavior
- all selector constants used as mode IDs in UI/runtime
- full enum inventory in `src/` + `samples/` for comprehensive reference

## Primary Runtime Modes

### Integration Render Mode

Enum: `TerminalRenderMode` (demo internal)
- file: `samples/RoyalTerminal.Demo/Services/TerminalModeResolver.cs`
- values:
  - `GhosttyRendered = 0`
  - `GhosttyNative = 1`
  - `NativeVt = 2`
  - `ManagedVt = 3`
  - `RenderedAuto = 4`

Cycle order:
- `GhosttyRendered -> GhosttyNative -> NativeVt -> ManagedVt -> RenderedAuto`

### VT Processor Preference

Enum: `VtProcessorPreference`
- file: `src/RoyalTerminal.Terminal/Terminal/VtProcessorPreference.cs`
- values:
  - `Auto = 0`
  - `Managed = 1`
  - `Native = 2`

### Ghostty Rendered Control Mode

Enum: `GhosttyRenderedTerminalRenderingMode`
- file: `src/RoyalTerminal.Avalonia.Ghostty/Controls/Common/GhosttyRenderedTerminalRenderingMode.cs`
- values:
  - `CpuCellRenderer = 0`
  - `TextureInterop = 1`

### Transport Mode Selectors (constant IDs)

Static IDs in `TerminalTransportIds`:
- file: `src/RoyalTerminal.Terminal/Terminal/TransportOptions.cs`
- values:
  - `pty`
  - `pipe`
  - `ssh`

### SSH Auth Mode Selectors (demo IDs)

Selector IDs in `SshAuthModeOption`:
- file: `samples/RoyalTerminal.Demo/ViewModels/MainWindowViewModel.cs`
- values:
  - `password`
  - `private-key`
  - `agent`
  - `password-key`

## Input And Interaction Modes

### Terminal mouse reporting modes

Enum: `TerminalMouseTrackingMode`
- file: `src/RoyalTerminal.Terminal/Terminal/TerminalMouseModeState.cs`
- values:
  - `None`
  - `X10Press`
  - `PressRelease`
  - `ButtonMotion`
  - `AnyMotion`

Enum: `TerminalMouseEncoding`
- file: `src/RoyalTerminal.Terminal/Terminal/TerminalMouseModeState.cs`
- values:
  - `Default`
  - `Utf8`
  - `Sgr`
  - `Urxvt`

### Terminal paste safety modes

Enums in `src/RoyalTerminal.Avalonia/Services/TerminalPasteContracts.cs`:
- `TerminalPasteSafetyPolicy`
- `TerminalPasteRisk`
- `TerminalPasteSafetyDecision`

### Endpoint/input event mode enums

Enums in `src/RoyalTerminal.Terminal/Terminal/TerminalEndpointContracts.cs`:
- `TerminalInputAction`
- `TerminalPointerEventKind`
- `TerminalMouseButton`
- `TerminalModifiers`

## Rendering And Interop Modes

Core rendering-related mode enums:
- `AvaloniaRenderBackendPreference`
- `RenderBackendKind`
- `RenderTargetKind`
- `RenderPixelFormat`
- `RenderFeatureFlags`
- `GhosttyRenderInteropResult`
- `CursorStyle`
- `TextDirectionMode`

Primary files:
- `src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/AvaloniaRenderBackendPreference.cs`
- `src/RoyalTerminal.Rendering.Contracts/Enums/RenderBackendKind.cs`
- `src/RoyalTerminal.Rendering.Contracts/Enums/RenderTargetKind.cs`
- `src/RoyalTerminal.Rendering.Contracts/Enums/RenderPixelFormat.cs`
- `src/RoyalTerminal.Rendering.Contracts/Enums/RenderFeatureFlags.cs`
- `src/RoyalTerminal.Rendering.Interop.Ghostty/Interop/GhosttyRenderInteropResult.cs`
- `src/RoyalTerminal.Rendering.Skia/Rendering/SkiaTerminalRenderer.cs`
- `src/RoyalTerminal.Rendering.Text/TextShaping/HarfBuzzTextShaper.cs`

## Security And Secret Modes

Enum:
- `DpapiSshSecretScope`
- file: `src/RoyalTerminal.Terminal/Terminal/SshCredentialProviders.cs`
- values:
  - `CurrentUser`
  - `LocalMachine`

## Ghostty Native API Mode Families

Large mode families exist in:
- `src/RoyalTerminal.GhosttySharp/Native/Enums.cs`
- `src/RoyalTerminal.GhosttySharp/Native/GhosttyVtNative.cs`

Important runtime-facing examples:
- `GhosttyBuildMode`
- `GhosttyCloseTabMode`
- `GhosttyColorScheme`
- `GhosttyRendererHealth`
- `GhosttyVtKeyAction`
- `GhosttyOscCommandType`
- `GhosttySgrAttributeTag`

These enums are exhaustive bindings to native API surfaces and are referenced by interop wrappers and tests.

## Full Enum Inventory

The following table lists every enum found under `src/` + `samples/` in this repository.

| Enum | File |
|---|---|
| `TerminalRenderMode` | `samples/RoyalTerminal.Demo/Services/TerminalModeResolver.cs` |
| `AvaloniaRenderBackendPreference` | `src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/AvaloniaRenderBackendPreference.cs` |
| `GhosttyRenderedTerminalRenderingMode` | `src/RoyalTerminal.Avalonia.Ghostty/Controls/Common/GhosttyRenderedTerminalRenderingMode.cs` |
| `GhosttyLogLevel` | `src/RoyalTerminal.Avalonia.Ghostty/Diagnostics/GhosttyLogLevel.cs` |
| `TerminalPasteSafetyPolicy` | `src/RoyalTerminal.Avalonia/Services/TerminalPasteContracts.cs` |
| `TerminalPasteRisk` | `src/RoyalTerminal.Avalonia/Services/TerminalPasteContracts.cs` |
| `TerminalPasteSafetyDecision` | `src/RoyalTerminal.Avalonia/Services/TerminalPasteContracts.cs` |
| `GhosttyPlatform` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyClipboard` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyClipboardRequest` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyMouseState` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyMouseButton` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyMouseMomentum` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyColorScheme` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyMods` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyBindingFlags` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyInputAction` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyKey` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyInputTriggerTag` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyBuildMode` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyPointTag` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyPointCoord` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttySurfaceContext` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyTargetTag` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttySplitDirection` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyGotoSplit` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyGotoWindow` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyResizeSplitDirection` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyGotoTab` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyFullscreen` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyFloatWindow` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttySecureInput` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyInspectorAction` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyQuitTimer` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyReadonly` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyPromptTitle` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyMouseShape` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyMouseVisibility` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyRendererHealth` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyColorKind` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyOpenUrlKind` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyCloseTabMode` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyProgressState` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyQuickTerminalSizeTag` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyKeyTableTag` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyActionTag` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyIpcTargetTag` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyIpcActionTag` | `src/RoyalTerminal.GhosttySharp/Native/Enums.cs` |
| `GhosttyResult` | `src/RoyalTerminal.GhosttySharp/Native/GhosttyVtNative.cs` |
| `GhosttyVtKeyAction` | `src/RoyalTerminal.GhosttySharp/Native/GhosttyVtNative.cs` |
| `GhosttyVtKey` | `src/RoyalTerminal.GhosttySharp/Native/GhosttyVtNative.cs` |
| `GhosttyVtMods` | `src/RoyalTerminal.GhosttySharp/Native/GhosttyVtNative.cs` |
| `GhosttyOscCommandType` | `src/RoyalTerminal.GhosttySharp/Native/GhosttyVtNative.cs` |
| `GhosttyOscCommandData` | `src/RoyalTerminal.GhosttySharp/Native/GhosttyVtNative.cs` |
| `GhosttySgrAttributeTag` | `src/RoyalTerminal.GhosttySharp/Native/GhosttyVtNative.cs` |
| `RenderBackendKind` | `src/RoyalTerminal.Rendering.Contracts/Enums/RenderBackendKind.cs` |
| `RenderTargetKind` | `src/RoyalTerminal.Rendering.Contracts/Enums/RenderTargetKind.cs` |
| `RenderPixelFormat` | `src/RoyalTerminal.Rendering.Contracts/Enums/RenderPixelFormat.cs` |
| `RenderFeatureFlags` | `src/RoyalTerminal.Rendering.Contracts/Enums/RenderFeatureFlags.cs` |
| `GhosttyRenderInteropResult` | `src/RoyalTerminal.Rendering.Interop.Ghostty/Interop/GhosttyRenderInteropResult.cs` |
| `CursorStyle` | `src/RoyalTerminal.Rendering.Skia/Rendering/SkiaTerminalRenderer.cs` |
| `TextDirectionMode` | `src/RoyalTerminal.Rendering.Text/TextShaping/HarfBuzzTextShaper.cs` |
| `CellAttributes` | `src/RoyalTerminal.Terminal/Rendering/TerminalCell.cs` |
| `TerminalInputAction` | `src/RoyalTerminal.Terminal/Terminal/TerminalEndpointContracts.cs` |
| `TerminalPointerEventKind` | `src/RoyalTerminal.Terminal/Terminal/TerminalEndpointContracts.cs` |
| `TerminalMouseButton` | `src/RoyalTerminal.Terminal/Terminal/TerminalEndpointContracts.cs` |
| `TerminalModifiers` | `src/RoyalTerminal.Terminal/Terminal/TerminalEndpointContracts.cs` |
| `TerminalMouseTrackingMode` | `src/RoyalTerminal.Terminal/Terminal/TerminalMouseModeState.cs` |
| `TerminalMouseEncoding` | `src/RoyalTerminal.Terminal/Terminal/TerminalMouseModeState.cs` |
| `VtProcessorPreference` | `src/RoyalTerminal.Terminal/Terminal/VtProcessorPreference.cs` |
| `DpapiSshSecretScope` | `src/RoyalTerminal.Terminal/Terminal/SshCredentialProviders.cs` |
| `EastAsianWidthClass` | `src/RoyalTerminal.Unicode/Unicode/EastAsianWidthClass.cs` |
| `GraphemeBreakClass` | `src/RoyalTerminal.Unicode/Unicode/GraphemeBreakClass.cs` |

## Primary Resolver Files

Use these files when mode behavior changes:
- `samples/RoyalTerminal.Demo/Services/TerminalModeResolver.cs`
- `samples/RoyalTerminal.Demo/Services/MainWindowController.cs`
- `samples/RoyalTerminal.Demo/ViewModels/MainWindowViewModel.cs`
- `src/RoyalTerminal.Terminal/Terminal/VtProcessorPreference.cs`
- `src/RoyalTerminal.Terminal/Terminal/TerminalMouseModeState.cs`
- `src/RoyalTerminal.Avalonia.Ghostty/Controls/Common/GhosttyRenderedTerminalRenderingMode.cs`
- `src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/AvaloniaRenderBackendPreference.cs`

## Code Examples

### Resolve supported mode with capability fallback

```csharp
TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(
    embeddedGhosttyAvailable: false,
    nativeVtAvailable: true);

TerminalRenderMode resolved = TerminalModeResolver.Default.ResolveSupportedMode(
    TerminalRenderMode.GhosttyRendered,
    capabilities);

// resolved => NativeVt (or ManagedVt if NativeVt unavailable)
```

### Cycle to next available mode

```csharp
TerminalRenderMode next = TerminalModeResolver.Default.ResolveNextMode(
    currentMode: TerminalRenderMode.NativeVt,
    capabilities: capabilities);
```

### Select transport mode by ID

```csharp
TransportModeOption selected = new(TerminalTransportIds.Ssh, "SSH");
_viewModel.SelectedTransportMode = selected;
```

### Mode-aware mouse state snapshot

```csharp
TerminalMouseModeState state = new(
    TrackingMode: TerminalMouseTrackingMode.ButtonMotion,
    Encoding: TerminalMouseEncoding.Sgr);

if (state.IsMouseReportingEnabled && state.ReportsMotion)
{
    // Send pointer move events to terminal app.
}
```
