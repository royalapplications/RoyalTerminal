// Copyright (c) Royal Apps. Licensed under the MIT license.
using Avalonia.iOS;
using Foundation;
using UIKit;

namespace RoyalTerminal.Multiplexing.Mobile.Apple;

// UIKit must allocate a concrete, nongeneric Objective-C class. Keep Avalonia's
// strongly typed bootstrap and scene selection on the managed side of that boundary.
[Register("WorkspaceAppDelegate")]
public sealed class AppDelegate : UIResponder, IUIApplicationDelegate
{
    private AvaloniaAppDelegate<MobileApp>? _bootstrap;

    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("application:didFinishLaunchingWithOptions:")]
    public bool FinishedLaunching(UIApplication application, NSDictionary? options)
    {
        _bootstrap = new AvaloniaAppDelegate<MobileApp>();
        return _bootstrap.FinishedLaunching(application, options);
    }

    [Export("application:configurationForConnectingSceneSession:options:")]
    public UISceneConfiguration GetConfiguration(UIApplication app, UISceneSession session, UISceneConnectionOptions options)
        => (_bootstrap ?? throw new InvalidOperationException("The workspace application has not started."))
            .GetConfiguration(app, session, options);
}

internal static class Program
{
    private static void Main(string[] args)
    {
        try { UIApplication.Main(args, null, typeof(AppDelegate)); }
        catch (Exception error) { Console.Error.WriteLine(error); throw; }
    }
}
