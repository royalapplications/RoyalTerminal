// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Protocol-space placement geometry, retaining unsigned values until render projection.</summary>
internal readonly record struct ManagedKittyPlacementOptions(
    uint SourceX, uint SourceY, uint SourceWidth, uint SourceHeight,
    uint XOffset, uint YOffset, uint Columns, uint Rows, int Z)
{
    internal static ManagedKittyPlacementOptions From(ManagedKittyGraphicsCommand command, uint cellWidth, uint cellHeight)
        => new(command.Get('x'), command.Get('y'), command.Get('w'), command.Get('h'),
            cellWidth == 0 ? command.Get('X') : Math.Min(command.Get('X'), cellWidth - 1),
            cellHeight == 0 ? command.Get('Y') : Math.Min(command.Get('Y'), cellHeight - 1),
            command.Get('c'), command.Get('r'), command.GetSigned('z'));

    internal ManagedKittyPlacementGeometry Calculate(uint imageWidth, uint imageHeight, uint cellWidth, uint cellHeight)
    {
        uint sourceX = Math.Min(SourceX, imageWidth);
        uint sourceY = Math.Min(SourceY, imageHeight);
        uint sourceWidth = Math.Min(SourceWidth == 0 ? imageWidth : SourceWidth, imageWidth - sourceX);
        uint sourceHeight = Math.Min(SourceHeight == 0 ? imageHeight : SourceHeight, imageHeight - sourceY);
        uint offsetX = cellWidth == 0 ? 0 : Math.Min(XOffset, cellWidth - 1);
        uint offsetY = cellHeight == 0 ? 0 : Math.Min(YOffset, cellHeight - 1);
        uint width = sourceWidth;
        uint height = sourceHeight;
        if (Columns > 0 && Rows > 0)
        {
            width = Subtract(Multiply(cellWidth, Columns), offsetX);
            height = Subtract(Multiply(cellHeight, Rows), offsetY);
        }
        else if (Columns > 0)
        {
            width = Subtract(Multiply(cellWidth, Columns), offsetX);
            height = Scale(width, sourceHeight, sourceWidth);
        }
        else if (Rows > 0)
        {
            height = Subtract(Multiply(cellHeight, Rows), offsetY);
            width = Scale(height, sourceWidth, sourceHeight);
        }
        uint columns = Columns > 0 && Rows > 0 ? Columns : DivideCeiling(Add(width, offsetX), cellWidth);
        uint rows = Columns > 0 && Rows > 0 ? Rows : DivideCeiling(Add(height, offsetY), cellHeight);
        return new(sourceX, sourceY, sourceWidth, sourceHeight, offsetX, offsetY, width, height, columns, rows);
    }

    internal bool TryClipTop(uint imageWidth, uint imageHeight, uint cellWidth, uint cellHeight,
        uint count, uint span, out ManagedKittyPlacementOptions clipped)
    {
        clipped = this;
        if (count == 0 || count >= span) return false;
        ManagedKittyPlacementGeometry source = Calculate(imageWidth, imageHeight, cellWidth, cellHeight);
        uint crop = Rows > 0 ? (uint)((ulong)source.SourceHeight * count / span) : Multiply(cellHeight, count);
        if (crop >= source.SourceHeight) return false;
        clipped = this with { SourceX = source.SourceX, SourceY = source.SourceY + crop,
            SourceWidth = source.SourceWidth, SourceHeight = source.SourceHeight - crop,
            Rows = Rows > 0 ? Subtract(Rows, count) : 0 };
        return true;
    }

    internal bool TryClipBottom(uint imageWidth, uint imageHeight, uint cellWidth, uint cellHeight,
        uint count, uint span, out ManagedKittyPlacementOptions clipped)
    {
        clipped = this;
        if (count == 0 || count >= span) return false;
        ManagedKittyPlacementGeometry source = Calculate(imageWidth, imageHeight, cellWidth, cellHeight);
        if (source.SourceHeight == 0) return false;
        uint height;
        if (Rows > 0)
        {
            uint crop = (uint)((ulong)source.SourceHeight * count / span);
            if (crop >= source.SourceHeight) return false;
            height = source.SourceHeight - crop;
        }
        else
        {
            uint visible = Multiply(cellHeight, span - count);
            if (visible <= source.OffsetY) return false;
            height = Math.Min(source.SourceHeight, visible - source.OffsetY);
        }
        clipped = this with { SourceX = source.SourceX, SourceY = source.SourceY,
            SourceWidth = source.SourceWidth, SourceHeight = height,
            Rows = Rows > 0 ? Subtract(Rows, count) : 0 };
        return true;
    }

    private static uint Multiply(uint left, uint right) => (uint)Math.Min(uint.MaxValue, (ulong)left * right);
    private static uint Add(uint left, uint right) => (uint)Math.Min(uint.MaxValue, (ulong)left + right);
    private static uint Subtract(uint left, uint right) => left >= right ? left - right : 0;
    private static uint DivideCeiling(uint value, uint divisor) => divisor == 0 ? 0 : (uint)(((ulong)value + divisor - 1) / divisor);
    private static uint Scale(uint value, uint numerator, uint denominator)
        => denominator == 0 ? 0 : (uint)Math.Min(uint.MaxValue, ((ulong)value * numerator + denominator / 2) / denominator);
}

internal readonly record struct ManagedKittyPlacementGeometry(
    uint SourceX, uint SourceY, uint SourceWidth, uint SourceHeight,
    uint OffsetX, uint OffsetY, uint Width, uint Height, uint Columns, uint Rows);
