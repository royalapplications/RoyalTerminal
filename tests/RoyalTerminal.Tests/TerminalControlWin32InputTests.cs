// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class TerminalControlHeadlessInteractionTests
{
    [AvaloniaTheory]
    [InlineData(VtProcessorPreference.Managed)]
    [InlineData(VtProcessorPreference.Native)]
    public async Task Headless_Win32BareModifiersUseConsoleRecords(VtProcessorPreference preference)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (preference == VtProcessorPreference.Native && !GhosttyVtProcessor.IsAvailable()) return;
        RecordingTransport transport = new();
        IVtProcessorFactory factory = new DefaultVtProcessorFactory([new GhosttyVtProcessorProvider()]);
        TerminalControl control = CreateControlWithTransport(transport, factory, preference);
        Window window = new() { Width = 640, Height = 400, Content = control };
        window.Show();
        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("\u001b[?9001h"u8);
            control.Focus();
            Dispatcher.UIThread.RunJobs();
            (PhysicalKey Key, RawInputModifiers Modifier, string Press, string Release)[] cases =
            [
                (PhysicalKey.ShiftLeft, RawInputModifiers.Shift, "16;42;0;1;16;1_", "16;42;0;0;0;1_"),
                (PhysicalKey.ShiftRight, RawInputModifiers.Shift, "16;54;0;1;16;1_", "16;54;0;0;0;1_"),
                (PhysicalKey.AltLeft, RawInputModifiers.Alt, "18;56;0;1;2;1_", "18;56;0;0;0;1_"),
                (PhysicalKey.AltRight, RawInputModifiers.Alt, "18;56;0;1;257;1_", "18;56;0;0;256;1_"),
                (PhysicalKey.ControlLeft, RawInputModifiers.Control, "17;29;0;1;8;1_", "17;29;0;0;0;1_"),
                (PhysicalKey.ControlRight, RawInputModifiers.Control, "17;29;0;1;260;1_", "17;29;0;0;256;1_"),
            ];
            foreach ((PhysicalKey key, RawInputModifiers modifier, string press, string release) in cases)
            {
                transport.ClearInputs();
                window.KeyPressQwerty(key, modifier);
                window.KeyReleaseQwerty(key, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.Collection(transport.Inputs,
                    bytes => Assert.Equal("\u001b[" + press, Encoding.UTF8.GetString(bytes)),
                    bytes => Assert.Equal("\u001b[" + release, Encoding.UTF8.GetString(bytes)));
            }
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }
}
