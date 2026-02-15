/*
 * ghostty_terminal.h — C API for Ghostty Terminal Processing
 *
 * This header declares the API for libghostty-terminal, a shared library
 * that wraps Ghostty's VT terminal emulation engine. It provides terminal
 * create/destroy, VT data processing, screen state reading, and mode queries.
 *
 * The terminal uses an "interactive stream" — it processes VT escape sequences,
 * updates internal screen state, and can deliver query responses (DSR, DA,
 * DECRQM, etc.) via a configurable callback function. This allows embedders
 * to get native Ghostty-quality VT processing with complete query support.
 *
 * Thread safety: All functions require external synchronization. Do not call
 * functions on the same terminal handle from multiple threads concurrently.
 */

#ifndef GHOSTTY_TERMINAL_H
#define GHOSTTY_TERMINAL_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ═══════════════════════════════════════════════════════════════════════ */
/* Opaque handle                                                          */
/* ═══════════════════════════════════════════════════════════════════════ */

/** Opaque handle to a terminal instance. */
typedef struct ghostty_terminal* ghostty_terminal_t;

/* ═══════════════════════════════════════════════════════════════════════ */
/* Cell information                                                       */
/* ═══════════════════════════════════════════════════════════════════════ */

/**
 * Information about a single terminal cell.
 *
 * Attribute bits (in `attrs`):
 *   bit  0: bold
 *   bit  1: italic
 *   bit  2: dim/faint
 *   bit  3: inverse/reverse
 *   bit  4: hidden/invisible
 *   bit  5: strikethrough
 *   bit  6: overline
 *   bits 8-10: underline style (0=none, 1=single, 2=double, 3=curly, 4=dotted, 5=dashed)
 *   bit 16: wide character (occupies 2 cells)
 *   bit 17: wide spacer (second cell of a wide character)
 */
typedef struct {
    uint32_t codepoint;  /**< UTF-32 codepoint (0 = empty). */
    uint32_t fg_color;   /**< Foreground as 0xAARRGGBB. */
    uint32_t bg_color;   /**< Background as 0xAARRGGBB. */
    uint32_t attrs;      /**< Packed attribute flags. */
} ghostty_terminal_cell_info_t;

/**
 * Grapheme span for a single cell.
 *
 * The span indexes into a flattened UTF-32 codepoint buffer returned by
 * `ghostty_terminal_get_row_cells_with_graphemes`. The flattened buffer
 * contains trailing codepoints only, i.e. codepoints that follow
 * `ghostty_terminal_cell_info_t.codepoint` in a grapheme cluster.
 *
 * If `length == 0`, the cell has no trailing grapheme codepoints.
 */
typedef struct {
    uint32_t offset;  /**< Start index in flattened grapheme codepoint buffer. */
    uint32_t length;  /**< Number of trailing grapheme codepoints. */
} ghostty_terminal_grapheme_span_t;

/* ═══════════════════════════════════════════════════════════════════════ */
/* Cursor information                                                     */
/* ═══════════════════════════════════════════════════════════════════════ */

/**
 * Cursor position and style.
 *
 * cursor_style values:
 *   0 = block (steady)
 *   1 = block (blink)
 *   2 = underline (steady)
 *   3 = underline (blink)
 *   4 = bar (steady)
 *   5 = bar (blink)
 */
typedef struct {
    uint32_t col;           /**< Column (0-based). */
    uint32_t row;           /**< Row (0-based). */
    uint8_t  visible;       /**< 1 = visible, 0 = hidden. */
    uint8_t  cursor_style;  /**< Cursor style enum. */
} ghostty_terminal_cursor_info_t;

/* ═══════════════════════════════════════════════════════════════════════ */
/* Lifecycle                                                              */
/* ═══════════════════════════════════════════════════════════════════════ */

/**
 * Creates a new terminal with the given grid dimensions.
 *
 * @param cols           Number of columns.
 * @param rows           Number of rows.
 * @param max_scrollback Maximum scrollback lines.
 * @return Terminal handle, or NULL on failure.
 */
