// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.GhosttySharp - .NET bindings for the Ghostty terminal emulator.

using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

/// <summary>Clipboard content with MIME type and data.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyClipboardContent
{
    /// <summary>Native field <c>Mime</c>.</summary>
    public byte* Mime;
    /// <summary>Native field <c>Data</c>.</summary>
    public byte* Data;
}

/// <summary>Key input event.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyInputKey
{
    /// <summary>Native field <c>Action</c>.</summary>
    public GhosttyInputAction Action;
    /// <summary>Native field <c>Mods</c>.</summary>
    public GhosttyMods Mods;
    /// <summary>Native field <c>ConsumedMods</c>.</summary>
    public GhosttyMods ConsumedMods;
    /// <summary>Native field <c>Keycode</c>.</summary>
    public uint Keycode;
    /// <summary>Native field <c>Text</c>.</summary>
    public byte* Text;
    /// <summary>Native field <c>UnshiftedCodepoint</c>.</summary>
    public uint UnshiftedCodepoint;
    /// <summary>Native field <c>Composing</c>.</summary>
    public bool Composing;
}

/// <summary>Input trigger key union.</summary>
[StructLayout(LayoutKind.Explicit)]
public struct GhosttyInputTriggerKey
{
    /// <summary>Native field <c>Translated</c>.</summary>
    [FieldOffset(0)] public GhosttyKey Translated;
    /// <summary>Native field <c>Physical</c>.</summary>
    [FieldOffset(0)] public GhosttyKey Physical;
    /// <summary>Native field <c>Unicode</c>.</summary>
    [FieldOffset(0)] public uint Unicode;
}

/// <summary>Input trigger for key binding matching.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyInputTrigger
{
    /// <summary>Native field <c>Tag</c>.</summary>
    public GhosttyInputTriggerTag Tag;
    /// <summary>Native field <c>Key</c>.</summary>
    public GhosttyInputTriggerKey Key;
    /// <summary>Native field <c>Mods</c>.</summary>
    public GhosttyMods Mods;
}

/// <summary>Command descriptor for configuration.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyCommand
{
    /// <summary>Native field <c>ActionKey</c>.</summary>
    public byte* ActionKey;
    /// <summary>Native field <c>Action</c>.</summary>
    public byte* Action;
    /// <summary>Native field <c>Title</c>.</summary>
    public byte* Title;
    /// <summary>Native field <c>Description</c>.</summary>
    public byte* Description;
}

/// <summary>Build/version information.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyInfo
{
    /// <summary>Native field <c>BuildMode</c>.</summary>
    public GhosttyBuildMode BuildMode;
    /// <summary>Native field <c>Version</c>.</summary>
    public byte* Version;
    /// <summary>Native field <c>VersionLen</c>.</summary>
    public nuint VersionLen;
}

/// <summary>Diagnostic message from configuration.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyDiagnostic
{
    /// <summary>Native field <c>Message</c>.</summary>
    public byte* Message;
}

/// <summary>Ghostty-owned string with length.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyString
{
    /// <summary>Native field <c>Ptr</c>.</summary>
    public byte* Ptr;
    /// <summary>Native field <c>Len</c>.</summary>
    public nuint Len;
    /// <summary>Native field <c>Sentinel</c>.</summary>
    public bool Sentinel;
}

/// <summary>Text data from terminal surface.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyText
{
    /// <summary>Native field <c>TlPxX</c>.</summary>
    public double TlPxX;
    /// <summary>Native field <c>TlPxY</c>.</summary>
    public double TlPxY;
    /// <summary>Native field <c>OffsetStart</c>.</summary>
    public uint OffsetStart;
    /// <summary>Native field <c>OffsetLen</c>.</summary>
    public uint OffsetLen;
    /// <summary>Native field <c>TextPtr</c>.</summary>
    public byte* TextPtr;
    /// <summary>Native field <c>TextLen</c>.</summary>
    public nuint TextLen;
}

/// <summary>Point in terminal coordinate space.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyPoint
{
    /// <summary>Native field <c>Tag</c>.</summary>
    public GhosttyPointTag Tag;
    /// <summary>Native field <c>Coord</c>.</summary>
    public GhosttyPointCoord Coord;
    /// <summary>Native field <c>X</c>.</summary>
    public uint X;
    /// <summary>Native field <c>Y</c>.</summary>
    public uint Y;
}

/// <summary>Selection range in terminal.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttySelection
{
    /// <summary>Native field <c>TopLeft</c>.</summary>
    public GhosttyPoint TopLeft;
    /// <summary>Native field <c>BottomRight</c>.</summary>
    public GhosttyPoint BottomRight;
    /// <summary>Native field <c>Rectangle</c>.</summary>
    public bool Rectangle;
}

