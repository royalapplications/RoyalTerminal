// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — UI flow tests for demo ViewModel command surface.

using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RoyalTerminal.Demo.ViewModels;
using ReactiveUI;
using Xunit;

namespace RoyalTerminal.Tests;

public class MainWindowViewModelFlowTests
{
    [AvaloniaFact]
    public void KeyboardShortcut_CtrlT_TriggersNewTabFlow()
    {
        MainWindowViewModel viewModel = new();
        Window window = CreateWindow(viewModel);
        using var called = new ManualResetEventSlim(false);

        try
        {
            using var registration = viewModel.CreateNewTabInteraction.RegisterHandler(context =>
            {
                context.SetOutput(Unit.Default);
                called.Set();
            });

            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.T, KeyModifiers.Control),
                Command = viewModel.NewTabCommand,
            });

            window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control);
            Assert.True(called.Wait(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void KeyboardShortcut_CtrlD2_TriggersSwitchTabFlow()
    {
        MainWindowViewModel viewModel = new();
        Window window = CreateWindow(viewModel);
        using var called = new ManualResetEventSlim(false);
        int capturedIndex = -1;

        try
        {
            using var registration = viewModel.SwitchToTabByIndexInteraction.RegisterHandler(context =>
            {
                capturedIndex = context.Input;
                context.SetOutput(Unit.Default);
                called.Set();
            });

            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.D2, KeyModifiers.Control),
                Command = viewModel.SwitchToTabByIndexCommand,
                CommandParameter = 1,
            });

            window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Control);

            Assert.True(called.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal(1, capturedIndex);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_CopyPaste_TriggerClipboardFlowAndStatus()
    {
        MainWindowViewModel viewModel = new();
        Window window = CreateWindow(viewModel);
        using var copyCalled = new ManualResetEventSlim(false);
        using var pasteCalled = new ManualResetEventSlim(false);

        try
        {
            using var copyRegistration = viewModel.CopySelectionInteraction.RegisterHandler(context =>
            {
                context.SetOutput(Unit.Default);
                copyCalled.Set();
            });
            using var pasteRegistration = viewModel.PasteClipboardInteraction.RegisterHandler(context =>
            {
                context.SetOutput(Unit.Default);
                pasteCalled.Set();
            });

            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.C, KeyModifiers.Control | KeyModifiers.Shift),
                Command = viewModel.CopySelectionCommand,
            });
            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.V, KeyModifiers.Control | KeyModifiers.Shift),
                Command = viewModel.PasteClipboardCommand,
            });

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.True(copyCalled.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal("Copied to clipboard", viewModel.StatusText);

            window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.True(pasteCalled.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal("Pasted from clipboard", viewModel.StatusText);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_CtrlTabAndCtrlShiftTab_TriggerCycleFlow()
    {
        MainWindowViewModel viewModel = new();
        Window window = CreateWindow(viewModel);
        using var called = new ManualResetEventSlim(false);
        List<bool> directions = [];

        try
        {
            using var registration = viewModel.CycleTabInteraction.RegisterHandler(context =>
            {
                directions.Add(context.Input);
                context.SetOutput(Unit.Default);
                if (directions.Count >= 2)
                {
                    called.Set();
                }
            });

            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.Tab, KeyModifiers.Control),
                Command = viewModel.CycleTabForwardCommand,
            });
            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift),
                Command = viewModel.CycleTabBackwardCommand,
            });

            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);

            Assert.True(called.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal([true, false], directions);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ModeSwitching_CycleRenderMode_UpdatesState()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(ghosttyAvailable: true, nativeVtAvailable: true);
        viewModel.SetRenderMode(useRenderedControl: true, useNativeControl: false, useNativeVtControl: false);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.False(viewModel.UseRenderedControl);
        Assert.True(viewModel.UseNativeControl);
        Assert.False(viewModel.UseNativeVtControl);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.False(viewModel.UseRenderedControl);
        Assert.False(viewModel.UseNativeControl);
        Assert.True(viewModel.UseNativeVtControl);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.False(viewModel.UseRenderedControl);
        Assert.False(viewModel.UseNativeControl);
        Assert.False(viewModel.UseNativeVtControl);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.True(viewModel.UseRenderedControl);
        Assert.True(viewModel.UseNativeControl);
        Assert.False(viewModel.UseNativeVtControl);
    }

    private static Window CreateWindow(object dataContext)
    {
        Window window = new()
        {
            Width = 800,
            Height = 600,
            DataContext = dataContext,
            Content = new Border(),
        };

        window.Show();
        window.Focus();
        return window;
    }
}
