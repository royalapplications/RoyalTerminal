// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Controls;

public partial class TerminalControl
{
    /// <summary>Defines the read-only host password-entry hint.</summary>
    public static readonly DirectProperty<TerminalControl, bool> PasswordInputProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, bool>(nameof(PasswordInput), control => control.PasswordInput);

    private DispatcherTimer? _passwordInputTimer;
    private bool _passwordInput;

    /// <summary>
    /// Whether the focused host's most recent terminal-mode poll detected canonical
    /// input without echo. Not a security guarantee or OS secure-input status.
    /// </summary>
    public bool PasswordInput => _passwordInput;

    private ITerminalPasswordInputSource? ResolvePasswordInputSource() =>
        TerminalSessionService.Endpoint is { } endpoint ? endpoint as ITerminalPasswordInputSource :
        TerminalSessionService.Transport is { } transport ? (transport.IsRunning ? transport as ITerminalPasswordInputSource : null) :
        TerminalSessionService.Pty is { IsRunning: true } pty ? pty as ITerminalPasswordInputSource : null;

    private void UpdatePasswordInputMonitoring()
    {
        if (!IsFocused || TopLevel.GetTopLevel(this) is not Window { IsActive: true } ||
            ResolvePasswordInputSource() is not { SupportsPasswordInputDetection: true })
        {
            _passwordInputTimer?.Stop();
            return;
        }

        PollPasswordInput();
        if (_passwordInputTimer is null)
        {
            _passwordInputTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _passwordInputTimer.Tick += (_, _) => PollPasswordInput();
        }
        _passwordInputTimer.Start();
    }

    private void PollPasswordInput()
    {
        ITerminalPasswordInputSource? source = ResolvePasswordInputSource();
        if (source is not { SupportsPasswordInputDetection: true })
        {
            StopPasswordInputMonitoring();
            return;
        }
        bool detected = source is not null && source.TryGetPasswordInput(out bool value) && value;
        // Like Exec.termiosTimer, publish transitions, not every sample. Besides
        // avoiding repeated state writes, unchanged termios must not undo RIS.
        if (detected != _passwordInput) SetPasswordInput(detected);
    }

    private void SetPasswordInput(bool value)
    {
        // Query outside the screen lock; only processor mutation shares the
        // parser's synchronization. The host flag is live, even during render holds.
        if (_screen is not null && _vtProcessor is ITerminalPasswordInputState state)
        {
            using (_screen.Synchronization.AcquireDemand()) state.PasswordInput = value;
        }
        if (SetAndRaise(PasswordInputProperty, ref _passwordInput, value)) _presenter?.Invalidate();
        UpdateSecureInputPolicy();
    }

    private void StopPasswordInputMonitoring()
    {
        _passwordInputTimer?.Stop();
        SetPasswordInput(false);
    }
}
