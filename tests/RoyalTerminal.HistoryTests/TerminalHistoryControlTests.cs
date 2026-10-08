// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using RoyalTerminal.Terminal.Transport.Pty;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Avalonia.Scrolling;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(RoyalTerminal.HistoryTests.HistoryTestAppBuilder))]

namespace RoyalTerminal.HistoryTests;

public sealed class HistoryTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

public sealed class TerminalHistoryControlTests
{
    [AvaloniaTheory]
    [InlineData(VtProcessorPreference.Managed)]
    [InlineData(VtProcessorPreference.Native)]
    public void ControlForwardsCaptureUnderItsLock(VtProcessorPreference preference)
    {
        var control = CreateControl(new DefaultVtProcessorFactory([new GhosttyVtProcessorProvider()]));
        control.VtProcessorPreference = preference;
        var window = new Window { Width = 400, Height = 200, Content = control };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            control.WriteOutput("first\r\nsecond"u8.ToArray());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TerminalHistoryStatus.Success, control.GetHistoryBufferInfo(out var buffer));
            Assert.NotNull(buffer);
            Assert.Equal(TerminalHistoryStatus.Success, control.CaptureHistory(new(2, 100), out var snapshot));
            Assert.NotNull(snapshot);
            Assert.Equal(2, snapshot.Rows.Length);
            Assert.Equal(buffer.BufferId, snapshot.Buffer.BufferId);
            Assert.Throws<ArgumentOutOfRangeException>(() => control.CaptureHistory(new(0, 1), out _));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ControlRequiresUiThreadAndOwnedPagesDoNot()
    {
        var control = new TerminalControl { VtProcessorPreference = VtProcessorPreference.Managed };
        var window = new Window { Width = 400, Height = 200, Content = control };
        window.Show();
        TerminalHistorySnapshot? snapshot;
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TerminalHistoryStatus.Success, control.CaptureHistory(new(1, 100), out snapshot));
            await Task.Run(() =>
            {
                Assert.Throws<InvalidOperationException>(() => control.GetHistoryBufferInfo(out _));
                Assert.Equal(TerminalHistoryStatus.Success, snapshot!.ReadPage(0, 1, 100).Status);
            }, TestContext.Current.CancellationToken);
        }
        finally { window.Close(); }
        Assert.Equal(TerminalHistoryStatus.Success, snapshot!.ReadPage(0, 1, 100).Status);
    }

    [AvaloniaFact]
    public void ControlReportsUnsupportedProcessors()
    {
        var control = CreateControl(new LegacyFactory());
        var window = new Window { Width = 400, Height = 200, Content = control };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TerminalHistoryStatus.Unsupported, control.GetHistoryBufferInfo(out var buffer));
            Assert.Null(buffer);
            Assert.Equal(TerminalHistoryStatus.Unsupported, control.CaptureHistory(new(1, 1), out var snapshot));
            Assert.Null(snapshot);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void UninitializedControlReportsBufferUnavailable()
    {
        var control = new TerminalControl();
        Assert.Equal(TerminalHistoryStatus.BufferUnavailable, control.GetHistoryBufferInfo(out _));
        Assert.Equal(TerminalHistoryStatus.BufferUnavailable, control.CaptureHistory(new(1, 1), out _));
    }

    private static TerminalControl CreateControl(IVtProcessorFactory factory) =>
        new(new TerminalSessionService(), new DefaultTerminalInputAdapter(), new DefaultTerminalSelectionService(),
            new DefaultTerminalScrollService(), factory, new DefaultPtyFactory());

    private sealed class LegacyFactory : IVtProcessorFactory
    {
        public IVtProcessor Create(RoyalTerminal.Avalonia.Rendering.TerminalScreen screen, VtProcessorPreference preference)
            => new LegacyProcessor(new BasicVtProcessor(screen));
    }

    // Deliberately exposes only IVtProcessor, as existing external processors do.
    private sealed class LegacyProcessor(IVtProcessor inner) : IVtProcessor
    {
        public int CursorCol => inner.CursorCol;
        public int CursorRow => inner.CursorRow;
        public bool CursorVisible => inner.CursorVisible;
        public bool ApplicationCursorKeys => inner.ApplicationCursorKeys;
        public bool ApplicationKeypad => inner.ApplicationKeypad;
        public bool AlternateScreen => inner.AlternateScreen;
        public bool BracketedPaste => inner.BracketedPaste;
        public bool Win32InputMode => inner.Win32InputMode;
        public TerminalModeState ModeState => inner.ModeState;
        public event EventHandler<TerminalModeState>? ModeChanged
        {
            add => inner.ModeChanged += value;
            remove => inner.ModeChanged -= value;
        }
        public Action<byte[]>? ResponseCallback { get => inner.ResponseCallback; set => inner.ResponseCallback = value; }
        public Action? BellCallback { get => inner.BellCallback; set => inner.BellCallback = value; }
        public Action<string>? TitleCallback { get => inner.TitleCallback; set => inner.TitleCallback = value; }
        public void Process(ReadOnlySpan<byte> data) => inner.Process(data);
        public void NotifyResize(int columns, int rows) => inner.NotifyResize(columns, rows);
        public void NotifyResize(int columns, int rows, int widthPx, int heightPx) => inner.NotifyResize(columns, rows, widthPx, heightPx);
        public void Reset() => inner.Reset();
        public void Dispose() => inner.Dispose();
    }
}
