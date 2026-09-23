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

#ifdef __cplusplus
}
#endif

#endif