/// <summary>Environment variable key-value pair.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyEnvVar
{
    /// <summary>Native field <c>Key</c>.</summary>
    public byte* Key;
    /// <summary>Native field <c>Value</c>.</summary>
    public byte* Value;
}

/// <summary>macOS platform-specific data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyPlatformMacOS
{
    /// <summary>Native field <c>NSView</c>.</summary>
    public nint NSView;
}

/// <summary>iOS platform-specific data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyPlatformIOS
{
    /// <summary>Native field <c>UIView</c>.</summary>
    public nint UIView;
}

/// <summary>Platform-specific union.</summary>
[StructLayout(LayoutKind.Explicit)]
public struct GhosttyPlatformUnion
{
    /// <summary>Native field <c>MacOS</c>.</summary>
    [FieldOffset(0)] public GhosttyPlatformMacOS MacOS;
    /// <summary>Native field <c>IOS</c>.</summary>
    [FieldOffset(0)] public GhosttyPlatformIOS IOS;
}

/// <summary>Surface creation configuration.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttySurfaceConfig
{
    /// <summary>Native field <c>PlatformTag</c>.</summary>
    public GhosttyPlatform PlatformTag;
    /// <summary>Native field <c>Platform</c>.</summary>
    public GhosttyPlatformUnion Platform;
    /// <summary>Native field <c>Userdata</c>.</summary>
    public nint Userdata;
    /// <summary>Native field <c>ScaleFactor</c>.</summary>
    public double ScaleFactor;
    /// <summary>Native field <c>FontSize</c>.</summary>
    public float FontSize;
    /// <summary>Native field <c>WorkingDirectory</c>.</summary>
    public byte* WorkingDirectory;
    /// <summary>Native field <c>Command</c>.</summary>
    public byte* Command;
    /// <summary>Native field <c>EnvVars</c>.</summary>
    public GhosttyEnvVar* EnvVars;
    /// <summary>Native field <c>EnvVarCount</c>.</summary>
    public nuint EnvVarCount;
    /// <summary>Native field <c>InitialInput</c>.</summary>
    public byte* InitialInput;
    /// <summary>Native field <c>WaitAfterCommand</c>.</summary>
    public bool WaitAfterCommand;
    /// <summary>Native field <c>Context</c>.</summary>
    public GhosttySurfaceContext Context;
}

/// <summary>Surface size information.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttySurfaceSize
{
    /// <summary>Native field <c>Columns</c>.</summary>
    public ushort Columns;
    /// <summary>Native field <c>Rows</c>.</summary>
    public ushort Rows;
    /// <summary>Native field <c>WidthPx</c>.</summary>
    public uint WidthPx;
    /// <summary>Native field <c>HeightPx</c>.</summary>
    public uint HeightPx;
    /// <summary>Native field <c>CellWidthPx</c>.</summary>
    public uint CellWidthPx;
    /// <summary>Native field <c>CellHeightPx</c>.</summary>
    public uint CellHeightPx;
}

/// <summary>RGB color.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyConfigColor
{
    /// <summary>Native field <c>R</c>.</summary>
    public byte R;
    /// <summary>Native field <c>G</c>.</summary>
    public byte G;
    /// <summary>Native field <c>B</c>.</summary>
    public byte B;
}

/// <summary>Color list.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyConfigColorList
{
    /// <summary>Native field <c>Colors</c>.</summary>
    public GhosttyConfigColor* Colors;
    /// <summary>Native field <c>Len</c>.</summary>
    public nuint Len;
}

/// <summary>Command list.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyConfigCommandList
{
    /// <summary>Native field <c>Commands</c>.</summary>
    public GhosttyCommand* Commands;
    /// <summary>Native field <c>Len</c>.</summary>
    public nuint Len;
}

/// <summary>256-color palette.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyConfigPalette
{
    public fixed byte Colors[256 * 3]; // 256 * sizeof(GhosttyConfigColor)
}

/// <summary>Quick terminal size value union.</summary>
[StructLayout(LayoutKind.Explicit)]
public struct GhosttyQuickTerminalSizeValue
{
    /// <summary>Native field <c>Percentage</c>.</summary>
    [FieldOffset(0)] public float Percentage;
    /// <summary>Native field <c>Pixels</c>.</summary>
    [FieldOffset(0)] public uint Pixels;
}

/// <summary>Quick terminal size.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyQuickTerminalSize
{
    /// <summary>Native field <c>Tag</c>.</summary>
    public GhosttyQuickTerminalSizeTag Tag;
    /// <summary>Native field <c>Value</c>.</summary>
    public GhosttyQuickTerminalSizeValue Value;
}

