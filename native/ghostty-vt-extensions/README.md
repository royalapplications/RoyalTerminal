# RoyalTerminal libghostty-vt extensions

This package builds the pinned `external/ghostty` dependency without modifying
its checkout. It retains Ghostty's build configuration, runtime module,
allocator, generation counter, public C exports and platform linker handling.
The build generates a copy of the upstream Zig root with the exports from
`src/extensions.zig` appended. Both shared and static libraries include them.

## Reviewed correctness overlays

The generated source copy also applies two corrections to pinned upstream
`22391ed6491f2924361dcad1f9a9176a390fd20f`. Each checks the original file's full
SHA-256 and exactly one matching source fragment; any upstream file change
fails the build until reviewed. The submodule checkout is never changed.

- `Terminal.zig`: mark the cursor row dirty before the mode-2027-off width-zero
  grapheme append. With a clean render snapshot between `K` and U+0301, unpatched
  native storage retained the suffix but render state stayed clean and exposed
  only `K`. The mode-on path already marks dirty. The overlay preserves the
  intended text semantics and incremental dirty-row rendering.
- `kitty/graphics_storage.zig`: use `number - 1` for the removed frame's index
  when updating `current_index`, which includes the root. Unpatched native code
  selected the red root and kept its generation after deleting displayed blue
  frame 2 from red/blue/white/green frames. Correct behavior selects the white
  successor and stamps changed content. Deleting another frame preserves the
  displayed frame's identity.

Both were reproduced through the native C API before correction and have
focused integration tests. No public upstream issue is claimed. Reassess and
remove an overlay when its upstream fix is incorporated.

The two additional C exports are declared in
`include/royalterminal_ghostty_vt.h`:

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
  terminal mutations. Both exports are required by the native VT integration.

`scripts/build-native.sh` and `scripts/build-native.ps1` build and stage this
package. CI and release jobs also use it. Do not stage a plain upstream build:
RoyalTerminal's native VT processor uses this extra export for idle animations.

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
