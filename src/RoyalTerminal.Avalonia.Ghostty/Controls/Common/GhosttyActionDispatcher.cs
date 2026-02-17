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

    public GhosttyActionDispatcher(
        Func<GhosttySurface?> surfaceAccessor,
        Action renderRequested,
        Action<string> titleChanged,
        Action<int> processExited,
        Action closeRequested)
    {
        _surfaceAccessor = surfaceAccessor ?? throw new ArgumentNullException(nameof(surfaceAccessor));
        _renderRequested = renderRequested ?? throw new ArgumentNullException(nameof(renderRequested));
        _titleChanged = titleChanged ?? throw new ArgumentNullException(nameof(titleChanged));
        _processExited = processExited ?? throw new ArgumentNullException(nameof(processExited));
        _closeRequested = closeRequested ?? throw new ArgumentNullException(nameof(closeRequested));
    }

    public bool HandleAction(GhosttyTarget target, GhosttyAction action)
    {
        GhosttySurface? surface = _surfaceAccessor();
        if (target.Tag == GhosttyTargetTag.Surface
            && surface is not null
            && target.Target.Surface != surface.Handle)
        {
            return false;
        }

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
