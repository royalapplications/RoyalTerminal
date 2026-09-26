// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace RoyalTerminal.Avalonia.Services;

internal interface ITerminalSecureInputPlatform
{
    bool IsSupported { get; }
    bool TryEnable();
    bool TryDisable();
}

internal sealed class TerminalSecureInputScope(ITerminalSecureInputPlatform platform) : ITerminalSecureInputScope
{
    public bool IsSupported => platform.IsSupported;
    public bool IsEnabled { get; private set; }

    public bool TrySetEnabled(bool enabled)
    {
        if (enabled == IsEnabled) return true;
        if (enabled ? !platform.IsSupported || !platform.TryEnable() : !platform.TryDisable()) return false;
        IsEnabled = enabled;
        return true;
    }
}

internal static class TerminalSecureInputScopeFactory
{
    internal static ITerminalSecureInputScope Create() => new TerminalSecureInputScope(new MacOsSecureInputPlatform());
}

internal sealed partial class MacOsSecureInputPlatform : ITerminalSecureInputPlatform
{
    private bool _available = true;
    public bool IsSupported => _available && OperatingSystem.IsMacOS();
    public bool TryEnable() => Invoke(enable: true);
    public bool TryDisable() => Invoke(enable: false);

    private bool Invoke(bool enable)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!IsSupported) return false;
        try { return (enable ? EnableSecureEventInput() : DisableSecureEventInput()) == 0; }
        catch (DllNotFoundException) { _available = false; return false; }
        catch (EntryPointNotFoundException) { _available = false; return false; }
    }

    // CarbonEventsCore.h: OSStatus (signed 32-bit), no arguments. APIs are not
    // thread safe and maintain a per-process count, so each scope balances only
    // its own successful enable. Never disable another host's secure input.
    internal const string Library = "/System/Library/Frameworks/Carbon.framework/Carbon";
    [LibraryImport(Library)]
    private static partial int EnableSecureEventInput();
    [LibraryImport(Library)]
    private static partial int DisableSecureEventInput();
}
