// Licensed under the MIT License.
// GhosttySharp - .NET bindings for the Ghostty terminal emulator.

using System.Runtime.InteropServices;

namespace GhosttySharp.Native;

/// <summary>Clipboard content with MIME type and data.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyClipboardContent
{
    public byte* Mime;
    public byte* Data;
}

/// <summary>Key input event.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyInputKey
{
    public GhosttyInputAction Action;
    public GhosttyMods Mods;
    public GhosttyMods ConsumedMods;
    public uint Keycode;
    public byte* Text;
    public uint UnshiftedCodepoint;
    public bool Composing;
}

/// <summary>Input trigger key union.</summary>
[StructLayout(LayoutKind.Explicit)]
public struct GhosttyInputTriggerKey
{
    [FieldOffset(0)] public GhosttyKey Translated;
    [FieldOffset(0)] public GhosttyKey Physical;
    [FieldOffset(0)] public uint Unicode;
}

/// <summary>Input trigger for key binding matching.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyInputTrigger
{
    public GhosttyInputTriggerTag Tag;
    public GhosttyInputTriggerKey Key;
    public GhosttyMods Mods;
}

/// <summary>Command descriptor for configuration.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyCommand
{
    public byte* ActionKey;
    public byte* Action;
    public byte* Title;
    public byte* Description;
}

/// <summary>Build/version information.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyInfo
{
    public GhosttyBuildMode BuildMode;
    public byte* Version;
    public nuint VersionLen;
}

/// <summary>Diagnostic message from configuration.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyDiagnostic
{
    public byte* Message;
}

/// <summary>Ghostty-owned string with length.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyString
{
    public byte* Ptr;
    public nuint Len;
    public bool Sentinel;
}

/// <summary>Text data from terminal surface.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyText
{
    public double TlPxX;
    public double TlPxY;
    public uint OffsetStart;
    public uint OffsetLen;
    public byte* TextPtr;
    public nuint TextLen;
}

/// <summary>Point in terminal coordinate space.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyPoint
{
    public GhosttyPointTag Tag;
    public GhosttyPointCoord Coord;
    public uint X;
    public uint Y;
}

/// <summary>Selection range in terminal.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttySelection
{
    public GhosttyPoint TopLeft;
    public GhosttyPoint BottomRight;
    public bool Rectangle;
}

/// <summary>Environment variable key-value pair.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyEnvVar
{
    public byte* Key;
    public byte* Value;
}

/// <summary>macOS platform-specific data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyPlatformMacOS
{
    public nint NSView;
}

/// <summary>iOS platform-specific data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyPlatformIOS
{
    public nint UIView;
}

/// <summary>Platform-specific union.</summary>
[StructLayout(LayoutKind.Explicit)]
public struct GhosttyPlatformUnion
{
    [FieldOffset(0)] public GhosttyPlatformMacOS MacOS;
    [FieldOffset(0)] public GhosttyPlatformIOS IOS;
}

/// <summary>Surface creation configuration.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttySurfaceConfig
{
    public GhosttyPlatform PlatformTag;
    public GhosttyPlatformUnion Platform;
    public nint Userdata;
    public double ScaleFactor;
    public float FontSize;
    public byte* WorkingDirectory;
    public byte* Command;
    public GhosttyEnvVar* EnvVars;
    public nuint EnvVarCount;
    public byte* InitialInput;
    public bool WaitAfterCommand;
    public GhosttySurfaceContext Context;
}

/// <summary>Surface size information.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttySurfaceSize
{
    public ushort Columns;
    public ushort Rows;
    public uint WidthPx;
    public uint HeightPx;
    public uint CellWidthPx;
    public uint CellHeightPx;
}

/// <summary>RGB color.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyConfigColor
{
    public byte R;
    public byte G;
    public byte B;
}

/// <summary>Color list.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyConfigColorList
{
    public GhosttyConfigColor* Colors;
    public nuint Len;
}

/// <summary>Command list.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyConfigCommandList
{
    public GhosttyCommand* Commands;
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
    [FieldOffset(0)] public float Percentage;
    [FieldOffset(0)] public uint Pixels;
}

/// <summary>Quick terminal size.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyQuickTerminalSize
{
    public GhosttyQuickTerminalSizeTag Tag;
    public GhosttyQuickTerminalSizeValue Value;
}

/// <summary>Quick terminal size configuration (primary + secondary).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyConfigQuickTerminalSize
{
    public GhosttyQuickTerminalSize Primary;
    public GhosttyQuickTerminalSize Secondary;
}

