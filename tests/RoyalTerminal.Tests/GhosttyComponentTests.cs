// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — tests for extracted Ghostty control infrastructure components.

using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Diagnostics;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttyComponentTests
{
    [AvaloniaFact]
    public void GhosttyActionDispatcher_Render_PostsRenderCallback()
    {
        bool renderCalled = false;
        GhosttyActionDispatcher dispatcher = CreateDispatcher(
            renderRequested: () => renderCalled = true);

        GhosttyAction action = new()
        {
            Tag = GhosttyActionTag.Render,
        };

        dispatcher.HandleAction(CreateAppTarget(), action);
        FlushUiThread();

        Assert.True(renderCalled);
    }

    [AvaloniaFact]
    public unsafe void GhosttyActionDispatcher_SetTitle_DecodesBeforeDispatch()
    {
        string? capturedTitle = null;
        GhosttyActionDispatcher dispatcher = CreateDispatcher(
            titleChanged: title => capturedTitle = title);

        nint titlePtr = Marshal.StringToCoTaskMemUTF8("phase-6-title");
        try
        {
            GhosttyAction action = new()
            {
                Tag = GhosttyActionTag.SetTitle,
                Action = new GhosttyActionValue
                {
                    SetTitle = new GhosttySetTitle
                    {
                        Title = (byte*)titlePtr,
                    }
                }
            };

            dispatcher.HandleAction(CreateAppTarget(), action);
        }
        finally
        {
            Marshal.FreeCoTaskMem(titlePtr);
        }

        FlushUiThread();
        Assert.Equal("phase-6-title", capturedTitle);
    }

    [AvaloniaFact]
    public void GhosttyActionDispatcher_SurfaceTargetMismatch_DoesNotDispatch()
    {
        bool renderCalled = false;
        GhosttySurface surface = new((nint)0x1111, ownsHandle: false);
        GhosttyActionDispatcher dispatcher = CreateDispatcher(
            surfaceAccessor: () => surface,
            renderRequested: () => renderCalled = true);

        GhosttyTarget target = new()
        {
            Tag = GhosttyTargetTag.Surface,
            Target = new GhosttyTargetUnion
            {
                Surface = (nint)0x9999,
            }
        };

        dispatcher.HandleAction(
            target,
            new GhosttyAction
            {
                Tag = GhosttyActionTag.Render,
            });

        FlushUiThread();
        Assert.False(renderCalled);
    }

    [AvaloniaFact]
    public void GhosttyActionDispatcher_ExitAndClose_PostCallbacks()
    {
        int? exitCode = null;
        int closeCount = 0;
        GhosttyActionDispatcher dispatcher = CreateDispatcher(
            processExited: code => exitCode = code,
            closeRequested: () => closeCount++);

        GhosttyAction exitedAction = new()
        {
            Tag = GhosttyActionTag.ShowChildExited,
            Action = new GhosttyActionValue
            {
                ChildExited = new GhosttyChildExited
                {
                    ExitCode = 37,
                }
            }
        };

        dispatcher.HandleAction(CreateAppTarget(), exitedAction);
        dispatcher.HandleAction(
            CreateAppTarget(),
            new GhosttyAction
            {
                Tag = GhosttyActionTag.CloseWindow,
            });

        FlushUiThread();
        Assert.Equal(37, exitCode);
        Assert.Equal(1, closeCount);
    }

    [AvaloniaFact]
    public void GhosttyActionDispatcher_RingBell_PostsCallback()
    {
        int bellCount = 0;
        GhosttyActionDispatcher dispatcher = CreateDispatcher(
            bellRequested: () => bellCount++);

        dispatcher.HandleAction(
            CreateAppTarget(),
            new GhosttyAction
            {
                Tag = GhosttyActionTag.RingBell,
            });

        FlushUiThread();
        Assert.Equal(1, bellCount);
    }

    [AvaloniaFact]
    public void GhosttyActionDispatcher_ColorConfigReload_PostCallbacks()
    {
        GhosttyColorChange? capturedColor = null;
        nint capturedConfig = nint.Zero;
        bool? capturedReloadSoft = null;

        GhosttyActionDispatcher dispatcher = CreateDispatcher(
            colorChanged: change => capturedColor = change,
            configChanged: configHandle => capturedConfig = configHandle,
            reloadConfig: soft => capturedReloadSoft = soft);

        dispatcher.HandleAction(
            CreateAppTarget(),
            new GhosttyAction
            {
                Tag = GhosttyActionTag.ColorChange,
                Action = new GhosttyActionValue
                {
                    ColorChange = new GhosttyColorChange
                    {
                        Kind = (GhosttyColorKind)15,
                        R = 0x12,
                        G = 0x34,
                        B = 0x56,
                    }
                }
            });

        dispatcher.HandleAction(
            CreateAppTarget(),
            new GhosttyAction
            {
                Tag = GhosttyActionTag.ConfigChange,
                Action = new GhosttyActionValue
                {
                    ConfigChange = new GhosttyConfigChange
                    {
                        Config = (nint)0x7777,
                    }
                }
            });

        dispatcher.HandleAction(
            CreateAppTarget(),
            new GhosttyAction
            {
                Tag = GhosttyActionTag.ReloadConfig,
                Action = new GhosttyActionValue
                {
                    ReloadConfig = new GhosttyReloadConfig
                    {
                        Soft = true,
                    }
                }
            });

        FlushUiThread();

        Assert.NotNull(capturedColor);
        Assert.Equal((GhosttyColorKind)15, capturedColor!.Value.Kind);
        Assert.Equal(0x12, capturedColor.Value.R);
        Assert.Equal(0x34, capturedColor.Value.G);
        Assert.Equal(0x56, capturedColor.Value.B);
        Assert.Equal((nint)0x7777, capturedConfig);
        Assert.True(capturedReloadSoft);
    }

    [AvaloniaFact]
    public unsafe void GhosttyActionDispatcher_MouseOverLink_PostsDecodedUrl()
    {
        string? capturedUrl = null;
        GhosttyActionDispatcher dispatcher = CreateDispatcher(
            mouseOverLinkChanged: url => capturedUrl = url);

        nint urlPtr = Marshal.StringToCoTaskMemUTF8("https://example.com/parity");
        try
        {
            GhosttyAction action = new()
            {
                Tag = GhosttyActionTag.MouseOverLink,
                Action = new GhosttyActionValue
                {
                    MouseOverLink = new GhosttyMouseOverLink
                    {
                        Url = (byte*)urlPtr,
                        Len = (nuint)"https://example.com/parity".Length,
                    }
                }
            };

            dispatcher.HandleAction(CreateAppTarget(), action);
        }
        finally
        {
            Marshal.FreeCoTaskMem(urlPtr);
        }

        FlushUiThread();
        Assert.Equal("https://example.com/parity", capturedUrl);
    }

    [AvaloniaFact]
    public unsafe void GhosttyActionDispatcher_SearchAndOpacity_PostCallbacks()
    {
        string? startedNeedle = null;
        bool ended = false;
        int? total = null;
        int? selected = null;
        int toggleCount = 0;

        GhosttyActionDispatcher dispatcher = CreateDispatcher(
            searchStarted: needle => startedNeedle = needle,
            searchEnded: () => ended = true,
            searchTotalChanged: value => total = value,
            searchSelectedChanged: value => selected = value,
            toggleBackgroundOpacity: () => toggleCount++);

        nint needlePtr = Marshal.StringToCoTaskMemUTF8("TODO");
        try
        {
            dispatcher.HandleAction(
                CreateAppTarget(),
                new GhosttyAction
                {
                    Tag = GhosttyActionTag.StartSearch,
                    Action = new GhosttyActionValue
                    {
                        StartSearch = new GhosttyStartSearch
                        {
                            Needle = (byte*)needlePtr,
                        }
                    }
                });
        }
        finally
        {
            Marshal.FreeCoTaskMem(needlePtr);
        }

        dispatcher.HandleAction(
            CreateAppTarget(),
            new GhosttyAction
            {
                Tag = GhosttyActionTag.SearchTotal,
                Action = new GhosttyActionValue
                {
                    SearchTotal = new GhosttySearchTotal
                    {
                        Total = 17,
                    }
                }
            });

        dispatcher.HandleAction(
            CreateAppTarget(),
            new GhosttyAction
            {
                Tag = GhosttyActionTag.SearchSelected,
                Action = new GhosttyActionValue
                {
                    SearchSelected = new GhosttySearchSelected
                    {
                        Selected = 4,
                    }
                }
            });

        dispatcher.HandleAction(
            CreateAppTarget(),
            new GhosttyAction
            {
                Tag = GhosttyActionTag.EndSearch,
            });

        dispatcher.HandleAction(
            CreateAppTarget(),
            new GhosttyAction
            {
                Tag = GhosttyActionTag.ToggleBackgroundOpacity,
            });

        FlushUiThread();

        Assert.Equal("TODO", startedNeedle);
        Assert.True(ended);
        Assert.Equal(17, total);
        Assert.Equal(4, selected);
        Assert.Equal(1, toggleCount);
    }

    [AvaloniaFact]
    public void GhosttySurfaceLifecycle_AttachAndDetach_WiresCallbacks()
    {
        GhosttyApp app = new((nint)0x1, ownsHandle: false);
        GhosttySurfaceLifecycle lifecycle = CreateLifecycle();

        Assert.Equal(0, GetEventSubscriptionCount(app, "WakeupRequested"));
        Assert.Equal(0, GetEventSubscriptionCount(app, "ActionRequested"));
        Assert.Equal(0, GetEventSubscriptionCount(app, "ClipboardReadRequested"));
        Assert.Equal(0, GetEventSubscriptionCount(app, "ClipboardWriteRequested"));
        Assert.Equal(0, GetEventSubscriptionCount(app, "SurfaceCloseRequested"));

        lifecycle.Attach(app);

        Assert.Equal(1, GetEventSubscriptionCount(app, "WakeupRequested"));
        Assert.Equal(1, GetEventSubscriptionCount(app, "ActionRequested"));
        Assert.Equal(1, GetEventSubscriptionCount(app, "ClipboardReadRequested"));
        Assert.Equal(1, GetEventSubscriptionCount(app, "ClipboardWriteRequested"));
        Assert.Equal(1, GetEventSubscriptionCount(app, "SurfaceCloseRequested"));

        lifecycle.Detach();

        Assert.Equal(0, GetEventSubscriptionCount(app, "WakeupRequested"));
        Assert.Equal(0, GetEventSubscriptionCount(app, "ActionRequested"));
        Assert.Equal(0, GetEventSubscriptionCount(app, "ClipboardReadRequested"));
        Assert.Equal(0, GetEventSubscriptionCount(app, "ClipboardWriteRequested"));
        Assert.Equal(0, GetEventSubscriptionCount(app, "SurfaceCloseRequested"));
    }

    [AvaloniaFact]
    public void GhosttySurfaceLifecycle_AttachNewApp_DetachesOldApp()
    {
        GhosttyApp firstApp = new((nint)0x1, ownsHandle: false);
        GhosttyApp secondApp = new((nint)0x2, ownsHandle: false);
        GhosttySurfaceLifecycle lifecycle = CreateLifecycle();

        lifecycle.Attach(firstApp);
        Assert.Equal(1, GetEventSubscriptionCount(firstApp, "WakeupRequested"));

        lifecycle.Attach(secondApp);

        Assert.Equal(0, GetEventSubscriptionCount(firstApp, "WakeupRequested"));
        Assert.Equal(1, GetEventSubscriptionCount(secondApp, "WakeupRequested"));
        Assert.Equal(1, GetEventSubscriptionCount(secondApp, "ActionRequested"));
        Assert.Equal(1, GetEventSubscriptionCount(secondApp, "ClipboardReadRequested"));
        Assert.Equal(1, GetEventSubscriptionCount(secondApp, "ClipboardWriteRequested"));
        Assert.Equal(1, GetEventSubscriptionCount(secondApp, "SurfaceCloseRequested"));
    }

    [Fact]
    public void GhosttyVtProcessor_ModeState_MatchesIndividualFlags_WhenAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        TerminalScreen screen = new(80, 24, 0);
        using GhosttyVtProcessor processor = new(screen);

        AssertModeStateParity(processor);
        Assert.False(processor.ApplicationKeypad);

        processor.Process("\x1b="u8);
        Assert.True(processor.ApplicationKeypad);
        Assert.True(processor.ModeState.ApplicationKeypad);
        AssertModeStateParity(processor);

        processor.Process("\x1b>"u8);
        Assert.False(processor.ApplicationKeypad);
        Assert.False(processor.ModeState.ApplicationKeypad);
        AssertModeStateParity(processor);

        processor.Reset();
        Assert.False(processor.ApplicationKeypad);
        Assert.False(processor.ModeState.ApplicationKeypad);
        AssertModeStateParity(processor);
    }

    [Theory]
    [InlineData(0, TerminalUnderlineStyle.None, false)]
    [InlineData(1, TerminalUnderlineStyle.Single, true)]
    [InlineData(2, TerminalUnderlineStyle.Double, true)]
    [InlineData(3, TerminalUnderlineStyle.Curly, true)]
    [InlineData(4, TerminalUnderlineStyle.Dotted, true)]
    [InlineData(5, TerminalUnderlineStyle.Dashed, true)]
    [InlineData(6, TerminalUnderlineStyle.None, false)]
    public void GhosttyVtProcessor_PrivateMappings_MapUnderlineStyleBits(
        int underlineBits,
        TerminalUnderlineStyle expectedStyle,
        bool expectsUnderlineAttribute)
    {
        uint attrs = (uint)(underlineBits << 8);

        TerminalUnderlineStyle mappedStyle = InvokePrivateStatic<TerminalUnderlineStyle>(
            typeof(GhosttyVtProcessor),
            "MapUnderlineStyle",
            attrs);
        CellAttributes mappedAttributes = InvokePrivateStatic<CellAttributes>(
            typeof(GhosttyVtProcessor),
            "MapAttributes",
            attrs);

        Assert.Equal(expectedStyle, mappedStyle);
        Assert.Equal(expectsUnderlineAttribute, (mappedAttributes & CellAttributes.Underline) != 0);
    }

    [Fact]
    public void GhosttyVtProcessor_PrivateMappings_MapOverlineDecoration()
    {
        uint attrs = 1u << 6;

        CellDecorations mappedDecorations = InvokePrivateStatic<CellDecorations>(
            typeof(GhosttyVtProcessor),
            "MapDecorations",
            attrs);
        CellAttributes mappedAttributes = InvokePrivateStatic<CellAttributes>(
            typeof(GhosttyVtProcessor),
            "MapAttributes",
            attrs);

        Assert.True((mappedDecorations & CellDecorations.Overline) != 0);
        Assert.False((mappedAttributes & CellAttributes.Underline) != 0);
    }

    [Theory]
    [InlineData(0, TerminalUnderlineStyle.None, false)]
    [InlineData(1, TerminalUnderlineStyle.Single, true)]
    [InlineData(2, TerminalUnderlineStyle.Double, true)]
    [InlineData(3, TerminalUnderlineStyle.Curly, true)]
    [InlineData(4, TerminalUnderlineStyle.Dotted, true)]
    [InlineData(5, TerminalUnderlineStyle.Dashed, true)]
    [InlineData(6, TerminalUnderlineStyle.None, false)]
    public void GhosttyRenderedTerminalControl_PrivateMappings_MapUnderlineStyleBits(
        int underlineBits,
        TerminalUnderlineStyle expectedStyle,
        bool expectsUnderlineAttribute)
    {
        ushort attrs = (ushort)(underlineBits << 8);

        TerminalUnderlineStyle mappedStyle = InvokePrivateStatic<TerminalUnderlineStyle>(
            typeof(GhosttyRenderedTerminalControl),
            "ConvertUnderlineStyle",
            attrs);
        CellAttributes mappedAttributes = InvokePrivateStatic<CellAttributes>(
            typeof(GhosttyRenderedTerminalControl),
            "ConvertAttributes",
            attrs);

        Assert.Equal(expectedStyle, mappedStyle);
        Assert.Equal(expectsUnderlineAttribute, (mappedAttributes & CellAttributes.Underline) != 0);
    }

    [Fact]
    public void GhosttyRenderedTerminalControl_PrivateMappings_MapOverlineDecoration()
    {
        const ushort attrs = 1 << 7;

        CellDecorations mappedDecorations = InvokePrivateStatic<CellDecorations>(
            typeof(GhosttyRenderedTerminalControl),
            "ConvertDecorations",
            attrs);
        CellAttributes mappedAttributes = InvokePrivateStatic<CellAttributes>(
            typeof(GhosttyRenderedTerminalControl),
            "ConvertAttributes",
            attrs);

        Assert.True((mappedDecorations & CellDecorations.Overline) != 0);
        Assert.False((mappedAttributes & CellAttributes.Underline) != 0);
    }

    [Theory]
    [InlineData(3, CellAttributes.Blink)]
    [InlineData(4, CellAttributes.Inverse)]
    [InlineData(5, CellAttributes.Hidden)]
    [InlineData(6, CellAttributes.Strikethrough)]
    public void GhosttyRenderedTerminalControl_PrivateMappings_MapCoreAttributeBits(
        int bit,
        CellAttributes expectedFlag)
    {
        ushort attrs = (ushort)(1 << bit);

        CellAttributes mappedAttributes = InvokePrivateStatic<CellAttributes>(
            typeof(GhosttyRenderedTerminalControl),
            "ConvertAttributes",
            attrs);

        Assert.True((mappedAttributes & expectedFlag) != 0);
    }

    [Theory]
    [InlineData(0, CursorStyle.Bar)]
    [InlineData(1, CursorStyle.Block)]
    [InlineData(2, CursorStyle.Underline)]
    [InlineData(3, CursorStyle.BlockHollow)]
    [InlineData(4, CursorStyle.Bar)]
    [InlineData(5, CursorStyle.Bar)]
    [InlineData(255, CursorStyle.Block)]
    public void GhosttyRenderedTerminalControl_PrivateMappings_MapCursorStyle(
        byte style,
        CursorStyle expected)
    {
        CursorStyle mapped = InvokePrivateStatic<CursorStyle>(
            typeof(GhosttyRenderedTerminalControl),
            "ConvertCursorStyle",
            style);

        Assert.Equal(expected, mapped);
    }

    [Fact]
    public void GhosttyRenderedTerminalControl_PrivateMappings_ResolveHyperlinkSpanFromPointer()
    {
        TerminalRow row = new(8);
        row[2].HyperlinkId = 42;
        row[3].HyperlinkId = 42;
        row[4].HyperlinkId = 42;

        TerminalHighlightSpan? resolved = InvokePrivateStaticNullable<TerminalHighlightSpan>(
            typeof(GhosttyRenderedTerminalControl),
            "ResolveHyperlinkSpanFromPointer",
            row,
            5,
            3);
        Assert.NotNull(resolved);
        Assert.Equal(5, resolved.Value.Row);
        Assert.Equal(2, resolved.Value.StartColumn);
        Assert.Equal(4, resolved.Value.EndColumn);
        Assert.Equal(TerminalHighlightKind.HyperlinkHover, resolved.Value.Kind);

        TerminalHighlightSpan? missing = InvokePrivateStaticNullable<TerminalHighlightSpan>(
            typeof(GhosttyRenderedTerminalControl),
            "ResolveHyperlinkSpanFromPointer",
            row,
            5,
            1);
        Assert.Null(missing);
    }

    private static GhosttySurfaceLifecycle CreateLifecycle()
    {
        GhosttyActionDispatcher dispatcher = CreateDispatcher();
        GhosttyClipboardAdapter clipboardAdapter = new(
            new Control(),
            static () => NullGhosttyLogger.Instance);
        return new GhosttySurfaceLifecycle(
            static () => null,
            dispatcher,
            clipboardAdapter,
            static () => { },
            static () => { });
    }

    private static GhosttyActionDispatcher CreateDispatcher(
        Func<GhosttySurface?>? surfaceAccessor = null,
        Action? renderRequested = null,
        Action<string>? titleChanged = null,
        Action<int>? processExited = null,
        Action? closeRequested = null,
        Action? bellRequested = null,
        Action<GhosttyColorChange>? colorChanged = null,
        Action<nint>? configChanged = null,
        Action<bool>? reloadConfig = null,
        Action<string?>? mouseOverLinkChanged = null,
        Action<string?>? searchStarted = null,
        Action? searchEnded = null,
        Action<int>? searchTotalChanged = null,
        Action<int>? searchSelectedChanged = null,
        Action? toggleBackgroundOpacity = null)
    {
        return new GhosttyActionDispatcher(
            surfaceAccessor ?? (static () => null),
            renderRequested ?? (static () => { }),
            titleChanged ?? (static _ => { }),
            processExited ?? (static _ => { }),
            closeRequested ?? (static () => { }),
            bellRequested,
            colorChanged,
            configChanged,
            reloadConfig,
            mouseOverLinkChanged,
            searchStarted,
            searchEnded,
            searchTotalChanged,
            searchSelectedChanged,
            toggleBackgroundOpacity);
    }

    private static GhosttyTarget CreateAppTarget()
    {
        return new GhosttyTarget
        {
            Tag = GhosttyTargetTag.App,
        };
    }

    private static void FlushUiThread()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.RunJobs();
            return;
        }

        Dispatcher.UIThread.Invoke(
            static () => Dispatcher.UIThread.RunJobs());
    }

    private static int GetEventSubscriptionCount(GhosttyApp app, string eventName)
    {
        FieldInfo? field = typeof(GhosttyApp).GetField(
            eventName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        Delegate? callback = field!.GetValue(app) as Delegate;
        return callback?.GetInvocationList().Length ?? 0;
    }

    private static T InvokePrivateStatic<T>(Type type, string methodName, params object[] args)
    {
        MethodInfo? method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        object? value = method!.Invoke(null, args);
        Assert.NotNull(value);
        return (T)value!;
    }

    private static T? InvokePrivateStaticNullable<T>(Type type, string methodName, params object[] args)
        where T : struct
    {
        MethodInfo? method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        object? value = method!.Invoke(null, args);
        if (value is null)
        {
            return null;
        }

        return (T)value;
    }

    private static void AssertModeStateParity(IVtProcessor processor)
    {
        Assert.Equal(processor.CursorVisible, processor.ModeState.CursorVisible);
        Assert.Equal(processor.ApplicationCursorKeys, processor.ModeState.ApplicationCursorKeys);
        Assert.Equal(processor.ApplicationKeypad, processor.ModeState.ApplicationKeypad);
        Assert.Equal(processor.AlternateScreen, processor.ModeState.AlternateScreen);
        Assert.Equal(processor.BracketedPaste, processor.ModeState.BracketedPaste);
    }
}
