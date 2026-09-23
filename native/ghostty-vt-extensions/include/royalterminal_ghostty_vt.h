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

#ifdef __cplusplus
}
#endif

#endif
