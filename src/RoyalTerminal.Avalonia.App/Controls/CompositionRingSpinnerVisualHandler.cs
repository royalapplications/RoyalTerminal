// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.App.Controls;

internal sealed class CompositionRingSpinnerVisualHandler : CompositionCustomVisualHandler
{
    private const float MinSweepAngle = 10F;
    private const float MaxSweepAngle = 280F;
    private static readonly TimeSpan RootRotationDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SliceDuration = TimeSpan.FromMilliseconds(1200);

    private bool _isActive = true;
    private Size _size;
    private Color _foregroundColor = Colors.DodgerBlue;
    private Color _trackColor = Colors.Transparent;
    private double _strokeThickness = 2D;
    private TimeSpan? _firstFrame;
    private TimeSpan _currentFrame;
    private bool _isAnimationFrameUpdateRegistered;

    public override void OnMessage(object message)
    {
        if (message is not CompositionRingSpinnerVisualState state)
        {
            return;
        }

        _isActive = state.IsActive;
        _size = state.Size;
        _foregroundColor = state.ForegroundColor;
        _trackColor = state.TrackColor;
        _strokeThickness = state.StrokeThickness;

        if (_isActive)
        {
            RegisterForNextAnimationFrameUpdateIfNeeded();
        }
        else
        {
            _firstFrame = null;
        }

        Invalidate();
    }

    public override void OnAnimationFrameUpdate()
    {
        _isAnimationFrameUpdateRegistered = false;

        if (!_isActive)
        {
            return;
        }

        _currentFrame = CompositionNow;
        _firstFrame ??= _currentFrame;
        Invalidate();
        RegisterForNextAnimationFrameUpdateIfNeeded();
    }

    public override void OnRender(ImmediateDrawingContext drawingContext)
    {
        if (_size.Width <= 0D || _size.Height <= 0D)
        {
            return;
        }

        if (!drawingContext.TryGetFeature<ISkiaSharpApiLeaseFeature>(out ISkiaSharpApiLeaseFeature? leaseFeature))
        {
            return;
        }

        using ISkiaSharpApiLease lease = leaseFeature.Lease();
        SKCanvas canvas = lease.SkCanvas;
        float side = (float)Math.Min(_size.Width, _size.Height);
        if (side <= 0F)
        {
            return;
        }

        float stroke = Math.Clamp((float)_strokeThickness, 1F, side / 2F);
        float inset = stroke / 2F;
        SKRect rect = SKRect.Create(
            ((float)_size.Width - side) / 2F + inset,
            ((float)_size.Height - side) / 2F + inset,
            side - stroke,
            side - stroke);

        if (_trackColor.A > 0)
        {
            using SKPaint trackPaint = new();
            trackPaint.Style = SKPaintStyle.Stroke;
            trackPaint.StrokeCap = SKStrokeCap.Round;
            trackPaint.StrokeWidth = stroke;
            trackPaint.IsAntialias = true;
            trackPaint.Color = _trackColor.ToSKColor();

            canvas.DrawOval(rect, trackPaint);
        }

        if (!_isActive)
        {
            return;
        }

        TimeSpan elapsed = _firstFrame.HasValue ? _currentFrame - _firstFrame.Value : TimeSpan.Zero;
        float rootRotation = Progress(elapsed, RootRotationDuration) * 359.99F;
        float sliceProgress = Progress(elapsed, SliceDuration);
        float sliceRotation = 0F;
        float sweep = MaxSweepAngle;

        if (sliceProgress < 0.33F)
        {
            float progress = EaseOutQuadratic(sliceProgress / 0.33F);
            sweep = Lerp(MinSweepAngle, MaxSweepAngle, progress);
        }
        else if (sliceProgress > 0.66F)
        {
            float progress = EaseOutQuadratic((sliceProgress - 0.66F) / 0.34F);
            sliceRotation = Lerp(0F, 359.99F, progress);
            sweep = Lerp(MaxSweepAngle, MinSweepAngle, progress);
        }

        float start = rootRotation + sliceRotation - 90F;

        using SKPaint arcPaint = new();
        arcPaint.Style = SKPaintStyle.Stroke;
        arcPaint.StrokeCap = SKStrokeCap.Round;
        arcPaint.StrokeWidth = stroke;
        arcPaint.IsAntialias = true;
        arcPaint.Color = _foregroundColor.ToSKColor();

        canvas.DrawArc(rect, start, sweep, false, arcPaint);
    }

    public override Rect GetRenderBounds() => new(_size);

    private static float Progress(TimeSpan elapsed, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return 0F;
        }

        long ticks = elapsed.Ticks % duration.Ticks;
        return (float)((double)ticks / duration.Ticks);
    }

    private static float EaseOutQuadratic(float value)
    {
        value = Math.Clamp(value, 0F, 1F);
        return 1F - (1F - value) * (1F - value);
    }

    private static float Lerp(float start, float end, float progress) =>
        start + ((end - start) * progress);

    private void RegisterForNextAnimationFrameUpdateIfNeeded()
    {
        if (_isAnimationFrameUpdateRegistered)
        {
            return;
        }

        _isAnimationFrameUpdateRegistered = true;
        RegisterForNextAnimationFrameUpdate();
    }
}
