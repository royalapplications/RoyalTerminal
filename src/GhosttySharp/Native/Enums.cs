// Licensed under the MIT License.
// GhosttySharp - .NET bindings for the Ghostty terminal emulator.

namespace GhosttySharp.Native;

/// <summary>Platform identifier for surface creation.</summary>
public enum GhosttyPlatform
{
    Invalid = 0,
    MacOS,
    IOS,
}

/// <summary>Clipboard type.</summary>
public enum GhosttyClipboard
{
    Standard = 0,
    Selection,
}

/// <summary>Clipboard request type for OSC 52 and paste operations.</summary>
public enum GhosttyClipboardRequest
{
    Paste = 0,
    Osc52Read,
    Osc52Write,
}

/// <summary>Mouse button press/release state.</summary>
public enum GhosttyMouseState
{
    Release = 0,
    Press,
}

/// <summary>Mouse button identifier.</summary>
public enum GhosttyMouseButton
{
    Unknown = 0,
    Left,
    Right,
    Middle,
    Four,
    Five,
    Six,
    Seven,
    Eight,
    Nine,
    Ten,
    Eleven,
}

/// <summary>Mouse scroll momentum phase (macOS trackpad).</summary>
public enum GhosttyMouseMomentum
{
    None = 0,
    Began,
    Stationary,
    Changed,
    Ended,
    Cancelled,
    MayBegin,
}

/// <summary>Color scheme preference.</summary>
public enum GhosttyColorScheme
{
    Light = 0,
    Dark = 1,
}

/// <summary>Input modifier flags.</summary>
[Flags]
public enum GhosttyMods
{
    None = 0,
    Shift = 1 << 0,
    Ctrl = 1 << 1,
    Alt = 1 << 2,
    Super = 1 << 3,
    Caps = 1 << 4,
    Num = 1 << 5,
    ShiftRight = 1 << 6,
    CtrlRight = 1 << 7,
    AltRight = 1 << 8,
    SuperRight = 1 << 9,
}

/// <summary>Key binding flags.</summary>
[Flags]
public enum GhosttyBindingFlags
{
    Consumed = 1 << 0,
    All = 1 << 1,
    Global = 1 << 2,
    Performable = 1 << 3,
}

/// <summary>Input action type (press, release, repeat).</summary>
public enum GhosttyInputAction
{
    Release = 0,
    Press,
    Repeat,
}

/// <summary>Keyboard key codes based on W3C UI Events Code specification.</summary>
public enum GhosttyKey
{
    Unidentified = 0,

    // Writing System Keys § 3.1.1
    Backquote,
    Backslash,
    BracketLeft,
    BracketRight,
    Comma,
    Digit0,
    Digit1,
    Digit2,
    Digit3,
    Digit4,
    Digit5,
    Digit6,
    Digit7,
    Digit8,
    Digit9,
    Equal,
    IntlBackslash,
    IntlRo,
    IntlYen,
    A,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,
    Minus,
    Period,
    Quote,
    Semicolon,
    Slash,

    // Functional Keys § 3.1.2
    AltLeft,
    AltRight,
    Backspace,
    CapsLock,
    ContextMenu,
    ControlLeft,
    ControlRight,
    Enter,
    MetaLeft,
    MetaRight,
    ShiftLeft,
    ShiftRight,
    Space,
    Tab,
    Convert,
    KanaMode,
    NonConvert,

    // Control Pad Section § 3.2
    Delete,
    End,
    Help,
    Home,
    Insert,
    PageDown,
    PageUp,

    // Arrow Pad Section § 3.3
    ArrowDown,
    ArrowLeft,
    ArrowRight,
    ArrowUp,