/// <summary>Action target union.</summary>
[StructLayout(LayoutKind.Explicit)]
public struct GhosttyTargetUnion
{
    [FieldOffset(0)] public nint Surface;
}

/// <summary>Action target (app or surface).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyTarget
{
    public GhosttyTargetTag Tag;
    public GhosttyTargetUnion Target;
}

/// <summary>Resize split action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyResizeSplit
{
    public ushort Amount;
    public GhosttyResizeSplitDirection Direction;
}

/// <summary>Move tab action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyMoveTab
{
    public nint Amount;
}

/// <summary>Size limit action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttySizeLimit
{
    public uint MinWidth;
    public uint MinHeight;
    public uint MaxWidth;
    public uint MaxHeight;
}

/// <summary>Initial size action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyInitialSize
{
    public uint Width;
    public uint Height;
}

/// <summary>Cell size action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyCellSize
{
    public uint Width;
    public uint Height;
}

/// <summary>Desktop notification action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyDesktopNotification
{
    public byte* Title;
    public byte* Body;
}

/// <summary>Set title action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttySetTitle
{
    public byte* Title;
}

/// <summary>PWD action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyPwd
{
    public byte* PwdPtr;
}

/// <summary>Mouse over link action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyMouseOverLink
{
    public byte* Url;
    public nuint Len;
}

/// <summary>Key sequence action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyKeySequence
{
    public bool Active;
    public GhosttyInputTrigger Trigger;
}

/// <summary>Key table value union.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyKeyTableActivate
{
    public byte* Name;
    public nuint Len;
}

/// <summary>Key table action union.</summary>
[StructLayout(LayoutKind.Explicit)]
public struct GhosttyKeyTableValue
{
    [FieldOffset(0)] public GhosttyKeyTableActivate Activate;
}

/// <summary>Key table action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyKeyTable
{
    public GhosttyKeyTableTag Tag;
    public GhosttyKeyTableValue Value;
}

/// <summary>Color change action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyColorChange
{
    public GhosttyColorKind Kind;
    public byte R;
    public byte G;
    public byte B;
}

/// <summary>Config change action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyConfigChange
{
    public nint Config;
}

/// <summary>Reload config action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyReloadConfig
{
    public bool Soft;
}

/// <summary>Open URL action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyOpenUrl
{
    public GhosttyOpenUrlKind Kind;
    public byte* Url;
    public nuint Len;
}

/// <summary>Child exited message data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyChildExited
{
    public uint ExitCode;
    public ulong TimetimeMs;
}

/// <summary>Progress report action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyProgressReport
{
    public GhosttyProgressState State;
    public sbyte Progress;
}

/// <summary>Command finished action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyCommandFinished
{
    public short ExitCode;
    public ulong Duration;
}

/// <summary>Start search action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyStartSearch
{
    public byte* Needle;
}

/// <summary>Search total action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttySearchTotal
{
    public nint Total;
}

/// <summary>Search selected action data.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttySearchSelected
{
    public nint Selected;
}

/// <summary>Scrollbar state.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyScrollbar
{
    public ulong Total;
    public ulong Offset;
    public ulong Len;
}

