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

## Kitty desktop notifications

Both VT adapters implement `ITerminalNotificationSource`; embedders can configure
`TerminalControl.NotificationHost` with a nonblocking `ITerminalNotificationHost`.
OSC 99 queries advertise only the backend's actual capabilities. Without a host,
notifications and support queries are silently ignored. The default application
now installs Linux freedesktop, macOS UserNotifications and Windows toast backends
for live sessions, shared by both engines; capture replay never installs a desktop
presenter. New platform implementations await runtime sign-off;
this is not a claim of verified desktop delivery.

The shared protocol implements chunked title/body/buttons/icon assembly, strict
safe UTF-8 and base64 input, application/type metadata, occasions, urgency, sound,
replacement identities, activation/close/alive replies and monotonic expiry.
Strings remain plain text, including literal markup. Encoded text preserves
newlines/tabs and removes other control codes. Callback feedback is consumed only
during serialized terminal refresh; it cannot write directly to the PTY from an
OS callback thread. Superseded and prior-session callbacks are ignored. Effects
remain responsive during synchronized-output holds without releasing the frame.

Limits are 64 unfinished and 64 active notifications, 4 MiB retained assembly
and request payload budgets, 64 KiB per text field, 1 MiB per icon, 32 names/types
or buttons, and a 128-entry/16 MiB session-local icon LRU. Individual OSC payloads
follow Ghostty's 2048-byte plain / 4096-byte encoded limits; metadata is capped at
8192 bytes and IDs at 256 ASCII identifier characters. Input never becomes an
arbitrary file path, shell command, or OS notification identity. Resource names
are restricted to single identifiers resolved inside configured local XDG roots.
Hosts must separately
bound native image decoding and their own queues. RIS clears unfinished chunks;
session changes, host replacement, detachment and disposal close owned revisions.

The Linux backend uses explicit D-Bus serialization and the negotiated desktop
service capabilities; no reflection proxy, shell process or libnotify dependency.
It binds calls/signals to a unique daemon owner, fails stale revisions on daemon
restart and retries connection initialization. A per-window async worker owns at
most 128 requests / 8 MiB of retained payload. Close is recorded as state even
when admission is full. In-flight delivery finishes before cleanup so its returned
OS ID can be closed; window closure waits asynchronously for this owned cleanup.
Transport operations are bounded, and no desktop call waits under the VT lock.

Per-pane facades cache focus/visibility on the UI thread and route activation to
the originating tab/pane. Queued focus is invalidated on session/host teardown.
Bodies are escaped only when the server supports markup; literal title text is
preserved, and servers without body support receive the body in the summary.
Images decode only their first PNG/JPEG/GIF frame, with 1 MiB encoded, 2048-pixel
per-axis and 1-megapixel decoded limits. All standard icon aliases and ordered
custom names use local XDG themes, inheritance, size/scale selection and unthemed
fallbacks. Locally installed desktop entries supply application icons; their
commands are never executed and caller-supplied paths/URLs are rejected. Explicit
names precede transmitted images; the application name is an implicit icon only
when neither names nor image data is supplied. Icons are advertised when the
daemon reports static or animated icon support; only the first frame is sent.

Theme roots follow XDG precedence, with GNOME GSettings, KDE and GTK configuration
sources for theme selection and hicolor/freedesktop defaults. Optional GLib
settings access uses explicit native imports, not GTK initialization or a shell.
Theme documents, lookup work and caches are bounded; worker-side refresh observes
file/theme changes on subsequent requests after five seconds. Standard and local
sound names follow sound-theme inheritance, locale/profile and generic-name
fallbacks, including user `.disabled` overrides. Both `sound-name` and resolved
`sound-file` are supplied: the server's `sound` capability guarantees the latter,
not the former. All standard sounds are advertised only when available locally
(or explicitly disabled by the user); otherwise only system/silent are advertised.

Wayland activation-token integration remains unfinished. Avalonia 12.1.1's
[native Wayland activation is a no-op](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Wayland/WindowImplBase.cs)
and exposes no token-aware activation feature. Linux focus capability is therefore
advertised only for an actual X11/XWayland window handle, not inferred from session
environment variables. Native Wayland delivery/reporting remains usable without
claiming that clicking a notification can focus the originating terminal.

New tests cover backend negotiation, replacement, early/stale signals, queue
saturation, in-flight close/shutdown, image limits and headless pane lifecycle.
Additional tests cover XDG root/name/theme order, desktop-entry masking, all icon
and sound aliases, locale and disabled sounds, cache invalidation, bounded lookup,
path traversal rejection and backend-specific focus capability.
An isolated loopback D-Bus peer exercises actual message serialization without
contacting the user's desktop bus. These tests have not yet been executed.

