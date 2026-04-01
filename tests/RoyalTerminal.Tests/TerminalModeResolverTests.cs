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

        TerminalModeCapabilities macLike = resolver.Resolve(embeddedGhosttyAvailable: true, nativeVtAvailable: true);
        Assert.True(macLike.EmbeddedGhosttyNativeAvailable);
        Assert.True(macLike.GhosttyRenderedAvailable);
        Assert.True(macLike.NativeVtAvailable);
        Assert.True(macLike.ManagedVtAvailable);

        TerminalModeCapabilities linuxWindowsLike = resolver.Resolve(embeddedGhosttyAvailable: false, nativeVtAvailable: true);
        Assert.False(linuxWindowsLike.EmbeddedGhosttyNativeAvailable);
        Assert.True(linuxWindowsLike.GhosttyRenderedAvailable);
        Assert.True(linuxWindowsLike.NativeVtAvailable);
        Assert.True(linuxWindowsLike.ManagedVtAvailable);

        TerminalModeCapabilities managedOnly = resolver.Resolve(embeddedGhosttyAvailable: false, nativeVtAvailable: false);
        Assert.False(managedOnly.EmbeddedGhosttyNativeAvailable);
        Assert.False(managedOnly.GhosttyRenderedAvailable);
        Assert.False(managedOnly.NativeVtAvailable);
        Assert.True(managedOnly.ManagedVtAvailable);
    }

    [Fact]
    public void ResolveSupportedMode_MacOsCapabilities_PreservesRequestedMode()
    {
        TerminalModeResolver resolver = TerminalModeResolver.Default;
        TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(
            embeddedGhosttyAvailable: true,
            nativeVtAvailable: true);

        Assert.Equal(
            TerminalRenderMode.GhosttyRendered,
            resolver.ResolveSupportedMode(TerminalRenderMode.GhosttyRendered, capabilities));
        Assert.Equal(
            TerminalRenderMode.GhosttyNative,
            resolver.ResolveSupportedMode(TerminalRenderMode.GhosttyNative, capabilities));
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
    public void ResolveSupportedMode_NativeVtOnlyCapabilities_PreservesGhosttyRenderedAndNativeVt()
    {
        TerminalModeResolver resolver = TerminalModeResolver.Default;
        TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(
            embeddedGhosttyAvailable: false,
            nativeVtAvailable: true);

        Assert.Equal(
            TerminalRenderMode.GhosttyRendered,
            resolver.ResolveSupportedMode(TerminalRenderMode.GhosttyRendered, capabilities));
        Assert.Equal(
            TerminalRenderMode.NativeVt,
            resolver.ResolveSupportedMode(TerminalRenderMode.GhosttyNative, capabilities));
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
        TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(
            embeddedGhosttyAvailable: false,
            nativeVtAvailable: false);

        Assert.Equal(
            TerminalRenderMode.ManagedVt,
            resolver.ResolveSupportedMode(TerminalRenderMode.GhosttyRendered, capabilities));
        Assert.Equal(
            TerminalRenderMode.ManagedVt,
            resolver.ResolveSupportedMode(TerminalRenderMode.GhosttyNative, capabilities));
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
        TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(
            embeddedGhosttyAvailable: true,
            nativeVtAvailable: true);

        Assert.Equal(
            TerminalRenderMode.GhosttyNative,
            resolver.ResolveNextMode(TerminalRenderMode.GhosttyRendered, capabilities));
        Assert.Equal(
            TerminalRenderMode.NativeVt,
            resolver.ResolveNextMode(TerminalRenderMode.GhosttyNative, capabilities));
        Assert.Equal(
            TerminalRenderMode.ManagedVt,
            resolver.ResolveNextMode(TerminalRenderMode.NativeVt, capabilities));
        Assert.Equal(
            TerminalRenderMode.RenderedAuto,
            resolver.ResolveNextMode(TerminalRenderMode.ManagedVt, capabilities));
        Assert.Equal(
            TerminalRenderMode.GhosttyRendered,
            resolver.ResolveNextMode(TerminalRenderMode.RenderedAuto, capabilities));
    }

    [Fact]
    public void ResolveNextMode_NativeVtOnlyCapabilities_StillIncludesGhosttyRendered()
    {
        TerminalModeResolver resolver = TerminalModeResolver.Default;
        TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(
            embeddedGhosttyAvailable: false,
            nativeVtAvailable: true);

        Assert.Equal(
            TerminalRenderMode.GhosttyRendered,
            resolver.ResolveNextMode(TerminalRenderMode.RenderedAuto, capabilities));
        Assert.Equal(
            TerminalRenderMode.NativeVt,
            resolver.ResolveNextMode(TerminalRenderMode.GhosttyRendered, capabilities));
        Assert.Equal(
            TerminalRenderMode.ManagedVt,
            resolver.ResolveNextMode(TerminalRenderMode.NativeVt, capabilities));
        Assert.Equal(
            TerminalRenderMode.RenderedAuto,
            resolver.ResolveNextMode(TerminalRenderMode.ManagedVt, capabilities));
    }

    [Fact]
    public void ResolveNextMode_ManagedOnly_SkipsToManagedAndRenderedOnly()
    {
        TerminalModeResolver resolver = TerminalModeResolver.Default;
        TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(
            embeddedGhosttyAvailable: false,
            nativeVtAvailable: false);

        Assert.Equal(
            TerminalRenderMode.ManagedVt,
            resolver.ResolveNextMode(TerminalRenderMode.RenderedAuto, capabilities));
        Assert.Equal(
            TerminalRenderMode.RenderedAuto,
            resolver.ResolveNextMode(TerminalRenderMode.ManagedVt, capabilities));
    }
}
