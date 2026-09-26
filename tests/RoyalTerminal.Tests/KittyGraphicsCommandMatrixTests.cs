// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty graphics_exec/storage define the command ordering and animation
// semantics. xterm.js addon-image implements basic Kitty commands but leaves
// animation actions unsupported; WT's SOS/PM/APC parser ignores this channel.
// Compare complete observable state after each command, including subsequent
// reuse, rather than treating matching success/error replies as full parity.
public sealed class KittyGraphicsCommandMatrixTests
{
    [Fact]
    public void PlaceholderScanningAfterUploadAndAnimationChurn()
    {
        foreach (int seed in new[] { 17, 291, 4093, 65521 })
        {
            MixedCommandsMatchNativeRepliesPixelsGeometryAndDeadlines(seed);
            AnimationEditsPlaybackAndFrameDeletionMatchNative(seed);
        }
        ScreenMutationAndPlaceholderLifecycleMatchNative(17);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(291)]
    [InlineData(4093)]
    [InlineData(65521)]
    public void MixedCommandsMatchNativeRepliesPixelsGeometryAndDeadlines(int seed)
    {
        Random random = new(seed);
        CompareCommands(seed, random, MixedCommands(random));
    }

    [Theory]
    [InlineData(17)]
    [InlineData(291)]
    [InlineData(4093)]
    [InlineData(65521)]
    public void AnimationEditsPlaybackAndFrameDeletionMatchNative(int seed)
    {
        Random random = new(seed);
        CompareCommands(seed, random, AnimationCommands(random));
    }

    [Theory]
    [InlineData(17)]
    [InlineData(291)]
    [InlineData(4093)]
    [InlineData(65521)]
    public void ScreenMutationAndPlaceholderLifecycleMatchNative(int seed)
    {
        Random random = new(seed);
        CompareCommands(seed, random, ScreenCommands(random));
    }

    private static void CompareCommands(int seed, Random random, IEnumerable<string> commands)
    {
        RequireNative();
        Clock clock = new();
        TerminalScreen managedScreen = new(8, 4, 40), nativeScreen = new(8, 4, 40);
        using BasicVtProcessor managed = new(managedScreen, new() { TimeProvider = clock });
        using GhosttyVtProcessor native = new(nativeScreen, clock);
        managed.NotifyResize(8, 4, 64, 64);
        native.NotifyResize(8, 4, 64, 64);
        List<string> actual = [], expected = [], trace = [];
        List<(TerminalKittyImageSource Image, byte[] Pixels)> retained = [];
        managed.ResponseCallback = bytes => actual.Add(Encoding.ASCII.GetString(bytes));
        native.ResponseCallback = bytes => expected.Add(Encoding.ASCII.GetString(bytes));
        int step = 0;
        foreach (string command in commands)
        {
            trace.Add(command);
            string context = $"Seed {seed}, step {step++}: {command}\n{string.Join("\n", trace)}";
            clock.Advance(random.Next(4) == 0 ? 40 : 0);
            byte[] wire = Encoding.UTF8.GetBytes(command);
            managed.Process(wire);
            native.Process(wire);
            foreach ((TerminalKittyImageSource image, byte[] pixels) in retained)
                Assert.True(image.RgbaPixels.AsSpan().SequenceEqual(pixels), context + "\nRetained pixels changed");
            Assert.True(expected.SequenceEqual(actual),
                $"{context}\nReplies expected {string.Join(" | ", expected)}; actual {string.Join(" | ", actual)}");
            Assert.True((native.CursorCol, native.CursorRow) == (managed.CursorCol, managed.CursorRow),
                $"{context}\nCursor expected {native.CursorCol},{native.CursorRow}; actual {managed.CursorCol},{managed.CursorRow}");
            TerminalKittyImagePlacement[] nativePlacements = nativeScreen.GetKittyPlacements().ToArray();
            TerminalKittyImagePlacement[] managedPlacements = managedScreen.GetKittyPlacements().ToArray();
            NormalizePaintTies(nativePlacements, context);
            NormalizePaintTies(managedPlacements, context);
            Assert.True(nativePlacements.Length == managedPlacements.Length &&
                nativePlacements.Zip(managedPlacements).All(pair => TerminalKittyImagePlacement.GeometryEquals(pair.First, pair.Second)),
                $"{context}\nPlacements expected {Describe(nativePlacements)}; actual {Describe(managedPlacements)}");
            foreach (TerminalKittyImagePlacement placement in nativePlacements)
            {
                Assert.True(nativeScreen.TryGetKittyImageSource(placement.ImageId, out TerminalKittyImageSource? nativeImage), context);
                Assert.True(managedScreen.TryGetKittyImageSource(placement.ImageId, out TerminalKittyImageSource? managedImage), context);
                Assert.True(nativeImage!.RgbaPixels.AsSpan().SequenceEqual(managedImage!.RgbaPixels), context + "\nPixels differ");
                Retain(nativeImage);
                Retain(managedImage);
            }
            Assert.True(native.NextTimedRefreshDelay == managed.NextTimedRefreshDelay,
                $"{context}\nDeadline expected {native.NextTimedRefreshDelay}; actual {managed.NextTimedRefreshDelay}");
            actual.Clear();
            expected.Clear();
        }

        void Retain(TerminalKittyImageSource image)
        {
            if (retained.Any(entry => ReferenceEquals(entry.Image, image))) return;
            if (retained.Count == 64) retained.RemoveAt(0);
            retained.Add((image, image.RgbaPixels.ToArray()));
        }
    }

