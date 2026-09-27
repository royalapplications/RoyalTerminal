// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace RoyalTerminal.Avalonia.Controls;

// IsEffectivelyVisibleChanged is internal in Avalonia. Observe public ancestor
// properties instead; reading the chain also avoids notification-order races.
internal sealed class TerminalPresenterVisibility(Visual presenter, Action<bool> changed) : IDisposable
{
    private readonly List<Visual> _ancestors = new();
    internal bool IsVisible { get; private set; }

    internal void Attach()
    {
        Detach();
        for (Visual? visual = presenter; visual is not null; visual = visual.GetVisualParent())
        {
            _ancestors.Add(visual);
            visual.PropertyChanged += OnPropertyChanged;
        }
        Update();
    }

    public void Dispose()
    {
        Detach();
        SetVisible(false);
    }

    private void Detach()
    {
        foreach (Visual visual in _ancestors) visual.PropertyChanged -= OnPropertyChanged;
        _ancestors.Clear();
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == Visual.IsVisibleProperty || change.Property == Visual.OpacityProperty ||
            change.Property == Window.WindowStateProperty) Update();
    }

    private void Update()
    {
        foreach (Visual visual in _ancestors)
        {
            if (!visual.IsVisible || visual.Opacity <= 0 || visual is Window { WindowState: WindowState.Minimized })
            {
                SetVisible(false);
                return;
            }
        }
        SetVisible(_ancestors.Count != 0);
    }

    private void SetVisible(bool visible)
    {
        if (IsVisible == visible) return;
        IsVisible = visible;
        changed(visible);
    }
}
