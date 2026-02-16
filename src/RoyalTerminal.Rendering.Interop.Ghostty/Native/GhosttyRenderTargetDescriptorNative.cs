// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Interop.Ghostty - Native render target descriptor layout.

using System.Runtime.InteropServices;
using RoyalTerminal.Rendering.Contracts;

namespace RoyalTerminal.Rendering.Interop.Ghostty.Native;

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
