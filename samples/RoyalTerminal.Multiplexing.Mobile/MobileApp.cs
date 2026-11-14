// Copyright (c) Royal Apps. Licensed under the MIT license.
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace RoyalTerminal.Multiplexing.Mobile;

public sealed class MobileApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme()); RequestedThemeVariant = ThemeVariant.Dark;
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime activity) activity.MainViewFactory = static () => new MobileWorkspaceView();
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single) single.MainView = new MobileWorkspaceView();
        base.OnFrameworkInitializationCompleted();
    }
}
