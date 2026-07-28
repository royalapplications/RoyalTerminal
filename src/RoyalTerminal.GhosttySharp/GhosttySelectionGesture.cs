// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>Frequently queried selection-gesture state returned by one native call.</summary>
/// <param name="ClickCount">Current click count, where zero means inactive.</param>
/// <param name="Dragged">Whether the current or last left-click gesture dragged.</param>
/// <param name="Autoscroll">Current selection autoscroll request.</param>
/// <param name="Behavior">Active click behavior.</param>
public readonly record struct GhosttySelectionGestureSnapshot(
    byte ClickCount,
    bool Dragged,
    GhosttyVtNative.GhosttySelectionGestureAutoscroll Autoscroll,
    GhosttyVtNative.GhosttySelectionGestureBehavior Behavior);

/// <summary>Owned, reusable input event for Ghostty's selection-gesture state machine.</summary>
public sealed class GhosttySelectionGestureEvent : IDisposable
{
    private nint _handle;
    private bool _disposed;

    /// <summary>Creates a reusable event with a fixed event type.</summary>
    public GhosttySelectionGestureEvent(GhosttyVtNative.GhosttySelectionGestureEventType type)
    {
        NativeLibraryLoader.Initialize();
        ThrowIfFailed(
            GhosttyVtNative.SelectionGestureEventNew(nint.Zero, out _handle, type),
            "ghostty_selection_gesture_event_new");
    }

    internal nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>Sets the grid reference under the pointer.</summary>
    public void SetReference(GhosttyVtNative.GhosttyGridRef reference)
        => SetOption(GhosttyVtNative.GhosttySelectionGestureEventOption.Reference, reference);

    /// <summary>Sets the pointer position in surface pixels.</summary>
    public void SetPosition(double x, double y)
        => SetOption(
            GhosttyVtNative.GhosttySelectionGestureEventOption.Position,
            new GhosttyVtNative.GhosttySurfacePosition { X = x, Y = y });

    /// <summary>Sets the maximum repeat-click distance in pixels.</summary>
    public void SetRepeatDistance(double pixels)
        => SetOption(GhosttyVtNative.GhosttySelectionGestureEventOption.RepeatDistance, pixels);

    /// <summary>Sets the optional monotonic event time in nanoseconds.</summary>
    public void SetTimeNanoseconds(ulong nanoseconds)
        => SetOption(GhosttyVtNative.GhosttySelectionGestureEventOption.TimeNanoseconds, nanoseconds);

    /// <summary>Sets the maximum interval between repeat clicks.</summary>
    public void SetRepeatIntervalNanoseconds(ulong nanoseconds)
        => SetOption(
            GhosttyVtNative.GhosttySelectionGestureEventOption.RepeatIntervalNanoseconds,
            nanoseconds);

