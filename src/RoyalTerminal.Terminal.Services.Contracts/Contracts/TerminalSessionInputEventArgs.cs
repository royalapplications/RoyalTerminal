// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Services.Contracts - Session input event payload.

namespace RoyalTerminal.Terminal.Services;

/// <summary>
/// Event payload raised when input bytes are sent through a terminal session.
/// </summary>
public sealed class TerminalSessionInputEventArgs : EventArgs
{
    /// <summary>UTF-8 bytes sent to the endpoint/transport/PTY.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    public TerminalSessionInputEventArgs(ReadOnlyMemory<byte> data)
    {
        Data = data;
    }
}
