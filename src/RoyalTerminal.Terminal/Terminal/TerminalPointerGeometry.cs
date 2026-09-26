// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

// Ghostty renderer/size.zig and input/mouse_encode.zig conversions. The C API
// receives f32 surface positions; normalize once to that precision in both engines.
internal readonly record struct TerminalPointerGeometry(
    TerminalPointerEncodingContext Context, int Column, int Row, int PixelX, int PixelY, bool Outside)
{
    internal static bool TryCreate(in TerminalPointerEvent pointer, in TerminalPointerEncodingContext context, TerminalMouseEncoding encoding,
        out TerminalPointerGeometry geometry)
    {
        geometry = default;
        if (context.ScreenWidthPx <= 0 || context.ScreenHeightPx <= 0 ||
            context.CellWidthPx <= 0 || context.CellHeightPx <= 0) return false;
        float x = (float)pointer.X, y = (float)pointer.Y;
        if (!float.IsFinite(x) || !float.IsFinite(y)) return false;
        TerminalPointerEncodingContext normalized = context with
        {
            PaddingLeftPx = Math.Max(0, context.PaddingLeftPx), PaddingRightPx = Math.Max(0, context.PaddingRightPx),
            PaddingTopPx = Math.Max(0, context.PaddingTopPx), PaddingBottomPx = Math.Max(0, context.PaddingBottomPx),
        };
        long width = Math.Max(0L, (long)context.ScreenWidthPx - normalized.PaddingLeftPx - normalized.PaddingRightPx);
        long height = Math.Max(0L, (long)context.ScreenHeightPx - normalized.PaddingTopPx - normalized.PaddingBottomPx);
        float columns = (float)width / context.CellWidthPx, rows = (float)height / context.CellHeightPx;
        double terminalX = (double)x - normalized.PaddingLeftPx, terminalY = (double)y - normalized.PaddingTopPx;
        double column = Math.Max(0, terminalX) / context.CellWidthPx, row = Math.Max(0, terminalY) / context.CellHeightPx;
        // Cell protocols never convert negative terminal coordinates to signed
        // pixels; even far-outside releases can safely clamp to the first cell.
        double pixelX = 0, pixelY = 0;
        if (encoding == TerminalMouseEncoding.SgrPixels)
        {
            pixelX = Math.Round(terminalX, MidpointRounding.AwayFromZero);
            pixelY = Math.Round(terminalY, MidpointRounding.AwayFromZero);
        }
        // Reject unrepresentable host input before native float-to-int conversions.
        if (columns >= 65536 || rows >= 65536 || column >= 65536 || row >= 65536 ||
            pixelX < int.MinValue || pixelX > int.MaxValue || pixelY < int.MinValue || pixelY > int.MaxValue) return false;
        geometry = new(normalized, Math.Min((int)column, Math.Max(1, (int)columns) - 1) + 1,
            Math.Min((int)row, Math.Max(1, (int)rows) - 1) + 1, (int)pixelX, (int)pixelY,
            x < 0 || y < 0 || x > (float)context.ScreenWidthPx || y > (float)context.ScreenHeightPx);
        return true;
    }
}