    /// <summary>Sets word-boundary codepoints, which are copied by the native event.</summary>
    public unsafe void SetWordBoundaryCodepoints(ReadOnlySpan<uint> codepoints)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        fixed (uint* codepointsPtr = codepoints)
        {
            GhosttyVtNative.GhosttyCodepoints value = new()
            {
                Pointer = codepointsPtr,
                Length = (nuint)codepoints.Length,
            };
            ThrowIfFailed(
                GhosttyVtNative.SelectionGestureEventSet(
                    _handle,
                    GhosttyVtNative.GhosttySelectionGestureEventOption.WordBoundaryCodepoints,
                    &value),
                "ghostty_selection_gesture_event_set(word_boundary_codepoints)");
        }
    }

    /// <summary>Sets the single-, double-, and triple-click behavior table.</summary>
    public void SetBehaviors(GhosttyVtNative.GhosttySelectionGestureBehaviors behaviors)
        => SetOption(GhosttyVtNative.GhosttySelectionGestureEventOption.Behaviors, behaviors);

    /// <summary>Sets whether drag selection is rectangular.</summary>
    public void SetRectangle(bool rectangle)
        => SetOption(GhosttyVtNative.GhosttySelectionGestureEventOption.Rectangle, rectangle);

    /// <summary>Sets the display geometry required by drag and autoscroll events.</summary>
    public void SetGeometry(GhosttyVtNative.GhosttySelectionGestureGeometry geometry)
        => SetOption(GhosttyVtNative.GhosttySelectionGestureEventOption.Geometry, geometry);

    /// <summary>Sets the viewport coordinate required by autoscroll-tick events.</summary>
    public void SetViewport(GhosttyVtNative.GhosttyPointCoordinate viewport)
        => SetOption(GhosttyVtNative.GhosttySelectionGestureEventOption.Viewport, viewport);

    /// <summary>Clears an optional event value so Ghostty uses its initialized default.</summary>
    public unsafe void Clear(GhosttyVtNative.GhosttySelectionGestureEventOption option)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(
            GhosttyVtNative.SelectionGestureEventSet(_handle, option, null),
            $"ghostty_selection_gesture_event_set({option})");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GhosttyVtNative.SelectionGestureEventFree(_handle);
        _handle = nint.Zero;
    }

    private unsafe void SetOption<T>(
        GhosttyVtNative.GhosttySelectionGestureEventOption option,
        T value)
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        T copy = value;
        ThrowIfFailed(
            GhosttyVtNative.SelectionGestureEventSet(_handle, option, &copy),
            $"ghostty_selection_gesture_event_set({option})");
    }

    private static void ThrowIfFailed(GhosttyVtNative.GhosttyResult result, string operation)
    {
        if (result != GhosttyVtNative.GhosttyResult.Success)
        {
            throw new InvalidOperationException($"{operation} failed with {result}.");
        }
    }
}

/// <summary>Owned state machine for Ghostty terminal text-selection gestures.</summary>
public sealed class GhosttySelectionGesture : IDisposable
{
    private nint _handle;
    private GhosttyTerminal? _lastTerminal;
    private bool _disposed;

    /// <summary>Creates an empty selection-gesture state machine.</summary>
    public GhosttySelectionGesture()
    {
        NativeLibraryLoader.Initialize();
        ThrowIfFailed(
            GhosttyVtNative.SelectionGestureNew(nint.Zero, out _handle),
            "ghostty_selection_gesture_new");
    }

    /// <summary>Applies an event and returns its selection snapshot when one is produced.</summary>
    public unsafe bool TryApply(
        GhosttyTerminal terminal,
        GhosttySelectionGestureEvent gestureEvent,
        out GhosttySelection selection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(gestureEvent);
        _lastTerminal = terminal;
        GhosttyVtNative.GhosttySelectionRange native =
            GhosttyVtNative.GhosttySelectionRange.CreateSized();
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.SelectionGestureEvent(
            _handle,
            terminal.Handle,
            gestureEvent.Handle,
            &native);
        if (result == GhosttyVtNative.GhosttyResult.NoValue)
        {
            selection = default;
            return false;
        }

        ThrowIfFailed(result, "ghostty_selection_gesture_event");
        selection = GhosttySelection.FromNative(in native);
        return true;
    }

    /// <summary>Applies an event while discarding any selection snapshot.</summary>
    public unsafe void Apply(GhosttyTerminal terminal, GhosttySelectionGestureEvent gestureEvent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(gestureEvent);
        _lastTerminal = terminal;
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.SelectionGestureEvent(
            _handle,
            terminal.Handle,
            gestureEvent.Handle,
            null);
        if (result != GhosttyVtNative.GhosttyResult.NoValue)
        {
            ThrowIfFailed(result, "ghostty_selection_gesture_event");
        }
    }

