// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;
using static RoyalTerminal.GhosttySharp.Native.GhosttyVtNative;

namespace RoyalTerminal.IntegrationTests;

public unsafe class GhosttyPasteAllocationTests
{
    [GhosttyNativeFact]
    public void ReaderCannotHideARefusedWriteOrPasteAPartialBuffer()
    {
        Context context = default;
        GhosttyAllocatorVtable callbacks = new() { Alloc = &Allocate, Resize = &Resize, Remap = &Remap, Free = &Release };
        GhosttyAllocator allocator = new() { Context = &context, Vtable = &callbacks };
        Assert.Equal(GhosttyResult.Success, TerminalNew((nint)(&allocator), out nint terminal, 80, 24));
        try
        {
            Assert.Equal(GhosttyResult.Success, TerminalSet(terminal, GhosttyTerminalOption.Userdata, &context));
            Assert.Equal(GhosttyResult.Success, TerminalSet(terminal, GhosttyTerminalOption.WritePty,
                (void*)(delegate* unmanaged[Cdecl]<nint, nint, nint, nuint, void>)&WritePty));
            fixed (byte* mimeBytes = "text/plain"u8)
            {
                GhosttyString mime = new((nint)mimeBytes, 10);
                GhosttyPasteRequest request = GhosttyPasteRequest.CreateSized();
                request.Mimes = &mime;
                request.MimesLength = 1;
                request.Reader = new((nint)(delegate* unmanaged[Cdecl]<nint, GhosttyString, GhosttyWriter, byte>)&Read,
                    (nint)(&context));
                context.Fail = true;
                // paste.h defines out_written only on success. On failure,
                // verify the PTY effect directly instead of reading unspecified data.
                Assert.Equal(GhosttyResult.OutOfMemory, TerminalPaste(terminal, &request, out _));
                Assert.Equal(1, context.Reads);
                Assert.Equal(1, context.Refusals);
                Assert.Equal(0, context.Writes);
                Assert.Equal(0, context.BytesWritten);

                context.Fail = false;
                Assert.Equal(GhosttyResult.Success, TerminalPaste(terminal, &request, out bool written));
                Assert.True(written);
                Assert.Equal(2, context.Reads);
                Assert.Equal(1, context.Refusals);
                Assert.True(context.Writes > 0);
                Assert.Equal(5, context.BytesWritten);
            }
        }
        finally { TerminalFree(terminal); }
    }

    private struct Context { public bool Fail; public int Reads; public int Refusals; public int Writes; public int BytesWritten; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void* Allocate(void* context, nuint length, byte alignment, nuint returnAddress)
    {
        if (((Context*)context)->Fail) return null;
        nuint boundary = Math.Max((nuint)IntPtr.Size, (nuint)1 << alignment);
        try { return NativeMemory.AlignedAlloc((length + boundary - 1) & ~(boundary - 1), boundary); }
        catch (OutOfMemoryException) { return null; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte Resize(void* context, void* memory, nuint oldLength, byte alignment, nuint newLength, nuint address) => 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void* Remap(void* context, void* memory, nuint oldLength, byte alignment, nuint newLength, nuint address) => null;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Release(void* context, void* memory, nuint length, byte alignment, nuint address) => NativeMemory.AlignedFree(memory);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte Read(nint userdata, GhosttyString mime, GhosttyWriter writer)
    {
        Context* context = (Context*)userdata;
        context->Reads++;
        fixed (byte* text = "hello"u8)
            if (((delegate* unmanaged[Cdecl]<nint, byte*, nuint, byte>)writer.Write)(writer.Userdata, text, 5) == 0)
                context->Refusals++;
        return 1; // Deliberately violates the reader contract; native must still reject the paste.
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void WritePty(nint terminal, nint userdata, nint data, nuint length)
    {
        ((Context*)userdata)->Writes++;
        ((Context*)userdata)->BytesWritten += (int)length;
    }
}
