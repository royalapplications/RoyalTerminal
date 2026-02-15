// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Interop - SafeHandle for ghostty_render_context_t.

using System.Runtime.InteropServices;
using RoyalTerminal.Rendering.Interop.Native;

namespace RoyalTerminal.Rendering.Interop;

/// <summary>
/// Safe handle wrapper for a native <c>ghostty_render_context_t</c>.
/// </summary>
internal sealed class GhosttyRenderContextHandle : SafeHandle
{
    /// <summary>
    /// Initializes an empty handle instance.
    /// </summary>
    public GhosttyRenderContextHandle()
        : base(nint.Zero, ownsHandle: true)
    {
    }

    internal GhosttyRenderContextHandle(nint handle)
        : this()
    {
        SetHandle(handle);
    }

    /// <inheritdoc />
    public override bool IsInvalid => handle == nint.Zero;

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        if (!IsInvalid)
        {
            GhosttyRendererNative.ContextFree(handle);
        }

        return true;
    }
}
