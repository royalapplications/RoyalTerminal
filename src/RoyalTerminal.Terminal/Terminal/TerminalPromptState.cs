// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>Shell policy for clearing prompt cells after a grid resize.</summary>
public enum TerminalPromptRedraw : byte
{
    /// <summary>The shell redraws every prompt line.</summary>
    All,
    /// <summary>The shell cannot redraw its prompt.</summary>
    None,
    /// <summary>The shell redraws only the cursor's current physical line.</summary>
    Last,
}

/// <summary>How OSC 133 asks the host to route clicks in the current prompt.</summary>
public enum TerminalPromptClick : byte
{
    /// <summary>No prompt click handling was requested.</summary>
    None,
    /// <summary>Send an SGR press with viewport coordinates.</summary>
    Absolute,
    /// <summary>Send an SGR press with a prompt-relative row.</summary>
    Relative,
    /// <summary>Use left/right keys within one input line.</summary>
    Line,
    /// <summary>Use left/right keys across input lines.</summary>
    Multiple,
    /// <summary>Request conservative vertical movement.</summary>
    ConservativeVertical,
    /// <summary>Request editor-aware vertical movement.</summary>
    SmartVertical,
}

/// <summary>Copied live shell metadata for the active screen.</summary>
/// <param name="Seen">Whether this screen has ever received a prompt marker.</param>
/// <param name="Content">Classification of the next printed cell.</param>
/// <param name="ClearAtEndOfLine">Whether an explicit newline ends input classification.</param>
/// <param name="Click">The most recent valid click policy for this screen.</param>
/// <param name="Redraw">The terminal-wide resize/redraw policy.</param>
public readonly record struct TerminalPromptState(bool Seen, TerminalSemanticContent Content,
    bool ClearAtEndOfLine, TerminalPromptClick Click, TerminalPromptRedraw Redraw);

/// <summary>Reads shell metadata without consuming shell integration events.</summary>
public interface ITerminalPromptStateSource
{
    /// <summary>Gets the live active-screen policy; serialize with processor access.</summary>
    TerminalPromptState PromptState { get; }
}
