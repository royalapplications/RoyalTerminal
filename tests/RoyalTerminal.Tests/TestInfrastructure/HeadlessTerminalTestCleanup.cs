using Avalonia.Controls;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;

namespace RoyalTerminal.Tests;

internal static class HeadlessTerminalTestCleanup
{
    private static readonly TimeSpan SessionQuiesceTimeout = TimeSpan.FromSeconds(2);

    public static void RunDispatcherJobs()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.RunJobs();
            return;
        }

        Dispatcher.UIThread.InvokeAsync(
            () => Dispatcher.UIThread.RunJobs(),
            DispatcherPriority.Background).GetAwaiter().GetResult();
    }

    public static async Task DrainDispatcherAsync()
    {
        for (int i = 0; i < 4; i++)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.RunJobs();
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(
                    () => Dispatcher.UIThread.RunJobs(),
                    DispatcherPriority.Background);
            }

            await Task.Delay(10);
        }
    }

    public static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.RunJobs();
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(
                    () => Dispatcher.UIThread.RunJobs(),
                    DispatcherPriority.Background);
            }

            if (predicate())
            {
                return true;
            }

            await Task.Delay(25);
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.RunJobs();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(
                () => Dispatcher.UIThread.RunJobs(),
                DispatcherPriority.Background);
        }

        return predicate();
    }

    public static Task CleanupControlAsync(TerminalControl control)
    {
        return CleanupControlAsync(control, control.StopPty);
    }

    public static async Task CleanupControlAsync(TerminalControl control, Action cleanup)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                cleanup();
            }
            catch
            {
                // Best effort cleanup in tests.
            }
        }, DispatcherPriority.Send);

        _ = await WaitUntilAsync(
            () => !control.HasActiveSession,
            SessionQuiesceTimeout);

        await DrainDispatcherAsync();
    }

    public static Task CleanupWindowAsync(Window window)
    {
        return CleanupWindowAsync(window, control: null, cleanup: null);
    }

    public static Task CleanupWindowAsync(Window window, TerminalControl control)
    {
        return CleanupWindowAsync(window, control, control.StopPty);
    }

    public static Task CleanupWindowAsync(Window window, Action cleanup)
    {
        return CleanupWindowAsync(window, window.Content as TerminalControl, cleanup);
    }

    public static async Task CleanupWindowAsync(Window window, TerminalControl? control, Action? cleanup)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cleanup is not null)
            {
                try
                {
                    cleanup();
                }
                catch
                {
                    // Best effort cleanup in tests.
                }
            }

            if (window.IsVisible)
            {
                window.Close();
            }
        }, DispatcherPriority.Send);

        if (control is not null)
        {
            _ = await WaitUntilAsync(
                () => !control.HasActiveSession,
                SessionQuiesceTimeout);
        }

        await DrainDispatcherAsync();
    }
}
