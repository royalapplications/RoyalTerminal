// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor : ITerminalChecksumPolicy
{
    private bool _checksumReportsEnabled;
    private TerminalChecksumFlags _defaultChecksumFlags;

    /// <inheritdoc />
    public bool ChecksumReportsEnabled
    {
        get => _checksumReportsEnabled;
        set { _terminal.SetXtChecksumReport(value); _checksumReportsEnabled = value; }
    }

    /// <inheritdoc />
    public TerminalChecksumFlags DefaultChecksumFlags
    {
        get => _defaultChecksumFlags;
        set { _terminal.SetXtChecksumExtension((byte)value); _defaultChecksumFlags = value; }
    }
}
