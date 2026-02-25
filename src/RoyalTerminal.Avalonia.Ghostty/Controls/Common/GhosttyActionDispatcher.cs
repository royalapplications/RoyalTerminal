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
    private readonly Action? _bellRequested;
    private readonly Action<GhosttyColorChange>? _colorChanged;
    private readonly Action<nint>? _configChanged;
    private readonly Action<bool>? _reloadConfig;
    private readonly Action<string?>? _mouseOverLinkChanged;
    private readonly Action<string?>? _searchStarted;
    private readonly Action? _searchEnded;
    private readonly Action<int>? _searchTotalChanged;
    private readonly Action<int>? _searchSelectedChanged;
    private readonly Action? _toggleBackgroundOpacity;

    public GhosttyActionDispatcher(
        Func<GhosttySurface?> surfaceAccessor,
        Action renderRequested,
        Action<string> titleChanged,
        Action<int> processExited,
        Action closeRequested,
        Action? bellRequested = null,
        Action<GhosttyColorChange>? colorChanged = null,
        Action<nint>? configChanged = null,
        Action<bool>? reloadConfig = null,
        Action<string?>? mouseOverLinkChanged = null,
        Action<string?>? searchStarted = null,
        Action? searchEnded = null,
        Action<int>? searchTotalChanged = null,
        Action<int>? searchSelectedChanged = null,
        Action? toggleBackgroundOpacity = null)
    {
        _surfaceAccessor = surfaceAccessor ?? throw new ArgumentNullException(nameof(surfaceAccessor));
        _renderRequested = renderRequested ?? throw new ArgumentNullException(nameof(renderRequested));
        _titleChanged = titleChanged ?? throw new ArgumentNullException(nameof(titleChanged));
        _processExited = processExited ?? throw new ArgumentNullException(nameof(processExited));
        _closeRequested = closeRequested ?? throw new ArgumentNullException(nameof(closeRequested));
        _bellRequested = bellRequested;
        _colorChanged = colorChanged;
        _configChanged = configChanged;
        _reloadConfig = reloadConfig;
        _mouseOverLinkChanged = mouseOverLinkChanged;
        _searchStarted = searchStarted;
        _searchEnded = searchEnded;
        _searchTotalChanged = searchTotalChanged;
        _searchSelectedChanged = searchSelectedChanged;
        _toggleBackgroundOpacity = toggleBackgroundOpacity;
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

                case GhosttyActionTag.MouseOverLink:
                    if (_mouseOverLinkChanged is not null)
                    {
                        string? url = TryReadMouseOverLink(action);
                        Dispatcher.UIThread.Post(() => _mouseOverLinkChanged(url));
                    }
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

                case GhosttyActionTag.RingBell:
                    if (_bellRequested is not null)
                    {
                        Dispatcher.UIThread.Post(_bellRequested);
                    }
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

                case GhosttyActionTag.StartSearch:
                    if (_searchStarted is not null)
                    {
                        string? needle = TryReadSearchNeedle(action);
                        Dispatcher.UIThread.Post(() => _searchStarted(needle));
                    }
                    break;

                case GhosttyActionTag.EndSearch:
                    if (_searchEnded is not null)
                    {
                        Dispatcher.UIThread.Post(_searchEnded);
                    }
                    break;

                case GhosttyActionTag.SearchTotal:
                    if (_searchTotalChanged is not null)
                    {
                        int total = ClampToInt32(action.Action.SearchTotal.Total);
                        Dispatcher.UIThread.Post(() => _searchTotalChanged(total));
                    }
                    break;

                case GhosttyActionTag.SearchSelected:
                    if (_searchSelectedChanged is not null)
                    {
                        int selected = ClampToInt32(action.Action.SearchSelected.Selected);
                        Dispatcher.UIThread.Post(() => _searchSelectedChanged(selected));
                    }
                    break;

                case GhosttyActionTag.ToggleBackgroundOpacity:
                    if (_toggleBackgroundOpacity is not null)
                    {
                        Dispatcher.UIThread.Post(_toggleBackgroundOpacity);
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

    private static unsafe string? TryReadMouseOverLink(GhosttyAction action)
    {
        byte* urlPtr = action.Action.MouseOverLink.Url;
        if (urlPtr is null)
        {
            return null;
        }

        nuint len = action.Action.MouseOverLink.Len;
        if (len == 0)
        {
            return null;
        }

        int length = len > int.MaxValue ? int.MaxValue : (int)len;
        return Marshal.PtrToStringUTF8((nint)urlPtr, length);
    }

    private static unsafe string? TryReadSearchNeedle(GhosttyAction action)
    {
        byte* needlePtr = action.Action.StartSearch.Needle;
        return needlePtr is null
            ? null
            : Marshal.PtrToStringUTF8((nint)needlePtr);
    }

    private static int ClampToInt32(nint value)
    {
        long number = value;
        return number switch
        {
            > int.MaxValue => int.MaxValue,
            < int.MinValue => int.MinValue,
            _ => (int)number,
        };
    }
}
