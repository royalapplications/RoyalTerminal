// Licensed under the MIT License.

using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Skia;

[assembly: AvaloniaTestApplication(typeof(GhosttySharp.Tests.TestAppBuilder))]

namespace GhosttySharp.Tests;

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
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
