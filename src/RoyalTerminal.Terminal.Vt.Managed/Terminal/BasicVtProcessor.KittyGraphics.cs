// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private TimeSpan? _animationNextTickDelay;
    private long _animationTickTimestamp;

    private void ProcessKittyCommand(ManagedKittyGraphicsCommand command)
    {
        if (!_kittyStore.Enabled) return;

        ulong initialRevision = _kittyStore.Revision;
        ManagedKittyGraphicsCommand responseCommand = command;
        int quiet = command.Quiet;
        string error = "OK";
        uint responseId = command.ImageId;
        uint responseFrame = 0;
        bool respond = true;
        bool changed = false;

        if (command.ImageId != 0 && command.ImageNumber != 0)
        {
            error = "EINVAL: image ID and number are mutually exclusive";
        }
        else
        {
            switch (command.Action)
            {
                case 'q':
                    if (command.ImageId == 0) respond = false;
                    else if (!ManagedKittyImageLoader.TryCreate(command, _options.KittyGraphicsPngDecoder,
                                 _options.KittyGraphicsMaxImageBytes, out ManagedKittyImageLoader? query,
                                 out error, _options.KittyGraphicsMediumReader)) { }
                    else if (!query.TryComplete(out _, out error)) { }
                    break;
                case 't':
                case 'T':
                case 'f':
                    quiet = command.Quiet == 0 ? _kittyStore.Loading?.Quiet ?? 0 : command.Quiet;
                    changed = ProcessKittyTransmission(command, out responseCommand, out responseId,
                        out responseFrame, out error, out respond);
                    break;
                case 'p':
                    changed = TryDisplayKittyImage(command, out responseId, out error);
                    break;
                case 'd':
                    changed = DeleteKittyImages(command);
                    respond = false;
                    break;
                case 'a':
                    ManagedKittyGraphicsStore.Image? controlled = _kittyStore.Find(command.ImageId, command.ImageNumber);
                    if (controlled is null) error = "ENOENT: image not found";
                    else
                    {
                        changed = controlled.Animation.ApplyControl(command);
                        if (changed) _kittyStore.MarkContentChanged(controlled);
                        respond = false;
                    }
                    break;
                case 'c':
                    ManagedKittyGraphicsStore.Image? composed = _kittyStore.Find(command.ImageId, command.ImageNumber);
                    if (composed is null) error = "ENOENT: image not found";
                    else
                    {
                        responseId = composed.Id;
                        ManagedKittyImagePixels previous = composed.Animation.CurrentPixels;
                        changed = composed.Animation.TryCompose(command, out error);
                        if (changed) _kittyStore.CommitAnimationBytes(composed);
                        if (!ReferenceEquals(previous, composed.Animation.CurrentPixels))
                            _kittyStore.MarkContentChanged(composed);
                    }
                    break;
            }
        }

        changed |= AdvanceKittyAnimations();
        if (changed || _kittyStore.Revision != initialRevision) PublishKittyGraphics();
        if (!respond || quiet == 2 || quiet == 1 && error == "OK") return;
        if (responseId == 0 && responseCommand.ImageNumber == 0) return;
        StringBuilder reply = new("\x1b_G");
        if (responseId != 0) reply.Append("i=").Append(responseId);
        if (responseCommand.ImageNumber != 0)
        {
            if (responseId != 0) reply.Append(',');
            reply.Append("I=").Append(responseCommand.ImageNumber);
        }
        if (responseCommand.PlacementId != 0)
            reply.Append(",p=").Append(responseCommand.PlacementId);
        if (responseFrame != 0) reply.Append(",r=").Append(responseFrame);
        reply.Append(';').Append(error).Append("\x1b\\");
        ResponseCallback?.Invoke(Encoding.ASCII.GetBytes(reply.ToString()));
    }

    private bool ProcessKittyTransmission(ManagedKittyGraphicsCommand command,
        out ManagedKittyGraphicsCommand responseCommand, out uint responseId,
        out uint responseFrame, out string error, out bool respond)
    {
        respond = true;
        ManagedKittyImageLoader? loader = _kittyStore.Loading;
        responseCommand = loader?.InitialCommand ?? command;
        responseId = responseCommand.ImageId;
        responseFrame = loader is null && responseCommand.Action == 'f' ? responseCommand.Get('r') : 0;
        if (loader is not null && responseCommand.Action == 'f') responseId = _kittyStore.LoadingImageId;
        if (loader is null)
        {
            _kittyStore.LoadingImageId = 0;
            _kittyStore.LoadingTargetGeneration = 0;
            if (command.Action == 'f')
            {
                error = "EINVAL: image ID or number required";
                if (command.ImageId == 0 && command.ImageNumber == 0) return false;
                ManagedKittyGraphicsStore.Image? target = _kittyStore.Find(command.ImageId, command.ImageNumber);
                error = "ENOENT: image not found";
                if (target is null) return false;
                responseId = target.Id;
                _kittyStore.LoadingImageId = target.Id;
                _kittyStore.LoadingTargetGeneration = target.Generation;
            }
            // Ghostty retires the previous image at the start of an explicit
            // retransmission, including one whose format or medium is invalid.
            if (command.ImageId != 0 && command.Action is 't' or 'T')
                _kittyStore.RemoveImage(_screen, command.ImageId);
            if (!ManagedKittyImageLoader.TryCreate(command, _options.KittyGraphicsPngDecoder,
                    _options.KittyGraphicsMaxImageBytes, out loader, out error,
                    _options.KittyGraphicsMediumReader)) return false;
            if (command.Action is 't' or 'T')
                _kittyStore.LoadingImageId = command.ImageId != 0 ? command.ImageId
                    : _kittyStore.AllocateImageId(command.ImageNumber != 0);
            // The loader's response has no frame number until a decoded frame
            // is resolved. Initial target/format errors above echo the request.
            responseFrame = 0;
        }
        else if (!loader.TryAppend(command, out error))
        {
            // A rejected chunk leaves the bounded accumulator intact. The
            // client may retry it or explicitly cancel with a delete command.
            return false;
        }

        if (responseCommand.Action == 'f') responseId = _kittyStore.LoadingImageId;

        if (command.MoreChunks)
        {
            _kittyStore.Loading = loader;
            respond = false;
            error = "OK";
            return false;
        }

        _kittyStore.Loading = null;
        ManagedKittyGraphicsStore.Image? animationImage = null;
        if (responseCommand.Action == 'f')
        {
            animationImage = _kittyStore.Find(responseCommand.ImageId, responseCommand.ImageNumber);
            error = "ENOENT: image not found";
            if (animationImage is null || animationImage.Generation != _kittyStore.LoadingTargetGeneration)
                return false;
        }
        if (!loader.TryComplete(out ManagedKittyImagePixels? decoded, out error)) return false;
        if (animationImage is not null)
        {
            responseId = animationImage.Id;
            error = "EINVAL: frame dimensions exceed image";
            if (decoded.Width > animationImage.Animation.Width || decoded.Height > animationImage.Animation.Height)
                return false;
            KittyGraphicsDecodedImage frame = decoded.GetRgbaImage();
            // Upstream promotes the base before resolving frame/base numbers or
            // reserving append space, even if either later check rejects it.
            _kittyStore.ConvertImageToRgba(animationImage);
            if (!animationImage.Animation.TryValidateFrame(responseCommand, frame, out responseFrame, out error))
                return false;
            if (!_kittyStore.TryReserveAnimation(_screen, animationImage,
                    animationImage.Animation.RequiredAdditionalBytes(responseCommand)))
            {
                error = "ENOSPC: animation frame storage full";
                return false;
            }
            ManagedKittyImagePixels previous = animationImage.Animation.CurrentPixels;
            if (!animationImage.Animation.TryTransmitFrame(responseCommand, frame,
                    _options.KittyGraphicsStorageLimitBytes, out responseFrame, out error)) return false;
            _kittyStore.CommitAnimationBytes(animationImage);
            if (!ReferenceEquals(previous, animationImage.Animation.CurrentPixels))
                _kittyStore.MarkContentChanged(animationImage);
            return true;
        }

        uint imageId = _kittyStore.LoadingImageId;
        if (responseCommand.ImageId == 0 && responseCommand.ImageNumber == 0) respond = false;
        if (!_kittyStore.TryAddImage(_screen, imageId, responseCommand.ImageNumber,
                decoded, transient: (responseCommand.Get('N') & 1) != 0, out error)) return false;
        responseId = imageId;
        if (responseCommand.Action == 'T')
            return TryDisplayKittyImage(responseCommand, out responseId, out error, imageId);
        return true;
    }

    private bool TryDisplayKittyImage(ManagedKittyGraphicsCommand command,
        out uint imageId, out string error, uint loadedId = 0)
    {
        imageId = loadedId != 0 ? loadedId : command.ImageId;
        error = "EINVAL: image ID or number required";
        if (imageId == 0 && command.ImageNumber == 0) return false;
        error = "EINVAL: virtual placement cannot refer to a parent";
        if (command.Get('U') != 0 && command.Get('P') != 0) return false;
        ManagedKittyGraphicsStore.Image? image = _kittyStore.Find(imageId, command.ImageNumber);
        error = "ENOENT: image not found";
        if (image is null) return false;
        imageId = image.Id;
        int anchorRow = Math.Max(0, _screen.TotalRows - _screen.ViewportRows) + _cursorRow;
        int anchorColumn = _cursorCol;
        if (!_kittyStore.TryAddPlacement(_screen, image, command, anchorRow, anchorColumn,
                (uint)GetEffectiveCellWidthPx(), (uint)GetEffectiveCellHeightPx(),
                out ManagedKittyGraphicsStore.Placement? placement, out error)) return false;
        if (placement?.Anchor is not null && command.Get('C') != 1)
        {
            ManagedKittyPlacementGeometry geometry = placement.Options.Calculate(
                (uint)image.Animation.Width, (uint)image.Animation.Height,
                (uint)GetEffectiveCellWidthPx(), (uint)GetEffectiveCellHeightPx());
            long target = (long)_cursorCol + geometry.Columns;
            long requestedRows = Math.Max(0L, (long)geometry.Rows - 1) + (target >= _screen.Columns ? 1 : 0);
            int rowsBeforeScroll = _cursorRow >= _scrollTop && _cursorRow <= _scrollBottom && CursorInsideHorizontalMargins
                ? _scrollBottom - _cursorRow : 0;
            // Bound untrusted dimensions to reaching the scroll-region bottom
            // plus one screen of scrolling, as in Ghostty's graphics_exec.zig.
            long rowsToMove = Math.Min(requestedRows, (long)rowsBeforeScroll + _screen.ViewportRows);
            for (long i = 0; i < rowsToMove; i++) LineFeed(wrapForced: false);
            _cursorCol = target >= _screen.Columns ? 0 : (int)target;
            ResetDelayedWrap();
        }
        return true;
    }

    private bool DeleteKittyImages(ManagedKittyGraphicsCommand command)
    {
        _kittyStore.Loading = null;
        char action = (char)command.Get('d', 'a');
        if (action is 'i' or 'I')
            return _kittyStore.DeleteById(_screen, command.ImageId, command.PlacementId, action == 'I');
        if (action is 'n' or 'N')
        {
            ManagedKittyGraphicsStore.Image? image = _kittyStore.Find(0, command.ImageNumber);
            return image is not null && _kittyStore.DeleteById(_screen, image.Id,
                command.PlacementId, action == 'N');
        }
        if (action is 'a' or 'A')
            return _kittyStore.DeleteVisible(_screen,
                (uint)GetEffectiveCellWidthPx(), (uint)GetEffectiveCellHeightPx(), action == 'A');
        if (action is 'r' or 'R')
            return _kittyStore.DeleteByRange(_screen, command.Get('x'), command.Get('y'), action == 'R');
        if (action is 'z' or 'Z')
            return _kittyStore.DeleteByZ(_screen, command.GetSigned('z'), action == 'Z');
        if (action is 'c' or 'C')
            return _kittyStore.DeleteAtCell(_screen, _cursorCol, _cursorRow, null,
                (uint)GetEffectiveCellWidthPx(), (uint)GetEffectiveCellHeightPx(), action == 'C');
        if (action is 'p' or 'P' or 'q' or 'Q')
        {
            uint x = command.Get('x');
            uint y = command.Get('y');
            if (x == 0 || y == 0 || x > int.MaxValue || y > int.MaxValue) return false;
            int? z = action is 'q' or 'Q' ? command.GetSigned('z') : null;
            return _kittyStore.DeleteAtCell(_screen, (int)x - 1, (int)y - 1, z,
                (uint)GetEffectiveCellWidthPx(), (uint)GetEffectiveCellHeightPx(), action is 'P' or 'Q');
        }
        if (action is 'x' or 'X')
        {
            uint x = command.Get('x');
            return x != 0 && x <= int.MaxValue && _kittyStore.DeleteByColumn(_screen, (int)x - 1,
                (uint)GetEffectiveCellWidthPx(), (uint)GetEffectiveCellHeightPx(), action == 'X');
        }
        if (action is 'y' or 'Y')
        {
            uint y = command.Get('y');
            return y != 0 && y <= int.MaxValue && _kittyStore.DeleteByRow(_screen, (int)y - 1,
                (uint)GetEffectiveCellWidthPx(), (uint)GetEffectiveCellHeightPx(), action == 'Y');
        }
        if (action is 'f' or 'F')
        {
            ManagedKittyGraphicsStore.Image? image = _kittyStore.Find(command.ImageId, command.ImageNumber);
            if (image is null) return false;
            if (!image.Animation.DeleteFrame(command.Get('r'), out bool visibleChanged))
                return action == 'F' && _kittyStore.RemoveImage(_screen, image.Id);
            _kittyStore.CommitAnimationBytes(image);
            if (visibleChanged) _kittyStore.MarkContentChanged(image);
            return visibleChanged;
        }
        return false;
    }

    private void PublishKittyGraphics()
        => _kittyStore.Publish(_screen,
            (uint)GetEffectiveCellWidthPx(), (uint)GetEffectiveCellHeightPx());

    private bool AdvanceKittyAnimations()
    {
        if (_renderHold is not null) return false;
        _kittyStore.ReapPrunedPlacements(_screen);
        long now = _options.TimeProvider.GetTimestamp();
        long milliseconds = Math.Max(0, (long)_options.TimeProvider.GetElapsedTime(0, now).TotalMilliseconds);
        long? nextDelay = null;
        bool changed = false;
        foreach (ManagedKittyGraphicsStore.Image image in _kittyStore.Images)
        {
            bool imageChanged = image.Animation.Tick(milliseconds, image.PlacementCount > 0, out long? delay);
            if (imageChanged) _kittyStore.MarkContentChanged(image);
            changed |= imageChanged;
            if (delay is long value && (nextDelay is null || value < nextDelay.Value))
                nextDelay = value;
        }
        _animationTickTimestamp = now;
        _animationNextTickDelay = nextDelay is long next ? TimeSpan.FromMilliseconds(next) : null;
        return changed;
    }
}
