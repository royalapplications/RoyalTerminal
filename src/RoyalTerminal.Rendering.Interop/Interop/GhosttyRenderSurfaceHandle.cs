// Licensed under the MIT License.
// GhosttySharp.Rendering.Interop - SafeHandle for ghostty_render_surface_t.

using System.Runtime.InteropServices;
using GhosttySharp.Rendering.Interop.Native;

namespace GhosttySharp.Rendering.Interop;

/// <summary>
/// Safe handle wrapper for a native <c>ghostty_render_surface_t</c>.
/// </summary>
internal sealed class GhosttyRenderSurfaceHandle : SafeHandle
{
    /// <summary>
    /// Initializes an empty handle instance.
    /// </summary>
    public GhosttyRenderSurfaceHandle()
        : base(nint.Zero, ownsHandle: true)
    {
    }

    internal GhosttyRenderSurfaceHandle(nint handle)
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
            GhosttyRendererNative.SurfaceFree(handle);
        }

        return true;
    }
}
