// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Unix pseudo-terminal (PTY) for macOS/Linux.
// Spawns a shell process with a real PTY via POSIX interop so terminal features
// work properly. Native posix_spawn performs all child setup without returning
// into the managed runtime between process creation and exec.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Diagnostics;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Unix PTY that spawns a child shell process (e.g., /bin/zsh, /bin/bash)
/// and provides read/write streams via POSIX interop. Uses posix_spawn on macOS/Linux.
/// Architecturally mirrors <see cref="WindowsPty"/> for Windows ConPTY.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public sealed class UnixPty : IPty, ITerminalOutputLeaseSource, ITerminalPasswordInputSource
{
    private int _masterFd = -1;
    private readonly object _descriptorSync = new();
    private int _childPid = -1;
    private readonly object _childProcessSync = new();
    private string? _slavePtyPath;
    private volatile bool _disposed;
    private volatile bool _readCompleted;
    private int _cleanupStarted;
    private readonly object _pendingWritesSync = new();
    private readonly Queue<PendingWrite> _priorityWrites = new();
    private readonly Queue<PendingWrite> _pendingWrites = new();
    private Thread? _readThread;
    private Thread? _writeThread;
    private UnixPtyPoller? _poller;
    private UnixPtyOutputRing? _outputRing;

    /// <inheritdoc />
    public Action<TerminalOutputLease>? OutputLeaseCallback { get; set; }

    /// <summary>Raised when data is received from the PTY.</summary>
    public event Action<byte[], int>? DataReceived;

    /// <summary>Raised when the child process exits.</summary>
    public event Action<int>? ProcessExited;

    /// <summary>Whether the PTY is currently active.</summary>
    public bool IsRunning => _masterFd >= 0 && _childPid > 0 && !_disposed && !_readCompleted;

    /// <summary>The child process ID.</summary>
    public int ChildPid => _childPid;

    /// <inheritdoc />
    public bool SupportsPasswordInputDetection => true;

    /// <inheritdoc />
    public bool TryGetPasswordInput(out bool passwordInput)
    {
        lock (_descriptorSync)
        {
            passwordInput = false;
            // Closing under the same lock prevents a probe reaching a recycled fd.
            return IsRunning && UnixPtyInputMode.TryGetPasswordInput(_masterFd, out passwordInput);
        }
    }

    /// <summary>
    /// Spawns a shell process with a PTY.
    /// </summary>
    /// <param name="shell">Shell path, e.g., "/bin/zsh". Null = auto-detect.</param>
    /// <param name="columns">Initial terminal width.</param>
    /// <param name="rows">Initial terminal height.</param>
    /// <param name="workingDirectory">Working directory for the shell.</param>
    /// <param name="environment">Additional environment variables.</param>
    /// <param name="arguments">Optional command arguments passed to the shell/program.</param>
    public void Start(
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

        (_childPid, _masterFd) = UnixPtyProcess.Start(shell, columns, rows,
            workingDirectory, environment, arguments);
        _slavePtyPath = TryGetSlavePtyPath(_masterFd);
        try
        {
            _poller = new UnixPtyPoller();
            _outputRing = new UnixPtyOutputRing(_poller.SignalIdle);
            if (!UnixPtyPoller.SetNonblocking(_masterFd))
            {
                throw new IOException("Unable to make the PTY master nonblocking.");
            }
        }
        catch
        {
            Dispose();
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
            Name = "PTY-Gather",
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
        if (OperatingSystem.IsMacOS())
        {
            _ = UnixThreadScheduling.TrySetCurrentThreadUserInitiated();
        }

        try
        {
            GatherOutput();
        }
        catch
        {
            // Reader thread must never crash the process on unexpected runtime/PInvoke errors.
        }

        _readCompleted = true;
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

    private unsafe void GatherOutput()
    {
        UnixPtyOutputRing ring = _outputRing!;
        UnixPtyPoller poller = _poller!;
        int fd = _masterFd;
        bool finished = false;
        while (!_disposed && !finished)
        {
            UnixPtyOutputRing.Slot? slot = ring.Acquire();
            if (slot is null)
            {
                break;
            }

            int length = 0;
            int spins = 0;
            long bridgeStarted = 0;
            fixed (byte* buffer = slot.Buffer)
            {
                while (!_disposed && length < slot.Buffer.Length)
                {
                    int bytesRead = (int)PosixRead(fd, buffer + length, (nuint)(slot.Buffer.Length - length));
                    if (bytesRead > 0)
                    {
                        length += bytesRead;
                        spins = 0;
                        continue;
                    }

                    if (bytesRead == 0)
                    {
                        finished = true;
                        break;
                    }

                    int error = Marshal.GetLastPInvokeError();
                    if (error == ErrnoInterrupted)
                    {
                        continue;
                    }

                    if (error != ErrnoWouldBlockLinux && error != ErrnoWouldBlockBsd)
                    {
                        finished = true;
                        break;
                    }

                    // Match Ghostty's adaptive gather: interactive bytes are delivered
                    // immediately, and refill gaps are bridged only behind a busy parser.
                    if (length < 1024)
                    {
                        break;
                    }

                    if (spins++ < 16)
                    {
                        continue;
                    }

                    long now = Stopwatch.GetTimestamp();
                    if (bridgeStarted == 0)
                    {
                        bridgeStarted = now;
                    }
                    else if (Stopwatch.GetElapsedTime(bridgeStarted, now) >= TimeSpan.FromMilliseconds(3))
                    {
                        break;
                    }

                    if (!ring.TryBeginBridge())
                    {
                        break;
                    }

                    UnixPtyPoller.Readiness readiness = poller.Wait(fd, writable: false,
                        timeoutMilliseconds: 1, includeIdle: true);
                    ring.EndBridge();
                    if (readiness != UnixPtyPoller.Readiness.Ready)
                    {
                        finished = readiness is UnixPtyPoller.Readiness.Stopped or UnixPtyPoller.Readiness.Closed;
                        break;
                    }
                }
            }

            TerminalOutputLease lease = slot.CreateLease(length);
            if (length == 0 || _disposed)
            {
                lease.Dispose();
            }
            else
            {
                try
                {
                    DataReceived?.Invoke(slot.Buffer, length);
                }
                catch
                {
                    // Conventional event subscribers must not terminate PTY reading.
                }

                Action<TerminalOutputLease>? receiver = OutputLeaseCallback;
                if (receiver is null)
                {
                    lease.Dispose();
                }
                else
                {
                    try
                    {
                        receiver(lease);
                    }
                    catch
                    {
                        lease.Dispose();
                    }
                }
            }

            if (!_disposed && !finished && length < slot.Buffer.Length)
            {
                finished = poller.Wait(fd, writable: false, timeoutMilliseconds: -1)
                    != UnixPtyPoller.Readiness.Ready;
            }
        }
    }

    private int WaitForChild()
    {
        lock (_childProcessSync)
        {
            if (_childPid <= 0) return -1;

            try
            {
                int result = Waitpid(_childPid, out int status, WNOHANG);
                if (result == _childPid)
                {
                    _childPid = -1;
                    return (status >> 8) & 0xFF; // WEXITSTATUS
                }
            }
            catch
            {
                // Best effort only; reader thread must not crash the process.
            }
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
            if (error == ErrnoInterrupted)
            {
                continue;
            }

            if (error == ErrnoWouldBlockLinux || error == ErrnoWouldBlockBsd)
            {
                if (_poller?.Wait(fd, writable: true, timeoutMilliseconds: -1) != UnixPtyPoller.Readiness.Ready)
                {
                    return;
                }

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
        bool initiateStop;
        lock (_pendingWritesSync)
        {
            initiateStop = !_disposed;
            _disposed = true;
            _priorityWrites.Clear();
            _pendingWrites.Clear();
            Monitor.PulseAll(_pendingWritesSync);
        }

        if (initiateStop)
        {
            _outputRing?.Stop();
            _poller?.SignalStop();
            // Signal child to terminate first.
            lock (_childProcessSync)
            {
                if (_childPid > 0)
                {
                    try { Kill(_childPid, 1); } catch { } // SIGHUP
                }
            }
        }

        // Nonblocking IO plus the stop pipe lets both workers exit before the
        // descriptor is closed, so a recycled descriptor can never be read/written.
        if (_readThread is not null && !ReferenceEquals(Thread.CurrentThread, _readThread))
        {
            _readThread.Join();
        }
        if (_writeThread is not null && !ReferenceEquals(Thread.CurrentThread, _writeThread))
        {
            _writeThread.Join();
        }

        if (Interlocked.Exchange(ref _cleanupStarted, 1) != 0)
        {
            return;
        }

        lock (_descriptorSync)
        {
            int fd = Interlocked.Exchange(ref _masterFd, -1);
            if (fd >= 0)
            {
                try { PosixClose(fd); } catch { }
            }
        }
        _slavePtyPath = null;

        _poller?.Dispose();

        // Reap child process (non-blocking to avoid hanging the UI thread)
        lock (_childProcessSync)
        {
            int pid = _childPid;
            if (pid > 0)
            {
                try
                {
                    int result = Waitpid(pid, out _, WNOHANG);
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
