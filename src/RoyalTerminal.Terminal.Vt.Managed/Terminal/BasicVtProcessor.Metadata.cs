// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Text.Unicode;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private MetadataBytes _title;
    private MetadataBytes _workingDirectory;
    private byte _statusDisplay;

    /// <inheritdoc />
    public bool TitleReportEnabled { get; set; }
    /// <inheritdoc />
    public bool TryCopyTitle(Span<byte> destination, out int requiredLength) => _title.TryCopy(destination, out requiredLength);
    /// <inheritdoc />
    public bool TryCopyWorkingDirectory(Span<byte> destination, out int requiredLength) => _workingDirectory.TryCopy(destination, out requiredLength);
    /// <inheritdoc />
    public void SetTitle(ReadOnlySpan<byte> title) => _title.Set(title);
    /// <inheritdoc />
    public void SetWorkingDirectory(ReadOnlySpan<byte> directory) => _workingDirectory.Set(directory);

    internal ReadOnlySpan<byte> SnapshotTitle => _title.Bytes;
    internal ReadOnlySpan<byte> SnapshotWorkingDirectory => _workingDirectory.Bytes;
    internal byte SnapshotStatusDisplay => _statusDisplay;

    internal void InstallSnapshotMetadata(GhosttySnapshotTerminalState state)
    {
        _title.Set(state.Title);
        _workingDirectory.Set(state.Pwd);
        _statusDisplay = state.Header.StatusDisplay;
    }

    private void SetOscTitle(ReadOnlySpan<byte> title)
    {
        // Ghostty validates the complete input before applying its byte limit;
        // truncation itself can leave an incomplete final UTF-8 scalar.
        if (!Utf8.IsValid(title)) return;
        _title.Set(title[..Math.Min(title.Length, 1024)]);
        TitleCallback?.Invoke(Encoding.UTF8.GetString(_title.Bytes));
    }

    private void SetOscWorkingDirectory(ReadOnlySpan<byte> directory, bool shellIntegration = false)
    {
        _workingDirectory.Set(directory[..Math.Min(directory.Length, 4096)]);
        if (shellIntegration || WorkingDirectoryCallback is not null)
        {
            string value = Encoding.UTF8.GetString(_workingDirectory.Bytes);
            if (shellIntegration) _shellIntegrationParser.TryHandleOsc(7, value);
            WorkingDirectoryCallback?.Invoke(value);
        }
    }

    private static bool IsCurrentDirectoryCommand(ReadOnlySpan<byte> command)
    {
        ReadOnlySpan<byte> prefix = "currentdir="u8;
        if (command.Length < prefix.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
        {
            byte ch = command[i];
            if (ch is >= (byte)'A' and <= (byte)'Z') ch += (byte)('a' - 'A');
            if (ch != prefix[i]) return false;
        }
        return true;
    }

    private void ReportTitle()
    {
        if (!TitleReportEnabled || ResponseCallback is null) return;
        byte[] response = new byte[checked(_title.Bytes.Length + 5)];
        "\u001b]l"u8.CopyTo(response);
        _title.Bytes.CopyTo(response.AsSpan(3));
        response[^2] = 0x1B; response[^1] = (byte)'\\';
        ResponseCallback(response);
    }

    private struct MetadataBytes
    {
        private byte[]? _buffer;
        private int _length;
        internal readonly ReadOnlySpan<byte> Bytes => _buffer.AsSpan(0, _length);
        internal void Set(ReadOnlySpan<byte> value)
        {
            if (value.Length > (_buffer?.Length ?? 0)) _buffer = value.ToArray();
            else value.CopyTo(_buffer);
            _length = value.Length;
        }
        internal readonly bool TryCopy(Span<byte> destination, out int requiredLength)
        {
            requiredLength = _length;
            if (destination.Length < requiredLength) return false;
            Bytes.CopyTo(destination);
            return true;
        }
    }
}