    /// <summary>Resets active click, drag, and tracked-reference state.</summary>
    public void Reset(GhosttyTerminal terminal)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(terminal);
        _lastTerminal = terminal;
        GhosttyVtNative.SelectionGestureReset(_handle, terminal.Handle);
    }

    /// <summary>Gets the current click count, where zero means inactive.</summary>
    public byte GetClickCount(GhosttyTerminal terminal)
        => GetValue<byte>(terminal, GhosttyVtNative.GhosttySelectionGestureData.ClickCount);

    /// <summary>Gets whether the current or last left-click gesture dragged.</summary>
    public bool GetDragged(GhosttyTerminal terminal)
        => GetValue<bool>(terminal, GhosttyVtNative.GhosttySelectionGestureData.Dragged);

    /// <summary>Gets the current selection autoscroll request.</summary>
    public GhosttyVtNative.GhosttySelectionGestureAutoscroll GetAutoscroll(GhosttyTerminal terminal)
        => GetValue<GhosttyVtNative.GhosttySelectionGestureAutoscroll>(
            terminal,
            GhosttyVtNative.GhosttySelectionGestureData.Autoscroll);

    /// <summary>Gets the active click behavior.</summary>
    public GhosttyVtNative.GhosttySelectionGestureBehavior GetBehavior(GhosttyTerminal terminal)
        => GetValue<GhosttyVtNative.GhosttySelectionGestureBehavior>(
            terminal,
            GhosttyVtNative.GhosttySelectionGestureData.Behavior);

    /// <summary>Gets frequently queried gesture state using one native multi-get call.</summary>
    public unsafe GhosttySelectionGestureSnapshot GetSnapshot(GhosttyTerminal terminal)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(terminal);
        _lastTerminal = terminal;

        byte clickCount = 0;
        bool dragged = false;
        GhosttyVtNative.GhosttySelectionGestureAutoscroll autoscroll = default;
        GhosttyVtNative.GhosttySelectionGestureBehavior behavior = default;
        GhosttyVtNative.GhosttySelectionGestureData* keys =
            stackalloc GhosttyVtNative.GhosttySelectionGestureData[4]
            {
                GhosttyVtNative.GhosttySelectionGestureData.ClickCount,
                GhosttyVtNative.GhosttySelectionGestureData.Dragged,
                GhosttyVtNative.GhosttySelectionGestureData.Autoscroll,
                GhosttyVtNative.GhosttySelectionGestureData.Behavior,
            };
        void** values = stackalloc void*[4]
        {
            &clickCount,
            &dragged,
            &autoscroll,
            &behavior,
        };
        nuint written = 0;

        ThrowIfFailed(
            GhosttyVtNative.SelectionGestureGetMulti(
                _handle,
                terminal.Handle,
                4,
                keys,
                values,
                &written),
            "ghostty_selection_gesture_get_multi");
        if (written != 4)
        {
            throw new InvalidOperationException(
                $"ghostty_selection_gesture_get_multi wrote {written} values; expected 4.");
        }

        return new GhosttySelectionGestureSnapshot(
            clickCount,
            dragged,
            autoscroll,
            behavior);
    }

    /// <summary>Gets the active untracked anchor snapshot when available.</summary>
    public unsafe bool TryGetAnchor(
        GhosttyTerminal terminal,
        out GhosttyVtNative.GhosttyGridRef anchor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(terminal);
        _lastTerminal = terminal;
        GhosttyVtNative.GhosttyGridRef value = GhosttyVtNative.GhosttyGridRef.CreateSized();
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.SelectionGestureGet(
            _handle,
            terminal.Handle,
            GhosttyVtNative.GhosttySelectionGestureData.Anchor,
            &value);
        if (result == GhosttyVtNative.GhosttyResult.NoValue)
        {
            anchor = default;
            return false;
        }

        ThrowIfFailed(result, "ghostty_selection_gesture_get(anchor)");
        anchor = value;
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        nint terminalHandle = _lastTerminal is { IsValid: true }
            ? _lastTerminal.Handle
            : nint.Zero;
        GhosttyVtNative.SelectionGestureFree(_handle, terminalHandle);
        _handle = nint.Zero;
        _lastTerminal = null;
    }

    private unsafe T GetValue<T>(
        GhosttyTerminal terminal,
        GhosttyVtNative.GhosttySelectionGestureData data)
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(terminal);
        _lastTerminal = terminal;
        T value = default;
        ThrowIfFailed(
            GhosttyVtNative.SelectionGestureGet(_handle, terminal.Handle, data, &value),
            $"ghostty_selection_gesture_get({data})");
        return value;
    }

    private static void ThrowIfFailed(GhosttyVtNative.GhosttyResult result, string operation)
    {
        if (result != GhosttyVtNative.GhosttyResult.Success)
        {
            throw new InvalidOperationException($"{operation} failed with {result}.");
        }
    }
}