    // Numpad Section § 3.4
    NumLock,
    Numpad0,
    Numpad1,
    Numpad2,
    Numpad3,
    Numpad4,
    Numpad5,
    Numpad6,
    Numpad7,
    Numpad8,
    Numpad9,
    NumpadAdd,
    NumpadBackspace,
    NumpadClear,
    NumpadClearEntry,
    NumpadComma,
    NumpadDecimal,
    NumpadDivide,
    NumpadEnter,
    NumpadEqual,
    NumpadMemoryAdd,
    NumpadMemoryClear,
    NumpadMemoryRecall,
    NumpadMemoryStore,
    NumpadMemorySubtract,
    NumpadMultiply,
    NumpadParenLeft,
    NumpadParenRight,
    NumpadSubtract,
    NumpadSeparator,
    NumpadUp,
    NumpadDown,
    NumpadRight,
    NumpadLeft,
    NumpadBegin,
    NumpadHome,
    NumpadEnd,
    NumpadInsert,
    NumpadDelete,
    NumpadPageUp,
    NumpadPageDown,

    // Function Section § 3.5
    Escape,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
    F13,
    F14,
    F15,
    F16,
    F17,
    F18,
    F19,
    F20,
    F21,
    F22,
    F23,
    F24,
    F25,
    Fn,
    FnLock,
    PrintScreen,
    ScrollLock,
    Pause,

    // Media Keys § 3.6
    BrowserBack,
    BrowserFavorites,
    BrowserForward,
    BrowserHome,
    BrowserRefresh,
    BrowserSearch,
    BrowserStop,
    Eject,
    LaunchApp1,
    LaunchApp2,
    LaunchMail,
    MediaPlayPause,
    MediaSelect,
    MediaStop,
    MediaTrackNext,
    MediaTrackPrevious,
    Power,
    Sleep,
    AudioVolumeDown,
    AudioVolumeMute,
    AudioVolumeUp,
    WakeUp,

    // Legacy, Non-standard, and Special Keys § 3.7
    Copy,
    Cut,
    Paste,
}

/// <summary>Input trigger tag for key binding matching.</summary>
public enum GhosttyInputTriggerTag
{
    Physical = 0,
    Unicode,
    CatchAll,
}

/// <summary>Build mode of the Ghostty library.</summary>
public enum GhosttyBuildMode
{
    Debug = 0,
    ReleaseSafe,
    ReleaseFast,
    ReleaseSmall,
}

/// <summary>Point tag for coordinate system selection.</summary>
public enum GhosttyPointTag
{
    Active = 0,
    Viewport,
    Screen,
    Surface,
}

/// <summary>Point coordinate mode.</summary>
public enum GhosttyPointCoord
{
    Exact = 0,
    TopLeft,
    BottomRight,
}

/// <summary>Surface context for new surfaces.</summary>
public enum GhosttySurfaceContext
{
    Window = 0,
    Tab = 1,
    Split = 2,
}

/// <summary>Action target type.</summary>
public enum GhosttyTargetTag
{
    App = 0,
    Surface,
}

/// <summary>Split direction for creating new splits.</summary>
public enum GhosttySplitDirection
{
    Right = 0,
    Down,
    Left,
    Up,
}

/// <summary>Direction for navigating between splits.</summary>
public enum GhosttyGotoSplit
{
    Previous = 0,
    Next,
    Up,
    Left,
    Down,
    Right,
}

/// <summary>Direction for navigating between windows.</summary>
public enum GhosttyGotoWindow
{
    Previous = 0,
    Next,
}

/// <summary>Direction for resizing splits.</summary>
public enum GhosttyResizeSplitDirection
{
    Up = 0,
    Down,
    Left,
    Right,
}

/// <summary>Tab navigation target.</summary>
public enum GhosttyGotoTab
{
    Previous = -1,
    Next = -2,
    Last = -3,
}

/// <summary>Fullscreen mode.</summary>
public enum GhosttyFullscreen
{
    Native = 0,
    NonNative,
    NonNativeVisibleMenu,
    NonNativePaddedNotch,
}

/// <summary>Float window state.</summary>
public enum GhosttyFloatWindow
{
    On = 0,
    Off,
    Toggle,
}

/// <summary>Secure input state.</summary>
public enum GhosttySecureInput
{
    On = 0,
    Off,
    Toggle,
}

/// <summary>Inspector visibility state.</summary>
public enum GhosttyInspectorAction
{
    Toggle = 0,
    Show,
    Hide,
}

/// <summary>Quit timer state.</summary>
public enum GhosttyQuitTimer
{
    Start = 0,
    Stop,
}

