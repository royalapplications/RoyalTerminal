// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using System.Runtime.InteropServices;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class UnixThreadSchedulingTests(ITestOutputHelper output)
{
    [Fact]
    public void DedicatedThread_RequestsUserInitiatedQosOrPreservesExistingPolicy()
    {
        if (!OperatingSystem.IsMacOS()) return;
        bool applied = false;
        int error = 0;
        uint before = 0;
        uint after = 0;
        Thread thread = new(() =>
        {
            if (OperatingSystem.IsMacOS())
            {
                before = GetCurrentQosClass();
                applied = UnixThreadScheduling.TrySetCurrentThreadUserInitiated(out error);
                after = GetCurrentQosClass();
            }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        output.WriteLine($"QoS request applied={applied}, error={error}, before=0x{before:x}, after=0x{after:x}");
        if (applied)
        {
            Assert.Equal(0, error);
            Assert.Equal(0x19u, after);
        }
        else
        {
            Assert.NotEqual(0, error);
            Assert.Equal(before, after);
        }
    }

    [Fact]
    public async Task SharedThreadPoolThread_DoesNotChangeQos()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Assert.False(await Task.Run(UnixThreadScheduling.TrySetCurrentThreadUserInitiated));
    }

    [LibraryImport("libSystem.dylib", EntryPoint = "qos_class_self")]
    private static partial uint GetCurrentQosClass();
}
