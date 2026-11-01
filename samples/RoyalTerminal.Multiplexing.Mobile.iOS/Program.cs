// Copyright (c) Royal Apps. Licensed under the MIT license.
using Avalonia.iOS;
using Foundation;
using UIKit;

namespace RoyalTerminal.Multiplexing.Mobile.Apple;

[Register("WorkspaceAppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<MobileApp> { }
internal static class Program
{
    private static void Main(string[] args) => UIApplication.Main(args,null,typeof(AppDelegate));
}