/// <summary>Terminal readonly state.</summary>
public enum GhosttyReadonly
{
    Off = 0,
    On,
}

/// <summary>Title prompt target.</summary>
public enum GhosttyPromptTitle
{
    Surface = 0,
    Tab,
}

/// <summary>Terminal mouse cursor shape.</summary>
public enum GhosttyMouseShape
{
    Default = 0,
    ContextMenu,
    Help,
    Pointer,
    Progress,
    Wait,
    Cell,
    Crosshair,
    Text,
    VerticalText,
    Alias,
    Copy,
    Move,
    NoDrop,
    NotAllowed,
    Grab,
    Grabbing,
    AllScroll,
    ColResize,
    RowResize,
    NResize,
    EResize,
    SResize,
    WResize,
    NeResize,
    NwResize,
    SeResize,
    SwResize,
    EwResize,
    NsResize,
    NeswResize,
    NwseResize,
    ZoomIn,
    ZoomOut,
}

/// <summary>Mouse cursor visibility.</summary>
public enum GhosttyMouseVisibility
{
    Visible = 0,
    Hidden,
}

/// <summary>Renderer health status.</summary>
public enum GhosttyRendererHealth
{
    Ok = 0,
    Unhealthy,
}

/// <summary>Color change kind (foreground, background, cursor, or palette index).</summary>
public enum GhosttyColorKind
{
    Foreground = -1,
    Background = -2,
    Cursor = -3,
}

/// <summary>URL open kind.</summary>
public enum GhosttyOpenUrlKind
{
    Unknown = 0,
    Text,
    Html,
}

/// <summary>Close tab mode.</summary>
public enum GhosttyCloseTabMode
{
    This = 0,
    Other,
    Right,
}

/// <summary>Progress report state for OSC progress indication.</summary>
public enum GhosttyProgressState
{
    Remove = 0,
    Set,
    Error,
    Indeterminate,
    Pause,
}

/// <summary>Quick terminal size mode.</summary>
public enum GhosttyQuickTerminalSizeTag
{
    None = 0,
    Percentage,
    Pixels,
}

/// <summary>Key table action tag.</summary>
public enum GhosttyKeyTableTag
{
    Activate = 0,
    Deactivate,
    DeactivateAll,
}

/// <summary>Action tag identifying which action is being performed.</summary>
public enum GhosttyActionTag
{
    Quit = 0,
    NewWindow,
    NewTab,
    CloseTab,
    NewSplit,
    CloseAllWindows,
    ToggleMaximize,
    ToggleFullscreen,
    ToggleTabOverview,
    ToggleWindowDecorations,
    ToggleQuickTerminal,
    ToggleCommandPalette,
    ToggleVisibility,
    ToggleBackgroundOpacity,
    MoveTab,
    GotoTab,
    GotoSplit,
    GotoWindow,
    ResizeSplit,
    EqualizeSplits,
    ToggleSplitZoom,
    PresentTerminal,
    SizeLimit,
    ResetWindowSize,
    InitialSize,
    CellSize,
    Scrollbar,
    Render,
    Inspector,
    ShowGtkInspector,
    RenderInspector,
    DesktopNotification,
    SetTitle,
    PromptTitle,
    Pwd,
    MouseShape,
    MouseVisibility,
    MouseOverLink,
    RendererHealth,
    OpenConfig,
    QuitTimer,
    FloatWindow,
    SecureInput,
    KeySequence,
    KeyTable,
    ColorChange,
    ReloadConfig,
    ConfigChange,
    CloseWindow,
    RingBell,
    Undo,
    Redo,
    CheckForUpdates,
    OpenUrl,
    ShowChildExited,
    ProgressReport,
    ShowOnScreenKeyboard,
    CommandFinished,
    StartSearch,
    EndSearch,
    SearchTotal,
    SearchSelected,
    Readonly,
}

/// <summary>IPC target type.</summary>
public enum GhosttyIpcTargetTag
{
    Class = 0,
    Detect,
}

/// <summary>IPC action type.</summary>
public enum GhosttyIpcActionTag
{
    NewWindow = 0,
}
