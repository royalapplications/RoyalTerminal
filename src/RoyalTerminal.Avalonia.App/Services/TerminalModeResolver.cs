// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App — Capability-driven render-mode resolution for shell orchestration.

namespace RoyalTerminal.Avalonia.App.Services;

internal readonly record struct TerminalModeCapabilities(
    bool NativeVtAvailable,
    bool ManagedVtAvailable)
{
    public static TerminalModeCapabilities Create(bool nativeVtAvailable)
    {
        return new TerminalModeCapabilities(
            NativeVtAvailable: nativeVtAvailable,
            ManagedVtAvailable: true);
    }
}

internal enum TerminalRenderMode
{
    NativeVt = 0,
    ManagedVt = 1,
    RenderedAuto = 2,
}

internal interface ITerminalModeCapabilityResolver
{
    TerminalModeCapabilities Resolve(bool nativeVtAvailable);
}

internal interface ITerminalModeResolver
{
    TerminalRenderMode ResolveSupportedMode(TerminalRenderMode requestedMode, TerminalModeCapabilities capabilities);

    TerminalRenderMode ResolveNextMode(TerminalRenderMode currentMode, TerminalModeCapabilities capabilities);

    bool IsSupported(TerminalRenderMode mode, TerminalModeCapabilities capabilities);
}

internal sealed class TerminalModeCapabilityResolver : ITerminalModeCapabilityResolver
{
    public TerminalModeCapabilities Resolve(bool nativeVtAvailable)
    {
        return TerminalModeCapabilities.Create(nativeVtAvailable);
    }
}

internal sealed class TerminalModeResolver : ITerminalModeResolver
{
    public static TerminalModeResolver Default { get; } = new();

    private static readonly TerminalRenderMode[] s_cycleOrder =
    [
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
