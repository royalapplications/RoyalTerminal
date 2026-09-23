// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private void ProcessKittyApc(ReadOnlySpan<byte> payload)
    {
        if (!_kittyStore.Enabled ||
            !ManagedKittyGraphicsCommand.TryParse(payload, _options.KittyGraphicsMaxApcBytes,
                out ManagedKittyGraphicsCommand? command)) return;

        ManagedKittyGraphicsCommand responseCommand = _kittyStore.Loading?.InitialCommand ?? command;
        int quiet = command.Quiet == 0 ? _kittyStore.Loading?.Quiet ?? 0 : command.Quiet;
        string error = "OK";
        uint responseId = command.ImageId;
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
                    if (command.ImageId == 0) error = "EINVAL: image ID required";
                    else if (!ManagedKittyImageLoader.TryCreate(command, _options.KittyGraphicsPngDecoder,
                                 _options.KittyGraphicsMaxImageBytes, out ManagedKittyImageLoader? query,
                                 out error, _options.KittyGraphicsMediumReader)) { }
                    else if (!query.TryComplete(out _, out error)) { }
                    break;
                case 't':
                case 'T':
                case 'f':
                    changed = ProcessKittyTransmission(command, out responseCommand, out responseId,
                        out error, out respond);
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
                    else changed = controlled.Animation.ApplyControl(command);
                    break;
                case 'c':
                    ManagedKittyGraphicsStore.Image? composed = _kittyStore.Find(command.ImageId, command.ImageNumber);
                    if (composed is null) error = "ENOENT: image not found";
                    else changed = composed.Animation.TryCompose(command, out error);
                    break;
            }
        }

        if (changed) PublishKittyGraphics();
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
        reply.Append(';').Append(error).Append("\x1b\\");
        ResponseCallback?.Invoke(Encoding.ASCII.GetBytes(reply.ToString()));
    }

    private bool ProcessKittyTransmission(ManagedKittyGraphicsCommand command,
        out ManagedKittyGraphicsCommand responseCommand, out uint responseId,
        out string error, out bool respond)
    {
        respond = true;
        ManagedKittyImageLoader? loader = _kittyStore.Loading;
        responseCommand = loader?.InitialCommand ?? command;
        responseId = responseCommand.ImageId;
        if (loader is null)
        {
            if (!ManagedKittyImageLoader.TryCreate(command, _options.KittyGraphicsPngDecoder,
                    _options.KittyGraphicsMaxImageBytes, out loader, out error,
                    _options.KittyGraphicsMediumReader)) return false;
            if (command.ImageId != 0 && command.Action is 't' or 'T')
                _kittyStore.RemoveImage(_screen, command.ImageId);
        }
        else if (!loader.TryAppend(command, out error))
        {
            _kittyStore.Loading = null;
            return false;
        }

        if (command.MoreChunks)
        {
            _kittyStore.Loading = loader;
            respond = false;
            error = "OK";
            return false;
        }

        _kittyStore.Loading = null;
        if (!loader.TryComplete(out KittyGraphicsDecodedImage? decoded, out error)) return false;
        if (responseCommand.Action == 'f' &&
            _kittyStore.Find(responseCommand.ImageId, responseCommand.ImageNumber) is { } animationImage)
        {
            responseId = animationImage.Id;
            if (!_kittyStore.TryReserveAnimation(_screen, animationImage,
                    animationImage.Animation.RequiredAdditionalBytes(responseCommand)))
            {
                error = "ENOSPC: animation frame storage full";
                return false;
            }
            if (!animationImage.Animation.TryTransmitFrame(responseCommand, decoded,
                    _options.KittyGraphicsStorageLimitBytes, out _, out error)) return false;
            _kittyStore.CommitAnimationBytes(animationImage);
            return true;
        }

        uint imageId = responseCommand.ImageId;
        if (imageId == 0)
        {
            imageId = _kittyStore.AllocateImageId(responseCommand.ImageNumber != 0);
            if (responseCommand.ImageNumber == 0) respond = false;
        }
        responseId = imageId;
        if (!_kittyStore.TryAddImage(_screen, imageId, responseCommand.ImageNumber,
                decoded, decoded.Rgba.Length, transient: false, out error)) return false;
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
        ManagedKittyGraphicsStore.Image? image = _kittyStore.Find(imageId, command.ImageNumber);
        error = "ENOENT: image not found";
        if (image is null) return false;
        imageId = image.Id;
        int anchorRow = _screen.GetAbsoluteRowForViewportRow(_cursorRow);
        int anchorColumn = _cursorCol;
        if (!_kittyStore.TryAddPlacement(_screen, image, command, anchorRow, anchorColumn,
                (uint)GetEffectiveCellWidthPx(), (uint)GetEffectiveCellHeightPx(),
                out ManagedKittyGraphicsStore.Placement? placement, out error)) return false;
        if (placement?.Anchor is not null && command.Get('C') != 1)
        {
            ManagedKittyPlacementGeometry geometry = placement.Options.Calculate(
                (uint)image.Animation.CurrentImage.Width, (uint)image.Animation.CurrentImage.Height,
                (uint)GetEffectiveCellWidthPx(), (uint)GetEffectiveCellHeightPx());
            long target = (long)_cursorCol + geometry.Columns;
            int rowsToMove = Math.Max(0, (int)Math.Min(int.MaxValue, geometry.Rows == 0 ? 0 : geometry.Rows - 1));
            if (target >= _screen.Columns) rowsToMove++;
            rowsToMove = Math.Min(rowsToMove, _screen.ViewportRows * 2);
            for (int i = 0; i < rowsToMove; i++) LineFeed(wrapForced: false);
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
        {
            if (command.ImageId == 0) return false;
            return action == 'I' && command.PlacementId == 0
                ? _kittyStore.RemoveImage(_screen, command.ImageId)
                : _kittyStore.RemovePlacement(_screen,
                    new(command.ImageId, command.PlacementId, false));
        }
        if (action is 'n' or 'N')
        {
            ManagedKittyGraphicsStore.Image? image = _kittyStore.Find(0, command.ImageNumber);
            return image is not null && _kittyStore.RemoveImage(_screen, image.Id);
        }
        if (action is 'a' or 'A')
        {
            _kittyStore.Clear(_screen);
            return true;
        }
        return false;
    }

    private void PublishKittyGraphics()
        => _kittyStore.Publish(_screen,
            (uint)GetEffectiveCellWidthPx(), (uint)GetEffectiveCellHeightPx());
}