Reference decision: the pinned Ghostty OSC 99 implementation supplies a parser
but no stream/desktop delivery. Windows Terminal's OSC dispatcher has no OSC 99
handler and xterm.js exposes host registration through `registerOscHandler`.
RoyalTerminal follows the [Kitty desktop notification protocol](https://sw.kovidgoyal.net/kitty/desktop-notifications/)
for the shared host lifecycle. Three reviewed native overlays expose the parsed
command without adding an upstream action enum value. Callback, protocol and
headless lifecycle tests have been added; execution awaits full validation.
Linux resource resolution follows the [icon theme specification](https://specifications.freedesktop.org/icon-theme/latest/)
and [sound theme specification](https://specifications.freedesktop.org/sound-theme/latest-single/).

The macOS presenter uses a separate universal arm64/x64 host bridge bundled in the
Avalonia application package; it does not depend on the selected VT engine or add
Ghostty VT exports. Apple UserNotifications requires a real `.app` bundle with a
bundle identifier. Missing native assets, unbundled command-line hosts, denied
authorization and a notification-center delegate owned by another embedding host
leave support unavailable. Initialization and support queries never prompt for
permission: only a real delivery can request alert/sound authorization.

Source-generated JSON carries bounded requests to a native per-client owner. Native
completion blocks never call managed function pointers. An owned managed poller
reads completion/activation and delivered-notification state, so callbacks from
superseded requests or previous sessions cannot activate the current pane. Delivered
replacements reuse their OS ID with a new revision token; a still-pending replacement
gets a different ID so late cancellation cannot remove its successor. Categories
retain other applications' registered categories and are removed on owner teardown.
Actions preserve button indices; the shared protocol decides whether to focus the
originating pane. The OS can still activate the application for its default action.

macOS icon names use standard system symbols or application bundle icons, in order,
then bounded single-frame PNG data. Attachment preparation/file writes run off the
AppKit queue and use generated private temporary directories, removed on completion
or cancellation. Only system/silent sounds are advertised, matching the upstream
macOS presenters. Low urgency is passive; normal/high preserve ordinary notification
policy without requesting a critical-alert entitlement. All-close-event and full
urgency capability flags are deliberately absent. Delivered-state polling provides
an eventually refreshed alive cache (two missing observations after a delivery
grace period); `c=1` receives the protocol's `untracked` response.

Closing a pane cancels its outstanding delivery wait without stopping other panes;
one native authorization wait serves the remaining requests without retaining the
cancelled payloads. Window close cancels all waits and awaits native cleanup.
An OS add completion that exceeds the bounded shutdown wait remains natively owned
and removes its own late request, without retaining a managed callback or handle.
Objective-C tests use a fake center for replacement/cancellation/delegate lifetimes;
managed tests use a fake transport. The universal bridge build and native tests are
wired into macOS CI and its artifact into cross-platform NuGet packaging. Build,
test execution, package inspection and real permission/activation sign-off remain
pending until the full validation phase.

The Windows presenter uses a separate C++/WinRT host bridge for x64 and arm64,
sharing the source-generated command transport, managed ownership and cancellation
with macOS. Its native actor owns the COM apartment, notification objects, event
subscriptions and local image files. No managed callbacks, reflection, PowerShell,
Windows App SDK runtime, shell execution or remote image URLs are used. Disabled
notification settings and unavailable native assets suppress advertised support.

Packaged hosts use their package identity. Unpackaged hosts register a per-user
Start Menu shortcut with a stable executable-path-derived AUMID, following the
[desktop toast identity contract](https://learn.microsoft.com/windows/win32/shell/enable-desktop-toast-with-appusermodelid).
The shortcut points only to the actual host executable, never to terminal data;
an existing nonmatching shortcut is not overwritten. Generic `dotnet`/test hosts
are excluded. The registration persists for Windows notification settings. It does
not install a COM activator or URI protocol handler, and does not alter taskbar pins
or the host's process-wide AppUserModelID. Installer-managed identity and interactive
desktop behavior require platform sign-off.

The design follows Windows Terminal's `DesktopNotification.cpp`: activation
callbacks belong to live toast objects, and tag/group identities support replacement.
Each native client owns a unique group, so windows do not clear each other's toasts.
Retired tokens cannot focus replacement sessions. XML is built through DOM text and
attribute setters; opaque button arguments cannot contain terminal-provided commands.
Windows supports at most five buttons; the first five retain their original indices.
Ordered stock/application icon names use a fixed local shell namespace, followed by
bounded normalized PNG input. Private, generated image files live only as long as the
native toast lease. No arbitrary caller path is opened. System/silent sounds are
supported; named sound support is not advertised. Low urgency suppresses the popup,
normal uses default priority, and high requests high priority without bypassing Focus
Assist or system notification policy.

Because Windows can launch a second host instance in addition to delivering an
in-process activation, embedders should call
`TerminalNotificationLaunch.IsInertActivation(args)` before creating their desktop
lifetime, and exit for this inert sentinel. The demo entry point does this. Closing
a notification cannot reopen a terminal or replay a command. Delivered-history polling
maintains an eventually refreshed alive cache; full close-event support is not claimed.
Shutdown revokes handlers, removes owned toasts and releases images. A stalled OS RPC
can outlive the bounded managed wait under native-only ownership; the bridge remains
loaded so eventual cleanup is safe.

Windows bridge source builds require Visual C++ build tools (x64 and ARM64) and the
Windows SDK, via `scripts/build-windows-notifications.ps1`. CI builds both architectures
and has a fake-platform native lifecycle harness plus cross-platform managed tests;
these tests do not register shortcuts or show desktop notifications. CI and release
packaging include both Windows DLLs and the universal macOS dylib. All new build,
test, packaging and real Windows activation/sign-off work awaits the full validation
phase; source implementation is not evidence of platform success.

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
