// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Terminal;

/// <summary>Owns a null-terminated UTF-8 argv vector during native spawning.</summary>
internal sealed unsafe class UnixPtyArguments : IDisposable
{
    private readonly nint[] _strings;
    internal byte** Pointer { get; private set; }

    internal UnixPtyArguments(string executable, IReadOnlyList<string>? arguments, string? script = null)
    {
        int prefix = script is null ? 1 : 2;
        _strings = new nint[prefix + (arguments?.Count ?? 0)];
        Pointer = (byte**)NativeMemory.AllocZeroed((nuint)(_strings.Length + 1), (nuint)sizeof(nint));
        try
        {
            for (int i = 0; i < _strings.Length; i++)
            {
                string value = i == 0 ? executable : i < prefix ? script! : arguments![i - prefix] ?? string.Empty;
                if (value.Contains('\0')) throw new ArgumentException("PTY arguments cannot contain NUL.", nameof(arguments));
                _strings[i] = Marshal.StringToCoTaskMemUTF8(value);
                Pointer[i] = (byte*)_strings[i];
            }
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        if (Pointer == null) return;
        foreach (nint value in _strings) Marshal.FreeCoTaskMem(value);
        NativeMemory.Free(Pointer);
        Pointer = null;
    }
}
