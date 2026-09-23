// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor
{
    private bool _titleReportEnabled;
    /// <inheritdoc />
    public bool TitleReportEnabled
    {
        get => _titleReportEnabled;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _terminal.SetTitleReport(value);
            _titleReportEnabled = value;
        }
    }
    /// <inheritdoc />
    public bool TryCopyTitle(Span<byte> destination, out int requiredLength) => _terminal.TryCopyTitle(destination, out requiredLength);
    /// <inheritdoc />
    public bool TryCopyWorkingDirectory(Span<byte> destination, out int requiredLength) => _terminal.TryCopyWorkingDirectory(destination, out requiredLength);
    /// <inheritdoc />
    public void SetTitle(ReadOnlySpan<byte> title) => _terminal.SetTitleBytes(title);
    /// <inheritdoc />
    public void SetWorkingDirectory(ReadOnlySpan<byte> directory) => _terminal.SetWorkingDirectoryBytes(directory);
}
