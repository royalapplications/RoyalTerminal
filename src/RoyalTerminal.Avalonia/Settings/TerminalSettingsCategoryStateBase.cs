// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;

namespace RoyalTerminal.Avalonia.Settings;

public abstract class TerminalSettingsCategoryStateBase : INotifyPropertyChanged
{
    private readonly HashSet<string> _trackedProperties;

    protected TerminalSettingsCategoryStateBase(TerminalSettingsPanelState owner, params string[] trackedProperties)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _trackedProperties = new HashSet<string>(trackedProperties, StringComparer.Ordinal);
        Owner.PropertyChanged += OnOwnerPropertyChanged;
    }

    protected TerminalSettingsPanelState Owner { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        string propertyName = e.Property.Name;
        if (_trackedProperties.Contains(propertyName))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
