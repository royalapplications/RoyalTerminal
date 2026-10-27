// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty's binary snapshot is the interoperability contract. xterm.js serializes
// VT text/modes rather than Kitty ownership; Windows Terminal SIXEL does not
// define these image IDs, frame records or incremental snapshot checkpoints.
public sealed class ManagedKittySnapshotTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("kitty-placed-v1.hex")]
    [InlineData("kitty-relative-v1.hex")]
    [InlineData("kitty-source-rect-v1.hex")]
    [InlineData("kitty-virtual-v1.hex")]
    [InlineData("kitty-animation-v1.hex")]
    [InlineData("kitty-history-v1.hex")]
    [InlineData("kitty-alternate-v1.hex")]
    [InlineData("kitty-loading-v1.hex")]
    [InlineData("kitty-payload-in-history-v1.hex")]
    [InlineData("unknown-records-v1.hex")]
    public void UpstreamFixturesRestoreAndExportAcrossBothEngines(string fixture)
    {
        byte[] bytes = GhosttySnapshotFramingTests.Fixture(fixture);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(bytes);
        byte[] exported = managed.Processor.GetBinarySnapshot();
        using ManagedTerminalSnapshot replay = ManagedTerminalSnapshot.Restore(exported);
        Assert.Equal(exported, replay.Processor.GetBinarySnapshot());
        if (!Available()) return;
        using GhosttyTerminal original = GhosttySnapshot.Decode(bytes);
        using GhosttyTerminal bounced = GhosttySnapshot.Decode(exported);
        Assert.Equal(original.GetActiveScreen(), bounced.GetActiveScreen());
        Assert.Equal((original.GetCursorX(), original.GetCursorY()), (bounced.GetCursorX(), bounced.GetCursorY()));
        using GhosttySnapshotStateReader state = new(bytes, new());
        GhosttySnapshotReadyState ready = state.ReadReady();
        foreach (GhosttySnapshotScreen screen in ready.Screens)
        {
            byte[] change = screen.State.Key == 0 ? "\x1b[?47l"u8.ToArray() : "\x1b[?47h"u8.ToArray();
            original.Write(change); bounced.Write(change);
            Assert.Equal(Placements(original), Placements(bounced));
            if (screen.Kitty is not { } storage) continue;
            Assert.True(original.TryGetKittyGraphics(out GhosttyKittyGraphics? expected));
            Assert.True(bounced.TryGetKittyGraphics(out GhosttyKittyGraphics? actual));
            foreach (GhosttySnapshotKittyImage image in storage.Images)
            {
                bool exists = expected!.TryGetImage(image.Id, out GhosttyKittyGraphicsImage expectedImage);
                Assert.Equal(exists, actual!.TryGetImage(image.Id, out GhosttyKittyGraphicsImage actualImage));
                if (!exists) continue;
                Assert.Equal(expectedImage.GetFormat(), actualImage.GetFormat());
                Assert.Equal(expectedImage.CopyData(), actualImage.CopyData());
                for (uint frame = 1; frame <= (image.Animation?.Frames.Length ?? 0) + 1; frame++)
                {
                    byte[] select = System.Text.Encoding.ASCII.GetBytes($"\x1b_Ga=a,i={image.Id},c={frame}\x1b\\");
                    original.Write(select); bounced.Write(select);
                    Assert.True(expected.TryGetImage(image.Id, out expectedImage));
                    Assert.True(actual.TryGetImage(image.Id, out actualImage));
                    Assert.Equal(expectedImage.CopyData(), actualImage.CopyData());
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadyReservesInvisiblePixelsAndLatePayloadCannotReplaceLiveImage(bool replace)
    {
        if (!Available()) return;
        using GhosttyTerminal source = new(10, 3);
        source.SetKittyImageStorageLimit(1024);
        source.Write("\x1b_Ga=T,i=7,p=1,s=1,v=1,C=1;/wAA/w==\x1b\\"u8);
        using ManagedTerminalSnapshotDecoder decoder = new(GhosttySnapshot.Encode(source));
        using ManagedTerminalSnapshot restored = decoder.Ready();
        Assert.Empty(restored.Screen.GetKittyPlacements().ToArray());
        if (replace) restored.Processor.Process("\x1b_Ga=T,i=7,p=1,s=1,v=1,C=1;AP8A/w==\x1b\\"u8);
        ManagedTerminalSnapshotProgress step = decoder.Next()!.Value;
        Assert.Equal(ManagedTerminalSnapshotProgressKind.KittyImage, step.Kind);
        Assert.Equal(7u, step.ImageId); Assert.Equal(1u, step.ImageFrame);
        Assert.Equal(replace ? 0 : 4, step.ImageBytesApplied);
        Assert.True(restored.Screen.TryGetKittyImageSource(7, out TerminalKittyImageSource? image));
        Assert.Equal(replace ? new byte[] { 0, 255, 0, 255 } : new byte[] { 255, 0, 0, 255 }, image!.RgbaPixels);
        while (decoder.Next() is not null) { }
    }

    [Fact]
    public void ChunkedTransmissionContinuesAfterCrossEngineSnapshot()
    {
        if (!Available()) return;
        using GhosttyTerminal source = new(10, 3);
        source.SetKittyImageStorageLimit(1024);
        source.Write("\x1b_Ga=T,i=7,p=1,s=1,v=1,C=1,m=1;AQI=\x1b\\"u8);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(GhosttySnapshot.Encode(source));
        using GhosttyTerminal bounced = GhosttySnapshot.Decode(managed.Processor.GetBinarySnapshot());
        managed.Processor.Process("\x1b_Gm=0;AwQ=\x1b\\"u8); bounced.Write("\x1b_Gm=0;AwQ=\x1b\\"u8);
        Assert.True(managed.Screen.TryGetKittyImageSource(7, out TerminalKittyImageSource? image));
        Assert.True(bounced.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        Assert.True(graphics!.TryGetImage(7, out GhosttyKittyGraphicsImage native));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, image!.RgbaPixels);
        Assert.Equal(image.RgbaPixels, native.CopyData());
    }

    [Fact]
    public void UnknownRecordsStillValidateChecksumAndConsumeBudget()
    {
        byte[] original = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        using MemoryStream extended = new();
        GhosttySnapshotFraming.WriteEnvelope(extended);
        GhosttySnapshotFraming.WriteRecord(extended, (GhosttySnapshotRecordTag)0x4000, "extension"u8);
        extended.Write(original.AsSpan(GhosttySnapshotFraming.Envelope.Length));
        byte[] bytes = extended.ToArray();
        using (ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(bytes)) Assert.True(restored.Screen.TotalRows > 0);
        long originalPayload = 0;
        using (GhosttySnapshotRecordReader reader = new(original, int.MaxValue))
        {
            reader.ReadEnvelope();
            GhosttySnapshotRecordTag tag;
            do { tag = reader.ReadRecord(out ReadOnlySpan<byte> payload); originalPayload += payload.Length; }
            while (tag != GhosttySnapshotRecordTag.Finish);
        }
        ManagedTerminalSnapshotOptions budget = new() { DecodeLimits = new(MaximumTotalPayloadBytes: originalPayload) };
        using (ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(original, budget)) Assert.True(restored.Screen.TotalRows > 0);
        Assert.Throws<InvalidDataException>(() => ManagedTerminalSnapshot.Restore(bytes, budget));
        bytes[20] ^= 1;
        Assert.Throws<InvalidDataException>(() => ManagedTerminalSnapshot.Restore(bytes));
    }

    [Fact]
    public void AnimationPayloadsBecomeVisibleInNativeOrderAndStartWithoutMoreInput()
    {
        byte[] bytes = GhosttySnapshotFramingTests.Fixture("kitty-animation-v1.hex");
        using ManagedTerminalSnapshotDecoder decoder = new(bytes);
        using ManagedTerminalSnapshot restored = decoder.Ready();
        Assert.Empty(restored.Screen.GetKittyPlacements().ToArray());
        Assert.Null(restored.Processor.NextTimedRefreshDelay);
        using GhosttySnapshotDecoder? nativeDecoder = Available() ? new(bytes) : null;
        using GhosttyTerminal? native = nativeDecoder?.Ready();
        GhosttyKittyGraphics? nativeGraphics = null;
        if (native is not null) Assert.True(native.TryGetKittyGraphics(out nativeGraphics));
        uint[] frames = [2, 1, 3];
        foreach (uint frame in frames)
        {
            ManagedTerminalSnapshotProgress step = decoder.Next()!.Value;
            Assert.Equal(ManagedTerminalSnapshotProgressKind.KittyImage, step.Kind);
            Assert.Equal(5u, step.ImageId); Assert.Equal(frame, step.ImageFrame); Assert.Equal(8, step.ImageBytesApplied);
            Assert.True(restored.Screen.TryGetKittyImageSource(5, out TerminalKittyImageSource? pixels));
            Assert.NotEmpty(pixels!.RgbaPixels);
            if (nativeDecoder is not null)
            {
                Assert.True(nativeDecoder.Next());
                Assert.Equal((uint?)frame, nativeDecoder.GetProgressImageFrame());
                Assert.True(nativeGraphics!.TryGetImage(5, out GhosttyKittyGraphicsImage image));
                Assert.Equal(image.CopyData(), pixels.RgbaPixels);
                Assert.Equal(frame == 3, nativeGraphics.AdvanceAnimations(1000) is not null);
            }
            Assert.Equal(frame == 3, restored.Processor.NextTimedRefreshDelay is not null);
        }
        Assert.Null(decoder.Next());
        Assert.False(nativeDecoder?.Next() ?? false);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(23)]
    [InlineData(24)]
    public void AnimationAdmissionReservesEveryPendingFrame(int limit)
    {
        byte[] bytes = GhosttySnapshotFramingTests.Fixture("kitty-animation-v1.hex");
        using ManagedTerminalSnapshotDecoder decoder = new(bytes, new()
        { ProcessorOptions = new() { KittyGraphicsStorageLimitBytes = limit } });
        using ManagedTerminalSnapshot restored = decoder.Ready();
        using GhosttySnapshotDecoder? nativeDecoder = Available() ? new(bytes) : null;
        nativeDecoder?.SetKittyImageStorageLimit((ulong)limit);
        using GhosttyTerminal? native = nativeDecoder?.Ready();
        while (decoder.Next() is { } progress)
        {
            Assert.Equal(limit >= 24 ? 8 : 0, progress.ImageBytesApplied);
            if (nativeDecoder is not null)
            {
                Assert.True(nativeDecoder.Next());
                Assert.Equal((nuint?)progress.ImageBytesApplied, nativeDecoder.GetProgressImageBytes());
            }
        }
        Assert.Equal(limit >= 24, restored.Screen.TryGetKittyImageSource(5, out _));
        Assert.False(nativeDecoder?.Next() ?? false);
    }

    [Theory]
    [InlineData("i=7", "i=7")]
    [InlineData("I=8", "I=8")]
    public void InProgressAnimationFrameSurvivesBothSnapshotDirections(string identity, string frameIdentity)
    {
        if (!Available()) return;
        using GhosttyTerminal source = new(10, 3);
        source.SetKittyImageStorageLimit(1024);
        source.Write(System.Text.Encoding.ASCII.GetBytes($"\x1b_Ga=T,{identity},p=1,s=1,v=1,C=1;AQIDBA==\x1b\\"));
        source.Write(System.Text.Encoding.ASCII.GetBytes($"\x1b_Ga=f,{frameIdentity},s=1,v=1,m=1;BQY=\x1b\\"));
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(GhosttySnapshot.Encode(source));
        using GhosttyTerminal bounced = GhosttySnapshot.Decode(restored.Processor.GetBinarySnapshot());
        byte[] completion = System.Text.Encoding.ASCII.GetBytes($"\x1b_Gm=0;Bwg=\x1b\\\x1b_Ga=a,{identity},c=2\x1b\\");
        List<string> replies = []; restored.Processor.ResponseCallback = value => replies.Add(System.Text.Encoding.UTF8.GetString(value));
        restored.Processor.Process(completion); source.Write(completion); bounced.Write(completion);
        uint id = identity == "i=7" ? 7u : 1u;
        Assert.Contains(replies, value => value.Contains("OK", StringComparison.Ordinal));
        Assert.True(restored.Screen.TryGetKittyImageSource((int)id, out TerminalKittyImageSource? managed));
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, managed!.RgbaPixels);
        foreach (GhosttyTerminal terminal in new[] { source, bounced })
        {
            Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
            Assert.True(graphics!.TryGetImage(id, out GhosttyKittyGraphicsImage image));
            Assert.Equal(managed.RgbaPixels, image.CopyData());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompressedAndPngLoadsContinueAcrossBothSnapshotDirections(bool png, bool compressed)
    {
        if (!Available()) return;
        GhosttySys.EnsureSkiaPngDecoderInstalled();
        byte[] pixels = [255, 0, 0, 128, 0, 255, 0, 255];
        byte[] data = png ? SkiaKittyGraphicsPngDecoderTests.CreatePng() : pixels;
        if (compressed)
        {
            using MemoryStream stream = new();
            using (System.IO.Compression.ZLibStream compressor = new(stream, System.IO.Compression.CompressionLevel.Fastest, true)) compressor.Write(data);
            data = stream.ToArray();
        }
        int cut = data.Length / 2;
        using GhosttyTerminal source = new(10, 3);
        source.SetKittyImageStorageLimit(1024);
        source.Write(System.Text.Encoding.ASCII.GetBytes($"\x1b_Ga=T,i=7,p=1,s=2,v=1,C=1,f={(png ? 100 : 32)}{(compressed ? ",o=z" : "")},m=1;{Convert.ToBase64String(data[..cut])}\x1b\\"));
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(GhosttySnapshot.Encode(source), new()
        { ProcessorOptions = new() { KittyGraphicsPngDecoder = new SkiaKittyGraphicsPngDecoder() } });
        using GhosttyTerminal bounced = GhosttySnapshot.Decode(restored.Processor.GetBinarySnapshot());
        byte[] completion = System.Text.Encoding.ASCII.GetBytes($"\x1b_Gm=0;{Convert.ToBase64String(data[cut..])}\x1b\\");
        restored.Processor.Process(completion); source.Write(completion); bounced.Write(completion);
        Assert.True(restored.Screen.TryGetKittyImageSource(7, out TerminalKittyImageSource? managed));
        Assert.Equal(pixels, managed!.RgbaPixels);
        foreach (GhosttyTerminal terminal in new[] { source, bounced })
        {
            Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
            Assert.True(graphics!.TryGetImage(7, out GhosttyKittyGraphicsImage image));
            Assert.Equal(pixels, image.CopyData());
        }
    }

    [Fact]
    public void BothScreensPreserveAnonymousPlacementsAndHistoryRelativeChains()
    {
        if (!Available()) return;
        using GhosttyTerminal source = new(10, 3);
        source.SetKittyImageStorageLimit(4096);
        source.Write("\x1b_Ga=T,i=7,p=1,s=1,v=1,C=1;/wAA/w==\x1b\\"u8);
        source.Write("\x1b_Ga=T,i=8,p=2,P=7,Q=1,H=1,V=2,s=1,v=1,C=1;AP8A/w==\x1b\\"u8);
        source.Write("\x1b_Ga=T,i=9,p=3,P=8,Q=2,H=-2,V=-1,s=1,v=1,C=1;AAD//w==\x1b\\"u8);
        source.Write("\x1b_Ga=T,s=1,v=1,C=1;AQIDBA==\x1b\\"u8);
        for (int i = 0; i < 150; i++) source.Write("line\r\n"u8);
        source.Write("\x1b[?47h\x1b_Ga=T,s=1,v=1,C=1;BQYHCA==\x1b\\"u8);
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(GhosttySnapshot.Encode(source));
        using GhosttyTerminal bounced = GhosttySnapshot.Decode(restored.Processor.GetBinarySnapshot());
        foreach (byte[] change in new[] { "\x1b[?47l"u8.ToArray(), "\x1b[?47h"u8.ToArray() })
        {
            source.Write(change); bounced.Write(change);
            string[] expected = Placements(source);
            Assert.NotEmpty(expected);
            Assert.Equal(expected, Placements(bounced));
        }
    }

    [Fact]
    public void ExportPrioritizesDisplayedFramesThenHistoryThenInactiveAndUnplacedPixels()
    {
        if (!Available()) return;
        using GhosttyTerminal source = new(10, 3, maxScrollback: 1024 * 1024);
        source.SetKittyImageStorageLimit(4096);
        source.Write("\x1b_Ga=T,i=7,p=1,s=1,v=1,C=1;AQIDBA==\x1b\\"u8);
        for (int i = 0; i < 1000; i++) source.Write("line\r\n"u8);
        source.Write("\x1b_Ga=T,i=8,p=1,s=1,v=1,C=1;BQYHCA==\x1b\\"u8);
        source.Write("\x1b_Ga=f,i=8,s=1,v=1;CQoLDA==\x1b\\\x1b_Ga=a,i=8,c=2\x1b\\"u8);
        source.Write("\x1b_Ga=t,i=9,s=1,v=1;DQ4PEA==\x1b\\"u8);
        source.Write("\x1b[?47h\x1b_Ga=T,i=10,s=1,v=1,C=1;ERITFA==\x1b\\\x1b[?47l"u8);
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(GhosttySnapshot.Encode(source));
        byte[] exported = restored.Processor.GetBinarySnapshot();
        List<string> progress = [];
        using (GhosttySnapshotStateReader reader = new(exported, new()))
        {
            reader.ReadReady();
            while (reader.ReadNext() is { } step)
                progress.Add(step.KittyPayload is { } image ? $"I{image.ImageId}/{image.Frame}" : "H");
        }
        Assert.Equal("I8/2", progress[0]); Assert.Equal("I8/1", progress[1]);
        int historyImage = progress.IndexOf("I7/1");
        Assert.True(historyImage > 2); Assert.Equal("H", progress[historyImage - 1]);
        Assert.Equal(new[] { "I10/1", "I9/1" }, progress.TakeLast(2));
        using ManagedTerminalSnapshotDecoder decoder = new(exported);
        using ManagedTerminalSnapshot first = decoder.Ready();
        Assert.Empty(first.Screen.GetKittyPlacements().ToArray());
        Assert.Equal(2u, decoder.Next()!.Value.ImageFrame);
        Assert.True(first.Screen.TryGetKittyImageSource(8, out TerminalKittyImageSource? visible));
        Assert.Equal(new byte[] { 9, 10, 11, 12 }, visible!.RgbaPixels);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(16)]
    public void OversizedNativePageHintsFailBeforeGridDecode(int offset)
    {
        byte[] page = GhosttySnapshotFramingTests.Fixture("page-v1.hex");
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(offset), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotPage.Read(page, 6, 64, 1024));
    }

    private bool Available()
    {
        bool value = GhosttyVtProcessor.IsAvailable(); output.WriteLine($"Native Kitty snapshot available: {value}"); return value;
    }

    private static string[] Placements(GhosttyTerminal terminal)
    {
        if (!terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics)) return [];
        List<string> placements = [];
        using GhosttyKittyGraphicsPlacementIterator iterator = graphics!.CreatePlacementIterator();
        iterator.Populate(graphics);
        while (iterator.MoveNext())
        {
            uint id = iterator.GetImageId();
            Assert.True(graphics.TryGetImage(id, out GhosttyKittyGraphicsImage image));
            var metadata = iterator.GetMetadata(graphics);
            bool positioned = iterator.TryGetViewportPosition(image, terminal, out int column, out int row);
            bool sourced = iterator.TryGetSourceRect(image, out uint x, out uint y, out uint width, out uint height);
            placements.Add($"{id}:{iterator.GetPlacementId()}:{metadata.Flags}:{metadata.RootImageId}:{metadata.RootPlacementId}:{metadata.RootInternal}:" +
                $"{metadata.HorizontalOffset}:{metadata.VerticalOffset}:{iterator.GetIsVirtual()}:{positioned}:{column}:{row}:" +
                $"{iterator.GetXOffset()}:{iterator.GetYOffset()}:{iterator.GetColumns()}:{iterator.GetRows()}:{iterator.GetZIndex()}:" +
                $"{sourced}:{x}:{y}:{width}:{height}");
        }
        placements.Sort(StringComparer.Ordinal);
        return [.. placements];
    }
}
