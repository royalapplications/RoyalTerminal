// Copyright (c) Royal Apps. Licensed under the MIT license.
using Avalonia.iOS;
using Foundation;
using UIKit;

namespace RoyalTerminal.Multiplexing.Mobile.Apple;

[Register("WorkspaceAppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<MobileApp>
{
    // Export from a concrete Objective-C class. A trampoline emitted for the open
    // generic AvaloniaAppDelegate`1 cannot materialize the first managed receiver.
    [Export("application:didFinishLaunchingWithOptions:")]
    public new bool FinishedLaunching(UIApplication application, NSDictionary? options)
        => base.FinishedLaunching(application, options);

    [Export("application:configurationForConnectingSceneSession:options:")]
    public new UISceneConfiguration GetConfiguration(UIApplication application,
        UISceneSession session, UISceneConnectionOptions options)
        => base.GetConfiguration(application, session, options);
}

internal static class Program
{
    private static void Main(string[] args)
    {
        try { UIApplication.Main(args, null, typeof(AppDelegate)); }
        catch (Exception error) { Console.Error.WriteLine(error); throw; }
    }
}