/// <summary>Quick terminal size configuration (primary + secondary).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyConfigQuickTerminalSize
{
    /// <summary>Native field <c>Primary</c>.</summary>
    public GhosttyQuickTerminalSize Primary;
    /// <summary>Native field <c>Secondary</c>.</summary>
    public GhosttyQuickTerminalSize Secondary;
}

/// <summary>Action target union.</summary>
[StructLayout(LayoutKind.Explicit)]
public struct GhosttyTargetUnion
{
    /// <summary>Native field <c>Surface</c>.</summary>
    [FieldOffset(0)] public nint Surface;
}

/// <summary>Action target (app or surface).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyTarget
{
    /// <summary>Native field <c>Tag</c>.</summary>
    public GhosttyTargetTag Tag;
    /// <summary>Native field <c>Target</c>.</summary>
    public GhosttyTargetUnion Target;
}

/// <summary>Resize split action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyResizeSplit
{
    /// <summary>Native field <c>Amount</c>.</summary>
    public ushort Amount;
    /// <summary>Native field <c>Direction</c>.</summary>
    public GhosttyResizeSplitDirection Direction;
}

/// <summary>Move tab action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyMoveTab
{
    /// <summary>Native field <c>Amount</c>.</summary>
    public nint Amount;
}

/// <summary>Size limit action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttySizeLimit
{
    /// <summary>Native field <c>MinWidth</c>.</summary>
    public uint MinWidth;
    /// <summary>Native field <c>MinHeight</c>.</summary>
    public uint MinHeight;
    /// <summary>Native field <c>MaxWidth</c>.</summary>
    public uint MaxWidth;
    /// <summary>Native field <c>MaxHeight</c>.</summary>
    public uint MaxHeight;
}

/// <summary>Initial size action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyInitialSize
{
    /// <summary>Native field <c>Width</c>.</summary>
    public uint Width;
    /// <summary>Native field <c>Height</c>.</summary>
    public uint Height;
}

/// <summary>Cell size action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyCellSize
{
    /// <summary>Native field <c>Width</c>.</summary>
    public uint Width;
    /// <summary>Native field <c>Height</c>.</summary>
    public uint Height;
}

/// <summary>Desktop notification action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyDesktopNotification
{
    /// <summary>Native field <c>Title</c>.</summary>
    public byte* Title;
    /// <summary>Native field <c>Body</c>.</summary>
    public byte* Body;
}

/// <summary>Set title action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttySetTitle
{
    /// <summary>Native field <c>Title</c>.</summary>
    public byte* Title;
}

/// <summary>PWD action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyPwd
{
    /// <summary>Native field <c>PwdPtr</c>.</summary>
    public byte* PwdPtr;
}

/// <summary>Mouse over link action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyMouseOverLink
{
    /// <summary>Native field <c>Url</c>.</summary>
    public byte* Url;
    /// <summary>Native field <c>Len</c>.</summary>
    public nuint Len;
}

/// <summary>Key sequence action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyKeySequence
{
    /// <summary>Native field <c>Active</c>.</summary>
    public bool Active;
    /// <summary>Native field <c>Trigger</c>.</summary>
    public GhosttyInputTrigger Trigger;
}

/// <summary>Key table value union.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyKeyTableActivate
{
    /// <summary>Native field <c>Name</c>.</summary>
    public byte* Name;
    /// <summary>Native field <c>Len</c>.</summary>
    public nuint Len;
}

/// <summary>Key table action union.</summary>
[StructLayout(LayoutKind.Explicit)]
public struct GhosttyKeyTableValue
{
    /// <summary>Native field <c>Activate</c>.</summary>
    [FieldOffset(0)] public GhosttyKeyTableActivate Activate;
}

/// <summary>Key table action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyKeyTable
{
    /// <summary>Native field <c>Tag</c>.</summary>
    public GhosttyKeyTableTag Tag;
    /// <summary>Native field <c>Value</c>.</summary>
    public GhosttyKeyTableValue Value;
}

/// <summary>Color change action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyColorChange
{
    /// <summary>Native field <c>Kind</c>.</summary>
    public GhosttyColorKind Kind;
    /// <summary>Native field <c>R</c>.</summary>
    public byte R;
    /// <summary>Native field <c>G</c>.</summary>
    public byte G;
    /// <summary>Native field <c>B</c>.</summary>
    public byte B;
}

/// <summary>Config change action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyConfigChange
{
    /// <summary>Native field <c>Config</c>.</summary>
    public nint Config;
}

