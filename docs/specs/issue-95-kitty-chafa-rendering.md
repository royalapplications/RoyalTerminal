# Issue 95: Kitty/Chafa Rendering Spec

## Problem

[Issue #95](https://github.com/royalapplications/RoyalTerminal/issues/95)
tracks Chafa image rendering and prompt placement differences between
RoyalTerminal and Ghostty. The linked
[Royal Apps community post](https://community.royalapps.com/t/royalts-v26-beta-w-royalterminal-some-issues/2309/6)
shows a Ghostty-good/RoyalTerminal-bad comparison on June 28, 2026. The earlier
June 25, 2026 report describes prompts being rendered under Chafa images and
images sometimes appearing only after resize.

## Reference Comparison

- [Kitty graphics protocol](https://sw.kovidgoyal.net/kitty/graphics-protocol/)
  places images at the current cursor, expects images to scroll with text, moves
  the cursor after the image by default, and uses `C=1` only when the client asks
  the terminal not to move the cursor.
- [Chafa](https://github.com/hpjansson/chafa) direct Kitty mode emits
  `a=T,f=...,s=...,v=...,c=...,r=...,m=1,q=2` and does not emit `C=1`. Chafa's
  `U=1` virtual placeholder output is used on the passthrough path, not the
  direct Ghostty/Kitty path.
- [Ghostty](https://github.com/ghostty-org/ghostty) exposes direct Kitty
  placements through viewport-relative row/column render info. Virtual
  placeholder placements are intentionally not visible through that direct
  placement API because they are rendered inline by text layout.
- [xterm.js](https://github.com/xtermjs/xterm.js) computes Kitty image cell
  geometry from the terminal's cell size, preserves scrolling image placement,
  applies `z`, and implements the same cursor rule: `C=1` restores the cursor,
  otherwise the cursor advances past the image.
- [Windows Terminal](https://github.com/microsoft/terminal) is not a Kitty image
  reference, but its Sixel path confirms the same raster lifecycle principles:
  image geometry is tied to cell metrics, scroll/clamp mode is explicit, and
  cursor position is updated from final image geometry when display mode allows.

## RoyalTerminal Behavior

RoyalTerminal follows Kitty, Chafa, Ghostty, and xterm.js for direct Chafa Kitty
graphics:

- Ghostty owns Kitty protocol parsing and cursor movement.
- The managed screen imports Ghostty's viewport-relative direct placements.
- Managed Kitty placements carry the native cell size used when Ghostty computed
  placement pixel dimensions only when the Kitty placement requested `c`, `r`,
  or both.
- Managed Kitty placements use `0` and `ScaleMode.None` for natural pixel-size
  placements where `c` and `r` were omitted; the Skia renderer treats those as
  unscaled literal pixels.
- The Skia renderer scales Kitty placement offsets and extents when Avalonia's
  current render cell size differs from that placement-time native cell size.
- Ghostty native resize and Kitty placement scaling use ceiling cell metrics to
  avoid fractional-cell truncation in native placement math.
- XTWINOPS size replies (`CSI 14t`/`CSI 16t`) use a separate, non-overreporting
  size-report metric so fractional grids do not report a larger pixel surface
  than the PTY/renderer was given.

## Validation Plan

- Verify Chafa is installed and emits direct Kitty graphics locally.
- Cover placement-time cell metrics in Ghostty VT tests.
- Cover natural pixel-size Kitty placements in Ghostty VT and Skia renderer
  tests so omitted `c`/`r` placements are not scaled.
- Cover fractional-cell Ghostty size replies so `CSI 14t`/`CSI 16t` do not round
  the report metric up.
- Cover direct Kitty prompt placement by asserting that `CRLF` after a two-row
  image writes the prompt below the image rows.
- Cover Skia Kitty placement scaling in renderer tests.
- Run the rendering/Ghostty test slice before merging.
