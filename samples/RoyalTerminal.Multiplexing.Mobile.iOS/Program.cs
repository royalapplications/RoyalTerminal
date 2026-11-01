// Copyright (c) Royal Apps. Licensed under the MIT license.
using Avalonia.iOS;
using Foundation;
using UIKit;

namespace RoyalTerminal.Multiplexing.Mobile.Apple;

[Register("WorkspaceAppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<MobileApp> { }
internal static class Program
{
    private static void Main(string[] args)
    {
        // The iOS scene delegate is selected by AvaloniaAppDelegate. Keep startup errors visible
        // to simctl without installing any process-wide handler that swallows application faults.
        try { UIApplication.Main(args, null, typeof(AppDelegate)); }
        catch (Exception error) { Console.Error.WriteLine($"Workspace startup: {error.GetType().FullName}: {error.Message}\n{error.StackTrace}"); throw; }
    }
}
