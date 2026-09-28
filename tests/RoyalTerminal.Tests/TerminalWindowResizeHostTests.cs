// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Headless.XUnit;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Avalonia.Settings;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalWindowResizeHostTests
{
    [Theory]
    [InlineData(1, 1, 296, 143.2)]
    [InlineData(0, 10, 0, 143.2)]
    [InlineData(40, 0, 296, 0)]
    [InlineData(80, 24, 588, 340.8)]
    public void GeometryPreservesZeroAndRoundsMinimaToPhysicalPixels(int columns, int rows, double expectedWidth, double expectedHeight)
    {
        Assert.True(TerminalWindowResizeGeometry.TryResolve(new((ushort)columns, (ushort)rows),
            7.3, 14.1, 3.5, 2, 1.25, out double width, out double height));
        Assert.Equal(expectedWidth, width, 6);
        Assert.Equal(expectedHeight, height, 6);
    }

    [Fact]
    public void EmptyInvalidAndOverflowingGeometryIsRejectedWithoutPartialOutput()
    {
        Assert.False(TerminalWindowResizeGeometry.TryResolve(default, 8, 16, 0, 0, 1, out _, out _));
        foreach (double invalid in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        {
            Assert.False(TerminalWindowResizeGeometry.TryResolve(new(80, 24), invalid, 16, 0, 0, 1, out _, out _));
            Assert.False(TerminalWindowResizeGeometry.TryResolve(new(80, 24), 8, invalid, 0, 0, 1, out _, out _));
            Assert.False(TerminalWindowResizeGeometry.TryResolve(new(80, 24), 8, 16, 0, 0, invalid, out _, out _));
        }
        foreach (double invalid in new[] { -1, double.NaN, double.PositiveInfinity })
        {
            Assert.False(TerminalWindowResizeGeometry.TryResolve(new(80, 24), 8, 16, invalid, 0, 1, out _, out _));
            Assert.False(TerminalWindowResizeGeometry.TryResolve(new(80, 24), 8, 16, 0, invalid, 1, out _, out _));
        }
        Assert.False(TerminalWindowResizeGeometry.TryResolve(new(80, 24), double.MaxValue, 16, 0, 0, 1,
            out double width, out double height));
        Assert.Equal(0, width); Assert.Equal(0, height);
    }

    [Fact]
    public void CoalescingQueueBoundsFloodsAndPreservesEarlierNonzeroDimensions()
    {
        List<Action> scheduled = [];
        List<TerminalWindowResizeRequest> applied = [];
        TerminalWindowResizeQueue queue = new(scheduled.Add, applied.Add);
        for (int index = 1; index <= 10000; index++) queue.Enqueue(new((ushort)(index % 100 + 1), 0));
        queue.Enqueue(new(0, 24));
        queue.Enqueue(default);
        Assert.Empty(applied);
        Assert.Single(scheduled)();
        Assert.Equal(new TerminalWindowResizeRequest(1, 24), Assert.Single(applied));
        queue.Enqueue(new(80, 0));
        Assert.Equal(2, scheduled.Count);
        scheduled[1]();
        Assert.Equal(new TerminalWindowResizeRequest(80, 0), applied[1]);
    }

    [Fact]
    public void ResetDropsOldDimensionsWithoutPostingAnotherAction()
    {
        List<Action> scheduled = [];
        List<TerminalWindowResizeRequest> applied = [];
        TerminalWindowResizeQueue queue = new(scheduled.Add, applied.Add);
        queue.Enqueue(new(80, 24));
        queue.Reset();
        queue.Enqueue(new(0, 30));
        Assert.Single(scheduled)();
        Assert.Equal(new TerminalWindowResizeRequest(0, 30), Assert.Single(applied));
        queue.Enqueue(new(100, 40)); queue.Reset();
        scheduled[1]();
        Assert.Single(applied);
    }

    [Fact]
    public void FailedSchedulingDoesNotWedgeDeliveryOrLeakFailedDimensions()
    {
        Action? scheduled = null;
        int attempts = 0;
        List<TerminalWindowResizeRequest> applied = [];
        TerminalWindowResizeQueue queue = new(action =>
        {
            if (++attempts == 1) throw new InvalidOperationException("scheduler unavailable");
            scheduled = action;
        }, applied.Add);
        Assert.Throws<InvalidOperationException>(() => queue.Enqueue(new(80, 24)));
        queue.Enqueue(new(0, 30));
        Assert.NotNull(scheduled);
        scheduled();
        Assert.Equal(new TerminalWindowResizeRequest(0, 30), Assert.Single(applied));
    }

    [Fact]
    public void ConcurrentParserRequestsPostOneUiAction()
    {
        System.Collections.Concurrent.ConcurrentQueue<Action> scheduled = new();
        List<TerminalWindowResizeRequest> applied = [];
        TerminalWindowResizeQueue queue = new(scheduled.Enqueue, applied.Add);
        Parallel.For(0, 10000, index => queue.Enqueue(index % 2 == 0 ? new(80, 0) : new(0, 24)));
        Assert.True(scheduled.TryDequeue(out Action? action));
        Assert.Empty(scheduled);
        action();
        Assert.Equal(new TerminalWindowResizeRequest(80, 24), Assert.Single(applied));
    }

    [Fact]
    public void WarmCoalescingDoesNotAllocatePerRequest()
    {
        TerminalWindowResizeQueue queue = new(_ => { }, _ => { });
        for (int index = 0; index < 1000; index++) queue.Enqueue(new(80, 24));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 10000; index++) queue.Enqueue(new(100, 30));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProfileSerializationRetainsExplicitPermission(bool allowed)
    {
        TerminalSessionProfilesDocument document = new()
        {
            Profiles = [new() { Id = "resize", Behavior = new() { AllowVtWindowResize = allowed } }],
        };
        TerminalSessionProfilesDocument restored = TerminalSessionProfileSerializer.FromJson(TerminalSessionProfileSerializer.ToJson(document));
        Assert.Equal(allowed, Assert.Single(restored.Profiles).Behavior.AllowVtWindowResize);
        Assert.False(new TerminalSessionBehaviorSettings().AllowVtWindowResize);
    }

    [AvaloniaFact]
    public void ViewModelPermissionDefaultsOffAndPublishesChanges()
    {
        RoyalTerminal.Avalonia.App.ViewModels.MainWindowViewModel model = new();
        Assert.False(model.AllowVtWindowResize);
        string? changed = null;
        model.PropertyChanged += (_, args) => changed = args.PropertyName;
        model.AllowVtWindowResize = true;
        Assert.True(model.AllowVtWindowResize);
        Assert.Equal(nameof(model.AllowVtWindowResize), changed);
    }

    [AvaloniaFact]
    public void SettingsEditorPersistsPermissionAndNotifiesItsCategory()
    {
        TerminalSettingsPanelState state = new();
        state.LoadDocument(new TerminalSessionProfilesDocument { Profiles = [new() { Id = "resize" }] });
        Assert.False(state.TerminalBehavior.AllowVtWindowResize);
        string? changed = null;
        state.TerminalBehavior.PropertyChanged += (_, args) => changed = args.PropertyName;
        state.TerminalBehavior.AllowVtWindowResize = true;
        Assert.Equal(nameof(state.AllowVtWindowResize), changed);
        Assert.True(Assert.Single(state.BuildDocument().Profiles).Behavior.AllowVtWindowResize);
        TerminalSettingsPanelState reloaded = new();
        reloaded.LoadDocument(state.BuildDocument());
        Assert.True(reloaded.TerminalBehavior.AllowVtWindowResize);
        reloaded.AllowVtWindowResize = false;
        Assert.False(Assert.Single(reloaded.BuildDocument().Profiles).Behavior.AllowVtWindowResize);
    }
}