    private static IEnumerable<string> MixedCommands(Random random)
    {
        for (int i = 0; i < 400; i++) yield return NextCommand(random);
    }

    private static IEnumerable<string> AnimationCommands(Random random)
    {
        for (int round = 0; round < 12; round++)
        {
            yield return Wire("a=d,d=I,i=1");
            int format = random.Next(2) == 0 ? 24 : 32;
            yield return Wire($"a=T,i=1,p=1,f={format},s=3,v=3,C=1;{Pixels(random, 3, 3, format)}");
            for (int frame = 2; frame <= 6; frame++)
                yield return Wire($"a=f,i=1,f={format},s=3,v=3,c={random.Next(frame)},X={random.Next(2)},z={random.Next(-1, 4) * 20};{Pixels(random, 3, 3, format)}");
            yield return Wire($"a=a,i=1,r=1,z=40,s={random.Next(2, 4)},v={random.Next(4)}");
            for (int edit = 0; edit < 35; edit++)
            {
                int source = random.Next(1, 7), target = random.Next(1, 7);
                string command = random.Next(5) switch
                {
                    0 => $"a=a,i=1,c={source},s={random.Next(1, 4)},v={random.Next(4)}",
                    1 => $"a=a,i=1,r={target},z={random.Next(-1, 5) * 20}",
                    2 => $"a=c,i=1,r={source},c={target},x={random.Next(2)},y={random.Next(2)},X={random.Next(2)},Y={random.Next(2)},w=2,h=2,C={random.Next(2)}",
                    3 => $"a=f,i=1,r={target},f=32,s=2,v=2,x={random.Next(2)},y={random.Next(2)},X={random.Next(2)},z={random.Next(-1, 4) * 20};{Pixels(random, 2, 2, 32)}",
                    _ => "a=p,i=1,p=1,C=1",
                };
                yield return Wire(command);
            }
            for (int remaining = 6; remaining > 0; remaining--)
            {
                yield return Wire($"a=a,i=1,c={random.Next(1, remaining + 1)},s=3");
                yield return Wire($"a=d,d=f,i=1,r={random.Next(1, remaining + 1)}");
            }
        }
    }

    private static string Pixels(Random random, int width, int height, int format)
    {
        byte[] pixels = new byte[width * height * (format / 8)];
        random.NextBytes(pixels);
        return Convert.ToBase64String(pixels);
    }

    private static string Wire(string command) => "\u001b_G" + command + "\u001b\\";

    private static IEnumerable<string> ScreenCommands(Random random)
    {
        for (int round = 0; round < 12; round++)
        {
            yield return "\u001bc\u001b[?2027h";
            for (int id = 1; id <= 4; id++)
                yield return Wire($"a=T,i={id},p=1,s=3,v=3,c=2,r=2,C=1;{Pixels(random, 3, 3, 32)}");
            yield return Wire("a=p,i=1,p=1,U=1,c=2,r=2");
            yield return Wire("a=p,i=2,p=1,P=1,Q=1,H=1,V=1,c=2,r=2");
            for (int step = 0; step < 100; step++)
            {
                int count = random.Next(1, 6);
                yield return random.Next(16) switch
                {
                    0 => $"\u001b[{random.Next(1, 5)};{random.Next(1, 9)}H",
                    1 => $"\u001b[{count}S",
                    2 => $"\u001b[{count}T",
                    3 => $"\u001b[{count}L",
                    4 => $"\u001b[{count}M",
                    5 => $"\u001b[{count}@",
                    6 => $"\u001b[{count}P",
                    7 => $"\u001b[{random.Next(4)}J",
                    8 => $"\u001b[{random.Next(3)}K",
                    9 => random.Next(2) == 0 ? "\u001b[2;4r" : "\u001b[r",
                    10 => random.Next(2) == 0 ? "\u001b[?69h\u001b[2;7s" : "\u001b[?69l",
                    11 => random.Next(2) == 0 ? "\u001b[?1049h" : "\u001b[?1049l",
                    12 => random.Next(2) == 0 ? "\r\n" : "\u001bM",
                    13 => $"\u001b[38;5;{random.Next(1, 5)}m\U0010EEEE\u0305\u0305\U0010EEEE\u0305\u030d",
                    14 => "\u001b[0mabc界",
                    _ => NextCommand(random),
                };
            }
        }
    }

