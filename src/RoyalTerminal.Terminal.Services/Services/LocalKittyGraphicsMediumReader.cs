// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace RoyalTerminal.Terminal.Services;

/// <summary>Loads bounded Kitty image payloads from explicitly enabled local host media.</summary>
public sealed class LocalKittyGraphicsMediumReader : IKittyGraphicsMediumReader
{
    private const int HardMaxBytes = 400 * 1024 * 1024;
    private readonly KittyGraphicsMediumPolicy _policy;
    private readonly UTF8Encoding _pathEncoding = new(false, true);

    /// <summary>Creates a reader. Omitting a policy disables every host-backed medium.</summary>
    public LocalKittyGraphicsMediumReader(KittyGraphicsMediumPolicy? policy = null)
        => _policy = policy ?? new KittyGraphicsMediumPolicy();

    /// <inheritdoc />
    public bool TryRead(
        KittyGraphicsMediumRequest request,
        int maxBytes,
        [NotNullWhen(true)] out byte[]? data,
        out string? error)
    {
        data = null;
        error = "EINVAL: invalid data";
        bool supported = request.Medium switch
        {
            KittyGraphicsMedium.File => _policy.FileEnabled,
            KittyGraphicsMedium.TemporaryFile => _policy.TemporaryDirectory is not null,
            KittyGraphicsMedium.SharedMemory => _policy.SharedMemoryEnabled && (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()),
            _ => false,
        };
        if (!supported)
        {
            error = "EINVAL: unsupported medium";
            return false;
        }

        if (maxBytes < 0 || request.ExpectedBytes < 0 || request.Path.Length is 0 or > 98304 || request.Path.Span.Contains((byte)0)) return false;
        int limit = Math.Min(maxBytes, HardMaxBytes);
        try
        {
            string path = _pathEncoding.GetString(request.Path.Span);
            if (request.Medium == KittyGraphicsMedium.SharedMemory)
            {
                if (!KittyGraphicsPathPolicy.IsSharedMemoryName(request.Path.Span)) return false;
                data = KittyGraphicsFileAccess.ReadSharedMemory(path, request.Offset, request.Size, request.ExpectedBytes, limit);
                error = null;
                return true;
            }

            if (OperatingSystem.IsWindows() && !KittyGraphicsPathPolicy.IsAllowedWindowsPath(path)) return false;
            using KittyGraphicsFileAccess.OpenedFile file = KittyGraphicsFileAccess.Open(path, request.Medium == KittyGraphicsMedium.TemporaryFile);
            if (!file.IsRegular || !file.HasAllowedPath) return false;
            bool cleanup = false;
            try
            {
                if (request.Medium == KittyGraphicsMedium.TemporaryFile)
                {
                    if (!IsTemporaryPath(file.Path))
                    {
                        error = "EINVAL: temporary file not in temp dir";
                        return false;
                    }
                    if (!file.Path.Contains("tty-graphics-protocol", StringComparison.Ordinal))
                    {
                        error = "EINVAL: temporary file not named correctly";
                        return false;
                    }
                    cleanup = true;
                }

                data = file.Read(request.Offset, request.Size, expectedBytes: null, limit);
                error = null;
                return true;
            }
            finally
            {
                // Only an opened, regular, allowed, correctly named temporary file is owned.
                if (cleanup) file.DeleteIfUnchanged();
            }
        }
        catch (OutOfMemoryException)
        {
            error = "ENOMEM: out of memory";
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private bool IsTemporaryPath(string path)
    {
        bool windows = OperatingSystem.IsWindows();
        if (!windows && (KittyGraphicsPathPolicy.IsWithinDirectory("/tmp", path, false) ||
            KittyGraphicsPathPolicy.IsWithinDirectory("/dev/shm", path, false))) return true;
        string directory = System.IO.Path.GetFullPath(_policy.TemporaryDirectory!);
        if (KittyGraphicsPathPolicy.IsWithinDirectory(directory, path, windows)) return true;
        using KittyGraphicsFileAccess.OpenedFile openedDirectory = KittyGraphicsFileAccess.Open(directory, temporary: false, directory: true);
        return KittyGraphicsPathPolicy.IsWithinDirectory(openedDirectory.Path, path, windows);
    }
}