ghostty_terminal_t ghostty_terminal_new(
    uint32_t cols,
    uint32_t rows,
    uint32_t max_scrollback);

/**
 * Destroys a terminal and releases all resources.
 */
void ghostty_terminal_free(ghostty_terminal_t terminal);

/* ═══════════════════════════════════════════════════════════════════════ */
/* Response callback                                                      */
/* ═══════════════════════════════════════════════════════════════════════ */

/**
 * Callback function type for terminal query responses.
 *
 * When the terminal encounters a query escape sequence (DSR cursor position,
 * DA device attributes, DECRQM mode request, etc.), it formats the
 * appropriate response and delivers it via this callback.
 *
 * @param data     Pointer to response bytes.
 * @param len      Number of response bytes.
 * @param userdata Opaque pointer passed when the callback was set.
 */
typedef void (*ghostty_terminal_response_callback_t)(
    const uint8_t* data,
    size_t len,
    void* userdata);

/**
 * Sets or clears the response callback.
 *
 * Supported query types:
 *   - DSR (Device Status Report): operating status, cursor position
 *   - DA  (Device Attributes): primary, secondary, tertiary
 *   - DECRQM (Request Mode): reports set/reset/unknown for any mode
 *   - XTVERSION: terminal identification
 *   - Kitty keyboard protocol query
 *
 * @param terminal Terminal handle.
 * @param callback Callback function, or NULL to disable responses.
 * @param userdata Opaque pointer passed through to callback.
 */
void ghostty_terminal_set_response_callback(
    ghostty_terminal_t terminal,
    ghostty_terminal_response_callback_t callback,
    void* userdata);

/* ═══════════════════════════════════════════════════════════════════════ */
/* Data processing                                                        */
/* ═══════════════════════════════════════════════════════════════════════ */

/**
 * Feeds raw terminal output data through the VT processor.
 * This updates internal screen state. If a response callback is set,
 * query sequences (DSR, DA, etc.) will trigger the callback with
 * the appropriate response bytes.
 *
 * @param terminal Terminal handle.
 * @param data     Pointer to VT data bytes.
 * @param len      Number of bytes.
 */
void ghostty_terminal_process(
    ghostty_terminal_t terminal,
    const uint8_t* data,
    size_t len);

/* ═══════════════════════════════════════════════════════════════════════ */
/* Screen state                                                           */
/* ═══════════════════════════════════════════════════════════════════════ */

/** Returns the number of columns. */
uint32_t ghostty_terminal_get_cols(ghostty_terminal_t terminal);

/** Returns the number of rows. */
uint32_t ghostty_terminal_get_rows(ghostty_terminal_t terminal);

/**
 * Fills cursor information into the provided struct.
 *
 * @param terminal Terminal handle.
 * @param out      Pointer to cursor info struct to fill.
 */
void ghostty_terminal_get_cursor(
    ghostty_terminal_t terminal,
    ghostty_terminal_cursor_info_t* out);

/**
 * Fills cell information for a viewport row.
 *
 * @param terminal  Terminal handle.
 * @param row_idx   Viewport row (0-based from top).
 * @param cells_out Array of cell info structs to fill.
 * @param max_cells Maximum number of cells to fill (length of cells_out).
 * @return Number of cells filled.
 */
uint32_t ghostty_terminal_get_row_cells(
    ghostty_terminal_t terminal,
    uint32_t row_idx,
    ghostty_terminal_cell_info_t* cells_out,
    uint32_t max_cells);

