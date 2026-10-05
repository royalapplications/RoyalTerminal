// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Terminal;

/// <summary>Native readiness waits with independent stop and parser-idle wake pipes.</summary>
internal sealed partial class UnixPtyPoller : IDisposable
{
    private readonly object _sync = new();
    private int _stopRead = -1;
    private int _stopWrite = -1;
    private int _idleRead = -1;
    private int _idleWrite = -1;

    public unsafe UnixPtyPoller()
    {
        int* descriptors = stackalloc int[2];
        // System.Native supplies a fixed ABI for pipe2/fcntl on all supported Unix
        // runtimes, including Apple's ARM64 variadic-call ABI.
        const int closeOnExec = 0x0010;
        if (Pipe(descriptors, closeOnExec) != 0)
        {
            throw new IOException("Unable to create the PTY stop pipe.");
        }

        _stopRead = descriptors[0];
        _stopWrite = descriptors[1];
        if (Pipe(descriptors, closeOnExec) != 0)
        {
            Dispose();
            throw new IOException("Unable to create the PTY parser-idle pipe.");
        }

        _idleRead = descriptors[0];
        _idleWrite = descriptors[1];
        if (!SetNonblocking(_stopRead) || !SetNonblocking(_stopWrite) ||
            !SetNonblocking(_idleRead) || !SetNonblocking(_idleWrite))
        {
            Dispose();
            throw new IOException("Unable to make the PTY wake pipes nonblocking.");
        }
    }

    public static bool SetNonblocking(int fd) => SetIsNonblocking(fd, 1) == 0;

    public void SignalStop()
    {
        lock (_sync)
        {
            Signal(_stopWrite);
        }
    }

    public void SignalIdle()
    {
        lock (_sync)
        {
            Signal(_idleWrite);
        }
    }

    public unsafe Readiness Wait(int fd, bool writable, int timeoutMilliseconds, bool includeIdle = false)
    {
        PollFd* descriptors = stackalloc PollFd[3];
        descriptors[0] = new PollFd { Descriptor = fd, Events = writable ? (short)4 : (short)1 };
        descriptors[1] = new PollFd { Descriptor = _stopRead, Events = 1 };
        descriptors[2] = new PollFd { Descriptor = _idleRead, Events = 1 };
        int result;
        do
        {
            result = Poll(descriptors, includeIdle ? 3u : 2u, timeoutMilliseconds);
        }
        while (result < 0 && Marshal.GetLastPInvokeError() == 4);

        if (result < 0 || (descriptors[1].ReturnedEvents & 0x39) != 0)
        {
            return Readiness.Stopped;
        }

        if (includeIdle && (descriptors[2].ReturnedEvents & 1) != 0)
        {
            byte* discarded = stackalloc byte[64];
            while (Read(_idleRead, discarded, 64) > 0) { }
            return Readiness.ParserIdle;
        }

        if (result == 0)
        {
            return Readiness.TimedOut;
        }

        return (descriptors[0].ReturnedEvents & (writable ? 4 : 1)) != 0
            ? Readiness.Ready
            : Readiness.Closed;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            Close(ref _stopRead);
            Close(ref _stopWrite);
            Close(ref _idleRead);
            Close(ref _idleWrite);
        }
    }

    private static unsafe void Signal(int fd)
    {
        if (fd >= 0)
        {
            byte value = 1;
            _ = Write(fd, &value, 1);
        }
    }

    private static void Close(ref int fd)
    {
        if (fd >= 0)
        {
            _ = CloseNative(fd);
            fd = -1;
        }
    }

    internal enum Readiness { Ready, TimedOut, ParserIdle, Closed, Stopped }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Descriptor;
        public short Events;
        public short ReturnedEvents;
    }

    [LibraryImport("System.Native", EntryPoint = "SystemNative_Pipe", SetLastError = true)]
    private static unsafe partial int Pipe(int* descriptors, int flags);
    [LibraryImport("System.Native", EntryPoint = "SystemNative_FcntlSetIsNonBlocking", SetLastError = true)]
    private static partial int SetIsNonblocking(nint fd, int enabled);
    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static unsafe partial int Poll(PollFd* descriptors, nuint count, int timeoutMilliseconds);
    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    private static unsafe partial nint Read(int fd, byte* buffer, nuint length);
    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    private static unsafe partial nint Write(int fd, byte* buffer, nuint length);
    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int CloseNative(int fd);
}
