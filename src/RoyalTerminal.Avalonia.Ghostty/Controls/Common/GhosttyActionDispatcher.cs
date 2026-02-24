// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Controls - Shared Ghostty action dispatch helpers.

using System.Runtime.InteropServices;
using Avalonia.Threading;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Avalonia.Controls;

internal sealed class GhosttyActionDispatcher
{
    private readonly Func<GhosttySurface?> _surfaceAccessor;
    private readonly Action _renderRequested;
    private readonly Action<string> _titleChanged;
    private readonly Action<int> _processExited;
    private readonly Action _closeRequested;
    private readonly Action<GhosttyColorChange>? _colorChanged;
    private readonly Action<nint>? _configChanged;
    private readonly Action<bool>? _reloadConfig;

    public GhosttyActionDispatcher(
        Func<GhosttySurface?> surfaceAccessor,
        Action renderRequested,
        Action<string> titleChanged,
        Action<int> processExited,
        Action closeRequested,
        Action<GhosttyColorChange>? colorChanged = null,
        Action<nint>? configChanged = null,
        Action<bool>? reloadConfig = null)
    {
        _surfaceAccessor = surfaceAccessor ?? throw new ArgumentNullException(nameof(surfaceAccessor));
        _renderRequested = renderRequested ?? throw new ArgumentNullException(nameof(renderRequested));
        _titleChanged = titleChanged ?? throw new ArgumentNullException(nameof(titleChanged));
        _processExited = processExited ?? throw new ArgumentNullException(nameof(processExited));
        _closeRequested = closeRequested ?? throw new ArgumentNullException(nameof(closeRequested));
        _colorChanged = colorChanged;
        _configChanged = configChanged;
        _reloadConfig = reloadConfig;
    }

    public bool HandleAction(GhosttyTarget target, GhosttyAction action)
    {
        GhosttySurface? surface = _surfaceAccessor();
        if (target.Tag == GhosttyTargetTag.Surface)
        {
            // Ignore surface-scoped actions when there is no active surface or when
            // the action targets a different surface (for example after a recreate).
            if (surface is null || target.Target.Surface != surface.Handle)
            {
                return false;
            }
        }

        try
        {
            switch (action.Tag)
            {
                case GhosttyActionTag.SetTitle:
                {
                    string? title = TryReadTitle(action);
                    if (title is null)
                    {
                        return false;
                    }

                    Dispatcher.UIThread.Post(() => _titleChanged(title));
                    break;
                }

                case GhosttyActionTag.Render:
                    Dispatcher.UIThread.Post(_renderRequested);
                    break;

                case GhosttyActionTag.ShowChildExited:
                {
                    int exitCode = (int)action.Action.ChildExited.ExitCode;
                    Dispatcher.UIThread.Post(() => _processExited(exitCode));
                    break;
                }

                case GhosttyActionTag.CloseWindow:
                case GhosttyActionTag.Quit:
                    Dispatcher.UIThread.Post(_closeRequested);
                    break;

                case GhosttyActionTag.ColorChange:
                    if (_colorChanged is not null)
                    {
                        GhosttyColorChange change = action.Action.ColorChange;
                        Dispatcher.UIThread.Post(() => _colorChanged(change));
                    }
                    break;

                case GhosttyActionTag.ConfigChange:
                    if (_configChanged is not null)
                    {
                        // The config pointer is callback-scoped; do not marshal it
                        // asynchronously to the UI thread. Consumers must snapshot
                        // required data synchronously inside the callback.
                        nint configHandle = action.Action.ConfigChange.Config;
                        _configChanged(configHandle);
                    }
                    break;

                case GhosttyActionTag.ReloadConfig:
                    if (_reloadConfig is not null)
                    {
                        bool soft = action.Action.ReloadConfig.Soft;
                        Dispatcher.UIThread.Post(() => _reloadConfig(soft));
                    }
                    break;
            }
        }
        catch
        {
            // Never let exceptions escape the Ghostty runtime callback path.
            return false;
        }

        return false;
    }

    private static unsafe string? TryReadTitle(GhosttyAction action)
    {
        byte* titlePtr = action.Action.SetTitle.Title;
        return titlePtr is null
            ? null
            : Marshal.PtrToStringUTF8((nint)titlePtr);
    }
}
