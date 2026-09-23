// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

[Collection("PtyContractTests")]
public sealed class UnixPtyGatherTests
{
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
