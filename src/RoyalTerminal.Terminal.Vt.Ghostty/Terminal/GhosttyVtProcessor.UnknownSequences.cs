// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor
{
    private int _unknownSequenceMaxBytes = 4096;

    /// <inheritdoc />
    public int UnknownSequenceMaxBytes
    {
        get => _unknownSequenceMaxBytes;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _terminal.SetUnknownSequenceMaxBytes((nuint)value);
            _unknownSequenceMaxBytes = value;
        }
    }
}
