// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Follow Ghostty input/mouse_encode.zig for terminal buttons 8/9. Windows
// Terminal mouseInput.cpp and xterm.js MouseService.ts accept fewer physical
// buttons. Ghostty tracks 10/11 safely but intentionally omits their wire codes.
public sealed class TerminalExtendedMouseButtonTests
{
    [Theory]
    [InlineData(false, 10)]
    [InlineData(false, 11)]
    [InlineData(true, 10)]
    [InlineData(true, 11)]
    public void UnsupportedHigherButtonsDoNotLeaveHeldState(bool native, int button)
    {
        Assert.SkipWhen(native && !GhosttyVtProcessor.IsAvailable(), "Native Ghostty is unavailable.");
        using IVtProcessor processor = native
            ? new GhosttyVtProcessor(new TerminalScreen(8, 3))
            : new BasicVtProcessor(new TerminalScreen(8, 3));
        ITerminalPointerSequenceEncoderSource encoder = (ITerminalPointerSequenceEncoderSource)processor;
        TerminalPointerEncodingContext context = new(80, 60, 10, 20);
        processor.Process("\u001b[?1003;1006h"u8);
        TerminalPointerEvent press = new(TerminalPointerEventKind.Button, 15, 25,
            (TerminalMouseButton)button, TerminalInputAction.Press, TerminalModifiers.None);
        ((ITerminalPointerButtonStateSink)processor).ObservePointerButton(press);
        Assert.False(encoder.TryEncodePointer(press, context, out byte[] bytes));
        Assert.Empty(bytes);
        Assert.False(encoder.TryEncodePointer(press with { Kind = TerminalPointerEventKind.Move,
            Button = TerminalMouseButton.None, X = -10 }, context, out _));
        Assert.False(encoder.TryEncodePointer(press with { Action = TerminalInputAction.Release }, context, out _));
        Assert.True(encoder.TryEncodePointer(press with { Button = TerminalMouseButton.Left }, context, out bytes));
        Assert.Equal("\u001b[<0;2;2M", Encoding.ASCII.GetString(bytes));
    }

    [Theory]
    [InlineData(false, TerminalMouseButton.Back, 128)]
    [InlineData(false, TerminalMouseButton.Forward, 129)]
    [InlineData(true, TerminalMouseButton.Back, 128)]
    [InlineData(true, TerminalMouseButton.Forward, 129)]
    public void ExtendedButtonsEncodePressDragReleaseAndReset(bool native, TerminalMouseButton button, int code)
    {
        Assert.SkipWhen(native && !GhosttyVtProcessor.IsAvailable(), "Native Ghostty is unavailable.");
        using IVtProcessor processor = native
            ? new GhosttyVtProcessor(new TerminalScreen(8, 3))
            : new BasicVtProcessor(new TerminalScreen(8, 3));
        ITerminalPointerSequenceEncoderSource encoder = (ITerminalPointerSequenceEncoderSource)processor;
        TerminalPointerEncodingContext context = new(80, 60, 10, 20);
        processor.Process("\u001b[?1003;1006h"u8);
        TerminalPointerEvent press = new(TerminalPointerEventKind.Button, 15, 25,
            button, TerminalInputAction.Press, TerminalModifiers.None);
        Assert.True(encoder.TryEncodePointer(press, context, out byte[] bytes));
        Assert.Equal($"\u001b[<{code};2;2M", Encoding.ASCII.GetString(bytes));
        TerminalPointerEvent drag = press with { Kind = TerminalPointerEventKind.Move, X = -10, Y = 45 };
        Assert.True(encoder.TryEncodePointer(drag, context, out bytes));
        Assert.Equal($"\u001b[<{code + 32};1;3M", Encoding.ASCII.GetString(bytes));
        Assert.True(encoder.TryEncodePointer(press with { Action = TerminalInputAction.Release }, context, out bytes));
        Assert.Equal($"\u001b[<{code};2;2m", Encoding.ASCII.GetString(bytes));
        Assert.False(encoder.TryEncodePointer(drag with { Button = TerminalMouseButton.None }, context, out _));

        ((ITerminalPointerButtonStateSink)processor).ObservePointerButton(press);
        Assert.True(encoder.TryEncodePointer(drag with { Button = TerminalMouseButton.None }, context, out _));
        ((ITerminalPointerStateResetSink)processor).ResetPointerState();
        Assert.False(encoder.TryEncodePointer(drag with { Button = TerminalMouseButton.None }, context, out _));

        processor.Process("\u001b[?9h"u8);
        Assert.False(encoder.TryEncodePointer(press, context, out _));
    }
}
