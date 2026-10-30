// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Semantic shell lifecycle step reported by OSC 133.</summary>
public enum TerminalSemanticPromptKind
{
    /// <summary>A primary or secondary prompt starts.</summary>
    PromptStart = 1,
    /// <summary>The shell starts accepting input.</summary>
    InputStart,
    /// <summary>The command starts producing output.</summary>
    OutputStart,
    /// <summary>The command completes.</summary>
    CommandEnd,
}

/// <summary>Placement or role of a semantic prompt.</summary>
public enum TerminalSemanticPromptRole
{
    /// <summary>Main command prompt.</summary>
    Primary,
    /// <summary>Prompt at the right edge.</summary>
    Right,
    /// <summary>Continuation of a multiline prompt.</summary>
    Continuation,
    /// <summary>Secondary prompt inside a command.</summary>
    Secondary,
}

/// <summary>Owned data from a validated semantic-prompt command.</summary>
/// <param name="Kind">Lifecycle step.</param>
/// <param name="Role">Prompt role, or Primary for non-prompt steps.</param>
/// <param name="ExitCode">Reported signed exit code; null when absent or invalid.</param>
/// <param name="Command">Decoded command bytes, preserving arbitrary percent-encoded data.</param>
/// <param name="Error">Error option bytes, without interpreting or executing them.</param>
public sealed record TerminalSemanticPromptReport(TerminalSemanticPromptKind Kind,
    TerminalSemanticPromptRole Role, int? ExitCode, ReadOnlyMemory<byte> Command, ReadOnlyMemory<byte> Error);

/// <summary>Ordered protocol lifecycle effects, delivered after terminal state changes.</summary>
public interface ITerminalLifecycleEffectSource
{
    /// <summary>OSC 133 A/N/P, B/I, C and D events. Fresh-line L does not emit an event.</summary>
    Action<TerminalSemanticPromptReport>? SemanticPromptCallback { get; set; }

    /// <summary>Called after protocol RIS. Programmatic resets and DECSTR do not emit this effect.</summary>
    Action? ResetCallback { get; set; }
}
