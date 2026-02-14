// Licensed under the MIT License.
// GhosttySharp.Rendering.Interop - Native render target descriptor layout.

using System.Runtime.InteropServices;
using GhosttySharp.Rendering.Contracts;

namespace GhosttySharp.Rendering.Interop.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyRenderTargetDescriptorNative
{
    public RenderBackendKind Backend;
    public RenderTargetKind TargetKind;
    public RenderPixelFormat PixelFormat;

    public int Width;
    public int Height;
    public uint SampleCount;

    public nint DeviceHandle;
    public nint ContextHandle;
    public nint CommandQueueHandle;
    public nint CommandBufferHandle;
    public nint TargetHandle;
    public nint TargetViewHandle;

    public ulong FrameId;
    public nint DebugNameUtf8;
}
