---
title: Ghostty Integration
---

# Ghostty Integration

RoyalTerminal uses Ghostty in two different ways: as a native VT engine with renderer interoperability, and as a public wrapper library for hosts that want direct access to the Ghostty C ABI from .NET. The second role is what `RoyalTerminal.GhosttySharp` exists for.

Both VT engines use RoyalTerminal's shared Avalonia/Skia host. The renderer bridge
does not embed Ghostty's complete Metal/OpenGL terminal renderer, and the managed
VT engine does not claim exhaustive native or standalone-application parity.
The [generated ABI inventory](../specs/ghostty-abi-inventory-2026.md) documents
the pinned native type and callback bindings; regenerate it with
`scripts/audit-ghostty-abi.py` after a dependency update.

## Font thickening

`TerminalControl.FontThicken` enables macOS CoreText font smoothing for terminal
text and IME preedit, for either VT engine. `FontThickenStrength` ranges from 0
(lightest smoothing) to 255 (strongest); it has no effect while thickening is
disabled. These values are also available as `Thicken` and `ThickenStrength` in
`TerminalFontRenderingSettings` and are persisted in appearance profiles.

This follows Ghostty's macOS smoothing path: grayscale CoreText masks, linear-gray
color space, a strength-controlled gray drawing color, and padded glyph bounds.
It is independent of the existing `FontEmbolden` synthetic-bold setting. Shaping,
cell advances and colors remain controlled by RoyalTerminal. Color-font glyphs
retain the Skia renderer, and other operating systems retain normal rendering,
matching Ghostty's platform support for this setting. Glyph and font caches are
bounded, invalidated when font settings change, and disposed with the renderer.

## Kitty drag and drop

Both VT adapters implement `ITerminalDragDropTarget`. A registered OSC 72 client
receives drag movement/leave and drop messages, then requests MIME representations
by index. Chunked registration and acceptance, multiplexer IDs, base64 data chunks,
completion markers and BEL/ST responses follow the pinned Ghostty state machine.
Registration survives RIS; new sessions unregister. Held data is released on
conclusion, a new drag, cancellation, unregister or processor disposal.

Avalonia's composed drop behavior advertises plain text, file URI lists and
supported typed platform MIME data. It captures representations only at drop time,
without opening files or retrieving remote contents. The host offers copy only:
its OS drag session finishes before the client concludes the asynchronous transfer,
so it cannot safely promise a source-file move. The core contract still encodes
copy/move operations for custom hosts. Unregistered drops remain available to the
embedding application's handlers; no shell commands or unsolicited paste are generated.

Drops are bounded to 16 representations and 64 MiB. Like the pinned Ghostty core,
remote transfer requests receive `EINVAL`, and unsupported drag-out receives
`EPERM`; neither capability is advertised. Native host hooks are repository-owned
extensions, not new upstream public C APIs. The native boundary copies all retained
data and serializes with normal terminal mutations.

## Ghostty-compatible shaders

RoyalTerminal also supports a Ghostty/Shadertoy-style shader compatibility mode in the managed Skia renderer. This is intentionally separate from the native Ghostty VT binding and from Ghostty renderer interop.

Use `TerminalShaderLanguage.GhosttyShadertoy` when you have a single-pass `mainImage` shader that samples `iChannel0`:

```csharp
using RoyalTerminal.Shaders;

Terminal.ShaderSources =
[
    new TerminalShaderSource(
        "Ghostty Compatible Shader",
        ghosttyStyleSource,
        TerminalShaderLanguage.GhosttyShadertoy)
];
```

The shader runs as a RoyalTerminal framebuffer post-process effect, so it works regardless of whether the active VT engine is managed or Ghostty-backed. Native Ghostty renderer `custom-shader` injection is not part of the current interop path.

See [Ghostty/Shadertoy Shader Compatibility](/articles/shaders-ghostty-shadertoy) for the supported uniforms, source shape, and limitations.

## Start with the high-level wrappers

If you want Ghostty behavior inside RoyalTerminal, you usually begin with the managed wrapper types instead of the raw ABI mirror.

