// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Full-canvas Kitty animation state, following Ghostty's graphics_animation/graphics_exec.
/// The caller serializes access and owns storage quotas, placement visibility and scheduling.
/// Published pixel arrays are never changed: frame edits use copy-on-write for retained screens.
/// </summary>
internal sealed class ManagedKittyAnimation
{
    private readonly List<Frame> _frames;
    private int _currentIndex;
    private uint _state;
    private uint _maximumLoops;
    private uint _currentLoop;
    private long? _frameShownAtMilliseconds;
    private long _durationMilliseconds;

    internal ManagedKittyAnimation(ManagedKittyImagePixels root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _frames = [new(root, 0)];
    }

    private ManagedKittyAnimation(ManagedKittyAnimation source)
    {
        _frames = new(source._frames);
        _currentIndex = source._currentIndex;
        _state = source._state;
        _maximumLoops = source._maximumLoops;
        _currentLoop = source._currentLoop;
        _frameShownAtMilliseconds = source._frameShownAtMilliseconds;
        _durationMilliseconds = source._durationMilliseconds;
    }

    internal ManagedKittyAnimation CreateStateCopy() => new(this);

    internal KittyGraphicsDecodedImage RootImage => _frames[0].Image.GetRgbaImage();
    internal KittyGraphicsDecodedImage CurrentImage => CurrentPixels.GetRgbaImage();
    internal ManagedKittyImagePixels CurrentPixels => _frames[_currentIndex].Image;
    internal int Width => _frames[0].Image.Width;
    internal int Height => _frames[0].Image.Height;
    internal int FrameCount => _frames.Count;
    internal uint CurrentFrameNumber => (uint)_currentIndex + 1;
    internal long StoredBytes => _frames[0].Image.StorageBytes + (long)_frames[0].Image.RgbaByteLength * (_frames.Count - 1);

    internal bool PromoteRootToRgba()
    {
        Frame root = _frames[0];
        if (root.Image.IsRgba) return false;
        _frames[0] = new(root.Image.AsRgba(), root.GapMilliseconds);
        return true;
    }

    internal int RequiredAdditionalBytes(ManagedKittyGraphicsCommand command)
    {
        uint editFrame = command.Get('r');
        return editFrame == 0 || editFrame > (uint)_frames.Count ? _frames[0].Image.RgbaByteLength : 0;
    }

    internal bool TryValidateFrame(ManagedKittyGraphicsCommand command, KittyGraphicsDecodedImage source,
        out uint frameNumber, out string error)
    {
        uint requested = command.Get('r');
        frameNumber = 0;
        error = "OK";
        if (source.Width > Width || source.Height > Height)
        {
            error = "EINVAL: frame dimensions exceed image";
            return false;
        }

        frameNumber = requested == 0 || requested > (uint)_frames.Count + 1 ? (uint)_frames.Count + 1 : requested;
        bool append = frameNumber == (uint)_frames.Count + 1;
        uint baseFrame = command.Get('c');
        if (append && baseFrame > (uint)_frames.Count)
        {
            error = "EINVAL: base frame not found";
            return false;
        }
        return true;
    }

