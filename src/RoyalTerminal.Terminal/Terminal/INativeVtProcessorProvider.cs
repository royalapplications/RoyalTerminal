// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Native VT provider abstraction.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Provides an optional native VT processor implementation.
/// </summary>
public interface INativeVtProcessorProvider
{
    /// <summary>
    /// Gets a value indicating whether this provider is currently available.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Creates a VT processor instance for the provided screen.
    /// </summary>
    IVtProcessor Create(TerminalScreen screen);
}