/// <summary>Action value union containing all possible action payloads.</summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct GhosttyActionValue
{
    [FieldOffset(0)] public GhosttySplitDirection NewSplit;
    [FieldOffset(0)] public GhosttyFullscreen ToggleFullscreen;
    [FieldOffset(0)] public GhosttyMoveTab MoveTab;
    [FieldOffset(0)] public GhosttyGotoTab GotoTab;
    [FieldOffset(0)] public GhosttyGotoSplit GotoSplit;
    [FieldOffset(0)] public GhosttyGotoWindow GotoWindow;
    [FieldOffset(0)] public GhosttyResizeSplit ResizeSplit;
    [FieldOffset(0)] public GhosttySizeLimit SizeLimit;
    [FieldOffset(0)] public GhosttyInitialSize InitialSize;
    [FieldOffset(0)] public GhosttyCellSize CellSize;
    [FieldOffset(0)] public GhosttyScrollbar Scrollbar;
    [FieldOffset(0)] public GhosttyInspectorAction Inspector;
    [FieldOffset(0)] public GhosttyDesktopNotification DesktopNotification;
    [FieldOffset(0)] public GhosttySetTitle SetTitle;
    [FieldOffset(0)] public GhosttyPromptTitle PromptTitle;
    [FieldOffset(0)] public GhosttyPwd Pwd;
    [FieldOffset(0)] public GhosttyMouseShape MouseShape;
    [FieldOffset(0)] public GhosttyMouseVisibility MouseVisibility;
    [FieldOffset(0)] public GhosttyMouseOverLink MouseOverLink;
    [FieldOffset(0)] public GhosttyRendererHealth RendererHealth;
    [FieldOffset(0)] public GhosttyQuitTimer QuitTimerAction;
    [FieldOffset(0)] public GhosttyFloatWindow FloatWindow;
    [FieldOffset(0)] public GhosttySecureInput SecureInput;
    [FieldOffset(0)] public GhosttyKeySequence KeySequence;
    [FieldOffset(0)] public GhosttyKeyTable KeyTableAction;
    [FieldOffset(0)] public GhosttyColorChange ColorChange;
    [FieldOffset(0)] public GhosttyReloadConfig ReloadConfig;
    [FieldOffset(0)] public GhosttyConfigChange ConfigChange;
    [FieldOffset(0)] public GhosttyOpenUrl OpenUrl;
    [FieldOffset(0)] public GhosttyCloseTabMode CloseTabMode;
    [FieldOffset(0)] public GhosttyChildExited ChildExited;
    [FieldOffset(0)] public GhosttyProgressReport ProgressReport;
    [FieldOffset(0)] public GhosttyCommandFinished CommandFinished;
    [FieldOffset(0)] public GhosttyStartSearch StartSearch;
    [FieldOffset(0)] public GhosttySearchTotal SearchTotal;
    [FieldOffset(0)] public GhosttySearchSelected SearchSelected;
    [FieldOffset(0)] public GhosttyReadonly Readonly;
}

/// <summary>Action dispatched via runtime callback.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyAction
{
    public GhosttyActionTag Tag;
    public GhosttyActionValue Action;
}

/// <summary>Runtime configuration with callback function pointers.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyRuntimeConfig
{
    public nint Userdata;
    public bool SupportsSelectionClipboard;
    public delegate* unmanaged[Cdecl]<nint, void> WakeupCb;
    public delegate* unmanaged[Cdecl]<nint, GhosttyTarget, GhosttyAction, byte> ActionCb;
    public delegate* unmanaged[Cdecl]<nint, GhosttyClipboard, nint, void> ReadClipboardCb;
    public delegate* unmanaged[Cdecl]<nint, nint, nint, GhosttyClipboardRequest, void> ConfirmReadClipboardCb;
    public delegate* unmanaged[Cdecl]<nint, GhosttyClipboard, nint, nuint, byte, void> WriteClipboardCb;
    public delegate* unmanaged[Cdecl]<nint, byte, void> CloseSurfaceCb;
}

/// <summary>Cell information for custom rendering. Contains resolved colors and attributes.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyCellInfo
{
    /// <summary>UTF-32 codepoint (0 for empty cells).</summary>
    public uint Codepoint;

    /// <summary>Resolved foreground color R.</summary>
    public byte FgR;
    /// <summary>Resolved foreground color G.</summary>
    public byte FgG;
    /// <summary>Resolved foreground color B.</summary>
    public byte FgB;

    /// <summary>Resolved background color R.</summary>
    public byte BgR;
    /// <summary>Resolved background color G.</summary>
    public byte BgG;
    /// <summary>Resolved background color B.</summary>
    public byte BgB;

    /// <summary>Whether background was explicitly set (vs default).</summary>
    public byte HasBg;

    /// <summary>
    /// Style attribute flags (packed from Ghostty's style.Flags).
    /// Bit 0: Bold, 1: Italic, 2: Faint, 3: Blink, 4: Inverse,
    /// 5: Invisible, 6: Strikethrough, 7: Overline, 8+: Underline.
    /// </summary>
    public ushort Attrs;

    /// <summary>Cell width: 0=narrow, 1=wide, 2=spacer_tail, 3=spacer_head.</summary>
    public byte Wide;
}

/// <summary>Cursor position and style information.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyCursorInfo
{
    /// <summary>Cursor column (0-based).</summary>
    public ushort X;
    /// <summary>Cursor row (0-based).</summary>
    public ushort Y;
    /// <summary>Cursor style (block, bar, underline, etc.).</summary>
    public byte Style;
    /// <summary>Whether cursor is visible.</summary>
    public byte Visible;
}
