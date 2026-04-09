// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — capability and mode resolver matrix coverage.

using RoyalTerminal.Demo.Services;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalModeResolverTests
{
    [Fact]
    public void CapabilityResolver_ResolvesExpectedFlags()
    {
        TerminalModeCapabilityResolver resolver = new();

        TerminalModeCapabilities nativeLike = resolver.Resolve(nativeVtAvailable: true);
        Assert.True(nativeLike.NativeVtAvailable);
        Assert.True(nativeLike.ManagedVtAvailable);

        TerminalModeCapabilities managedOnly = resolver.Resolve(nativeVtAvailable: false);
        Assert.False(managedOnly.NativeVtAvailable);
        Assert.True(managedOnly.ManagedVtAvailable);
    }

    [Fact]
    public void ResolveSupportedMode_NativeCapabilities_PreservesRequestedMode()
    {
        TerminalModeResolver resolver = TerminalModeResolver.Default;
        TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(nativeVtAvailable: true);

        Assert.Equal(
            TerminalRenderMode.NativeVt,
            resolver.ResolveSupportedMode(TerminalRenderMode.NativeVt, capabilities));
        Assert.Equal(
            TerminalRenderMode.ManagedVt,
            resolver.ResolveSupportedMode(TerminalRenderMode.ManagedVt, capabilities));
        Assert.Equal(
            TerminalRenderMode.RenderedAuto,
            resolver.ResolveSupportedMode(TerminalRenderMode.RenderedAuto, capabilities));
    }

    [Fact]
    public void ResolveSupportedMode_ManagedOnlyCapabilities_FallsBackToManagedOrRendered()
    {
        TerminalModeResolver resolver = TerminalModeResolver.Default;
        TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(nativeVtAvailable: false);

        Assert.Equal(
            TerminalRenderMode.ManagedVt,
            resolver.ResolveSupportedMode(TerminalRenderMode.NativeVt, capabilities));
        Assert.Equal(
            TerminalRenderMode.ManagedVt,
            resolver.ResolveSupportedMode(TerminalRenderMode.ManagedVt, capabilities));
        Assert.Equal(
            TerminalRenderMode.RenderedAuto,
            resolver.ResolveSupportedMode(TerminalRenderMode.RenderedAuto, capabilities));
    }

    [Fact]
    public void ResolveNextMode_AllCapabilities_UsesStableCycleOrder()
    {
        TerminalModeResolver resolver = TerminalModeResolver.Default;
        TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(nativeVtAvailable: true);

        Assert.Equal(
            TerminalRenderMode.ManagedVt,
            resolver.ResolveNextMode(TerminalRenderMode.NativeVt, capabilities));
        Assert.Equal(
            TerminalRenderMode.RenderedAuto,
            resolver.ResolveNextMode(TerminalRenderMode.ManagedVt, capabilities));
        Assert.Equal(
            TerminalRenderMode.NativeVt,
            resolver.ResolveNextMode(TerminalRenderMode.RenderedAuto, capabilities));
    }

    [Fact]
    public void ResolveNextMode_ManagedOnly_SkipsToManagedAndRenderedOnly()
    {
        TerminalModeResolver resolver = TerminalModeResolver.Default;
        TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(nativeVtAvailable: false);

        Assert.Equal(
            TerminalRenderMode.ManagedVt,
            resolver.ResolveNextMode(TerminalRenderMode.RenderedAuto, capabilities));
        Assert.Equal(
            TerminalRenderMode.RenderedAuto,
            resolver.ResolveNextMode(TerminalRenderMode.ManagedVt, capabilities));
    }
}
