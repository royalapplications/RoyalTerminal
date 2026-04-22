# PR Summary: Terminal Font Settings, Scrollback, and Resize Reflow Fixes

## Overview

This PR addresses issue #43 and the related scrollback/resize regressions found during manual testing of command output such as `ls -al`.

It adds first-class terminal font configuration, fixes wheel scrolling that could appear stuck at the bottom for certain font metrics, prevents output from being written into historical scrollback while the user is scrolled up, and implements default-on managed terminal reflow for horizontal resize behavior.

The user-facing result is that RoyalTerminal now behaves closer to native terminals such as Ghostty when a terminal is narrowed and widened again: long command output is reflowed instead of being permanently cut off, while fullscreen alternate-screen applications remain free to redraw themselves on resize.

## Branch Commits

- `620faed` `feat: add configurable terminal font support`
- `85e4994` `feat: expose terminal font settings in UI`
- `ce9c547` `fix: preserve scrollback position while wheel scrolling`
- `37927a1` `fix: write output against live terminal viewport`
- `2ca9c6d` `fix: preserve row content across horizontal resize`
- `6453345` `fix: reflow terminal rows on horizontal resize`
- `77e7aa1` `fix: keep live cursor anchored during scrolled resize`
- `ddbea41` `docs: add issue 43 pr summary`

## User-Facing Changes

- Added terminal appearance settings for:
  - system font selection,
  - loading a font from disk,
  - detected file font family display,
  - terminal font size.
- Added a terminal behavior setting for `ReflowOnResize`.
- Enabled resize reflow by default.
- Preserved opt-out behavior for users who want fixed-width historical rows instead of terminal-style reflow.
- Kept current, replayed, and newly created demo tabs aligned with the selected terminal settings.

## Font Configuration

- Added `TerminalFontSource` with `System` and `File` modes.
- Extended terminal appearance profile settings with:
  - `FontSource`,
  - `FontFamilyName`,
  - `FontFilePath`,
  - `FontSize`.
- Updated profile serialization so font source, selected system family, selected file path, and font size round-trip correctly.
- Normalized invalid file-font settings back to system font mode when no usable file path is available.
- Added Skia-backed installed font discovery.
- Added file-backed font family detection through `SKTypeface.FromFile`.
- Updated glyph cache and renderer construction to resolve the primary typeface from either an installed family or selected font file.
- Preserved renderer fallback behavior when a selected font file is missing or invalid.

## Settings UI

- Added the appearance UI needed to configure terminal font source, font family, font file, detected file family, and font size.
- Added the behavior UI needed to enable or disable resize reflow.
- Kept the UI MVVM-oriented: file browsing is surfaced through a command/request flow instead of code-behind event handlers.
- Updated the demo shell controller and view model so saved settings apply consistently across startup, active sessions, replayed sessions, and newly opened tabs.

## Scrollback Fixes

- Removed the unconditional scroll-to-bottom call from output handling when `AutoScroll` is enabled.
- Preserved sticky-bottom behavior: output keeps the terminal at the bottom only when it was already at the bottom.
- Added fractional wheel row accumulation so small and high-resolution wheel deltas are not truncated to zero.
- Reset pending fractional wheel movement when scroll direction changes.
- Fixed managed VT output processing while scrolled back by temporarily anchoring writes to the live bottom viewport.
- Prevented new command output from being written into historical rows after the user scrolls back and forth through buffered output.

## Horizontal Resize and Reflow

- Preserved buffered row cells across horizontal shrink/restore cycles so text produced at a wider width is not permanently truncated after the window is narrowed and expanded again.
- Reflowed managed-terminal logical lines when the terminal column count changes and `ReflowOnResize` is enabled.
- Preserved soft-wrap metadata so shrink and restore cycles reconstruct logical command output.
- Remapped the managed VT cursor through reflow so the next prompt or command output continues at the correct logical position.
- Anchored cursor remapping to the live bottom viewport even when the user is scrolled back during resize.
- Disabled managed reflow while the alternate screen is active so fullscreen TUI applications can handle redraw through normal resize notifications.
- Kept the non-reflow path capable of preserving hidden cells for shrink/restore cycles when users opt out of reflow.

## Performance Notes

- The resize path walks rows once and reuses a logical-line scratch list during each resize.
- The hot paths avoid LINQ and avoid unnecessary per-cell allocations.
- Row backing storage grows only when needed to preserve cells that are temporarily outside the visible column range.
- Reflow is skipped when the column count is unchanged or when the alternate screen is active.

## Tests

Added and updated tests for:

- profile serialization of file-backed font settings,
- invalid file-font normalization,
- terminal control fallback for missing font files,
- settings state persistence for font source, path, family, size, and reflow behavior,
- demo controller apply/save flows for font and behavior settings,
- output handling preserving scrollback offset while scrolled up,
- output handling keeping bottom position when already at bottom,
- fractional wheel-delta accumulation,
- managed terminal output preserving visible scrollback rows while scrolled away from the bottom,
- horizontal resize shrink/restore behavior at both the screen model and terminal control levels,
- default-on and persisted reflow-on-resize settings,
- managed VT cursor preservation after reflow followed by additional output,
- managed VT cursor preservation when resizing while scrolled back, then writing more output at the live prompt,
- opt-out behavior that keeps fixed-width hidden cells available for shrink/restore without reflow.

## Validation

The following checks passed locally:

```text
git diff --check
dotnet build RoyalTerminal.sln --no-restore
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj
dotnet test tests/RoyalTerminal.IntegrationTests/RoyalTerminal.IntegrationTests.csproj
```

Unit test result:

```text
Passed: 710
Skipped: 14
Failed: 0
Total: 724
```

Integration test result:

```text
Passed: 3
Skipped: 41
Failed: 0
Total: 44
```
