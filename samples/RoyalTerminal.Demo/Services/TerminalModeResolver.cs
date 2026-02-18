// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo — Capability-driven render-mode resolution for demo orchestration.

namespace RoyalTerminal.Demo.Services;

internal readonly record struct TerminalModeCapabilities(
    bool EmbeddedGhosttyNativeAvailable,
    bool EmbeddedGhosttyRenderedAvailable,
    bool NativeVtAvailable,
    bool ManagedVtAvailable)
{
    public static TerminalModeCapabilities Create(bool embeddedGhosttyAvailable, bool nativeVtAvailable)
    {
        return new TerminalModeCapabilities(
            EmbeddedGhosttyNativeAvailable: embeddedGhosttyAvailable,
            EmbeddedGhosttyRenderedAvailable: embeddedGhosttyAvailable,
            NativeVtAvailable: nativeVtAvailable,
            ManagedVtAvailable: true);
    }
}

internal enum TerminalRenderMode
{
    GhosttyRendered = 0,
    GhosttyNative = 1,
    NativeVt = 2,
    ManagedVt = 3,
    RenderedAuto = 4,
}

internal interface ITerminalModeCapabilityResolver
{
    TerminalModeCapabilities Resolve(bool embeddedGhosttyAvailable, bool nativeVtAvailable);
}

internal interface ITerminalModeResolver
{
    TerminalRenderMode ResolveSupportedMode(TerminalRenderMode requestedMode, TerminalModeCapabilities capabilities);

    TerminalRenderMode ResolveNextMode(TerminalRenderMode currentMode, TerminalModeCapabilities capabilities);

    bool IsSupported(TerminalRenderMode mode, TerminalModeCapabilities capabilities);
}

internal sealed class TerminalModeCapabilityResolver : ITerminalModeCapabilityResolver
{
    public TerminalModeCapabilities Resolve(bool embeddedGhosttyAvailable, bool nativeVtAvailable)
    {
        return TerminalModeCapabilities.Create(embeddedGhosttyAvailable, nativeVtAvailable);
    }
}

internal sealed class TerminalModeResolver : ITerminalModeResolver
{
    public static TerminalModeResolver Default { get; } = new();

    private static readonly TerminalRenderMode[] s_cycleOrder =
    [
        TerminalRenderMode.GhosttyRendered,
        TerminalRenderMode.GhosttyNative,
        TerminalRenderMode.NativeVt,
        TerminalRenderMode.ManagedVt,
        TerminalRenderMode.RenderedAuto,
    ];

    public TerminalRenderMode ResolveSupportedMode(
        TerminalRenderMode requestedMode,
        TerminalModeCapabilities capabilities)
    {
        if (IsSupported(requestedMode, capabilities))
        {
            return requestedMode;
        }

        int requestedIndex = Array.IndexOf(s_cycleOrder, requestedMode);
        int startIndex = requestedIndex >= 0 ? requestedIndex : -1;
        return FindNextSupportedMode(startIndex, capabilities);
    }

    public TerminalRenderMode ResolveNextMode(
        TerminalRenderMode currentMode,
        TerminalModeCapabilities capabilities)
    {
        int currentIndex = Array.IndexOf(s_cycleOrder, currentMode);
        int startIndex = currentIndex >= 0 ? currentIndex : -1;
        return FindNextSupportedMode(startIndex, capabilities);
    }

    public bool IsSupported(TerminalRenderMode mode, TerminalModeCapabilities capabilities)
    {
        return mode switch
        {
            TerminalRenderMode.GhosttyRendered => capabilities.EmbeddedGhosttyRenderedAvailable,
            TerminalRenderMode.GhosttyNative => capabilities.EmbeddedGhosttyNativeAvailable,
            TerminalRenderMode.NativeVt => capabilities.NativeVtAvailable,
            TerminalRenderMode.ManagedVt => capabilities.ManagedVtAvailable,
            TerminalRenderMode.RenderedAuto => true,
            _ => false,
        };
    }

    private TerminalRenderMode FindNextSupportedMode(int startIndex, TerminalModeCapabilities capabilities)
    {
        int modeCount = s_cycleOrder.Length;
        for (int offset = 1; offset <= modeCount; offset++)
        {
            int candidateIndex = (startIndex + offset + modeCount) % modeCount;
            TerminalRenderMode candidate = s_cycleOrder[candidateIndex];
            if (IsSupported(candidate, capabilities))
            {
                return candidate;
            }
        }

        return TerminalRenderMode.RenderedAuto;
    }
}
