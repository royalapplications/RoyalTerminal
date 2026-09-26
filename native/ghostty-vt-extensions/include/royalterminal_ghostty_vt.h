#ifndef ROYALTERMINAL_GHOSTTY_VT_H
#define ROYALTERMINAL_GHOSTTY_VT_H

#include <ghostty/vt.h>

#ifdef __cplusplus
extern "C" {
#endif

/** Host-provided drop representation. Pointers are borrowed only for the call;
 * the upstream state copies data at drop time. MIME is a printable ASCII token
 * (1-1024 bytes, no spaces). At most 16 items and 64 MiB total data are accepted. */
typedef struct {
    const uint8_t* mime;
    size_t mime_len;
    const uint8_t* data;
    size_t data_len;
} RoyalDndItem;

/** Query registered (0/1) and accepted (-1 unanswered, 0 reject, 1 copy, 2 move).
 * Serialize these APIs with all terminal access. No borrowed pointers escape. */
GHOSTTY_API GhosttyResult ghostty_royal_dnd_state(
    GhosttyTerminal terminal, uint8_t* registered, int32_t* accepted);
/** Copy raw registration MIME bytes. OUT_OF_SPACE sets length without copying. */
GHOSTTY_API GhosttyResult ghostty_royal_dnd_mimes(
    GhosttyTerminal terminal, uint8_t* output, size_t capacity, size_t* length);
/** Host event: 1 move, 2 drop, 3 leave, 4 cancel held data, 5 unregister. Coordinates
 * are zero-based cells and content-relative terminal pixels; operations is a
 * copy=1/move=2 mask. Writer synchronously receives protocol input for the client.
 * Items are used by move (MIME only) and drop. No writer or input is retained. */
GHOSTTY_API GhosttyResult ghostty_royal_dnd_event(
    GhosttyTerminal terminal, uint32_t kind, uint32_t column, uint32_t row,
    int32_t pixel_x, int32_t pixel_y, uint32_t operations,
    const RoyalDndItem* items, size_t count, GhosttyWriter writer);

/** Effective mouse state consumed by the native encoder, independent of DEC
 * mode bits. Set size before calling. tracking uses GhosttyMouseTrackingMode;
 * format uses GhosttyMouseFormat. No allocation, mutation, or borrowed pointers.
 * Serialize with terminal mutation. Invalid arguments leave output untouched.
 */
typedef struct {
    size_t size;
    uint32_t tracking, format, shift_capture; /** shift_capture: 0 default, 1 false, 2 true. */
    uint32_t shape; /** W3C shape registry, matching GhosttyMouseShape (0-33). */
} RoyalMouseState;
/** Copies modifyOtherKeys mode-2 state as 0 or 1. Invalid arguments leave output
 * untouched. No allocation; serialize with terminal mutation. */
GHOSTTY_API GhosttyResult ghostty_royal_modify_other_keys_2(
    GhosttyTerminal terminal, uint8_t* output);

/** Copies/sets host-reported password-input metadata (0 or 1). Does not enable
 * OS secure input. Invalid arguments leave state/output untouched. Serialize
 * with all terminal access. No allocation or VT replay. */
GHOSTTY_API GhosttyResult ghostty_royal_password_input_get(
    GhosttyTerminal terminal, uint8_t* output);
GHOSTTY_API GhosttyResult ghostty_royal_password_input_set(
    GhosttyTerminal terminal, uint8_t value);

GHOSTTY_API GhosttyResult ghostty_royal_mouse_state(
    GhosttyTerminal terminal, RoyalMouseState* output);

/** Sets XTSHIFTESCAPE state without replaying VT. 0 default, 1 false, 2 true.
 * Invalid values leave the terminal unchanged. Serialize with terminal mutation. */
GHOSTTY_API GhosttyResult ghostty_royal_mouse_shift_capture_set(
    GhosttyTerminal terminal, uint32_t value);

/** Copied active-screen prompt policy. Set size before calling. Content uses
 * Ghostty cell semantic values. Click: 0 none, 1 absolute, 2 relative, 3 line,
 * 4 multiple, 5 conservative vertical, 6 smart vertical. Redraw: 0 all, 1 none,
 * 2 last. implicit_id is the cursor's next generated OSC 8 ID.
 */
typedef struct {
    size_t size;
    uint32_t seen, content, clear_eol, click, redraw, implicit_id;
} RoyalPromptState;
GHOSTTY_API GhosttyResult ghostty_royal_prompt_state(
    GhosttyTerminal terminal, RoyalPromptState* output);

/** Exact OSC 8 identity. Zero uri_len means no link; zero id_len means an
 * implicit ID. Set size. Probe with zero-capacity buffers: OUT_OF_SPACE fills
 * lengths and leaves both buffers unchanged. Copy does not append NUL bytes.
 * Serialize probe/copy with terminal mutation; a stale grid reference is invalid.
 */
typedef struct {
    size_t size, uri_len, id_len;
    uint32_t implicit_id;
} RoyalHyperlinkMetadata;
GHOSTTY_API GhosttyResult ghostty_royal_grid_ref_hyperlink(
    const GhosttyGridRef* reference, RoyalHyperlinkMetadata* output,
    uint8_t* uri_buffer, size_t uri_capacity,
    uint8_t* id_buffer, size_t id_capacity);

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
