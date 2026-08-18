// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Unix pseudo-terminal (PTY) for macOS/Linux.
// Spawns a shell process with a real PTY via POSIX interop so terminal features
// work properly. Fork-safe: all native memory is pre-allocated before fork, and
// only raw function-pointer calls are made in the child process (no .NET runtime usage).

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Diagnostics;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Unix PTY that spawns a child shell process (e.g., /bin/zsh, /bin/bash)
/// and provides read/write streams via POSIX interop. Uses forkpty() on macOS/Linux.
/// Architecturally mirrors <see cref="WindowsPty"/> for Windows ConPTY.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public sealed class UnixPty : IPty
{
    private int _masterFd = -1;
    private int _childPid = -1;
    private string? _slavePtyPath;
    private volatile bool _disposed;
    private readonly object _pendingWritesSync = new();
    private readonly Queue<PendingWrite> _priorityWrites = new();
    private readonly Queue<PendingWrite> _pendingWrites = new();
    private Thread? _readThread;
    private Thread? _writeThread;

    /// <summary>Raised when data is received from the PTY.</summary>
    public event Action<byte[], int>? DataReceived;

    /// <summary>Raised when the child process exits.</summary>
    public event Action<int>? ProcessExited;

    /// <summary>Whether the PTY is currently active.</summary>
    public bool IsRunning => _masterFd >= 0 && _childPid > 0 && !_disposed;

    /// <summary>The child process ID.</summary>
    public int ChildPid => _childPid;

    /// <summary>
    /// Spawns a shell process with a PTY.
    /// </summary>
    /// <param name="shell">Shell path, e.g., "/bin/zsh". Null = auto-detect.</param>
    /// <param name="columns">Initial terminal width.</param>
    /// <param name="rows">Initial terminal height.</param>
    /// <param name="workingDirectory">Working directory for the shell.</param>
    /// <param name="environment">Additional environment variables.</param>
    /// <param name="arguments">Optional command arguments passed to the shell/program.</param>
    public unsafe void Start(
        string? shell = null,
        int columns = 80,
        int rows = 24,
        string? workingDirectory = null,
        Dictionary<string, string>? environment = null,
        IReadOnlyList<string>? arguments = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException("UnixPty is only supported on macOS and Linux.");
        }

        shell ??= DetectShell();

        // 1. Open master PTY descriptor
        _masterFd = PosixOpen("/dev/ptmx", O_RDWR | O_NOCTTY);
        if (_masterFd < 0)
        {
            throw new InvalidOperationException($"Failed to open /dev/ptmx: {Marshal.GetLastPInvokeError()}");
        }

        try
        {
            // 2. Grant and unlock PTY
            if (GrantPt(_masterFd) != 0 || UnlockPt(_masterFd) != 0)
            {
                throw new InvalidOperationException($"Failed to grantpt/unlockpt: {Marshal.GetLastPInvokeError()}");
            }

            // 3. Get slave path name
            nint slaveNamePtr = PtsName(_masterFd);
            if (slaveNamePtr == nint.Zero)
            {
                throw new InvalidOperationException($"Failed to get ptsname: {Marshal.GetLastPInvokeError()}");
            }
            string slaveName = Marshal.PtrToStringAnsi(slaveNamePtr)!;
            _slavePtyPath = slaveName;

            // Apply initial window size to the master FD
            var winSize = new WinSize
            {
                ws_col = (ushort)columns,
                ws_row = (ushort)rows,
            };
            Ioctl(_masterFd, TIOCSWINSZ, (nint)(&winSize));

            // 4. Initialize posix_spawn actions and attributes
            byte[] actions = new byte[1024];
            byte[] attr = new byte[1024];

            fixed (byte* pActions = actions)
            fixed (byte* pAttr = attr)
            {
                if (PosixSpawnFileActionsInit(pActions) != 0)
                {
                    throw new InvalidOperationException("Failed to init spawn file actions.");
                }

                try
                {
                    if (PosixSpawnAttrInit(pAttr) != 0)
                    {
                        throw new InvalidOperationException("Failed to init spawn attributes.");
                    }

                    try
                    {
                        // Open slave PTY in the child process for fd 0 (which makes it the controlling terminal of the new session)
                        if (PosixSpawnFileActionsAddOpen(pActions, 0, slaveName, O_RDWR, 0) != 0)
                        {
                            throw new InvalidOperationException($"Failed to add open action: {Marshal.GetLastPInvokeError()}");
                        }
                        
                        // Duplicate to standard descriptors 1 and 2
                        PosixSpawnFileActionsAddDup2(pActions, 0, 1);
                        PosixSpawnFileActionsAddDup2(pActions, 0, 2);
                        
                        // Close master PTY descriptor in the child process
                        PosixSpawnFileActionsAddClose(pActions, _masterFd);

                        // Set setsid flag to launch child shell as the session leader
                        PosixSpawnAttrSetFlags(pAttr, POSIX_SPAWN_SETSID);

                        // 5. Build argv for spawning target shell directly
                        int argumentCount = arguments?.Count ?? 0;
                        byte** argv = (byte**)Marshal.AllocHGlobal((argumentCount + 2) * IntPtr.Size);
                        IntPtr nativeShell = Marshal.StringToHGlobalAnsi(shell);
                        argv[0] = (byte*)nativeShell;

                        IntPtr[] nativeArguments = new IntPtr[argumentCount];
                        for (int i = 0; i < argumentCount; i++)
                        {
                            string argument = arguments![i] ?? string.Empty;
                            IntPtr nativeArgument = Marshal.StringToHGlobalAnsi(argument);
                            nativeArguments[i] = nativeArgument;
                            argv[i + 1] = (byte*)nativeArgument;
                        }
                        argv[argumentCount + 1] = null;

                        // 6. Build merged environment envp
                        var envVars = Environment.GetEnvironmentVariables();
                        var mergedEnv = new Dictionary<string, string>();
                        foreach (System.Collections.DictionaryEntry de in envVars)
                        {
                            mergedEnv[de.Key.ToString()!] = de.Value?.ToString() ?? string.Empty;
                        }
                        mergedEnv["TERM"] = "xterm-256color";
                        if (environment != null)
                        {
                            foreach (var kv in environment)
                            {
                                mergedEnv[kv.Key] = kv.Value;
                            }
                        }

                        int envCount = mergedEnv.Count;
                        byte** envp = (byte**)Marshal.AllocHGlobal((envCount + 1) * IntPtr.Size);
                        IntPtr[] allocatedStrings = new IntPtr[envCount];
                        int envIndex = 0;
                        foreach (var kv in mergedEnv)
                        {
                            string entry = $"{kv.Key}={kv.Value}";
                            IntPtr nativeEntry = Marshal.StringToHGlobalAnsi(entry);
                            allocatedStrings[envIndex] = nativeEntry;
                            envp[envIndex] = (byte*)nativeEntry;
                            envIndex++;
                        }
                        envp[envCount] = null;

                        // 7. Spawn child process (temporarily changing CWD to align workingDirectory)
                        string originalCwd = Directory.GetCurrentDirectory();
                        if (!string.IsNullOrEmpty(workingDirectory))
                        {
                            try
                            {
                                Directory.SetCurrentDirectory(workingDirectory);
                            }
                            catch
                            {
                                // Fallback: try directory change, ignore if fails to prevent start crash
                            }
                        }

                        int childPid = 0;
                        int spawnResult;
                        try
                        {
                            spawnResult = PosixSpawn(
                                &childPid,
                                shell,
                                pActions,
                                pAttr,
                                argv,
                                envp);
                        }
                        finally
                        {
                            if (!string.IsNullOrEmpty(workingDirectory))
                            {
                                try
                                {
                                    Directory.SetCurrentDirectory(originalCwd);
                                }
                                catch
                                {
                                    // Suppress fallback cleanup directory changes exceptions
                                }
                            }
                        }

                        // 8. Free native argv and envp structures
                        Marshal.FreeHGlobal(nativeShell);
                        for (int i = 0; i < argumentCount; i++)
                        {
                            Marshal.FreeHGlobal(nativeArguments[i]);
                        }
                        Marshal.FreeHGlobal((IntPtr)argv);

                        for (int i = 0; i < envCount; i++)
                        {
                            Marshal.FreeHGlobal(allocatedStrings[i]);
                        }
                        Marshal.FreeHGlobal((IntPtr)envp);

                        if (spawnResult != 0)
                        {
                            throw new InvalidOperationException($"posix_spawn failed with error {spawnResult}: {Marshal.GetLastPInvokeError()}");
                        }

                        _childPid = childPid;
                    }
                    finally
                    {
                        PosixSpawnAttrDestroy(pAttr);
                    }
                }
                finally
                {
                    PosixSpawnFileActionsDestroy(pActions);
                }
            }
        }
        catch
        {
            PosixClose(_masterFd);
            _masterFd = -1;
            throw;
        }

        // Start writing to the master FD on a dedicated worker so UI/key handling
        // callers never block on back-pressured PTY input.
        _writeThread = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "PTY-Writer",
        };
        _writeThread.Start();

        // Start reading from the master FD
        _readThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "PTY-Reader",
        };
        _readThread.Start();
    }

    /// <summary>
    /// Writes data to the PTY (sends to the shell's stdin).
    /// </summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        if (_masterFd < 0 || _disposed || data.IsEmpty) return;

        byte[] copy = data.ToArray();
        lock (_pendingWritesSync)
        {
            if (_masterFd < 0 || _disposed)
            {
                return;
            }

            Queue<PendingWrite> queue = IsPriorityControlWrite(copy)
                ? _priorityWrites
                : _pendingWrites;
            queue.Enqueue(new PendingWrite(copy));
            Monitor.PulseAll(_pendingWritesSync);
        }
    }

    /// <summary>
    /// Writes a string to the PTY.
    /// </summary>
    public void Write(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Write(bytes);
    }

    /// <summary>
    /// Writes a byte array segment to the PTY.
    /// </summary>
    public void Write(byte[] data, int offset, int count)
    {
        Write(data.AsSpan(offset, count));
    }

    /// <summary>
    /// Resizes the PTY to the given dimensions.
    /// </summary>
    public void Resize(int columns, int rows)
    {
        Resize(columns, rows, 0, 0);
    }

    public void Resize(int columns, int rows, int widthPixels, int heightPixels)
    {
        if (_masterFd < 0 || _disposed) return;

        // macOS: P/Invoking variadic ioctl for TIOCSWINSZ can produce corrupted
        // winsize values. Using stty against the slave PTY path is stable.
        var resized = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
            TryResizeWithStty(columns, rows);

        if (!resized)
        {
            var winSize = new WinSize
            {
                ws_col = (ushort)columns,
                ws_row = (ushort)rows,
                ws_xpixel = (ushort)widthPixels,
                ws_ypixel = (ushort)heightPixels,
            };

            unsafe
            {
                _ = Ioctl(_masterFd, TIOCSWINSZ, (nint)(&winSize));
            }
        }

        // Some full-screen TUI apps only redraw after SIGWINCH reaches the
        // foreground process group; proactively signal it after window-size update.
        try
        {
            int foregroundPgrp = Tcgetpgrp(_masterFd);
            if (foregroundPgrp > 0)
            {
                // Negative PID targets the entire process group.
                _ = Kill(-foregroundPgrp, SIGWINCH);
            }
            else if (_childPid > 0)
            {
                _ = Kill(_childPid, SIGWINCH);
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    private void ReadLoop()
    {
        var buffer = new byte[8192];

        try
        {
            while (!_disposed)
            {
                int bytesRead;
                unsafe
                {
                    fixed (byte* ptr = buffer)
                    {
                        bytesRead = (int)PosixRead(_masterFd, ptr, (nuint)buffer.Length);
                    }
                }

                if (_disposed)
                {
                    break;
                }

                if (bytesRead < 0)
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error == ErrnoInterrupted || error == ErrnoWouldBlockLinux || error == ErrnoWouldBlockBsd)
                    {
                        Thread.Yield();
                        continue;
                    }

                    break;
                }

                if (bytesRead == 0)
                {
                    // EOF — child process likely exited or PTY was closed.
                    break;
                }

                try
                {
                    DataReceived?.Invoke(buffer, bytesRead);
                }
                catch
                {
                    // Don't let subscriber exceptions kill the read loop
                }
            }
        }
        catch
        {
            // Reader thread must never crash the process on unexpected runtime/PInvoke errors.
        }

        if (_disposed) return;

        // Check child exit status
        var exitCode = WaitForChild();
        try
        {
            ProcessExited?.Invoke(exitCode);
        }
        catch
        {
            // Ignore
        }
    }

    private int WaitForChild()
    {
        if (_childPid <= 0) return -1;

        try
        {
            var result = Waitpid(_childPid, out var status, WNOHANG);
            if (result == _childPid)
            {
                return (status >> 8) & 0xFF; // WEXITSTATUS
            }
        }
        catch
        {
            // Best effort only; reader thread must not crash the process.
        }

        return -1;
    }

    private void WriteLoop()
    {
        try
        {
            while (true)
            {
                PendingWrite pending;
                lock (_pendingWritesSync)
                {
                    while (!_disposed && _priorityWrites.Count == 0 && _pendingWrites.Count == 0)
                    {
                        Monitor.Wait(_pendingWritesSync);
                    }

                    if (_disposed && _priorityWrites.Count == 0 && _pendingWrites.Count == 0)
                    {
                        return;
                    }

                    pending = _priorityWrites.Count > 0
                        ? _priorityWrites.Dequeue()
                        : _pendingWrites.Dequeue();
                }

                WritePending(ref pending);
            }
        }
        catch
        {
            // Writer thread must never crash the process on unexpected runtime/PInvoke errors.
        }
    }

    private unsafe void WritePending(ref PendingWrite pending)
    {
        while (!_disposed && pending.Offset < pending.Buffer.Length)
        {
            int fd = _masterFd;
            if (fd < 0)
            {
                return;
            }

            nint written;
            fixed (byte* ptr = pending.Buffer)
            {
                byte* cursor = ptr + pending.Offset;
                nuint remaining = (nuint)(pending.Buffer.Length - pending.Offset);
                nuint chunkLength = remaining > 4096 ? 4096 : remaining;
                written = PosixWrite(fd, cursor, chunkLength);
            }

            if (written > 0)
            {
                pending.Offset += (int)written;
                continue;
            }

            if (written == 0)
            {
                Thread.Yield();
                continue;
            }

            int error = Marshal.GetLastPInvokeError();
            if (error == ErrnoInterrupted || error == ErrnoWouldBlockLinux || error == ErrnoWouldBlockBsd)
            {
                Thread.Yield();
                continue;
            }

            return;
        }
    }

    private static bool IsPriorityControlWrite(byte[] payload)
    {
        return payload.Length == 1 && payload[0] is 0x03 or 0x0C or 0x1A or 0x1C;
    }

    /// <summary>
    /// Stops the PTY and kills the child process. Same as Dispose.
    /// </summary>
    public void Stop() => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_pendingWritesSync)
        {
            _priorityWrites.Clear();
            _pendingWrites.Clear();
            Monitor.PulseAll(_pendingWritesSync);
        }

        // Signal child to terminate first
        var pid = _childPid;
        if (pid > 0)
        {
            try { Kill(pid, 1); } catch { } // SIGHUP
        }

        // Close master FD — this unblocks the read loop
        var fd = _masterFd;
        _masterFd = -1;
        if (fd >= 0)
        {
            try { PosixClose(fd); } catch { }
        }
        _slavePtyPath = null;

        // Wait for read thread (don't block too long)
        _readThread?.Join(TimeSpan.FromMilliseconds(500));
        _writeThread?.Join(TimeSpan.FromMilliseconds(500));

        // Reap child process (non-blocking to avoid hanging the UI thread)
        if (pid > 0)
        {
            try
            {
                var result = Waitpid(pid, out _, WNOHANG);
                if (result == 0) // Still running
                {
                    Kill(pid, 9); // SIGKILL
                    Waitpid(pid, out _, WNOHANG);
                }
            }
            catch { }
            _childPid = -1;
        }
    }

    #region Helpers

    private static string DetectShell()
    {
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
            return shell;

        if (File.Exists("/bin/zsh")) return "/bin/zsh";
        if (File.Exists("/bin/bash")) return "/bin/bash";
        return "/bin/sh";
    }

    private static string EscapeShellArg(string arg)
    {
        return "'" + arg.Replace("'", "'\\''") + "'";
    }

    private static string? TryGetSlavePtyPath(int masterFd)
    {
        if (masterFd < 0)
        {
            return null;
        }

        try
        {
            nint ptr = PtsName(masterFd);
            return ptr == nint.Zero ? null : Marshal.PtrToStringAnsi(ptr);
        }
        catch
        {
            return null;
        }
    }

    private bool TryResizeWithStty(int columns, int rows)
    {
        string? slavePath = _slavePtyPath;
        if (string.IsNullOrWhiteSpace(slavePath))
        {
            return false;
        }

        try
        {
            string sttyPath = File.Exists("/bin/stty") ? "/bin/stty" : "stty";
            ProcessStartInfo startInfo = new(sttyPath)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // macOS uses -f <device>; Linux uses -F <device>.
            startInfo.ArgumentList.Add(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "-f" : "-F");
            startInfo.ArgumentList.Add(slavePath);
            startInfo.ArgumentList.Add("rows");
            startInfo.ArgumentList.Add(rows.ToString());
            startInfo.ArgumentList.Add("cols");
            startInfo.ArgumentList.Add(columns.ToString());

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(500))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort cleanup only.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Native Interop

    private static readonly ulong TIOCSWINSZ = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        ? 0x80087467UL
        : 0x5414UL;

    private const int WNOHANG = 1;
    private const int SIGWINCH = 28;
    private const int ErrnoInterrupted = 4;
    private const int ErrnoWouldBlockLinux = 11;
    private const int ErrnoWouldBlockBsd = 35;

    private const int O_RDWR = 2;
    private static readonly int O_NOCTTY = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 0x20000 : 0x400;
    private static readonly short POSIX_SPAWN_SETSID = (short)(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 0x0400 : 0x80);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }

    private struct PendingWrite
    {
        public PendingWrite(byte[] buffer)
        {
            Buffer = buffer;
            Offset = 0;
        }

        public byte[] Buffer;
        public int Offset;
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int PosixOpen(string path, int flags);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern unsafe int PosixOpen(byte* path, int flags);

    [DllImport("libc", EntryPoint = "grantpt", SetLastError = true)]
    private static extern int GrantPt(int fd);

    [DllImport("libc", EntryPoint = "unlockpt", SetLastError = true)]
    private static extern int UnlockPt(int fd);

    [DllImport("libc", EntryPoint = "posix_spawn", SetLastError = true)]
    private static extern unsafe int PosixSpawn(
        int* pid,
        string path,
        byte* fileActions,
        byte* spawnAttr,
        byte** argv,
        byte** envp);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_init", SetLastError = true)]
    private static extern unsafe int PosixSpawnFileActionsInit(byte* fileActions);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_destroy", SetLastError = true)]
    private static extern unsafe int PosixSpawnFileActionsDestroy(byte* fileActions);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2", SetLastError = true)]
    private static extern unsafe int PosixSpawnFileActionsAddDup2(byte* fileActions, int fd, int newFd);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addclose", SetLastError = true)]
    private static extern unsafe int PosixSpawnFileActionsAddClose(byte* fileActions, int fd);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addopen", SetLastError = true)]
    private static extern unsafe int PosixSpawnFileActionsAddOpen(
        byte* fileActions,
        int fd,
        string path,
        int oflag,
        int mode);

    [DllImport("libc", EntryPoint = "posix_spawnattr_init", SetLastError = true)]
    private static extern unsafe int PosixSpawnAttrInit(byte* spawnAttr);

    [DllImport("libc", EntryPoint = "posix_spawnattr_destroy", SetLastError = true)]
    private static extern unsafe int PosixSpawnAttrDestroy(byte* spawnAttr);

    [DllImport("libc", EntryPoint = "posix_spawnattr_setflags", SetLastError = true)]
    private static extern unsafe int PosixSpawnAttrSetFlags(byte* spawnAttr, short flags);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern unsafe nint PosixRead(int fd, byte* buf, nuint count);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern unsafe nint PosixWrite(int fd, byte* buf, nuint count);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int PosixClose(int fd);

    [DllImport("libc", EntryPoint = "ioctl", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Ioctl(int fd, ulong request, nint arg);

    [DllImport("libc", EntryPoint = "ptsname")]
    private static extern nint PtsName(int fd);

    [DllImport("libc", EntryPoint = "waitpid")]
    private static extern int Waitpid(int pid, out int status, int options);

    [DllImport("libc", EntryPoint = "kill")]
    private static extern int Kill(int pid, int sig);

    [DllImport("libc", EntryPoint = "tcgetpgrp")]
    private static extern int Tcgetpgrp(int fd);

    #endregion
}
