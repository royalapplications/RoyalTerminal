// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.GhosttySharp - .NET bindings for the Ghostty terminal emulator.

using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Managed wrapper for Ghostty configuration.
/// Handles creation, loading, querying, and lifecycle management.
/// Implements <see cref="IDisposable"/> for deterministic cleanup of native resources.
/// </summary>
public sealed class GhosttyConfig : IDisposable
{
    private nint _handle;
    private bool _disposed;
    private readonly bool _ownsHandle;

    /// <summary>Gets the native handle. Throws if disposed.</summary>
    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>Returns true if the configuration handle is valid.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    /// <summary>Creates a new default configuration.</summary>
    public GhosttyConfig()
    {
        NativeLibraryLoader.Initialize();
        _handle = GhosttyNative.ConfigNew();
        if (_handle == nint.Zero)
            throw new InvalidOperationException("Failed to create Ghostty configuration.");
        _ownsHandle = true;
    }

    /// <summary>Creates a managed wrapper from an existing native config handle.</summary>
    internal GhosttyConfig(nint handle, bool ownsHandle = true)
    {
        _handle = handle;
        _ownsHandle = ownsHandle;
    }

    /// <summary>Clones this configuration into a new independent instance.</summary>
    public GhosttyConfig Clone()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var cloned = GhosttyNative.ConfigClone(_handle);
        if (cloned == nint.Zero)
            throw new InvalidOperationException("Failed to clone Ghostty configuration.");
        return new GhosttyConfig(cloned);
    }

    /// <summary>Loads configuration from command line arguments.</summary>
    public void LoadCliArgs()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.ConfigLoadCliArgs(_handle);
    }

    /// <summary>Loads configuration from a file.</summary>
    public unsafe void LoadFile(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bytes = Encoding.UTF8.GetBytes(path + '\0');
        fixed (byte* ptr = bytes)
        {
            GhosttyNative.ConfigLoadFile(_handle, ptr);
        }
    }

    /// <summary>Loads configuration from default file locations.</summary>
    public void LoadDefaultFiles()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.ConfigLoadDefaultFiles(_handle);
    }

    /// <summary>Loads configuration from recursive file locations.</summary>
    public void LoadRecursiveFiles()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.ConfigLoadRecursiveFiles(_handle);
    }

    /// <summary>Finalizes the configuration. Must be called after all loading is complete.</summary>
    public void Finalize_()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.ConfigFinalize(_handle);
    }

    /// <summary>Gets a typed configuration value by key.</summary>
    /// <typeparam name="T">Blittable type matching the config value.</typeparam>
    /// <param name="key">Configuration key name.</param>
    /// <param name="value">Receives the configuration value if found.</param>
    /// <returns>True if the value was found and retrieved.</returns>
    public unsafe bool TryGet<T>(string key, out T value) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        value = default;
        var keyBytes = Encoding.UTF8.GetBytes(key);
        fixed (byte* keyPtr = keyBytes)
        fixed (T* valuePtr = &value)
        {
            return GhosttyNative.ConfigGet(_handle, valuePtr, keyPtr, (nuint)keyBytes.Length) != 0;
        }
    }

    /// <summary>Gets a configuration value as a string by key using Span for zero-copy where possible.</summary>
    public unsafe bool TryGetString(ReadOnlySpan<byte> keyUtf8, out string? result)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        result = null;
        nint strPtr = nint.Zero;
        fixed (byte* keyPtr = keyUtf8)
        {
            if (GhosttyNative.ConfigGet(_handle, &strPtr, keyPtr, (nuint)keyUtf8.Length) == 0)
                return false;
        }

        if (strPtr == nint.Zero)
            return false;

        result = Marshal.PtrToStringUTF8(strPtr);
        return true;
    }

    /// <summary>Gets a key trigger for the given action.</summary>
    public unsafe GhosttyInputTrigger GetTrigger(string action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bytes = Encoding.UTF8.GetBytes(action);
        fixed (byte* ptr = bytes)
        {
            return GhosttyNative.ConfigTrigger(_handle, ptr, (nuint)bytes.Length);
        }
    }

    /// <summary>Gets the number of configuration diagnostics.</summary>
    public uint DiagnosticsCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GhosttyNative.ConfigDiagnosticsCount(_handle);
        }
    }

    /// <summary>Gets a diagnostic message by index.</summary>
    public unsafe string? GetDiagnostic(uint index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var diag = GhosttyNative.ConfigGetDiagnostic(_handle, index);
        return diag.Message != null ? Marshal.PtrToStringUTF8((nint)diag.Message) : null;
    }

    /// <summary>Gets all diagnostic messages.</summary>
    public IReadOnlyList<string> GetAllDiagnostics()
    {
        var count = DiagnosticsCount;
        var result = new List<string>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var msg = GetDiagnostic(i);
            if (msg != null)
                result.Add(msg);
        }
        return result;
    }

    /// <summary>Gets the default configuration file open path.</summary>
    public static unsafe string? GetOpenPath()
    {
        NativeLibraryLoader.Initialize();
        var str = GhosttyNative.ConfigOpenPath();
        if (str.Ptr == null || str.Len == 0)
            return null;
        var result = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(str.Ptr, (int)str.Len));
        GhosttyNative.StringFree(str);
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle != nint.Zero)
        {
            if (_ownsHandle)
            {
                GhosttyNative.ConfigFree(_handle);
            }
            _handle = nint.Zero;
        }
    }
}
