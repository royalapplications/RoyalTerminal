// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// One terminal's balanced operating-system secure-input ownership. Instances must
/// not be shared between controls. All calls and property reads occur on the UI thread.
/// </summary>
public interface ITerminalSecureInputScope
{
    /// <summary>Whether this platform supports secure event input.</summary>
    bool IsSupported { get; }

    /// <summary>Whether this scope owns a successful enable not yet balanced by a successful disable.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Changes ownership, returning false on failure. Duplicate requests are
    /// idempotent. Failed disables retain ownership and must be retried; they must
    /// never be replaced by another enable. This is not a global OS-state query.
    /// </summary>
    bool TrySetEnabled(bool enabled);
}
