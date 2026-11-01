// Copyright (c) Royal Apps. Licensed under the MIT license.
using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Avalonia.Android;

namespace RoyalTerminal.Multiplexing.Mobile.Droid;

[Activity(Name = "com.royalapplications.experimental.workspace.MainActivity",
    Label = "RoyalTerminal Workspace", MainLauncher = true, Exported = true,
    Theme = "@style/Theme.AppCompat.DayNight.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : AvaloniaMainActivity { }

[Application]
public sealed class AndroidApp(IntPtr handle, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<MobileApp>(handle, transfer);
