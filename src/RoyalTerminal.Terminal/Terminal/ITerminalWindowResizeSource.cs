// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>A CSI 8 t request to resize the host's terminal text area in cells.</summary>
/// <param name="Columns">Requested columns; zero preserves the current width.</param>
/// <param name="Rows">Requested rows; zero preserves the current height.</param>
public readonly record struct TerminalWindowResizeRequest(ushort Columns, ushort Rows);

/// <summary>Optional host-policy boundary for terminal-originated window resizing.</summary>
public interface ITerminalWindowResizeSource
{
    /// <summary>
    /// Observes valid CSI 8 t requests synchronously on the parser thread. Null
    /// disables delivery. The host must explicitly permit resizing, marshal to
    /// its UI thread and reject unsuitable windows/layouts. Requests never resize
    /// the terminal grid directly, and zero dimensions mean preserve, not maximize.
    /// A callback must not throw, block, or re-enter the processor.
    /// </summary>
    Action<TerminalWindowResizeRequest>? WindowResizeCallback { get; set; }
}

/// <summary>Embedding host that applies window-resize policy on the UI thread.</summary>
public interface ITerminalWindowResizeHost
{
    /// <summary>
    /// Processes a request after the terminal control's explicit opt-in gate.
    /// Recheck window/layout eligibility and apply supported dimensions only.
    /// Do not resize the terminal grid directly: normal layout updates it.
    /// </summary>
    void RequestResize(TerminalWindowResizeRequest request);
}