| Type | Purpose |
| --- | --- |
| `GhosttyTerminal` | Managed lifetime wrapper for the native terminal. |
| `GhosttyRenderState` | Managed wrapper for render-state extraction from the native terminal. |
| `GhosttyFormatterScreenOptions` | Screen-level formatter options. |
| `GhosttyFormatterExtraOptions` | Formatter extras for palette, modes, tabstops, keyboard state, and screen details. |
| `GhosttyFormatterOptions` | High-level formatter request model. |
| `GhosttyFormatter` | Formatter for exporting terminal state. |
| `GhosttyKeyEncoder` | Native-backed key encoder. |
| `GhosttyKeyEvent` | Mutable key event payload. |
| `GhosttyMouseEncoder` | Native-backed mouse encoder. |
| `GhosttyMouseEvent` | Mutable mouse event payload. |
| `GhosttyPaste` | Static helper for paste encoding. |
| `GhosttySelection` | Managed selection value used by formatters and helpers. |
| `GhosttySelectionGesture` | Owned state machine for native text-selection gestures. |
| `GhosttySelectionGestureEvent` | Reusable selection pointer/click event. |
| `GhosttyTrackedGridReference` | Owned reference that follows a cell through scroll, pruning, and reflow. |
| `GhosttyColorUtilities` | Native-backed color parsing, palettes, color math, X11 names, and color-scheme reports. |
| `GhosttyUnicode` | Ghostty-exact codepoint and grapheme width helpers. |
| `GhosttyKittyGraphics` | Managed helper for Kitty graphics extraction. |
| `GhosttyKittyGraphicsImage` | Managed Kitty image snapshot. |
| `GhosttyKittyGraphicsPlacementIterator` | Managed iterator over Kitty placements. |
| `GhosttySys` | Static system helper surface. |
| `GhosttyVtHelpers` | Static helper surface for protocol encoders and build info. |
| `GhosttyBuildInfoSnapshot` | Full native build metadata snapshot. |
| `GhosttyBuildFeatures` | Compact native build capability snapshot. |
| `TerminalBuffer` | Managed helper for reading terminal content. |
| `TerminalDataProcessor` | Static helper for processing terminal data through Ghostty-backed models. |
| `NativeLibraryLoader` | Native library loader for `libghostty-vt`. |

These are the types used by RoyalTerminal itself when it wants native VT behavior without forcing consumers to work directly against raw pointers and C structs.

## Engine-neutral effects and Unicode

Hosts that can use either VT engine should query the terminal contracts instead
of depending directly on Ghostty types:

| Contract | Capability |
| --- | --- |
| `ITerminalEffectSource` | Clipboard writes, desktop notifications, progress reports, and working-directory changes. |
| `ITerminalUnicodeWidthProvider` | Codepoint width and first-grapheme width using the active engine's rules. |

`GhosttyVtProcessor` implements these contracts with the normalized
libghostty callbacks and Ghostty Unicode tables. `BasicVtProcessor` implements
the same contracts with managed OSC parsing and RoyalTerminal Unicode tables.
The effect callbacks are policy boundaries: applications still decide whether
clipboard writes and desktop notifications are allowed.

## The raw VT mirror is also public

Under those wrappers, `GhosttyVtNative` exposes the Ghostty VT ABI directly. This is not the right layer for most applications, but it is the right layer for advanced interop, diagnostics, or custom wrappers.

At the current Ghostty revision, `TerminalNew` takes `columns` and `rows`
directly. Configure the returned terminal through `TerminalSet`; the former
`GhosttyTerminalOptions` constructor struct no longer exists.

### Root VT exports

`GhosttyVtNative`, `GhosttyResult`, `GhosttyVtKeyAction`, `GhosttyVtKey`, `GhosttyVtMods`, `GhosttyOscCommandType`, `GhosttyOscCommandData`, `GhosttySgrAttributeTag`, `GhosttySgrUnderline`, `GhosttySgrUnknown`, `GhosttySgrAttributeValue`, `GhosttySgrAttribute`, `GhosttyColorRgb`