/**
 * Fills cell information for a viewport row and optionally exports grapheme
 * spans plus flattened trailing grapheme codepoints.
 *
 * The first codepoint of each grapheme is always in `cells_out[i].codepoint`.
 * Additional grapheme codepoints are emitted to `grapheme_codepoints_out` and
 * indexed by `grapheme_spans_out[i]`.
 *
 * @param terminal                     Terminal handle.
 * @param row_idx                      Viewport row (0-based from top).
 * @param cells_out                    Array of cell info structs to fill.
 * @param max_cells                    Maximum number of cells to fill.
 * @param grapheme_spans_out           Array of grapheme spans, one per cell.
 * @param max_spans                    Maximum number of spans to fill.
 * @param grapheme_codepoints_out      Flattened UTF-32 trailing grapheme codepoints.
 * @param max_grapheme_codepoints      Capacity of `grapheme_codepoints_out`.
 * @param grapheme_codepoints_written  Optional out count of flattened codepoints written.
 * @return Number of cells filled.
 */
uint32_t ghostty_terminal_get_row_cells_with_graphemes(
    ghostty_terminal_t terminal,
    uint32_t row_idx,
    ghostty_terminal_cell_info_t* cells_out,
    uint32_t max_cells,
    ghostty_terminal_grapheme_span_t* grapheme_spans_out,
    uint32_t max_spans,
    uint32_t* grapheme_codepoints_out,
    uint32_t max_grapheme_codepoints,
    uint32_t* grapheme_codepoints_written);

/* ═══════════════════════════════════════════════════════════════════════ */
/* Notification callback                                                  */
/* ═══════════════════════════════════════════════════════════════════════ */

/**
 * Callback function type for terminal notifications (bell, title change).
 *
 * @param event_type  1 = bell, 2 = window title changed.
 * @param data        Pointer to event data (UTF-8 title for type 2, NULL for type 1).
 * @param len         Length of event data in bytes.
 * @param userdata    Opaque pointer passed when the callback was set.
 */
typedef void (*ghostty_terminal_notification_callback_t)(
    uint8_t event_type,
    const uint8_t* data,
    size_t len,
    void* userdata);

/**
 * Sets or clears the notification callback.
 *
 * @param terminal Terminal handle.
 * @param callback Callback function, or NULL to disable notifications.
 * @param userdata Opaque pointer passed through to callback.
 */
void ghostty_terminal_set_notification_callback(
    ghostty_terminal_t terminal,
    ghostty_terminal_notification_callback_t callback,
    void* userdata);

/* ═══════════════════════════════════════════════════════════════════════ */
/* Resize                                                                 */
/* ═══════════════════════════════════════════════════════════════════════ */

/**
 * Resizes the terminal grid to new dimensions.
 * Both cols and rows must be > 0.
 */
void ghostty_terminal_resize(
    ghostty_terminal_t terminal,
    uint32_t cols,
    uint32_t rows);

/**
 * Resizes the terminal grid with pixel dimensions.
 * Pixel dimensions are used for CSI 14t/16t size reports.
 * Both cols and rows must be > 0.
 *
 * @param terminal  Terminal handle.
 * @param cols      Number of columns.
 * @param rows      Number of rows.
 * @param width_px  Width in pixels.
 * @param height_px Height in pixels.
 */
void ghostty_terminal_resize_with_pixels(
    ghostty_terminal_t terminal,
    uint32_t cols,
    uint32_t rows,
    uint32_t width_px,
    uint32_t height_px);

/* ═══════════════════════════════════════════════════════════════════════ */
/* Mode queries                                                           */
/* ═══════════════════════════════════════════════════════════════════════ */

/** Returns 1 if application cursor key mode (DECCKM) is active. */
uint8_t ghostty_terminal_get_mode_app_cursor(ghostty_terminal_t terminal);

/** Returns 1 if application keypad mode is active. */
uint8_t ghostty_terminal_get_mode_app_keypad(ghostty_terminal_t terminal);

/** Returns 1 if bracketed paste mode is active. */
uint8_t ghostty_terminal_get_mode_bracketed_paste(ghostty_terminal_t terminal);

/** Returns 1 if the alternate screen buffer is active. */
uint8_t ghostty_terminal_get_mode_alt_screen(ghostty_terminal_t terminal);

#ifdef __cplusplus
}
#endif

#endif /* GHOSTTY_TERMINAL_H */
