// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing;

/// <summary>Permissions granted by the host to an attachment.</summary>
public enum MultiplexerRole
{
    /// <summary>Receives output and dimensions without changing the process.</summary>
    Observer,
    /// <summary>Exclusively owns input and shared terminal dimensions until detached.</summary>
    Controller,
}

/// <summary>Lifecycle of a server-owned terminal block.</summary>
public enum MultiplexerBlockState
{
    /// <summary>The transport is being started.</summary>
    Starting,
    /// <summary>The transport has started.</summary>
    Running,
    /// <summary>The process exited; retained output remains inspectable.</summary>
    Exited,
    /// <summary>The block was explicitly closed.</summary>
    Closed,
}

/// <summary>Ordered kinds of a block stream.</summary>
public enum MultiplexerEventKind
{
    /// <summary>Unmodified terminal output bytes.</summary>
    Output,
    /// <summary>Authoritative terminal geometry, including the initial dimensions.</summary>
    Resize,
    /// <summary>Process completion, emitted at most once.</summary>
    Exit,
    /// <summary>The block was explicitly closed.</summary>
    Closed,
}

/// <summary>An owned event. Sequence numbers begin at one and never reset within a block.</summary>
/// <param name="Sequence">Monotonic per-block stream cursor.</param>
/// <param name="Kind">Payload discriminator.</param>
/// <param name="Data">Output bytes owned by this event and its recipient.</param>
/// <param name="Dimensions">Dimensions for a resize event.</param>
/// <param name="ExitCode">Code for an exit event.</param>
/// <param name="Message">Optional diagnostic message.</param>
public sealed record MultiplexerEvent(long Sequence, MultiplexerEventKind Kind,
    byte[]? Data = null, TerminalSessionDimensions? Dimensions = null,
    int? ExitCode = null, string? Message = null);

/// <summary>Orientation of a binary split in a session window.</summary>
public enum MultiplexerSplitOrientation
{
    /// <summary>Children are beside each other.</summary>
    Horizontal,
    /// <summary>Children are above and below each other.</summary>
    Vertical,
}

/// <summary>Immutable layout node: a block leaf or a split with exactly two children.</summary>
/// <param name="BlockId">Block identity for a leaf; null for a split.</param>
/// <param name="Orientation">Split orientation; null for a leaf.</param>
/// <param name="Ratio">First child's fraction, strictly between zero and one.</param>
/// <param name="First">First split child.</param>
/// <param name="Second">Second split child.</param>
public sealed record MultiplexerLayoutNode(Guid? BlockId = null,
    MultiplexerSplitOrientation? Orientation = null, double Ratio = 0.5,
    MultiplexerLayoutNode? First = null, MultiplexerLayoutNode? Second = null)
{
    /// <summary>Creates a leaf for one block.</summary>
    public static MultiplexerLayoutNode Leaf(Guid blockId) => new(BlockId: blockId);

    /// <summary>Creates a binary split; the broker validates all leaves and ratios on insertion.</summary>
    public static MultiplexerLayoutNode Split(MultiplexerSplitOrientation orientation,
        MultiplexerLayoutNode first, MultiplexerLayoutNode second, double ratio = 0.5)
        => new(Orientation: orientation, Ratio: ratio, First: first, Second: second);
}

/// <summary>An immutable window snapshot. A null layout means there are no tiled blocks.</summary>
/// <param name="Id">Window identity.</param>
/// <param name="Label">Human-readable label.</param>
/// <param name="Layout">Binary split tree.</param>
public sealed record MultiplexerWindowDescriptor(Guid Id, string Label, MultiplexerLayoutNode? Layout);

/// <summary>An immutable block snapshot; its dimensions are shared by all viewers.</summary>
/// <param name="Id">Block identity.</param>
/// <param name="SessionId">Owning session.</param>
/// <param name="WindowId">Home window, including for an unplaced block.</param>
/// <param name="Label">Human-readable label.</param>
/// <param name="State">Current lifecycle.</param>
/// <param name="Dimensions">Authoritative grid and pixel size.</param>
/// <param name="ExitCode">Observed process exit code, if any.</param>
/// <param name="LastSequence">Latest published event cursor.</param>
/// <param name="FirstRetainedSequence">Oldest retained event; later starting points require an existing client state.</param>
/// <param name="AttachmentCount">Current attachment count.</param>
/// <param name="ControllerId">Exclusive input/resize owner, if attached.</param>
public sealed record MultiplexerBlockDescriptor(Guid Id, Guid SessionId, Guid WindowId,
    string Label, MultiplexerBlockState State, TerminalSessionDimensions Dimensions,
    int? ExitCode, long LastSequence, long FirstRetainedSequence, int AttachmentCount,
    Guid? ControllerId)
{
    /// <summary>Current position independent of the lifetime of the underlying process.</summary>
    public MultiplexerPaneKind Placement { get; init; }
    /// <summary>Normalized window coordinates for a floating block; null for other placements.</summary>
    public MultiplexerFloatBounds? FloatingBounds { get; init; }
}

/// <summary>An immutable session snapshot. Revision tracks structural and label changes.</summary>
/// <param name="Id">Session identity.</param>
/// <param name="Label">Human-readable label.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
/// <param name="CreatedUtc">Creation timestamp.</param>
/// <param name="Windows">Owned windows in display order.</param>
/// <param name="Blocks">Owned terminal blocks in creation order.</param>
public sealed record MultiplexerSessionDescriptor(Guid Id, string Label, long Revision,
    DateTimeOffset CreatedUtc, IReadOnlyList<MultiplexerWindowDescriptor> Windows,
    IReadOnlyList<MultiplexerBlockDescriptor> Blocks);

/// <summary>Stable error categories for hosts to map to their wire protocol.</summary>
public enum MultiplexerErrorCode
{
    /// <summary>The requested identity does not exist.</summary>
    NotFound,
    /// <summary>A revision or control owner conflicts with the request.</summary>
    Conflict,
    /// <summary>The attachment lacks permission.</summary>
    Forbidden,
    /// <summary>The requested replay cursor precedes retained history.</summary>
    HistoryGap,
    /// <summary>A configured resource limit was reached.</summary>
    CapacityExceeded,
    /// <summary>The attachment or transport is no longer active.</summary>
    Disconnected,
}

/// <summary>An expected multiplexer operation failure, safe for a host to classify.</summary>
public sealed class MultiplexerException : InvalidOperationException
{
    /// <summary>Creates an operation error with a stable category.</summary>
    public MultiplexerException(MultiplexerErrorCode code, string message) : base(message) => Code = code;
    /// <summary>Gets the error category.</summary>
    public MultiplexerErrorCode Code { get; }
}
