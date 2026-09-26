// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalNotificationTests
{
    // Reference decision: pinned Ghostty's OSC 99 parser validates/retains slices
    // but its stream ignores the command. WT's ActionOscDispatch has no OSC 99;
    // xterm.js exposes registerOscHandler to embedders. Both RoyalTerminal engines
    // therefore share the Kitty specification's host lifecycle, not OSC 9/777.
    // https://sw.kovidgoyal.net/kitty/desktop-notifications/
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterleavedChunksAndMetadataProduceOwnedPlainTextRequests(bool native)
    {
        using Harness h = new(native);
        h.Command("i=one:d=0:f=YXBw:n=aW5mbw:t=am9i:u=2:w=1500:a=-focus,report", "<title>");
        h.Command("i=two:d=0", "second");
        h.Command("i=one:d=0:p=body:e=1", B64("line1\nline2\x1b"));
        h.Command("i=one:d=0:n=d2Fybg:t=ZG9uZQ", " & suffix");
        h.Command("i=one:p=buttons:e=1", B64("Yes\u2028No"));
        TerminalNotificationRequest request = Assert.Single(h.Host.Shown);
        Assert.Equal("<title> & suffix", request.Title);
        Assert.Equal("line1\nline2", request.Body);
        Assert.Equal("app", request.ApplicationName);
        Assert.Equal(new[] { "info", "warn" }, request.IconNames);
        Assert.Equal(new[] { "job", "done" }, request.Types);
        Assert.Equal(new[] { "Yes", "No" }, request.Buttons);
        Assert.Equal((2, 1500), (request.Urgency, request.ExpireMilliseconds));
        h.Command("i=two:p=body", "body");
        Assert.Equal("second", h.Host.Shown[1].Title);
        Assert.Equal("<title> & suffix", request.Title);
        h.Host.Activate(request, 2);
        Assert.Equal(string.Empty, h.Responses.ToString()); // Callbacks do not call into VT.
        h.Refresh();
        Assert.Equal("\x1b]99;i=one;2\x1b\\", h.TakeResponses());
        Assert.Equal(0, h.Host.FocusCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownAndMalformedChunksDoNotFinishPendingNotification(bool native)
    {
        using Harness h = new(native);
        h.Command("i=a:d=0", "first");
        h.Command("i=a:p=query", "ignored");
        h.Command("i=a:p=future", "ignored");
        h.Command("i=a:e=1", "%%%%");
        h.Command("i=a:p=icon", "not base64");
        h.Command("i=a", new string('x', 2049));
        h.Command("i=a:e=1", new string('A', 4100));
        Assert.Empty(h.Host.Shown);
        h.Command("i=a:p=body", "last");
        Assert.Equal(("first", "last"), (h.Host.Shown[0].Title, h.Host.Shown[0].Body));
        h.Command("p=body", "title fallback");
        Assert.Equal(("title fallback", ""), (h.Host.Shown[1].Title, h.Host.Shown[1].Body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryInputSplitAndUtf8ChunkBoundaryPreservesNotification(bool native)
    {
        using Harness h = new(native);
        byte[] command = Encoding.UTF8.GetBytes("\x1b]99;i=split;Hi 👩‍💻\x1b\\");
        for (int split = 0; split <= command.Length; split++)
        {
            h.Processor.Process(command.AsSpan(0, split));
            h.Processor.Process(command.AsSpan(split));
            Assert.Equal("Hi 👩‍💻", h.Host.Shown[^1].Title);
        }
        // Encoded chunks can split a UTF-8 scalar: decode UTF-8 after assembly.
        h.Command("i=utf:d=0:e=1", Convert.ToBase64String(new byte[] { 0xF0, 0x9F }));
        h.Command("i=utf:e=1", Convert.ToBase64String(new byte[] { 0x98, 0x80 }));
        Assert.Equal("😀", h.Host.Shown[^1].Title);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueriesReflectRealHostCapabilitiesAndSanitizeReplyIds(bool native)
    {
        using Harness h = new(native);
        h.Source.NotificationHost = null;
        h.Command("i=query:p=?");
        Assert.Empty(h.TakeResponses());
        h.Source.NotificationHost = h.Host;
        h.Host.Capabilities = TerminalNotificationCapabilities.None;
        h.Command("i=query:p=?");
        Assert.Empty(h.TakeResponses());
        h.Host.Capabilities = TerminalNotificationCapabilities.Display;
        h.Command("i=query:p=?", bell: true);
        Assert.Equal("\x1b]99;i=query:p=?;p=title,body:o=always,invisible,unfocused\a", h.TakeResponses());
        h.Host.Capabilities = AllCapabilities;
        h.Command("i=unsafe/id:p=?");
        string response = h.TakeResponses();
        Assert.StartsWith("\x1b]99;i=0:p=?;", response);
        Assert.Contains("p=title,body,close,alive,icon,buttons", response);
        Assert.Contains(":a=focus,report:c=1:", response);
        Assert.Contains(":s=system,silent,error,warn,warning,info,question:u=0,1,2:w=1", response);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacementHasUniqueTokenAndIgnoresOldCallbacks(bool native)
    {
        using Harness h = new(native);
        h.Command("i=job:c=1:a=report", "old");
        TerminalNotificationRequest old = h.Host.Shown[0];
        h.Command("i=job:c=1:a=report", "new");
        TerminalNotificationRequest current = h.Host.Shown[1];
        Assert.Equal(old.Token, current.ReplacesToken);
        Assert.NotEqual(old.Token, current.Token);
        h.Host.Activate(old);
        h.Refresh();
        Assert.Empty(h.TakeResponses());
        Assert.Equal(0, h.Host.FocusCount);
        h.Host.Activate(current);
        h.Refresh();
        Assert.Equal("\x1b]99;i=job;\x1b\\\x1b]99;i=job:p=close;\x1b\\", h.TakeResponses());
        Assert.Equal(1, h.Host.FocusCount);
        h.Host.Dismiss(current);
        h.Refresh();
        Assert.Empty(h.TakeResponses());
        h.Command("", "anonymous1");
        h.Command("", "anonymous2");
        Assert.Null(h.Host.Shown[^1].ReplacesToken);
        Assert.Null(h.Host.Shown[^2].ReplacesToken);
        h.Command("p=close");
        Assert.DoesNotContain(h.Host.Shown[^1].Token, h.Host.Closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpiryAndActivationRemainResponsiveDuringRenderHold(bool native)
    {
        using Harness h = new(native);
        h.Processor.Process("A\x1b[?2026hB"u8);
        h.Command("i=short:w=25:c=1", "expires");
        Assert.Equal(TimeSpan.FromMilliseconds(25), h.Timer.NextTimedRefreshDelay);
        h.Clock.Advance(25);
        Assert.False(h.Timer.RefreshTimedState());
        Assert.Equal('A', (char)h.Screen.GetViewportRow(0)[0].Codepoint);
        Assert.Equal(1, h.Processor.CursorCol);
        Assert.Equal("\x1b]99;i=short:p=close;\x1b\\", h.TakeResponses());
        Assert.Contains(h.Host.Shown[0].Token, h.Host.Closed);
        Assert.Equal(TimeSpan.FromMilliseconds(975), h.Timer.NextTimedRefreshDelay);
        h.Clock.Advance(975);
        Assert.True(h.Timer.RefreshTimedState());
        Assert.Equal(2, h.Processor.CursorCol);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RisClearsAssemblyButNewSessionAndHostReplacementCloseOwnedNotifications(bool native)
    {
        using Harness h = new(native);
        h.Command("i=a:c=1:a=report", "alive");
        h.Command("i=b:d=0", "incomplete");
        h.Processor.Process("\u001bc"u8);
        h.Command("i=b:p=body", "fresh");
        Assert.Equal("fresh", h.Host.Shown[1].Title);
        Assert.DoesNotContain(h.Host.Shown[0].Token, h.Host.Closed);
        ((ITerminalSessionHistoryController)h.Processor).PrepareForNewSession(true);
        Assert.Contains(h.Host.Shown[0].Token, h.Host.Closed);
        Assert.Contains(h.Host.Shown[1].Token, h.Host.Closed);
        h.Host.Activate(h.Host.Shown[0]);
        h.Refresh();
        Assert.Empty(h.TakeResponses());
        Assert.Equal(0, h.Host.FocusCount);
        h.Command("i=a", "new session");
        Assert.Null(h.Host.Shown[^1].ReplacesToken);
        h.Source.NotificationHost = new FakeHost();
        Assert.Contains(h.Host.Shown[^1].Token, h.Host.Closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AliveAndUntrackedCloseReportsFollowBackendState(bool native)
    {
        using Harness h = new(native);
        h.Host.Capabilities &= ~TerminalNotificationCapabilities.CloseEvents;
        h.Command("i=a:c=1", "first", bell: true);
        Assert.Equal("\x1b]99;i=a:p=close;untracked\a", h.TakeResponses());
        h.Command("i=b", "second");
        h.Command("i=query:p=alive");
        Assert.Equal("\x1b]99;i=query:p=alive;a,b\x1b\\", h.TakeResponses());
        h.Host.Alive.Remove(h.Host.Shown[0].Token); // Backend poll, no close event.
        h.Command("i=query:p=alive");
        Assert.Equal("\x1b]99;i=query:p=alive;b\x1b\\", h.TakeResponses());
        h.Command("i=b:p=close");
        Assert.Contains(h.Host.Shown[1].Token, h.Host.Closed);
        Assert.Null(h.Timer.NextTimedRefreshDelay);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OccasionsAndBackendFailureNeverPrintOrCreatePhantomNotifications(bool native)
    {
        using Harness h = new(native);
        h.Host.IsFocused = h.Host.IsVisible = true;
        h.Command("o=unfocused", "skip");
        h.Command("o=invisible", "skip");
        Assert.Empty(h.Host.Shown);
        h.Host.IsFocused = false;
        h.Command("o=unfocused", "show");
        h.Command("o=invisible", "skip");
        Assert.Single(h.Host.Shown);
        h.Host.IsVisible = false;
        h.Command("o=invisible", "show");
        Assert.Equal(2, h.Host.Shown.Count);
        h.Host.ThrowOnShow = true;
        h.Command("i=failed:c=1", "fail");
        h.Command("i=q:p=alive");
        Assert.DoesNotContain("failed", h.TakeResponses());
        Assert.Equal(0, h.Processor.CursorCol);
        h.Host.ThrowOnShow = false;
        h.Host.FailSynchronously = true;
        h.Command("i=failed:c=1", "async fail");
        h.Refresh();
        Assert.Equal("\x1b]99;i=failed:p=close;\x1b\\", h.TakeResponses());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncodedIconCacheIsOwnedAndSessionScoped(bool native)
    {
        using Harness h = new(native);
        h.Command("i=icon:g=art:p=icon:e=1", "AAEC/w==");
        Assert.Empty(h.Host.Shown);
        h.Command("g=art", "with image");
        Assert.Equal(new byte[] { 0, 1, 2, 255 }, h.Host.Shown[0].IconData.ToArray());
        h.Command("g=art:p=icon:e=1", "BAU=");
        h.Command("g=art", "replacement cache");
        Assert.Equal(new byte[] { 4, 5 }, h.Host.Shown[1].IconData.ToArray());
        Assert.Equal(new byte[] { 0, 1, 2, 255 }, h.Host.Shown[0].IconData.ToArray());
        ((ITerminalSessionHistoryController)h.Processor).PrepareForNewSession(false);
        h.Command("g=art", "no prior session image");
        Assert.True(h.Host.Shown[^1].IconData.IsEmpty);
    }

    [Fact]
    public void ParserMatchesGhosttyMetadataDefaultsAndSafeUtf8Rules()
    {
        Assert.True(TerminalNotificationMetadata.TryParse("i=bad/id:i=good:p=title:p=body:d=2:e=true:u=3:w=1_000:a=-focus,unknown"u8, out var metadata));
        Assert.Equal(string.Empty, metadata.Id);
        Assert.True(metadata.Done);
        Assert.False(metadata.Encoded);
        Assert.Equal("title", metadata.Payload);
        Assert.Equal((true, false), TerminalNotificationMetadata.Actions(metadata['a']!));
        Assert.Equal(-1, TerminalNotificationMetadata.Expiry(metadata['w']!));
        Assert.True(TerminalNotificationMetadata.TryParse(" i = ok :n=aW5mbw:n=d2Fybg"u8, out metadata));
        Assert.Equal("ok", metadata.Id);
        Assert.Equal(new[] { "info", "warn" }, metadata.IconNames);
        Assert.False(TerminalNotificationMetadata.IsSafeUtf8(new byte[] { 0xc0, 0xaf }));
        Assert.False(TerminalNotificationMetadata.IsSafeUtf8("bad\n"u8));
        Assert.False(TerminalNotificationMetadata.IsSafeUtf8("bad\u0085"u8));
        Assert.True(TerminalNotificationMetadata.IsSafeUtf8("😀"u8));
        Assert.Null(TerminalNotificationMetadata.DecodeBase64("QQ==\n"u8));
        Assert.Null(TerminalNotificationMetadata.DecodeBase64("Q==="u8));
    }

    [Theory]
    [InlineData("QQ", "A")]
    [InlineData("QQ==", "A")]
    [InlineData("QUI", "AB")]
    [InlineData("QUI=", "AB")]
    [InlineData("QUJD", "ABC")]
    [InlineData("Q", null)]
    [InlineData("QQ=", null)]
    [InlineData("Q Q", null)]
    public void Base64AcceptsOptionalFinalPaddingButNotMalformedGroups(string encoded, string? expected)
    {
        byte[]? decoded = TerminalNotificationMetadata.DecodeBase64(Encoding.ASCII.GetBytes(encoded));
        Assert.Equal(expected, decoded is null ? null : Encoding.UTF8.GetString(decoded));
    }

    [Fact]
    public void QuotasAndPendingResetAreBounded()
    {
        using Harness h = new(false);
        string chunk = new('x', 2048);
        for (int i = 0; i < 64; i++) h.Command($"i={i}:d=0", chunk);
        h.Command("i=overflow", "refused");
        Assert.Empty(h.Host.Shown);
        h.Command("i=0", "done");
        Assert.Single(h.Host.Shown);
        h.Processor.Reset();
        for (int i = 0; i < 32; i++) h.Command("i=long:d=0", chunk);
        h.Command("i=long", "over quota");
        Assert.Single(h.Host.Shown);
        h.Command("i=long", "fresh");
        Assert.Equal("fresh", h.Host.Shown[^1].Title);
        h.Processor.Dispose();
        Assert.Contains(h.Host.Shown[^1].Token, h.Host.Closed);
    }

    [Fact]
    public void IconCacheEvictsLeastRecentlyUsedEntriesAndAccountsReplacement()
    {
        TerminalNotificationIconCache cache = new();
        for (int i = 0; i < 128; i++) cache.Put(i.ToString(), new byte[] { (byte)i });
        Assert.Equal((byte)0, cache.Get("0").Span[0]);
        cache.Put("overflow", new byte[] { 255 });
        Assert.True(cache.Get("1").IsEmpty);
        Assert.False(cache.Get("0").IsEmpty);
        cache.Clear();
        byte[] image = new byte[1024 * 1024];
        for (int i = 0; i < 16; i++) cache.Put(i.ToString(), image);
        cache.Put("0", new byte[] { 7 });
        cache.Put("16", image);
        Assert.True(cache.Get("1").IsEmpty);
        Assert.Equal((byte)7, cache.Get("0").Span[0]);
        Assert.False(cache.Get("2").IsEmpty);
    }

    [Fact]
    public async Task AsyncCallbacksAndReentrantSessionResetCannotSendToReplacementSession()
    {
        using Harness h = new(false);
        h.Command("i=old:c=1:a=report", "old session");
        TerminalNotificationRequest request = h.Host.Shown[0];
        await Task.Run(() => h.Host.Activate(request));
        Assert.Empty(h.TakeResponses());
        h.Processor.ResponseCallback = _ => ((ITerminalSessionHistoryController)h.Processor).PrepareForNewSession(false);
        h.Refresh();
        Assert.Equal(0, h.Host.FocusCount);
        Assert.Null(h.Timer.NextTimedRefreshDelay);
        Assert.Contains(request.Token, h.Host.Closed);
    }

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    private const TerminalNotificationCapabilities AllCapabilities = (TerminalNotificationCapabilities)2047;

    private sealed class Clock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(int milliseconds) => _timestamp += TimeSpan.FromMilliseconds(milliseconds).Ticks;
    }

    private sealed class FakeHost : ITerminalNotificationHost
    {
        public TerminalNotificationCapabilities Capabilities { get; set; } = AllCapabilities;
        public bool IsFocused { get; set; }
        public bool IsVisible { get; set; } = true;
        internal List<TerminalNotificationRequest> Shown { get; } = new();
        internal HashSet<Guid> Alive { get; } = new();
        internal List<Guid> Closed { get; } = new();
        private readonly Dictionary<Guid, Action<TerminalNotificationFeedback>> _callbacks = new();
        internal int FocusCount;
        internal bool ThrowOnShow, FailSynchronously;
        public bool Show(TerminalNotificationRequest request, Action<TerminalNotificationFeedback> feedback)
        {
            if (ThrowOnShow) throw new InvalidOperationException("backend failed");
            if (request.ReplacesToken is Guid old) Alive.Remove(old);
            Shown.Add(request);
            Alive.Add(request.Token);
            _callbacks.Add(request.Token, feedback);
            if (FailSynchronously) feedback(new(TerminalNotificationEvent.Failed));
            return true;
        }
        public void Close(Guid token) { Closed.Add(token); Alive.Remove(token); }
        public bool IsAlive(Guid token) => Alive.Contains(token);
        public void Focus() => FocusCount++;
        internal void Activate(TerminalNotificationRequest request, int button = 0)
            => _callbacks[request.Token](new(TerminalNotificationEvent.Activated, button));
        internal void Dismiss(TerminalNotificationRequest request)
        {
            Alive.Remove(request.Token);
            _callbacks[request.Token](new(TerminalNotificationEvent.Closed));
        }
    }

    private sealed class Harness : IDisposable
    {
        internal Clock Clock { get; } = new();
        internal TerminalScreen Screen { get; } = new(20, 4, 20);
        internal IVtProcessor Processor { get; }
        internal ITerminalNotificationSource Source => (ITerminalNotificationSource)Processor;
        internal ITerminalTimedRefreshSource Timer => (ITerminalTimedRefreshSource)Processor;
        internal FakeHost Host { get; } = new();
        internal StringBuilder Responses { get; } = new();
        internal Harness(bool native)
        {
            if (native && !GhosttyVtProcessor.IsAvailable())
            {
                Assert.NotEqual("1", Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS"));
                Assert.Skip("Native Ghostty runtime unavailable.");
            }
            Processor = native ? new GhosttyVtProcessor(Screen, Clock) : new BasicVtProcessor(Screen, new() { TimeProvider = Clock });
            Processor.ResponseCallback = bytes => Responses.Append(Encoding.UTF8.GetString(bytes));
            Source.NotificationHost = Host;
        }
        internal void Command(string metadata, string payload = "", bool bell = false)
            => Processor.Process(Encoding.UTF8.GetBytes("\x1b]99;" + metadata + ";" + payload + (bell ? "\a" : "\x1b\\")));
        internal void Refresh() => Timer.RefreshTimedState();
        internal string TakeResponses() { string text = Responses.ToString(); Responses.Clear(); return text; }
        public void Dispose() => Processor.Dispose();
    }
}