/// <summary>Reload config action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyReloadConfig
{
    /// <summary>Native field <c>Soft</c>.</summary>
    public bool Soft;
}

/// <summary>Open URL action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyOpenUrl
{
    /// <summary>Native field <c>Kind</c>.</summary>
    public GhosttyOpenUrlKind Kind;
    /// <summary>Native field <c>Url</c>.</summary>
    public byte* Url;
    /// <summary>Native field <c>Len</c>.</summary>
    public nuint Len;
}

/// <summary>Child exited message data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyChildExited
{
    /// <summary>Native field <c>ExitCode</c>.</summary>
    public uint ExitCode;
    /// <summary>Native field <c>TimetimeMs</c>.</summary>
    public ulong TimetimeMs;
}

/// <summary>Progress report action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyProgressReport
{
    /// <summary>Native field <c>State</c>.</summary>
    public GhosttyProgressState State;
    /// <summary>Native field <c>Progress</c>.</summary>
    public sbyte Progress;
}

/// <summary>Command finished action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyCommandFinished
{
    /// <summary>Native field <c>ExitCode</c>.</summary>
    public short ExitCode;
    /// <summary>Native field <c>Duration</c>.</summary>
    public ulong Duration;
}

/// <summary>Start search action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyStartSearch
{
    /// <summary>Native field <c>Needle</c>.</summary>
    public byte* Needle;
}

/// <summary>Search total action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttySearchTotal
{
    /// <summary>Native field <c>Total</c>.</summary>
    public nint Total;
}

/// <summary>Search selected action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttySearchSelected
{
    /// <summary>Native field <c>Selected</c>.</summary>
    public nint Selected;
}

/// <summary>Scrollbar state.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyScrollbar
{
    /// <summary>Native field <c>Total</c>.</summary>
    public ulong Total;
    /// <summary>Native field <c>Offset</c>.</summary>
    public ulong Offset;
    /// <summary>Native field <c>Len</c>.</summary>
    public ulong Len;
}

