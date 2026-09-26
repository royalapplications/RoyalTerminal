// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.Avalonia.Services;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalSecureInputScopeTests
{
    [Fact]
    public void RepeatedRequestsOwnExactlyOneBalancedEnable()
    {
        FakeSecureInputPlatform platform = new();
        TerminalSecureInputScope scope = new(platform);
        Assert.True(scope.IsSupported);
        Assert.False(scope.IsEnabled);
        for (int i = 0; i < 10; i++) Assert.True(scope.TrySetEnabled(true));
        Assert.True(scope.IsEnabled);
        Assert.Equal(1, platform.EnableCalls);
        for (int i = 0; i < 10; i++) Assert.True(scope.TrySetEnabled(false));
        Assert.False(scope.IsEnabled);
        Assert.Equal(1, platform.DisableCalls);
        Assert.Equal(0, platform.Owners);
    }

    [Fact]
    public void FailedEnableNeverCreatesOwnershipAndCanBeRetried()
    {
        FakeSecureInputPlatform platform = new() { EnableFailures = 1 };
        TerminalSecureInputScope scope = new(platform);
        Assert.False(scope.TrySetEnabled(true));
        Assert.False(scope.IsEnabled);
        Assert.True(scope.TrySetEnabled(false));
        Assert.Equal(0, platform.DisableCalls);
        Assert.True(scope.TrySetEnabled(true));
        Assert.True(scope.IsEnabled);
        Assert.Equal(2, platform.EnableCalls);
        Assert.True(scope.TrySetEnabled(false));
        Assert.Equal(0, platform.Owners);
    }

    [Fact]
    public void FailedDisableRetainsOwnershipWithoutAnotherEnable()
    {
        FakeSecureInputPlatform platform = new() { DisableFailures = 1 };
        TerminalSecureInputScope scope = new(platform);
        Assert.True(scope.TrySetEnabled(true));
        Assert.False(scope.TrySetEnabled(false));
        Assert.True(scope.IsEnabled);
        Assert.True(scope.TrySetEnabled(true));
        Assert.Equal(1, platform.EnableCalls);
        Assert.True(scope.TrySetEnabled(false));
        Assert.False(scope.IsEnabled);
        Assert.Equal(2, platform.DisableCalls);
        Assert.Equal(0, platform.Owners);
    }

    [Fact]
    public void MultipleScopesCannotReleaseEachOthersOwnership()
    {
        FakeSecureInputPlatform platform = new();
        TerminalSecureInputScope first = new(platform), second = new(platform);
        Assert.True(first.TrySetEnabled(true));
        Assert.True(second.TrySetEnabled(true));
        Assert.Equal(2, platform.Owners);
        Assert.True(first.TrySetEnabled(false));
        Assert.True(first.TrySetEnabled(false));
        Assert.True(second.IsEnabled);
        Assert.Equal(1, platform.Owners);
        Assert.True(second.TrySetEnabled(false));
        Assert.Equal(0, platform.Owners);
    }

    [Fact]
    public void UnsupportedPlatformsNeverInvokeNativeEnableOrDisable()
    {
        FakeSecureInputPlatform platform = new() { IsSupported = false };
        TerminalSecureInputScope scope = new(platform);
        Assert.False(scope.IsSupported);
        Assert.False(scope.TrySetEnabled(true));
        Assert.True(scope.TrySetEnabled(false));
        Assert.False(scope.IsEnabled);
        Assert.Equal(0, platform.EnableCalls);
        Assert.Equal(0, platform.DisableCalls);
    }

    [Fact]
    public void MacOsFrameworkExportsBalancedApiWithoutEnablingSecureInput()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Assert.True(NativeLibrary.TryLoad(MacOsSecureInputPlatform.Library, out nint library));
        try
        {
            Assert.True(NativeLibrary.TryGetExport(library, "EnableSecureEventInput", out _));
            Assert.True(NativeLibrary.TryGetExport(library, "DisableSecureEventInput", out _));
        }
        finally { NativeLibrary.Free(library); }
    }
}

internal sealed class FakeSecureInputPlatform : ITerminalSecureInputPlatform
{
    public bool IsSupported { get; set; } = true;
    public int EnableFailures { get; set; }
    public int DisableFailures { get; set; }
    public int EnableCalls { get; private set; }
    public int DisableCalls { get; private set; }
    public int Owners { get; private set; }
    public bool TryEnable()
    {
        EnableCalls++;
        if (EnableFailures > 0) { EnableFailures--; return false; }
        Owners++;
        return true;
    }
    public bool TryDisable()
    {
        DisableCalls++;
        if (DisableFailures > 0) { DisableFailures--; return false; }
        Assert.True(Owners > 0);
        Owners--;
        return true;
    }
}
