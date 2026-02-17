// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal — Factory abstraction for VT processors.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Factory for creating terminal VT processors.
/// </summary>
public interface IVtProcessorFactory
{
    /// <summary>
    /// Creates a VT processor for the provided screen.
    /// </summary>
    /// <param name="screen">Terminal screen model.</param>
    /// <param name="preference">Explicit processor preference.</param>
    /// <returns>A configured VT processor instance.</returns>
    IVtProcessor Create(TerminalScreen screen, VtProcessorPreference preference);
}
