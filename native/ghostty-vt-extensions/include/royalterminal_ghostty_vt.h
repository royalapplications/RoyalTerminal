#ifndef ROYALTERMINAL_GHOSTTY_VT_H
#define ROYALTERMINAL_GHOSTTY_VT_H

#include <ghostty/vt.h>

#ifdef __cplusplus
extern "C" {
#endif

/**
 * Advances the active storage's Kitty animations on the caller's monotonic
 * millisecond clock. Serialize with all access to the owning terminal.
 * The storage handle must have been obtained since the last terminal mutation.
 * This call mutates image pixels/generations and invalidates borrowed images.
 *
 * Returns GHOSTTY_SUCCESS with the next relative delay, GHOSTTY_NO_VALUE when
 * no animation needs another tick, or GHOSTTY_INVALID_VALUE for NULL arguments.
 */
GHOSTTY_API GhosttyResult ghostty_royal_kitty_graphics_animation_tick(
    GhosttyKittyGraphics graphics, uint64_t now_ms, uint64_t* out_delay_ms);

/** Immutable metadata copied from a placement iterator. Set size before calling.
 * Flags: 1 = internal ID namespace, 2 = virtual, 4 = relative with virtual root.
 * Root fields are meaningful only with flag 4. No pointers are retained.
 */
typedef struct {
    size_t size;
    uint32_t image_id, placement_id, flags;
    uint32_t root_image_id, root_placement_id, root_internal;
    int32_t horizontal_offset, vertical_offset;
} RoyalKittyPlacementMetadata;

/** Read the current placement without allocation/mutation. Serialize access to
 * the owning terminal. Both borrowed handles must refer to the same storage,
 * obtained after its last mutation; the iterator must have a current entry.
 * Returns INVALID_VALUE for null pointers, undersized output or no current entry.
 */
GHOSTTY_API GhosttyResult ghostty_royal_kitty_graphics_placement_metadata(
    GhosttyKittyGraphics graphics, GhosttyKittyGraphicsPlacementIterator iterator,
    RoyalKittyPlacementMetadata* output);

/** Copied glyph metadata. Set size before calling. Sizing: 0 = unchanged,
 * 1 = aspect-preserving fit (Ghostty constraint.cover), 2 = stretch.
 * Alignment: 0 = start, 1 = center, 2 = end. These are normalized renderer
 * constraints: native storage does not retain the original request names.
 */
typedef struct {
    size_t size;
    uint32_t codepoint, units_per_em, advance_width, line_height, width;
    uint32_t sizing, horizontal, vertical, contour_count, point_count;
    double pad_top, pad_right, pad_bottom, pad_left;
} RoyalGlyphMetadata;

/** Y-up design coordinate; on_curve is exactly zero or one. */
typedef struct { int32_t x, y; uint32_t on_curve; } RoyalGlyphPoint;

/** Read the count and pending glyph dirty flag without mutation/allocation.
 * Inspect before render-state update clears dirty flags. Reset/disable also
 * change the count. Serialize all calls below with terminal mutation/disposal.
 */
GHOSTTY_API GhosttyResult ghostty_royal_glyph_glossary_info(
    GhosttyTerminal terminal, uint32_t* out_count, uint8_t* out_dirty);

/** Copy one FIFO-ordered entry's metadata. NO_VALUE means index is past the end;
 * INVALID_VALUE means null arguments or an undersized output structure.
 */
GHOSTTY_API GhosttyResult ghostty_royal_glyph_metadata(
    GhosttyTerminal terminal, uint32_t index, RoyalGlyphMetadata* output);

/** Copy validated contours/points into caller-owned buffers. All validation
 * precedes writes: OUT_OF_SPACE leaves both buffers unchanged. Null buffers are
 * valid only for zero-length outlines. No borrowed pointer escapes the call.
 * Do not mutate the terminal between metadata and outline calls.
 */
GHOSTTY_API GhosttyResult ghostty_royal_glyph_outline(
    GhosttyTerminal terminal, uint32_t index,
    RoyalGlyphPoint* points, size_t point_capacity,
    uint16_t* contours, size_t contour_capacity);

#ifdef __cplusplus
}
#endif

#endif