### Core protocol and build-info exports

`GhosttyString`, `GhosttyMode`, `GhosttyModeReportState`, `GhosttyOptimizeMode`, `GhosttyBuildInfoData`, `GhosttyFocusEvent`, `GhosttySizeReportStyle`, `GhosttySizeReportSize`, `GhosttyDeviceAttributesPrimary`, `GhosttyDeviceAttributesSecondary`, `GhosttyDeviceAttributesTertiary`, `GhosttyDeviceAttributes`, `GhosttyPointCoordinate`, `GhosttyPointTag`, `GhosttyPoint`

### Formatter exports

`GhosttyFormatterFormat`, `GhosttyFormatterScreenExtra`, `GhosttyFormatterTerminalExtra`, `GhosttyFormatterTerminalOptions`

### Input and pointer exports

`GhosttyKittyKeyFlags`, `GhosttyOptionAsAlt`, `GhosttyKeyEncoderOption`, `GhosttyMouseAction`, `GhosttyMouseButtonId`, `GhosttyMousePosition`, `GhosttyMouseTrackingMode`, `GhosttyMouseFormat`, `GhosttyMouseEncoderSize`, `GhosttyMouseEncoderOption`

### Kitty graphics exports

`GhosttyKittyGraphicsData`, `GhosttyKittyGraphicsPlacementData`, `GhosttyKittyPlacementLayer`, `GhosttyKittyGraphicsPlacementIteratorOption`, `GhosttyKittyImageFormat`, `GhosttyKittyImageCompression`, `GhosttyKittyGraphicsImageData`

### Render-state exports

`GhosttyRenderStateDirty`, `GhosttyRenderStateCursorVisualStyle`, `GhosttyRenderStateData`, `GhosttyRenderStateOption`, `GhosttyRenderStateRowData`, `GhosttyRenderStateRowOption`, `GhosttyRenderStateRowCellsData`, `GhosttyRenderStateColors`, `GhosttyRenderStateRowSelection`, `GhosttyBuffer`

### Screen exports

`GhosttyStyleColorTag`, `GhosttyStyleColorValue`, `GhosttyStyleColor`, `GhosttyStyle`, `GhosttyGridRef`, `GhosttyCellContentTag`, `GhosttyCellWide`, `GhosttyCellSemanticContent`, `GhosttyCellData`, `GhosttyRowSemanticPrompt`, `GhosttyRowData`

### Selection, system, and terminal exports

`GhosttySelectionRange`, `GhosttySelectionOrder`, `GhosttySelectionAdjust`, `GhosttySelectionGestureBehavior`, `GhosttySelectionGestureBehaviors`, `GhosttySelectionGestureGeometry`, `GhosttySelectionGestureAutoscroll`, `GhosttySelectionGestureData`, `GhosttySelectionGestureEventType`, `GhosttySelectionGestureEventOption`, `GhosttyAllocatorVtable`, `GhosttyAllocator`, `GhosttySysImage`, `GhosttySysDecodePngCallback`, `GhosttySysOption`, `GhosttyTerminalScrollViewportTag`, `GhosttyTerminalScrollViewportValue`, `GhosttyTerminalScrollViewport`, `GhosttyTerminalCompressionMode`, `GhosttyTerminalCompressionResult`, `GhosttyTerminalScreen`, `GhosttyTerminalScrollbar`, `GhosttyTerminalBellCallback`, `GhosttyTerminalWritePtyCallback`, `GhosttyTerminalTitleChangedCallback`, `GhosttyTerminalEnquiryCallback`, `GhosttyTerminalXtversionCallback`, `GhosttyTerminalSizeCallback`, `GhosttyTerminalColorSchemeCallback`, `GhosttyTerminalDeviceAttributesCallback`, `GhosttyTerminalPwdChangedCallback`, `GhosttyTerminalClipboardWriteCallback`, `GhosttyTerminalDesktopNotificationCallback`, `GhosttyTerminalProgressReportCallback`, `GhosttyTerminalOption`, `GhosttyTerminalData`

## Runtime enums, structs, and callbacks