    private static void NormalizePaintTies(TerminalKittyImagePlacement[] placements, string context)
    {
        // Ghostty renderer/image.zig uses an unstable sort by z then image ID.
        // Hash iteration order inside an equal paint key is not a contract.
        // Still require real paint ordering, then compare the full geometry
        // multiset inside ties, including duplicate placements.
        for (int i = 1; i < placements.Length; i++)
            Assert.True(TerminalKittyImagePlacement.ComparePaintOrder(placements[i - 1], placements[i]) <= 0, context);
        Array.Sort(placements, (left, right) =>
        {
            int order = TerminalKittyImagePlacement.ComparePaintOrder(left, right);
            return order != 0 ? order : StringComparer.Ordinal.Compare(Describe([left]), Describe([right]));
        });
    }

    private static string Describe(TerminalKittyImagePlacement[] placements) => string.Join(" | ", placements.Select(p =>
        $"id={p.ImageId} cell={p.ViewportColumn},{p.ViewportRow} offset={p.XOffsetPx},{p.YOffsetPx} size={p.WidthPx},{p.HeightPx} source={p.SourceX},{p.SourceY},{p.SourceWidth},{p.SourceHeight} scale={p.ScaleMode} cellSize={p.CellWidthPx},{p.CellHeightPx} z={p.ZIndex}"));

    private static string NextCommand(Random random)
    {
        int id = random.Next(1, 5), placement = random.Next(0, 4);
        int kind = random.Next(16);
        if (kind == 0) return $"\u001b[{random.Next(1, 5)};{random.Next(1, 9)}H";
        if (kind == 1) return random.Next(2) == 0 ? "\r\n" : "\u001b[S";
        string selector = random.Next(5) == 0 ? $"I={id}" : $"i={id}";
        string command;
        if (kind < 6)
        {
            int width = random.Next(1, 4), height = random.Next(1, 4);
            int format = random.Next(2) == 0 ? 24 : 32;
            byte[] pixels = new byte[width * height * (format / 8)];
            random.NextBytes(pixels);
            command = $"a={(kind == 2 ? 't' : kind == 3 ? 'f' : 'T')},{selector},p={placement},f={format},s={width},v={height},C=1";
            if (kind == 3) command += $",r={random.Next(5)},c={random.Next(5)},x={random.Next(4)},y={random.Next(4)},X={random.Next(3)},z={random.Next(-1, 3) * 20}";
            else command += $",x={random.Next(3)},y={random.Next(3)},c={random.Next(4)},r={random.Next(4)},X={random.Next(12)},Y={random.Next(20)}";
            command += ";" + Convert.ToBase64String(pixels);
        }
        else if (kind < 9)
        {
            command = $"a=p,{selector},p={placement},c={random.Next(4)},r={random.Next(4)},X={random.Next(12)},Y={random.Next(20)},C={random.Next(3)},z={random.Next(-2, 3)}";
            if (kind == 7) command += $",U={random.Next(3)}";
            if (kind == 8) command += $",P={random.Next(1, 5)},Q={random.Next(4)},H={random.Next(-2, 3)},V={random.Next(-2, 3)}";
        }
        else if (kind < 12)
        {
            const string actions = "aAiInNpPqQrRxXyYzZfFcC";
            command = $"a=d,d={actions[random.Next(actions.Length)]},{selector},p={placement},x={random.Next(10)},y={random.Next(6)},z={random.Next(-2, 3)},r={random.Next(5)}";
        }
        else if (kind < 14)
            command = $"a=a,{selector},p={placement},r={random.Next(5)},c={random.Next(5)},z={random.Next(-1, 3) * 20},s={random.Next(5)},v={random.Next(4)}";
        else
            command = $"a=c,{selector},p={placement},r={random.Next(5)},c={random.Next(5)},x={random.Next(4)},y={random.Next(4)},X={random.Next(4)},Y={random.Next(4)},w={random.Next(4)},h={random.Next(4)},C={random.Next(3)}";
        return "\u001b_G" + command + "\u001b\\";
    }

    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable() && GhosttyVtHelpers.GetBuildFeatures().KittyGraphics) return;
        Assert.NotEqual("1", Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS"));
        Assert.Skip("Native Ghostty Kitty graphics runtime unavailable.");
    }

    private sealed class Clock : TimeProvider
    {
        private long _milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _milliseconds;
        internal void Advance(int milliseconds) => _milliseconds += milliseconds;
    }
}
