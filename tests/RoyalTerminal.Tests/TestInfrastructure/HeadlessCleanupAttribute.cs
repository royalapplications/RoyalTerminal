using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Xunit.v3;

namespace RoyalTerminal.Tests;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public sealed class HeadlessCleanupAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        _ = methodUnderTest;
        _ = test;

        RunCleanup();
    }

    internal static void RunCleanup()
    {
        static void CleanupOpenWindows()
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
            {
                return;
            }

            Window[] windows = lifetime.Windows.Reverse().ToArray();
            foreach (Window window in windows)
            {
                try
                {
                    if (window.IsVisible)
                    {
                        window.Close();
                    }
                }
                catch
                {
                    // Best effort cleanup for test infrastructure.
                }
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            CleanupOpenWindows();
            Dispatcher.UIThread.RunJobs();
        }
        else
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                CleanupOpenWindows();
                Dispatcher.UIThread.RunJobs();
            }, DispatcherPriority.Send);
        }

        for (int i = 0; i < 4; i++)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.RunJobs();
            }
            else
            {
                Dispatcher.UIThread.Invoke(() => Dispatcher.UIThread.RunJobs(), DispatcherPriority.Background);
            }

            Thread.Sleep(10);
        }
    }
}