/// <summary>Action value union containing all possible action payloads.</summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct GhosttyActionValue
{
    /// <summary>Native field <c>NewSplit</c>.</summary>
    [FieldOffset(0)] public GhosttySplitDirection NewSplit;
    /// <summary>Native field <c>ToggleFullscreen</c>.</summary>
    [FieldOffset(0)] public GhosttyFullscreen ToggleFullscreen;
    /// <summary>Native field <c>MoveTab</c>.</summary>
    [FieldOffset(0)] public GhosttyMoveTab MoveTab;
    /// <summary>Native field <c>GotoTab</c>.</summary>
    [FieldOffset(0)] public GhosttyGotoTab GotoTab;
    /// <summary>Native field <c>GotoSplit</c>.</summary>
    [FieldOffset(0)] public GhosttyGotoSplit GotoSplit;
    /// <summary>Native field <c>GotoWindow</c>.</summary>
    [FieldOffset(0)] public GhosttyGotoWindow GotoWindow;
    /// <summary>Native field <c>ResizeSplit</c>.</summary>
    [FieldOffset(0)] public GhosttyResizeSplit ResizeSplit;
    /// <summary>Native field <c>SizeLimit</c>.</summary>
    [FieldOffset(0)] public GhosttySizeLimit SizeLimit;
    /// <summary>Native field <c>InitialSize</c>.</summary>
    [FieldOffset(0)] public GhosttyInitialSize InitialSize;
    /// <summary>Native field <c>CellSize</c>.</summary>
    [FieldOffset(0)] public GhosttyCellSize CellSize;
    /// <summary>Native field <c>Scrollbar</c>.</summary>
    [FieldOffset(0)] public GhosttyScrollbar Scrollbar;
    /// <summary>Native field <c>Inspector</c>.</summary>
    [FieldOffset(0)] public GhosttyInspectorAction Inspector;
    /// <summary>Native field <c>DesktopNotification</c>.</summary>
    [FieldOffset(0)] public GhosttyDesktopNotification DesktopNotification;
    /// <summary>Native field <c>SetTitle</c>.</summary>
    [FieldOffset(0)] public GhosttySetTitle SetTitle;
    /// <summary>Native field <c>PromptTitle</c>.</summary>
    [FieldOffset(0)] public GhosttyPromptTitle PromptTitle;
    /// <summary>Native field <c>Pwd</c>.</summary>
    [FieldOffset(0)] public GhosttyPwd Pwd;
    /// <summary>Native field <c>MouseShape</c>.</summary>
    [FieldOffset(0)] public GhosttyMouseShape MouseShape;
    /// <summary>Native field <c>MouseVisibility</c>.</summary>
    [FieldOffset(0)] public GhosttyMouseVisibility MouseVisibility;
    /// <summary>Native field <c>MouseOverLink</c>.</summary>
    [FieldOffset(0)] public GhosttyMouseOverLink MouseOverLink;
    /// <summary>Native field <c>RendererHealth</c>.</summary>
    [FieldOffset(0)] public GhosttyRendererHealth RendererHealth;
    /// <summary>Native field <c>QuitTimerAction</c>.</summary>
    [FieldOffset(0)] public GhosttyQuitTimer QuitTimerAction;
    /// <summary>Native field <c>FloatWindow</c>.</summary>
    [FieldOffset(0)] public GhosttyFloatWindow FloatWindow;
    /// <summary>Native field <c>SecureInput</c>.</summary>
    [FieldOffset(0)] public GhosttySecureInput SecureInput;
    /// <summary>Native field <c>KeySequence</c>.</summary>
    [FieldOffset(0)] public GhosttyKeySequence KeySequence;
    /// <summary>Native field <c>KeyTableAction</c>.</summary>
    [FieldOffset(0)] public GhosttyKeyTable KeyTableAction;
    /// <summary>Native field <c>ColorChange</c>.</summary>
    [FieldOffset(0)] public GhosttyColorChange ColorChange;
    /// <summary>Native field <c>ReloadConfig</c>.</summary>
    [FieldOffset(0)] public GhosttyReloadConfig ReloadConfig;
    /// <summary>Native field <c>ConfigChange</c>.</summary>
    [FieldOffset(0)] public GhosttyConfigChange ConfigChange;
    /// <summary>Native field <c>OpenUrl</c>.</summary>
    [FieldOffset(0)] public GhosttyOpenUrl OpenUrl;
    /// <summary>Native field <c>CloseTabMode</c>.</summary>
    [FieldOffset(0)] public GhosttyCloseTabMode CloseTabMode;
    /// <summary>Native field <c>ChildExited</c>.</summary>
    [FieldOffset(0)] public GhosttyChildExited ChildExited;
    /// <summary>Native field <c>ProgressReport</c>.</summary>
    [FieldOffset(0)] public GhosttyProgressReport ProgressReport;
    /// <summary>Native field <c>CommandFinished</c>.</summary>
    [FieldOffset(0)] public GhosttyCommandFinished CommandFinished;
    /// <summary>Native field <c>StartSearch</c>.</summary>
    [FieldOffset(0)] public GhosttyStartSearch StartSearch;
    /// <summary>Native field <c>SearchTotal</c>.</summary>
    [FieldOffset(0)] public GhosttySearchTotal SearchTotal;
    /// <summary>Native field <c>SearchSelected</c>.</summary>
    [FieldOffset(0)] public GhosttySearchSelected SearchSelected;
    /// <summary>Native field <c>Readonly</c>.</summary>
    [FieldOffset(0)] public GhosttyReadonly Readonly;
}

/// <summary>Action dispatched via runtime callback.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyAction
{
    /// <summary>Native field <c>Tag</c>.</summary>
    public GhosttyActionTag Tag;
    /// <summary>Native field <c>Action</c>.</summary>
    public GhosttyActionValue Action;
}

/// <summary>Runtime configuration with callback function pointers.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyRuntimeConfig
{
    /// <summary>Native field <c>Userdata</c>.</summary>
    public nint Userdata;
    /// <summary>Native field <c>SupportsSelectionClipboard</c>.</summary>
    public bool SupportsSelectionClipboard;
    /// <summary>Native field <c>WakeupCb</c>.</summary>
    public delegate* unmanaged[Cdecl]<nint, void> WakeupCb;
    /// <summary>Native field <c>ActionCb</c>.</summary>
    public delegate* unmanaged[Cdecl]<nint, GhosttyTarget, GhosttyAction, byte> ActionCb;
    /// <summary>Native field <c>ReadClipboardCb</c>.</summary>
    public delegate* unmanaged[Cdecl]<nint, GhosttyClipboard, nint, void> ReadClipboardCb;
    /// <summary>Native field <c>ConfirmReadClipboardCb</c>.</summary>
    public delegate* unmanaged[Cdecl]<nint, nint, nint, GhosttyClipboardRequest, void> ConfirmReadClipboardCb;
    /// <summary>Native field <c>WriteClipboardCb</c>.</summary>
    public delegate* unmanaged[Cdecl]<nint, GhosttyClipboard, nint, nuint, byte, void> WriteClipboardCb;
    /// <summary>Native field <c>CloseSurfaceCb</c>.</summary>
    public delegate* unmanaged[Cdecl]<nint, byte, void> CloseSurfaceCb;
}