    internal bool TryTransmitFrame(ManagedKittyGraphicsCommand command, KittyGraphicsDecodedImage source,
        long maxStoredBytes, out uint frameNumber, out string error)
    {
        if (!TryValidateFrame(command, source, out frameNumber, out error)) return false;
        PromoteRootToRgba();
        KittyGraphicsDecodedImage root = RootImage;
        bool append = frameNumber == (uint)_frames.Count + 1;
        uint baseFrame = command.Get('c');
        if (append && (maxStoredBytes < StoredBytes || root.Rgba.Length > maxStoredBytes - StoredBytes))
        {
            error = "ENOSPC: animation frame storage full";
            return false;
        }

        byte[] pixels;
        if (!append) pixels = (byte[])_frames[(int)frameNumber - 1].Image.GetRgbaImage().Rgba.Clone();
        else if (baseFrame > 0) pixels = (byte[])_frames[(int)baseFrame - 1].Image.GetRgbaImage().Rgba.Clone();
        else
        {
            pixels = GC.AllocateUninitializedArray<byte>(root.Rgba.Length);
            ManagedKittyAnimationPixels.Fill(pixels, command.Get('Y'));
        }

        uint x = command.Get('x');
        uint y = command.Get('y');
        if (x < root.Width && y < root.Height)
        {
            ManagedKittyAnimationPixels.Compose(pixels, root.Width, source.Rgba, source.Width,
                Math.Min(source.Width, root.Width - (int)x), Math.Min(source.Height, root.Height - (int)y),
                0, 0, (int)x, (int)y, command.Get('X') == 1);
        }

        int gap = command.GetSigned('z');
        ManagedKittyImagePixels image = new(new KittyGraphicsDecodedImage(root.Width, root.Height, pixels));
        if (append)
        {
            uint frameGap = gap > 0 ? (uint)gap : gap < 0 ? 0u : 40u;
            _frames.Add(new(image, frameGap));
            _durationMilliseconds += frameGap;
        }
        else
        {
            int index = (int)frameNumber - 1;
            if (gap != 0) SetGap(index, gap > 0 ? (uint)gap : 0);
            _frames[index] = new(image, _frames[index].GapMilliseconds);
            if (index == _currentIndex) _frameShownAtMilliseconds = null;
        }
        return true;
    }

    /// <summary>Applies independent control fields in protocol order and reports displayed-frame changes.</summary>
    internal bool ApplyControl(ManagedKittyGraphicsCommand command)
    {
        uint frame = command.Get('r');
        int gap = command.GetSigned('z');
        if (frame > 0 && frame <= (uint)_frames.Count && gap != 0)
            SetGap((int)frame - 1, gap > 0 ? (uint)gap : 0);

        bool changed = false;
        uint current = command.Get('c');
        if (current > 0 && current <= (uint)_frames.Count && current - 1 != _currentIndex)
        {
            _currentIndex = (int)current - 1;
            _frameShownAtMilliseconds = null;
            changed = true;
        }

        uint state = command.Get('s');
        if (state is >= 1 and <= 3)
        {
            if (_state is 0 or 1 && state != 1) _frameShownAtMilliseconds = null;
            _state = state;
            _currentLoop = 0;
        }

        uint loops = command.Get('v');
        if (loops > 0) _maximumLoops = loops - 1;
        return changed;
    }

    internal bool TryCompose(ManagedKittyGraphicsCommand command, out string error)
    {
        uint sourceFrame = command.Get('r');
        uint destinationFrame = command.Get('c');
        error = "OK";
        if (sourceFrame == 0 || sourceFrame > (uint)_frames.Count)
        {
            error = "ENOENT: source frame not found";
            return false;
        }
        if (destinationFrame == 0 || destinationFrame > (uint)_frames.Count)
        {
            error = "ENOENT: destination frame not found";
            return false;
        }

        uint width = command.Get('w');
        uint height = command.Get('h');
        if (width == 0) width = (uint)Width;
        if (height == 0) height = (uint)Height;
        uint destinationX = command.Get('x');
        uint destinationY = command.Get('y');
        uint sourceX = command.Get('X');
        uint sourceY = command.Get('Y');
        if ((ulong)destinationX + width > (uint)Width || (ulong)destinationY + height > (uint)Height)
        {
            error = "EINVAL: destination rectangle out of bounds";
            return false;
        }
        if ((ulong)sourceX + width > (uint)Width || (ulong)sourceY + height > (uint)Height)
        {
            error = "EINVAL: source rectangle out of bounds";
            return false;
        }
        if (sourceFrame == destinationFrame &&
            Math.Max(sourceX, destinationX) < (ulong)Math.Min(sourceX, destinationX) + width &&
            Math.Max(sourceY, destinationY) < (ulong)Math.Min(sourceY, destinationY) + height)
        {
            error = "EINVAL: source and destination rectangles overlap";
            return false;
        }

        PromoteRootToRgba();
        Frame destination = _frames[(int)destinationFrame - 1];
        byte[] pixels = (byte[])destination.Image.GetRgbaImage().Rgba.Clone();
        ManagedKittyAnimationPixels.Compose(pixels, Width, _frames[(int)sourceFrame - 1].Image.GetRgbaImage().Rgba,
            Width, (int)width, (int)height, (int)sourceX, (int)sourceY, (int)destinationX, (int)destinationY,
            command.Get('C') != 0);
        ManagedKittyImagePixels image = new(new KittyGraphicsDecodedImage(Width, Height, pixels));
        _frames[(int)destinationFrame - 1] = new(image, destination.GapMilliseconds);
        return true;
    }

