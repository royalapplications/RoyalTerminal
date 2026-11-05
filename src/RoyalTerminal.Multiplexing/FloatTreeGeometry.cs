// Copyright (c) Royal Apps. Licensed under the MIT license.
namespace RoyalTerminal.Multiplexing;

/// <summary>Shared validation and rounding for floating trees. Children partition their parent without gaps.</summary>
public static class FloatTreeGeometry
{
    /// <summary>Returns leaf rectangles in layout order. Invalid or unrepresentable layouts fail before mutation.</summary>
    public static IReadOnlyDictionary<Guid, MultiplexerFloatBounds> Project(MultiplexerLayoutNode root, MultiplexerFloatBounds frame)
    {
        ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(frame); frame.Validate();
        var result = new Dictionary<Guid, MultiplexerFloatBounds>();
        Visit(root, frame, 0);
        return result;

        void Visit(MultiplexerLayoutNode node, MultiplexerFloatBounds r, int depth)
        {
            if (depth > 32) throw new ArgumentException("Floating tree is too deep.");
            if (node.BlockId is Guid id)
            {
                if (id == Guid.Empty || node.First is not null || node.Second is not null || node.Orientation.HasValue || !result.TryAdd(id,r))
                    throw new ArgumentException("A floating leaf must identify one distinct block.");
                return;
            }
            if (node.First is null || node.Second is null || node.Orientation is not (MultiplexerSplitOrientation.Horizontal or MultiplexerSplitOrientation.Vertical) || !(node.Ratio > 0 && node.Ratio < 1))
                throw new ArgumentException("A floating divider requires two children and a finite interior ratio.");
            bool horizontal = node.Orientation == MultiplexerSplitOrientation.Horizontal;
            int span = horizontal ? r.Width : r.Height;
            if (span < 2) throw new ArgumentException("Floating frame is too small for this split.");
            int a = Math.Clamp((int)Math.Round(span * node.Ratio, MidpointRounding.AwayFromZero),1,span-1);
            Visit(node.First, horizontal ? r with { Width=a } : r with { Height=a }, depth+1);
            Visit(node.Second, horizontal ? r with { X=r.X+a, Width=span-a } : r with { Y=r.Y+a, Height=span-a }, depth+1);
        }
    }
}
