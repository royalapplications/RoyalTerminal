# PR Summary: Terminal Font Settings, Scrollback, and Resize Reflow Fixes

## Overview

This change addresses issue #43 by adding first-class terminal font configuration, fixing scrollback behavior where wheel scrolling could appear stuck at the bottom for specific font metrics, and correcting managed terminal resize behavior so long command output reflows instead of being cut off after horizontal window shrink/restore cycles.

Users can now choose between installed system fonts and a font file loaded from disk, configure the terminal font size through the settings UI, and have those settings persisted through terminal session profiles.

## Commits

- `feat: add configurable terminal font support`
- `feat: expose terminal font settings in UI`
- `fix: preserve scrollback position while wheel scrolling`
- `fix: write output against live terminal viewport`
- `fix: preserve row content across horizontal resize`
- `fix: reflow terminal rows on horizontal resize`
- `fix: keep live cursor anchored during scrolled resize`

## Core Font Configuration

- Added `TerminalFontSource` with `System` and `File` modes.
- Extended terminal appearance profile settings with:
  - `FontSource`
  - `FontFamilyName`
  - `FontFilePath`
  - `FontSize`
- Updated profile serialization so file-backed font settings round-trip correctly.
- Normalized invalid file-font settings back to system font mode when no file path is available.

## Rendering Support

- Added Skia-backed font catalog support for installed system font discovery.
- Added font-file family detection through `SKTypeface.FromFile`.
- Updated glyph cache and renderer construction to resolve the primary typeface from either:
  - an installed system font family, or
  - a selected font file path.
- Preserved fallback behavior when a selected font file is missing or invalid.

## Settings UI

- Added appearance settings UI for:
  - selecting font source,
  - choosing from installed system fonts,
  - browsing for font files,
  - displaying the detected file font family,
  - changing terminal font size.
- Kept the UI MVVM-oriented: file browsing is surfaced through a command/request flow rather than code-behind event handlers.
- Updated the demo shell to apply font settings to current, replayed, and newly created terminal tabs.

## Scrollback Fix

- Removed the unconditional scroll-to-bottom call from output handling when `AutoScroll` is enabled.
- Preserved the existing sticky-bottom behavior: output keeps the terminal at the bottom only when it was already at the bottom.
- Added fractional wheel row accumulation so small/high-resolution wheel deltas are not truncated to zero.
- Reset pending fractional wheel movement when scroll direction changes to avoid stale remainder affecting the next gesture.
- Fixed managed VT output processing while scrolled back by temporarily anchoring writes to the live bottom viewport.
- Prevented new command output, such as `ls -al`, from being written into historical rows when the user scrolls back and forth during or after output.
- Preserved buffered row cells across horizontal shrink/restore cycles so text produced at a wider terminal width is not permanently truncated after the window is narrowed and expanded again.

## Resize Reflow

- Added a default-enabled `ReflowOnResize` terminal behavior setting.
- Exposed the setting in the terminal behavior settings UI and demo settings surface.
- Persisted the setting through `TerminalSessionBehaviorSettings` and profile serialization.
- Reflowed buffered managed-terminal logical lines when the terminal column count changes.
- Preserved soft-wrap metadata so shrink and restore cycles reconstruct logical command output instead of losing hidden right-side cells.
- Remapped the managed VT cursor through reflow so the next prompt/input write continues at the correct logical position.
- Anchored cursor remapping to the live bottom viewport even when the user is scrolled back during resize.
- Disabled managed reflow while the alternate screen is active so full-screen TUI applications can handle redraw through normal resize notifications.
- Kept the reflow path allocation-conscious: it uses one pass over rows, reuses a logical-line scratch list per resize, skips LINQ in the hot path, and grows row backing arrays only when needed.

## Tests

Added and updated tests for:

- profile serialization of file-backed font settings,
- invalid file-font normalization,
- terminal control fallback for missing font files,
- settings state persistence for font source/path/family/size,
- demo controller apply/save flows for font settings,
- output handling preserving scrollback offset while scrolled up,
- output handling keeping bottom when already at bottom,
- fractional wheel-delta accumulation.
- managed terminal output preserving visible scrollback rows while scrolled away from the bottom.
- horizontal resize shrink/restore behavior at both the screen model and terminal control levels.
- default-on and persisted reflow-on-resize settings.
- managed VT cursor preservation after reflow followed by additional output.
- managed VT cursor preservation when resizing while scrolled back, then writing more output at the live prompt.
- opt-out behavior that keeps fixed-width hidden cells available for shrink/restore without reflow.

## Validation

The following checks passed locally:

```text
dotnet build RoyalTerminal.sln --no-restore
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj
dotnet test tests/RoyalTerminal.IntegrationTests/RoyalTerminal.IntegrationTests.csproj
git diff --check
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
