// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Managed helper for Ghostty's paste safety and encoding utilities.
/// </summary>
public static class GhosttyPaste
{
    /// <summary>
    /// Pastes one UTF-8 text representation through Ghostty's terminal-aware paste pipeline.
    /// </summary>
    public static bool PasteText(
        GhosttyTerminal terminal,
        string text,
        bool allowUnsafe = false,
        GhosttyVtNative.GhosttyPasteSource source = GhosttyVtNative.GhosttyPasteSource.Clipboard,
        GhosttyVtNative.GhosttyClipboardLocation location = GhosttyVtNative.GhosttyClipboardLocation.Standard)
    {
        ArgumentNullException.ThrowIfNull(text);
        Dictionary<string, ReadOnlyMemory<byte>> representations = new(StringComparer.OrdinalIgnoreCase)
        {
            ["text/plain;charset=utf-8"] = Encoding.UTF8.GetBytes(text),
        };
        return Paste(terminal, representations, allowUnsafe, source, location);
    }

    /// <summary>
    /// Pastes MIME representations through Ghostty. Data is pulled only for the representation
    /// selected by the terminal and streamed to its configured PTY writer.
    /// </summary>
    public static unsafe bool Paste(
        GhosttyTerminal terminal,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> representations,
        bool allowUnsafe = false,
        GhosttyVtNative.GhosttyPasteSource source = GhosttyVtNative.GhosttyPasteSource.Clipboard,
        GhosttyVtNative.GhosttyClipboardLocation location = GhosttyVtNative.GhosttyClipboardLocation.Standard)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(representations);

        PasteContext context = new(representations);
        GCHandle contextHandle = GCHandle.Alloc(context);
        GCHandle[] mimeHandles = new GCHandle[context.MimeBytes.Length];
        GhosttyVtNative.GhosttyString[] mimes = new GhosttyVtNative.GhosttyString[context.MimeBytes.Length];
        try
        {
            for (int i = 0; i < context.MimeBytes.Length; i++)
            {
                byte[] bytes = context.MimeBytes[i];
                mimeHandles[i] = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                mimes[i] = new GhosttyVtNative.GhosttyString(
                    mimeHandles[i].AddrOfPinnedObject(),
                    (nuint)bytes.Length);
            }

            fixed (GhosttyVtNative.GhosttyString* mimePointer = mimes)
            {
                GhosttyVtNative.GhosttyPasteRequest request =
                    GhosttyVtNative.GhosttyPasteRequest.CreateSized();
                request.Location = location;
                request.Source = source;
                request.Mimes = mimePointer;
                request.MimesLength = (nuint)mimes.Length;
                request.Reader = new GhosttyVtNative.GhosttyMimeReader(
                    (nint)(delegate* unmanaged[Cdecl]<nint, GhosttyVtNative.GhosttyString,
                        GhosttyVtNative.GhosttyWriter, byte>)&ReadMime,
                    GCHandle.ToIntPtr(contextHandle));
                request.AllowUnsafe = allowUnsafe;

                GhosttyVtNative.GhosttyResult result = GhosttyVtNative.TerminalPaste(
                    terminal.Handle,
                    &request,
                    out bool written);
                ThrowIfFailed(result, "ghostty_terminal_paste");
                return written;
            }
        }
        finally
        {
            for (int i = 0; i < mimeHandles.Length; i++)
            {
                if (mimeHandles[i].IsAllocated)
                {
                    mimeHandles[i].Free();
                }
            }

            contextHandle.Free();
        }
    }

    /// <summary>
    /// Returns whether the supplied text is considered safe to paste.
    /// </summary>
    public static unsafe bool IsSafe(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        NativeLibraryLoader.Initialize();

        byte[] data = Encoding.UTF8.GetBytes(text);
        fixed (byte* dataPtr = data)
        {
            return GhosttyVtNative.PasteIsSafe(dataPtr, checked((nuint)data.Length));
        }
    }

    /// <summary>
    /// Encodes the supplied text using Ghostty's native paste encoder.
    /// </summary>
    public static unsafe byte[] Encode(string text, bool bracketedPaste)
    {
        ArgumentNullException.ThrowIfNull(text);
        NativeLibraryLoader.Initialize();

        byte[] data = Encoding.UTF8.GetBytes(text);
        nuint required = 0;
        fixed (byte* dataPtr = data)
        {
            GhosttyVtNative.GhosttyResult probe = GhosttyVtNative.PasteEncode(
                dataPtr,
                checked((nuint)data.Length),
                bracketedPaste,
                null,
                0,
                out required);
            if (probe != GhosttyVtNative.GhosttyResult.OutOfSpace)
            {
                ThrowIfFailed(probe, "ghostty_paste_encode(probe)");
            }

            if (required == 0)
            {
                return [];
            }

            byte[] result = new byte[checked((int)required)];
            fixed (byte* resultPtr = result)
            {
                ThrowIfFailed(
                    GhosttyVtNative.PasteEncode(
                        dataPtr,
                        checked((nuint)data.Length),
                        bracketedPaste,
                        resultPtr,
                        checked((nuint)result.Length),
                        out nuint written),
                    "ghostty_paste_encode");

                if (written == (nuint)result.Length)
                {
                    return result;
                }

                byte[] resized = new byte[checked((int)written)];
                Array.Copy(result, resized, resized.Length);
                return resized;
            }
        }
    }

    private static void ThrowIfFailed(GhosttyVtNative.GhosttyResult result, string operation)
    {
        if (result == GhosttyVtNative.GhosttyResult.Success)
        {
            return;
        }

        throw new InvalidOperationException($"{operation} failed with {result}.");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe byte ReadMime(
        nint userdata,
        GhosttyVtNative.GhosttyString mime,
        GhosttyVtNative.GhosttyWriter writer)
    {
        try
        {
            PasteContext? context = GCHandle.FromIntPtr(userdata).Target as PasteContext;
            if (context is null || writer.Write == nint.Zero ||
                !context.Representations.TryGetValue(mime.ToUtf8String(), out byte[]? data))
            {
                return 0;
            }

            if (data.Length == 0)
            {
                return 1;
            }

            fixed (byte* pointer = data)
            {
                delegate* unmanaged[Cdecl]<nint, byte*, nuint, byte> write =
                    (delegate* unmanaged[Cdecl]<nint, byte*, nuint, byte>)writer.Write;
                return write(writer.Userdata, pointer, (nuint)data.Length);
            }
        }
        catch
        {
            // Exceptions cannot cross an unmanaged callback boundary.
            return 0;
        }
    }

    private sealed class PasteContext
    {
        public PasteContext(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> representations)
        {
            Representations = new Dictionary<string, byte[]>(representations.Count, StringComparer.OrdinalIgnoreCase);
            MimeBytes = new byte[representations.Count][];
            int index = 0;
            foreach ((string mime, ReadOnlyMemory<byte> data) in representations)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(mime);
                Representations.Add(mime, data.ToArray());
                MimeBytes[index++] = Encoding.UTF8.GetBytes(mime);
            }
        }

        public Dictionary<string, byte[]> Representations { get; }

        public byte[][] MimeBytes { get; }
    }
}
