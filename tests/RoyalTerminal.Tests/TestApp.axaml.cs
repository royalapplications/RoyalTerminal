// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Skia;
using Avalonia.Threading;
using ReactiveUI.Avalonia.Reactive;
using ReactiveUI.Primitives.Reactive.Concurrency;
using ReactiveUI.Reactive;

[assembly: AvaloniaTestApplication(typeof(RoyalTerminal.Tests.TestAppBuilder))]

namespace RoyalTerminal.Tests;

public class TestApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHarfBuzz()
            .UseReactiveUI(_ => { })
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            })
            // Headless replaces Dispatcher.UIThread between isolated cases.
            // The package singleton retains the first dispatcher; bind command
            // result delivery to this application's dispatcher after setup.
            .AfterSetup(_ => RxSchedulers.MainThreadScheduler = new AvaloniaScheduler(Dispatcher.UIThread));
}
