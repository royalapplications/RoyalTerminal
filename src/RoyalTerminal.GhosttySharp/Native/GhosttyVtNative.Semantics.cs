// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Owned active-screen policy; numeric values match TerminalPromptState.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RoyalPromptState
    {
        /// <summary>Structure size in bytes.</summary>
        public nuint Size;
        /// <summary>Whether a prompt was observed on this screen.</summary>
        public uint Seen;
        /// <summary>Live cursor semantic classification.</summary>
        public uint Content;
        /// <summary>Whether input ends on an explicit newline.</summary>
        public uint ClearEol;
        /// <summary>Normalized click policy, zero through six.</summary>
        public uint Click;
        /// <summary>Redraw policy: all, none, last.</summary>
        public uint Redraw;
        /// <summary>The next generated implicit hyperlink ID.</summary>
        public uint ImplicitId;
        /// <summary>Initializes the required size field.</summary>
        public static RoyalPromptState CreateSized() => new() { Size = (nuint)Unsafe.SizeOf<RoyalPromptState>() };
    }

    /// <summary>Lengths and identity for a copied grid hyperlink.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RoyalHyperlinkMetadata
    {
        /// <summary>Structure size in bytes.</summary>
        public nuint Size;
        /// <summary>Original URI byte length.</summary>
        public nuint UriLength;
        /// <summary>Explicit ID byte length; zero means implicit.</summary>
        public nuint IdLength;
        /// <summary>Implicit ID when IdLength is zero.</summary>
        public uint ImplicitId;
        /// <summary>Initializes the required size field.</summary>
        public static RoyalHyperlinkMetadata CreateSized() => new() { Size = (nuint)Unsafe.SizeOf<RoyalHyperlinkMetadata>() };
    }

    /// <summary>Copies the live screen's policy without clearing dirty state.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_prompt_state")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult PromptState(nint terminal, RoyalPromptState* output);

    /// <summary>Probes/copies original hyperlink bytes. Serialize with grid mutations.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_grid_ref_hyperlink")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult GridRefHyperlink(in GhosttyGridRef reference,
        RoyalHyperlinkMetadata* metadata, byte* uri, nuint uriCapacity, byte* id, nuint idCapacity);
}
