// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Common PTY interface for cross-platform terminal I/O.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Common interface for pseudo-terminal implementations across platforms.
/// </summary>
public interface IPty : IDisposable
{
    /// <summary>
    /// Starts the PTY and launches the child shell process.
    /// </summary>
    /// <param name="shell">Shell path, or null for auto-detect.</param>
    /// <param name="columns">Initial terminal columns.</param>
    /// <param name="rows">Initial terminal rows.</param>
    /// <param name="workingDirectory">Working directory, or null for default.</param>
    /// <param name="environment">Additional environment variables.</param>
    void Start(
        string? shell = null,
        int columns = 80,
        int rows = 24,
        string? workingDirectory = null,
        Dictionary<string, string>? environment = null);

    /// <summary>Raised when data is received from the PTY.</summary>
    event Action<byte[], int>? DataReceived;

    /// <summary>Raised when the child process exits.</summary>
    event Action<int>? ProcessExited;

    /// <summary>Whether the PTY is currently active.</summary>
    bool IsRunning { get; }

    /// <summary>The child process ID.</summary>
    int ChildPid { get; }

    /// <summary>Writes a string to the PTY input.</summary>
    void Write(string text);

    /// <summary>Writes raw bytes to the PTY input.</summary>
    void Write(byte[] data, int offset, int count);

    /// <summary>Resizes the PTY.</summary>
    void Resize(int columns, int rows);

    /// <summary>Resizes the PTY with pixel dimensions for programs that use them.</summary>
    void Resize(int columns, int rows, int widthPixels, int heightPixels) => Resize(columns, rows);

    /// <summary>Stops the PTY and kills the child process.</summary>
    void Stop();
}
