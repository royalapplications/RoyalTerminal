// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Interop - Native renderer result code mapping.

namespace RoyalTerminal.Rendering.Interop;

/// <summary>
/// Represents renderer C API result codes projected into managed code.
/// </summary>
public enum GhosttyRenderInteropResult
{
    /// <summary>Unknown or unmapped native result.</summary>
    Unknown = -1,

    /// <summary>Operation succeeded.</summary>
    Ok = 0,

    /// <summary>One or more arguments are invalid.</summary>
    InvalidArgument = 1,

    /// <summary>The requested backend is not implemented.</summary>
    UnsupportedBackend = 2,

    /// <summary>The requested backend is unsupported on the current platform.</summary>
    UnsupportedPlatform = 3,

    /// <summary>The target descriptor failed validation.</summary>
    InvalidTarget = 4,

    /// <summary>The renderer failed while executing the operation.</summary>
    RenderFailed = 5,

    /// <summary>The renderer failed due to memory allocation failure.</summary>
    OutOfMemory = 6,
}
