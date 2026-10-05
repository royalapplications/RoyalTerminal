// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>Adapts synchronous Ghostty writer callbacks to managed streams.</summary>
internal static class GhosttyStreamWriter
{
    internal delegate GhosttyVtNative.GhosttyResult WriteOperation(
        GhosttyVtNative.GhosttyWriter writer);

    internal static unsafe void Write(
        Stream destination,
        WriteOperation operation,
        string operationName)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(operation);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream is not writable.", nameof(destination));
        }

        WriterContext context = new(destination);
        GCHandle contextHandle = GCHandle.Alloc(context);
        try
        {
            GhosttyVtNative.GhosttyWriter writer = new(
                (nint)(delegate* unmanaged[Cdecl]<nint, byte*, nuint, byte>)&WriteCallback,
                GCHandle.ToIntPtr(contextHandle));
            GhosttyVtNative.GhosttyResult result = operation(writer);
            context.Failure?.Throw();
            if (result != GhosttyVtNative.GhosttyResult.Success)
            {
                throw new InvalidOperationException($"{operationName} failed with {result}.");
            }
        }
        finally
        {
            contextHandle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe byte WriteCallback(nint userdata, byte* data, nuint length)
    {
        WriterContext? context = GCHandle.FromIntPtr(userdata).Target as WriterContext;
        if (context is null || length > int.MaxValue)
        {
            return 0;
        }

        try
        {
            context.Stream.Write(new ReadOnlySpan<byte>(data, checked((int)length)));
            return 1;
        }
        catch (Exception exception)
        {
            context.Failure = ExceptionDispatchInfo.Capture(exception);
            return 0;
        }
    }

    private sealed class WriterContext(Stream stream)
    {
        internal Stream Stream { get; } = stream;

        internal ExceptionDispatchInfo? Failure { get; set; }
    }
}
