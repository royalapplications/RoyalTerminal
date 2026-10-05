// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Raw terminal title and working-directory state. Serialize access with terminal input.</summary>
public interface ITerminalMetadata
{
    /// <summary>Allows CSI 21 t to report title bytes to the child. Disabled by default.</summary>
    bool TitleReportEnabled { get; set; }

    /// <summary>Copies title bytes, without UTF-8 conversion; on insufficient space, writes nothing and returns the required length.</summary>
    bool TryCopyTitle(Span<byte> destination, out int requiredLength);

    /// <summary>Copies working-directory bytes, without URI decoding; on insufficient space, writes nothing and returns the required length.</summary>
    bool TryCopyWorkingDirectory(Span<byte> destination, out int requiredLength);

    /// <summary>Copies host-provided title bytes. Empty clears the title. Does not emit a title-change callback.</summary>
    void SetTitle(ReadOnlySpan<byte> title);

    /// <summary>Copies host-provided working-directory bytes. Empty clears the value. Does not emit a directory-change callback.</summary>
    void SetWorkingDirectory(ReadOnlySpan<byte> directory);
}
