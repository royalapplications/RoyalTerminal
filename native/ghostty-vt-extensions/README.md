# RoyalTerminal libghostty-vt extensions

This package builds the pinned `external/ghostty` dependency without modifying
its checkout. It retains Ghostty's build configuration, runtime module,
allocator, generation counter, public C exports and platform linker handling.
The build generates a copy of the upstream Zig root with the exports from
`src/extensions.zig` appended. Both shared and static libraries include them.

## Reviewed correctness overlays

The generated source copy also applies six corrections to pinned upstream
`4ae9f1a2de5484de3d6a13fe03676b8853b9c41c` (identical runtime sources to the
previously reviewed `22391ed6491f2924361dcad1f9a9176a390fd20f`). Each checks the original file's full
SHA-256 and the exact expected source-fragment count; any upstream file change
fails the build until reviewed. The submodule checkout is never changed.

- `Terminal.zig`: mark the cursor row dirty before the mode-2027-off width-zero
  grapheme append. With a clean render snapshot between `K` and U+0301, unpatched
  native storage retained the suffix but render state stayed clean and exposed
  only `K`. The mode-on path already marks dirty. The overlay preserves the
  intended text semantics and incremental dirty-row rendering.
- `Terminal.zig`: mark the previous row dirty when overwriting either half of
  a wrapped wide glyph normalizes its spacer head. Both `printCell` branches
  changed native storage without refreshing an already-clean render snapshot.
  Raw-grid snapshots reproduced the mismatch before correction. The overlay
  checks exactly two replacement sites and leaves ordinary spacer tails alone.
- `kitty/graphics_storage.zig`: use `number - 1` for the removed frame's index
  when updating `current_index`, which includes the root. Unpatched native code
  selected the red root and kept its generation after deleting displayed blue
  frame 2 from red/blue/white/green frames. Correct behavior selects the white
  successor and stamps changed content. Deleting another frame preserves the
  displayed frame's identity.
- `stream_continuation.zig`: canonicalize export after an APC-to-C1 DCS/CSI/OSC
  transition commits the preceding APC. Unpatched export retained the entire
  Kitty query, causing snapshot validation to reject the exported continuation
  and allowing direct replay to repeat the query. The overlay scans for the last
  committed transition before writing to a non-rewindable writer, emits the new
  introducer as ESC plus its 7-bit final, and preserves omission of already
  executed C0 controls. Ordinary C1 payload bytes in OSC/DCS remain untouched.
  Only export changes; tracking limits and the input feed path remain unchanged.
  Buffer/callback exports and snapshot round trips are tested at every split.

- `Terminal.zig`: dirty semantic prompt rows after OSC 133 and implicit
  newline continuations. After publishing a clean frame, OSC `133;P` changed
  native row storage but left render-state row metadata stale. The same occurred
  for input/prompt newline continuation markers. Marking the affected cursor row
  at the mutation sites preserves incremental render updates without scanning
  the grid or forcing a full refresh. Focused tests cover metadata-only changes,
  newline continuations, and synchronized-output release.

- `Screen.zig`: mark the next row dirty when `cursorResetWrap` clears its
  wrap-continuation marker. Source inspection found this metadata mutation also
  bypassed dirty publication. A focused clean-frame EL regression is written;
  validation of this sixth overlay is deferred until after the requested push.

The first five were reproduced through the native C API before correction and
have focused tests. No public upstream issue is claimed. Reassess and
remove an overlay when its upstream fix is incorporated.

The eight additional C exports are declared in
`include/royalterminal_ghostty_vt.h`:

- `ghostty_royal_mouse_state` copies the effective tracking/format flags consumed
  by `mouse_encode.setopt_from_terminal`, without allocation or mutation. The
  upstream boolean terminal query ORs independent mode bits; mixed mode resets
  and decoded snapshots can legitimately disagree with those bits. The adapter
  uses the effective flags for input routing and pixel-coordinate decisions.

- `ghostty_royal_prompt_state` copies live cursor classification, prompt-seen,
  click/redraw policies and implicit hyperlink counter without modifying state.
- `ghostty_royal_grid_ref_hyperlink` copies original URI and explicit ID bytes,
  or the numeric implicit ID. Capacity probing writes only metadata; a short
  buffer leaves both byte buffers untouched. The host resolves identities with
  borrowed spans and copies bytes only for a new registry entry. These two new
  exports have ABI/buffer/lifetime regressions awaiting post-push validation.

- `ghostty_royal_kitty_graphics_animation_tick` calls Ghostty's own
  `ImageStorage.animationTick`. The public C API exposes the current animation
  frame but not the tick normally performed by Ghostty's renderer. The caller
  supplies monotonically increasing milliseconds and receives the relative
  delay until the next frame, or `GHOSTTY_NO_VALUE` when no timer is needed.
  Calls must be serialized with access to the owning terminal. Reacquire image
  handles after ticking; image/storage generations identify updated pixels.

- `ghostty_royal_kitty_graphics_placement_metadata` reads the current placement
  iterator entry, retaining internal/external placement namespaces and virtual
  root identity. Relative offsets use upstream `resolveChain`, including its
  saturation behavior. This is read-only and allocation-free. The iterator must
  belong to the supplied storage and have a current entry; serialize access with
  terminal mutations.

- `ghostty_royal_glyph_glossary_info`, `ghostty_royal_glyph_metadata` and
  `ghostty_royal_glyph_outline` copy session glyph information without mutation,
  allocation or borrowed output pointers. Entries use FIFO indices, valid only
  while terminal mutation is excluded. Metadata describes normalized Ghostty
  constraints, not unrecoverable request aliases. Outline copying validates both
  buffers before writing either. The native VT adapter reads the dirty flag before
  render-state update clears it, and publishes owned, immutable glyph models only
  at presentation boundaries. Unchanged frames perform one allocation-free info
  query and retain existing glyph objects. Reset/disable also change the count.

`scripts/build-native.sh` and `scripts/build-native.ps1` build and stage this
package. CI and release jobs also use it. Do not stage a plain upstream build:
RoyalTerminal's native VT processor requires these exports for idle animations,
virtual-placement metadata, glyph publication and exact hyperlink identity.

The integration intentionally uses upstream animation state and composition,
not a parallel implementation of animation semantics. If Ghostty adds a public
equivalent, remove this extension and migrate its call sites. Until then, review
`src/terminal/kitty/graphics_storage.zig` and the C storage handle representation
when updating the upstream pin. Ghostty commit `aee7bf347` introduced renderer
animation ticks; `73903f76a` makes the active frame visible through the C API.

The host's `ITerminalTimedRefreshSource` timer drives this API even without new
PTY output, and also expires synchronized-output holds. The pixel bridge
fuses borrowed-data copying with RGB/grayscale expansion, following Ghostty's
`169213cd2` optimization; the Skia renderer already consumes RGBA8888 directly
and caches uploaded image bitmaps by content.