    /// <summary>Deletes one frame; false means only the root remains (the caller handles uppercase deletion).</summary>
    internal bool DeleteFrame(uint frameNumber, out bool visibleChanged)
    {
        visibleChanged = false;
        if (_frames.Count == 1) return false;
        int removedIndex = (int)(Math.Clamp(frameNumber, 1, (uint)_frames.Count) - 1);
        ManagedKittyImagePixels previous = CurrentPixels;
        _durationMilliseconds -= _frames[removedIndex].GapMilliseconds;
        _frames.RemoveAt(removedIndex);
        if (removedIndex < _currentIndex) _currentIndex--;
        else if (_currentIndex >= _frames.Count) _currentIndex = _frames.Count - 1;
        visibleChanged = !ReferenceEquals(previous, CurrentPixels);
        if (visibleChanged) _frameShownAtMilliseconds = null;
        return true;
    }

    /// <summary>Advances at most one displayed frame, skipping gapless frames; no catch-up when ticks lag.</summary>
    internal bool Tick(long nowMilliseconds, bool placed, out long? nextDelayMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nowMilliseconds);
        nextDelayMilliseconds = null;
        if (_state is 0 or 1 || _frames.Count == 1 || !placed || _durationMilliseconds == 0 ||
            _maximumLoops > 0 && _currentLoop >= _maximumLoops) return false;

        long shownAt = _frameShownAtMilliseconds is long previous && previous <= nowMilliseconds ? previous : nowMilliseconds;
        _frameShownAtMilliseconds = shownAt;
        long nextAt = SaturatingAdd(shownAt, _frames[_currentIndex].GapMilliseconds);
        bool changed = false;
        if (nowMilliseconds >= nextAt)
        {
            int index = _currentIndex;
            do
            {
                index = (index + 1) % _frames.Count;
                if (index == 0)
                {
                    if (_state == 2) return false;
                    _currentLoop = unchecked(_currentLoop + 1);
                    if (_maximumLoops > 0 && _currentLoop >= _maximumLoops) return false;
                }
            } while (_frames[index].GapMilliseconds == 0);

            _currentIndex = index;
            _frameShownAtMilliseconds = nowMilliseconds;
            nextAt = SaturatingAdd(nowMilliseconds, _frames[index].GapMilliseconds);
            changed = true;
        }

        if (nextAt > nowMilliseconds) nextDelayMilliseconds = nextAt - nowMilliseconds;
        return changed;
    }

    private void SetGap(int index, uint gap)
    {
        Frame frame = _frames[index];
        _durationMilliseconds += (long)gap - frame.GapMilliseconds;
        _frames[index] = new(frame.Image, gap);
    }

    private static long SaturatingAdd(long timestamp, uint milliseconds) =>
        timestamp > long.MaxValue - milliseconds ? long.MaxValue : timestamp + milliseconds;

    private readonly record struct Frame(ManagedKittyImagePixels Image, uint GapMilliseconds);
}
