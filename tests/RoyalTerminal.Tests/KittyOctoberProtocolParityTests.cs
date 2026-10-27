// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty 44c4c8f55 and 3079a4396. Clipboard alias replacement retains one
// target per alias; a file transfer starts a new image/frame and quiet policy.
public sealed class KittyOctoberProtocolParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedClipboardAliasUpdatesKeepOnlyTheLatestTarget(bool native)
    {
        using IVtProcessor processor = Create(native, out _);
        TerminalClipboardWrite? write = null;
        ((ITerminalEffectSource)processor).ClipboardWriteRequestCallback = request =>
        {
            write = request;
            return new(TerminalClipboardWriteResult.Success);
        };
        Clipboard(processor, "type=write:id=aliases");
        Clipboard(processor, $"type=wdata:mime={B64("text/plain")};{B64("plain")}");
        Clipboard(processor, $"type=wdata:mime={B64("text/html")};{B64("html")}");
        for (int i = 0; i < 2048; i++)
            Clipboard(processor, $"type=walias:mime={B64(i % 2 == 0 ? "text/plain" : "text/html")};{B64("application/x-alias")}");
        Assert.Null(write);
        Clipboard(processor, "type=wdata");
        Assert.NotNull(write);
        Assert.Equal(3, write.Contents.Count);
        TerminalClipboardContent alias = Assert.Single(write.Contents, value => value.MimeType == "application/x-alias");
        Assert.Equal("html", Encoding.UTF8.GetString(alias.Data));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FileTransmissionAbandonsChunksAndStartsItsOwnQuietPolicy(bool native, bool animation)
    {
        string path = Path.Combine(Path.GetTempPath(), $"rt-kitty-{Guid.NewGuid():N}.data");
        File.WriteAllBytes(path, [0, 255, 0, 0, 255, 0]);
        try
        {
            using IVtProcessor processor = Create(native, out TerminalScreen screen);
            List<string> replies = [];
            processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
            Graphics(processor, "a=T,f=24,s=2,v=1,i=1,C=1;////////");
            replies.Clear();
            Graphics(processor, $"a={(animation ? 'f' : 't')},i={(animation ? 1 : 9)},f=24,s=2,v=1,m=1,q=2;/wAA");
            Assert.Empty(replies);
            Graphics(processor, $"a={(animation ? 'f' : 'T')},i={(animation ? 1 : 2)},t=f,f=24,s=2,v=1,C=1;{B64(path)}");
            Assert.Equal(animation ? ["\u001b_Gi=1,r=2;OK\u001b\\"] : ["\u001b_Gi=2;OK\u001b\\"], replies);
            if (animation) Graphics(processor, "a=a,i=1,c=2");
            Assert.True(screen.TryGetKittyImageSource(animation ? 1 : 2, out TerminalKittyImageSource? image));
            Assert.NotNull(image);
            Assert.Equal(new byte[] { 0, 255, 0, 255, 0, 255, 0, 255 }, image.RgbaPixels);
        }
        finally { File.Delete(path); }
    }

    private static IVtProcessor Create(bool native, out TerminalScreen screen)
    {
        Assert.SkipWhen(native && !GhosttyVtProcessor.IsAvailable(), "Native Ghostty is unavailable.");
        screen = new(8, 3);
        IVtProcessor processor = native ? new GhosttyVtProcessor(screen)
            : new BasicVtProcessor(screen, new() { KittyGraphicsMediumReader = new LocalKittyGraphicsMediumReader(new() { FileEnabled = true }) });
        processor.NotifyResize(8, 3, 80, 30);
        return processor;
    }

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    private static void Graphics(IVtProcessor processor, string value) => Write(processor, $"\u001b_G{value}\u001b\\");
    private static void Clipboard(IVtProcessor processor, string value) => Write(processor, $"\u001b]5522;{value}\u001b\\");
    private static void Write(IVtProcessor processor, string value) => processor.Process(Encoding.UTF8.GetBytes(value));
}
