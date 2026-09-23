// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>OSC 133 classification captured when a cell is printed.</summary>
public enum TerminalSemanticContent : byte
{
    /// <summary>Command output, or text printed without shell integration.</summary>
    Output = 0,
    /// <summary>User input following a prompt.</summary>
    Input = 1,
    /// <summary>Text belonging to a shell prompt.</summary>
    Prompt = 2,
}

/// <summary>OSC 133 prompt marker on a physical terminal row, independently of its cells.</summary>
public enum TerminalSemanticPrompt : byte
{
    /// <summary>No prompt marker.</summary>
    None = 0,
    /// <summary>An initial or right-side prompt.</summary>
    Prompt = 1,
    /// <summary>A continued or secondary prompt.</summary>
    PromptContinuation = 2,
}
