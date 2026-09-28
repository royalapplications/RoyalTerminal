// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalFontWarmupTests
{
    [Fact]
    public async Task QueryRunsOffCallerThreadAndReturnedTaskOwnsCompletion()
    {
        using ManualResetEventSlim release = new();
        using ManualResetEventSlim started = new();
        int caller = Environment.CurrentManagedThreadId;
        int queryThread = 0;
        int cleanedUp = 0;
        Task<bool> warmup = TerminalFontWarmup.StartAsync(() =>
        {
            try { queryThread = Environment.CurrentManagedThreadId; started.Set(); release.Wait(); return true; }
            finally { Interlocked.Increment(ref cleanedUp); }
        });
        try
        {
            // Keep the caller occupied until the query begins so a later pool
            // continuation cannot legitimately reuse the caller's thread ID.
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            Assert.NotEqual(caller, queryThread);
            Assert.False(warmup.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref cleanedUp));
        }
        finally { release.Set(); }
        Assert.True(await warmup.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, Volatile.Read(ref cleanedUp));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingFontsAndQueryFailuresDoNotFaultStartupOrPoisonLaterRequests(bool throws)
    {
        int cleanedUp = 0;
        Assert.False(await TerminalFontWarmup.StartAsync(() =>
        {
            try
            {
                if (throws) throw new DllNotFoundException("injected");
                return false;
            }
            finally { cleanedUp++; }
        }));
        Assert.Equal(1, cleanedUp);
        Assert.True(await TerminalFontWarmup.StartAsync(() => true));
    }

    [Fact]
    public async Task PublicWarmupUsesMacOSRegistryAndIsANoOpElsewhere()
    {
        Task<bool> warmup = TerminalFontWarmup.StartAsync();
        if (!OperatingSystem.IsMacOS()) Assert.True(warmup.IsCompletedSuccessfully);
        Assert.Equal(OperatingSystem.IsMacOS(), await warmup.WaitAsync(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void NullQueryIsAProgrammingError()
        => Assert.Throws<ArgumentNullException>(() => { _ = TerminalFontWarmup.StartAsync(null!); });
}
