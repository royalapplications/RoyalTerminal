# Managed tracked-cell anchors

The managed graphics store uses opaque `TerminalScreenAnchor` identities, not
`TerminalRow` references or viewport coordinates. Rows are reused when history is
evicted, and synchronized-output transactions clone row metadata; neither changes
the identity of a surviving tracked cell.

## Reference behavior

- Ghostty `PageList.zig` at `22391ed6491f` tracks pins through row copies and
  reflow, marks pins garbage when their history is pruned, and preserves pin
  mappings when cloning. Its Kitty storage reaps garbage pins lazily and surrounds
  margin scrolling with a placement-specific restore operation.
- Ghostty reflow clamps pins in trailing blanks to the remaining destination
  row width. A blank pin does not add phantom wrapped rows. Pins on actual cells
  follow those cells, including a wide glyph that moves to the next row.
- xterm.js `Buffer.ts` markers follow insertion/deletion/trim events and are
  disposed when their rows are removed. They are line markers, so Ghostty is the
  closer reference for column-aware Kitty placement anchors.
- Windows Terminal `textBuffer.cpp` copies image slices with rows and at old-row
  boundaries during reflow. Its image-slice ownership differs from Kitty's
  independent image/placement store; it is not a substitute for tracked Kitty
  cell identities.

## Ownership and mutation

The screen lock protects both the registry and row state. Creation validates an
active-buffer cell. Resolution is active-buffer-only; switching buffers retains
inactive positions, while clearing/discarding that buffer invalidates them.
History trimming invalidates removed pins before row storage can be reused.
An in-place row-region shift moves only that region's pins and invalidates those
that leave it. Graphics-specific clipping and straddling-placement behavior is
handled by the graphics store around that shift, not by this generic registry.

State copies retain the same immutable token identities in independent position
dictionaries. Publishing adopts the complete registry along with the rows.
Explicit release removes the identity. A placement scroll guard may reposition
an existing pruned identity, but cannot resurrect a released one. Tokens from an
unrelated screen never resolve, even if both screens have created the same number
of anchors.

No writable cell span/reference may be retained across a synchronized state copy.
No external code should retain the discarded live screen after publication.

`TerminalScreenAnchorTests` cover history eviction, row reuse, limit changes,
region copies and restoration, alternate buffers, COW publication, narrow/wide
reflow, blank-cell clamping, pruning, and invalid/foreign identities. End-to-end
Kitty placement differential tests remain an additional graphics-store obligation.
