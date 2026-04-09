---
title: Terminal Engine And Screen State
---

# Terminal Engine And Screen State

This is the part of RoyalTerminal that behaves most like a terminal rather than a UI control. It is where bytes become a screen, modes affect input encoding, selections turn into exported text, and Unicode width rules determine how cells line up.

## The screen model is explicit

RoyalTerminal exposes the screen state directly instead of hiding it behind a renderer-only abstraction. That is what makes it possible to support multiple VT processors, multiple renderers, capture/replay, search, snapshot export, and test-friendly host integration.

### Core screen types

| Type | Purpose |
| --- | --- |
| `TerminalCell` | One grid cell, including codepoint or grapheme text, colors, attributes, hyperlink id, underline data, and width. |
| `CellAttributes` | Packed attribute flags. |
| `TerminalUnderlineStyle` | Underline style enum. |
| `CellDecorations` | Additional decoration flags such as overline. |
| `TerminalHighlightKind` | Search match, selected match, or hyperlink hover overlay category. |
| `TerminalHighlightSpan` | Viewport-local highlight span. |
| `TerminalRow` | Dirty-tracked row of cells. |
| `TerminalScreen` | The full scrollback-aware screen model, including hyperlinks, Kitty images, theme state, and viewport position. |
| `TerminalKittyImageLayer` | Kitty image z-order. |
| `TerminalKittyImageSource` | Decoded Kitty image payload. |
| `TerminalKittyImagePlacement` | Viewport-relative Kitty image placement. |

## Two VT engines, one processor boundary

RoyalTerminal uses one public processor contract and lets the host choose whether the implementation is fully managed or native.

| Type | Role |
| --- | --- |
| `IVtProcessor` | Core VT processor contract. |
| `IVtProcessorFactory` | Factory for processor creation. |
| `INativeVtProcessorProvider` | Optional native VT provider contract. |
| `VtProcessorPreference` | Managed, native, or automatic preference. |
| `BasicVtProcessor` | Pure C# fallback processor. |
| `GhosttyVtProcessor` | Native Ghostty-backed processor. |
| `GhosttyVtProcessorProvider` | Native provider used by the default factory. |
| `DefaultVtProcessorFactory` | Policy object that selects managed or native processing. |

In practice, this means the host can ask for strict native behavior, strict managed behavior, or native-when-available behavior without changing its session code.

## Mode-aware host integration

The processor boundary is richer than "write bytes, get pixels". The host also needs cursor state, mode state, focus mode, and theme application.

| Type | Purpose |
| --- | --- |
| `TerminalModeState` | Snapshot of the major runtime VT modes. |
| `TerminalCursorStyle` | Host-facing cursor style enum. |
| `ITerminalThemeSink` | Applies a new immutable theme to the active processor. |
| `IKittyKeyboardStateSource` | Exposes Kitty keyboard flags. |
| `ITerminalCursorStyleSource` | Exposes cursor shape and blink state. |
| `ITerminalFocusEventModeSource` | Exposes focus event reporting state. |
| `TerminalWin32InputModeTracker` | Tracks Win32 input mode and focus mode from terminal output. |

These capabilities are what make `TerminalControl` able to encode input correctly without knowing which VT engine is active under the hood.

## Endpoints and host-owned terminals

Not every RoyalTerminal host wants the built-in transport stack. Some applications already own the remote session or local terminal connection. That is why the endpoint contracts are public.

| Type | Purpose |
| --- | --- |
| `ITerminalEndpoint` | Host-owned terminal endpoint abstraction. |
| `ITerminalInputSink` | Sends text or bytes into an endpoint. |
| `ITerminalSelectionSource` | Exposes selection state and export behavior. |
| `ITerminalModeSource` | Exposes mode state from an endpoint. |
| `ITerminalScaleSink` | Receives content scale changes. |

## Input encoding is part of the terminal model

Keyboard and pointer input are not generic UI events once you are inside a terminal. The active modes, pointer protocol, Kitty keyboard flags, and application keypad state all matter.

### Event models

| Type | Purpose |
| --- | --- |
| `TerminalInputAction` | High-level input action enum. |
| `TerminalPointerEventKind` | Pointer event kind enum. |
| `TerminalMouseButton` | Pointer button enum. |
| `TerminalModifiers` | Modifier flags for terminal input. |
| `TerminalKeyEvent` | Host key event payload. |
| `TerminalPointerEvent` | Host pointer event payload. |

### Encoding and capability contracts

| Type | Purpose |
| --- | --- |
| `TerminalKeyEncodingRequest` | Key encoding request payload. |
| `TerminalPointerEncodingContext` | Pointer encoding context. |
| `ITerminalKeySequenceEncoderSource` | Capability contract for key sequence encoding. |
| `ITerminalPointerSequenceEncoderSource` | Capability contract for pointer encoding. |
| `ITerminalMouseReportingStateSource` | Capability contract for current mouse reporting state. |
| `TerminalPasteEncoder` | Static helper for plain or bracketed paste sequences. |

## Mouse reporting, viewport state, and search

Terminals have a surprisingly large amount of public behavior around mouse state and viewport tracking. RoyalTerminal keeps that explicit because the UI layer needs it.

| Type | Purpose |
| --- | --- |
| `TerminalMouseTrackingMode` | Active DEC mouse tracking mode. |
| `TerminalMouseEncoding` | Active mouse encoding mode. |
| `TerminalMouseModeState` | Snapshot of current mouse-reporting behavior. |
| `TerminalMouseModeTracker` | Incremental parser that tracks mouse mode changes from terminal output. |
| `TerminalMouseProtocolEncoder` | Static encoder for X10, UTF-8, SGR, and URXVT mouse protocols. |
| `TerminalViewportScrollState` | Viewport scrollbar state for native processors. |
| `ITerminalViewportScrollSource` | Capability contract for viewport scroll state. |
| `TerminalSearchMatch` | Search hit location. |
| `ITerminalSearchSource` | Search capability contract. |

## Selection, snapshot export, and durable copies

Selections and snapshots sit right on the boundary between terminal logic and user workflows. That is why the contracts are in the core package instead of in Avalonia.

| Type | Purpose |
| --- | --- |
| `TerminalSelectionRange` | Absolute selection range model. |
| `ITerminalSelectionExportSource` | Selection export capability contract. |
| `ITerminalPasteSequenceEncoderSource` | Paste sequence capability contract. |
| `TerminalSnapshotExportFormat` | Snapshot export format enum. |
| `TerminalSnapshotExportExtras` | Additional export detail flags. |
| `TerminalSnapshotExportOptions` | Snapshot export request model. |
| `ITerminalSnapshotExportSource` | Snapshot export capability contract. |

## Unicode is not an implementation detail

Terminal layout depends on Unicode width rules and grapheme boundaries, especially for emoji, combining marks, CJK full-width content, and selection fidelity. RoyalTerminal exposes these primitives because they affect correctness across the stack.

| Type | Purpose |
| --- | --- |
| `TerminalCellWidthCalculator` | Width calculator used by the terminal stack. |
| `Codepoint` | Unicode codepoint wrapper with metadata helpers. |
| `Grapheme` | Grapheme cluster slice. |
| `GraphemeEnumerator` | Grapheme enumerator over UTF-16 text. |
| `GraphemeBreakClass` | Grapheme break class enum. |
| `EastAsianWidthClass` | East Asian width classification enum. |

When something looks wrong in a terminal, it is often one of three things: the VT parser, the renderer, or width/grapheme handling. This is the layer where the parser and width logic meet.
