// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Services;

namespace RoyalTerminal.Avalonia.Controls;

public partial class TerminalControl
{
    /// <summary>Defines whether password detection automatically requests OS secure input.</summary>
    public static readonly DirectProperty<TerminalControl, bool> AutoSecureInputProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, bool>(nameof(AutoSecureInput), control => control.AutoSecureInput,
            (control, value) => control.AutoSecureInput = value, unsetValue: true);

    /// <summary>Defines whether this control currently owns an OS secure-input enable.</summary>
    public static readonly DirectProperty<TerminalControl, bool> SecureInputEnabledProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, bool>(nameof(SecureInputEnabled), control => control.SecureInputEnabled);

    private readonly ITerminalSecureInputScope _secureInputScope;
    private Window? _secureInputWindow;
    private DispatcherTimer? _secureInputRetryTimer;
    private bool _autoSecureInput = true;
    private bool _secureInputEnabled;

    /// <summary>
    /// Enables automatic macOS secure input for detected password entry while
    /// this terminal and its window have focus. Defaults to true. Disable if it
    /// conflicts with accessibility tools. Detection itself remains enabled.
    /// </summary>
    public bool AutoSecureInput
    {
        get => _autoSecureInput;
        set
        {
            if (SetAndRaise(AutoSecureInputProperty, ref _autoSecureInput, value)) UpdateSecureInputPolicy();
        }
    }

    /// <summary>
    /// Whether this control owns a successful OS secure-input enable. Not a
    /// global state query or proof that heuristic password detection is complete.
    /// </summary>
    public bool SecureInputEnabled => _secureInputEnabled;

    private void AttachSecureInputWindow()
    {
        DetachSecureInputWindow();
        _secureInputWindow = TopLevel.GetTopLevel(this) as Window;
        if (_secureInputWindow is not null)
        {
            _secureInputWindow.PropertyChanged += OnSecureInputWindowPropertyChanged;
            _secureInputWindow.Closed += OnSecureInputWindowClosed;
        }
        UpdateSecureInputPolicy();
    }

    private void DetachSecureInputWindow()
    {
        if (_secureInputWindow is not null)
        {
            _secureInputWindow.PropertyChanged -= OnSecureInputWindowPropertyChanged;
            _secureInputWindow.Closed -= OnSecureInputWindowClosed;
            _secureInputWindow = null;
        }
        UpdateSecureInputPolicy();
    }

    private void OnSecureInputWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        // Avalonia raises Activated before assigning IsActive. Observe the
        // property itself so activation and deactivation use the settled state.
        if (args.Property != WindowBase.IsActiveProperty) return;
        UpdatePasswordInputMonitoring();
        UpdateSecureInputPolicy();
    }

    private void OnSecureInputWindowClosed(object? sender, EventArgs args)
    {
        _passwordInputTimer?.Stop();
        DetachSecureInputWindow();
    }

    private void UpdateSecureInputPolicy()
    {
        bool desired = AutoSecureInput && PasswordInput && IsFocused &&
            ResolvePasswordInputSource() is { SupportsPasswordInputDetection: true } &&
            _secureInputWindow is { IsActive: true } && _secureInputScope.IsSupported;
        bool applied = _secureInputScope.TrySetEnabled(desired);
        SetAndRaise(SecureInputEnabledProperty, ref _secureInputEnabled, _secureInputScope.IsEnabled);
        if (applied)
        {
            _secureInputRetryTimer?.Stop();
            return;
        }

        // Retain the scope until a failed release succeeds, including after
        // detach. The desired state is recalculated; retries cannot revive a
        // closed, unfocused, opted-out or no-longer-password surface.
        if (_secureInputRetryTimer is null)
        {
            _secureInputRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _secureInputRetryTimer.Tick += (_, _) => UpdateSecureInputPolicy();
        }
        _secureInputRetryTimer.Start();
    }
}