The public Ghostty surface also includes the broader runtime enum and action/config mirror types that are not part of `GhosttyVtNative` itself.

### Runtime enums

`GhosttyPlatform`, `GhosttyClipboard`, `GhosttyClipboardRequest`, `GhosttyMouseState`, `GhosttyMouseButton`, `GhosttyMouseMomentum`, `GhosttyColorScheme`, `GhosttyMods`, `GhosttyBindingFlags`, `GhosttyInputAction`, `GhosttyKey`, `GhosttyInputTriggerTag`, `GhosttyBuildMode`, `GhosttyPointTag`, `GhosttyPointCoord`, `GhosttySurfaceContext`, `GhosttyTargetTag`, `GhosttySplitDirection`, `GhosttyGotoSplit`, `GhosttyGotoWindow`, `GhosttyResizeSplitDirection`, `GhosttyGotoTab`, `GhosttyFullscreen`, `GhosttyFloatWindow`, `GhosttySecureInput`, `GhosttyInspectorAction`, `GhosttyQuitTimer`, `GhosttyReadonly`, `GhosttyPromptTitle`, `GhosttyMouseShape`, `GhosttyMouseVisibility`, `GhosttyRendererHealth`, `GhosttyColorKind`, `GhosttyOpenUrlKind`, `GhosttyCloseTabMode`, `GhosttyProgressState`, `GhosttyQuickTerminalSizeTag`, `GhosttyKeyTableTag`, `GhosttyActionTag`, `GhosttyIpcTargetTag`, `GhosttyIpcActionTag`

### Runtime structs

`GhosttyClipboardContent`, `GhosttyInputKey`, `GhosttyInputTriggerKey`, `GhosttyInputTrigger`, `GhosttyCommand`, `GhosttyInfo`, `GhosttyDiagnostic`, `GhosttyString`, `GhosttyText`, `GhosttyPoint`, `GhosttySelection`, `GhosttyEnvVar`, `GhosttyPlatformMacOS`, `GhosttyPlatformIOS`, `GhosttyPlatformUnion`, `GhosttySurfaceConfig`, `GhosttySurfaceSize`, `GhosttyConfigColor`, `GhosttyConfigColorList`, `GhosttyConfigCommandList`, `GhosttyConfigPalette`, `GhosttyQuickTerminalSizeValue`, `GhosttyQuickTerminalSize`, `GhosttyConfigQuickTerminalSize`, `GhosttyTargetUnion`, `GhosttyTarget`, `GhosttyResizeSplit`, `GhosttyMoveTab`, `GhosttySizeLimit`, `GhosttyInitialSize`, `GhosttyCellSize`, `GhosttyDesktopNotification`, `GhosttySetTitle`, `GhosttyPwd`, `GhosttyMouseOverLink`, `GhosttyKeySequence`, `GhosttyKeyTableActivate`, `GhosttyKeyTableValue`, `GhosttyKeyTable`, `GhosttyColorChange`, `GhosttyConfigChange`, `GhosttyReloadConfig`, `GhosttyOpenUrl`, `GhosttyChildExited`, `GhosttyProgressReport`, `GhosttyCommandFinished`, `GhosttyStartSearch`, `GhosttySearchTotal`, `GhosttySearchSelected`, `GhosttyScrollbar`, `GhosttyActionValue`, `GhosttyAction`, `GhosttyRuntimeConfig`

### Runtime delegates

`GhosttyWakeupCallback`, `GhosttyActionCallback`, `GhosttyReadClipboardCallback`, `GhosttyConfirmReadClipboardCallback`, `GhosttyWriteClipboardCallback`, `GhosttyCloseSurfaceCallback`

## Which layer should you choose?

Use the layers in this order:

1. stay in the main RoyalTerminal packages if all you need is a terminal control, VT processor, or renderer
2. use the high-level GhosttySharp wrappers if you need native terminal behavior directly
3. drop to `GhosttyVtNative` and the runtime mirror types only if you are building new interop or diagnostics on top of Ghostty itself

That keeps the common path small while still preserving the full native escape hatch for advanced consumers.
