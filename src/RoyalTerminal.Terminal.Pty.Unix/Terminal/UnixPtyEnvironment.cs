// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections;
using System.Runtime.InteropServices;

namespace RoyalTerminal.Terminal;

/// <summary>Parent-owned environment storage prepared completely before spawning.</summary>
internal sealed unsafe class UnixPtyEnvironment : IDisposable
{
    private readonly nint[] _strings;
    internal byte** Pointer { get; private set; }

    internal UnixPtyEnvironment(IReadOnlyDictionary<string, string>? overrides)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            values[(string)entry.Key] = (string)entry.Value!;
        values["TERM"] = "xterm-256color";
        if (overrides is not null)
        {
            foreach ((string key, string value) in overrides)
            {
                ArgumentException.ThrowIfNullOrEmpty(key);
                ArgumentNullException.ThrowIfNull(value);
                if (key.Contains('=') || key.Contains('\0') || value.Contains('\0'))
                    throw new ArgumentException("PTY environment entries must be valid POSIX names and values.", nameof(overrides));
                values[key] = value;
            }
        }

        _strings = new nint[values.Count];
        Pointer = (byte**)NativeMemory.AllocZeroed((nuint)(values.Count + 1), (nuint)sizeof(nint));
        try
        {
            int index = 0;
            foreach ((string key, string value) in values)
            {
                _strings[index] = Marshal.StringToCoTaskMemUTF8($"{key}={value}");
                Pointer[index] = (byte*)_strings[index];
                index++;
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Pointer == null) return;
        foreach (nint value in _strings) Marshal.FreeCoTaskMem(value);
        NativeMemory.Free(Pointer);
        Pointer = null;
    }
}
