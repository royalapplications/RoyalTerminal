// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

[Collection("PtyContractTests")]
public sealed class UnixPtyGatherTests
{
    [Fact]
    public void RepeatedStartupExecutesWithPreparedEnvironment()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        for (int iteration = 0; iteration < 20; iteration++)
        {
            using UnixPty pty = new();
            using ManualResetEventSlim received = new(false);
            StringBuilder output = new();
            string expected = $"royal-test:{iteration}:é";
            pty.DataReceived += (bytes, count) =>
            {
                lock (output)
                {
                    output.Append(Encoding.UTF8.GetString(bytes, 0, count));
                    if (output.ToString().Contains(expected, StringComparison.Ordinal)) received.Set();
                }
            };
            pty.Start(shell: "sh", arguments: ["-c", "printf '%s:%s:é' \"$TERM\" \"$ROYALTERMINAL_ITERATION\""],
                environment: new Dictionary<string, string>
                {
                    ["PATH"] = "/bin:/usr/bin", ["TERM"] = "royal-test",
                    ["ROYALTERMINAL_ITERATION"] = iteration.ToString(CultureInfo.InvariantCulture),
                });
            Assert.True(received.Wait(TimeSpan.FromSeconds(5)), $"Startup {iteration} did not execute the command.");
        }
    }

    [Theory]
    [InlineData("/bin/sh")]
    [InlineData("/royalterminal-nonexistent-executable")]
    public unsafe void StartPreservesCallingThreadSignalMask(string executable)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        nint library = NativeLibrary.Load(OperatingSystem.IsMacOS() ? "libSystem.dylib" : "libc.so.6");
        try
        {
            var mask = (delegate* unmanaged[Cdecl]<int, void*, void*, int>)NativeLibrary.GetExport(library, "pthread_sigmask");
            ulong* before = stackalloc ulong[16];
            ulong* after = stackalloc ulong[16];
            new Span<ulong>(before, 16).Clear();
            new Span<ulong>(after, 16).Clear();
            int setMask = OperatingSystem.IsMacOS() ? 3 : 2;
            Assert.Equal(0, mask(setMask, null, before));
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
            using UnixPty pty = new();
            if (executable == "/bin/sh") pty.Start(shell: executable, arguments: ["-c", "exit 0"]);
            else Assert.Throws<IOException>(() =>
            {
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    pty.Start(shell: executable, arguments: ["-c", "exit 0"]);
            });
            Assert.Equal(0, mask(setMask, null, after));
            Assert.True(new ReadOnlySpan<ulong>(before, 16).SequenceEqual(new ReadOnlySpan<ulong>(after, 16)));
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [Fact]
    public void ImmediateInterruptAndStopDoNotReachParentSignalHandlers()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        int parentSignals = 0;
        void RecordSignal(PosixSignalContext context)
        {
            Interlocked.Increment(ref parentSignals);
            context.Cancel = true;
        }
        using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, RecordSignal);
        using PosixSignalRegistration hangup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, RecordSignal);
        for (int i = 0; i < 25; i++)
        {
            using UnixPty pty = new();
            pty.Start(shell: "/bin/sh", arguments: ["-c", "sleep 0.02"]);
            pty.Write("\u0003");
            pty.Stop();
        }
        Assert.Equal(0, Volatile.Read(ref parentSignals));
    }

    [Fact]
    public void LeasedOutput_PreservesOrderAndFinalPartialBatch()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using UnixPty pty = new();
        using ManualResetEventSlim exited = new(false);
        List<byte> received = [];
        pty.OutputLeaseCallback = lease =>
        {
            lock (received)
            {
                received.AddRange(lease.Data.ToArray());
            }
            lease.Dispose();
        };
        pty.ProcessExited += _ => exited.Set();
        pty.Start(shell: "/bin/sh", arguments:
            ["-c", "i=0; while [ \"$i\" -lt 2000 ]; do printf '%04d|' \"$i\"; i=$((i+1)); done"]);

        Assert.True(exited.Wait(TimeSpan.FromSeconds(10)));
        Assert.False(pty.IsRunning);
        StringBuilder expected = new();
        for (int index = 0; index < 2000; index++)
        {
            expected.Append(index.ToString("D4", CultureInfo.InvariantCulture)).Append('|');
        }

        lock (received)
        {
            Assert.Equal(expected.ToString(), Encoding.ASCII.GetString(received.ToArray()));
        }
    }

    [Fact]
    public void Stop_UnblocksFullGatherRing_WithoutInvalidatingOutstandingLeases()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using UnixPty pty = new();
        using ManualResetEventSlim full = new(false);
        List<TerminalOutputLease> received = [];
        pty.OutputLeaseCallback = lease =>
        {
            lock (received)
            {
                received.Add(lease);
                if (received.Count == 4) full.Set();
            }
        };
        pty.Start(shell: "/bin/sh", arguments: ["-c", "head -c 1048576 /dev/zero"]);
        Assert.True(full.Wait(TimeSpan.FromSeconds(10)));
        pty.Stop();
        Assert.False(pty.IsRunning);

        lock (received)
        {
            Assert.Equal(4, received.Count);
            foreach (TerminalOutputLease lease in received)
            {
                Assert.InRange(lease.Data.Length, 1, 64 * 1024);
                Assert.True(lease.Data.Span.IndexOfAnyExcept((byte)0) < 0);
                lease.Dispose();
            }
        }
    }

    [Fact]
    public void Stop_WakesQuietPtyPoll()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using UnixPty pty = new();
        pty.Start(shell: "/bin/sh", arguments: ["-c", "sleep 30"]);
        pty.Stop();
        Assert.False(pty.IsRunning);
    }

    [Fact]
    public async Task Dispose_ConcurrentCallersJoinQuietReader()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using UnixPty pty = new();
        pty.Start(shell: "/bin/sh", arguments: ["-c", "sleep 30"]);
        await Task.WhenAll(Task.Run(pty.Dispose), Task.Run(pty.Dispose))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pty.IsRunning);
    }

    [Fact]
    public void Dispose_FromOutputCallback_DoesNotJoinItself()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using UnixPty pty = new();
        using ManualResetEventSlim stopped = new(false);
        pty.DataReceived += (_, _) =>
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                pty.Dispose();
            }
            stopped.Set();
        };
        pty.Start(shell: "/bin/sh", arguments: ["-c", "printf done; sleep 30"]);
        Assert.True(stopped.Wait(TimeSpan.FromSeconds(5)));
        pty.Dispose();
        Assert.False(pty.IsRunning);
    }
}
