// Licensed under the MIT License.
// GhosttySharp - .NET bindings for the Ghostty terminal emulator.

namespace GhosttySharp.Native;

/// <summary>Platform identifier for surface creation.</summary>
public enum GhosttyPlatform
{
    /// <summary><c>Invalid</c> enum value.</summary>
    Invalid = 0,
    /// <summary><c>MacOS</c> enum value.</summary>
    MacOS,
    /// <summary><c>IOS</c> enum value.</summary>
    IOS,
}

/// <summary>Clipboard type.</summary>
public enum GhosttyClipboard
{
    /// <summary><c>Standard</c> enum value.</summary>
    Standard = 0,
    /// <summary><c>Selection</c> enum value.</summary>
    Selection,
}

/// <summary>Clipboard request type for OSC 52 and paste operations.</summary>
public enum GhosttyClipboardRequest
{
    /// <summary><c>Paste</c> enum value.</summary>
    Paste = 0,
    /// <summary><c>Osc52Read</c> enum value.</summary>
    Osc52Read,
    /// <summary><c>Osc52Write</c> enum value.</summary>
    Osc52Write,
}

/// <summary>Mouse button press/release state.</summary>
public enum GhosttyMouseState
{
    /// <summary><c>Release</c> enum value.</summary>
    Release = 0,
    /// <summary><c>Press</c> enum value.</summary>
    Press,
}

/// <summary>Mouse button identifier.</summary>
public enum GhosttyMouseButton
{
    /// <summary><c>Unknown</c> enum value.</summary>
    Unknown = 0,
    /// <summary><c>Left</c> enum value.</summary>
    Left,
    /// <summary><c>Right</c> enum value.</summary>
    Right,
    /// <summary><c>Middle</c> enum value.</summary>
    Middle,
    /// <summary><c>Four</c> enum value.</summary>
    Four,
    /// <summary><c>Five</c> enum value.</summary>
    Five,
    /// <summary><c>Six</c> enum value.</summary>
    Six,
    /// <summary><c>Seven</c> enum value.</summary>
    Seven,
    /// <summary><c>Eight</c> enum value.</summary>
    Eight,
    /// <summary><c>Nine</c> enum value.</summary>
    Nine,
    /// <summary><c>Ten</c> enum value.</summary>
    Ten,
    /// <summary><c>Eleven</c> enum value.</summary>
    Eleven,
}

/// <summary>Mouse scroll momentum phase (macOS trackpad).</summary>
public enum GhosttyMouseMomentum
{
    /// <summary><c>None</c> enum value.</summary>
    None = 0,
    /// <summary><c>Began</c> enum value.</summary>
    Began,
    /// <summary><c>Stationary</c> enum value.</summary>
    Stationary,
    /// <summary><c>Changed</c> enum value.</summary>
    Changed,
    /// <summary><c>Ended</c> enum value.</summary>
    Ended,
    /// <summary><c>Cancelled</c> enum value.</summary>
    Cancelled,
    /// <summary><c>MayBegin</c> enum value.</summary>
    MayBegin,
}

/// <summary>Color scheme preference.</summary>
public enum GhosttyColorScheme
{
    /// <summary><c>Light</c> enum value.</summary>
    Light = 0,
    /// <summary><c>Dark</c> enum value.</summary>
    Dark = 1,
}

/// <summary>Input modifier flags.</summary>
[Flags]
public enum GhosttyMods
{
    /// <summary><c>None</c> enum value.</summary>
    None = 0,
    /// <summary><c>Shift</c> enum value.</summary>
    Shift = 1 << 0,
    /// <summary><c>Ctrl</c> enum value.</summary>
    Ctrl = 1 << 1,
    /// <summary><c>Alt</c> enum value.</summary>
    Alt = 1 << 2,
    /// <summary><c>Super</c> enum value.</summary>
    Super = 1 << 3,
    /// <summary><c>Caps</c> enum value.</summary>
    Caps = 1 << 4,
    /// <summary><c>Num</c> enum value.</summary>
    Num = 1 << 5,
    /// <summary><c>ShiftRight</c> enum value.</summary>
    ShiftRight = 1 << 6,
    /// <summary><c>CtrlRight</c> enum value.</summary>
    CtrlRight = 1 << 7,
    /// <summary><c>AltRight</c> enum value.</summary>
    AltRight = 1 << 8,
    /// <summary><c>SuperRight</c> enum value.</summary>
    SuperRight = 1 << 9,
}

/// <summary>Key binding flags.</summary>
[Flags]
public enum GhosttyBindingFlags
{
    /// <summary><c>Consumed</c> enum value.</summary>
    Consumed = 1 << 0,
    /// <summary><c>All</c> enum value.</summary>
    All = 1 << 1,
    /// <summary><c>Global</c> enum value.</summary>
    Global = 1 << 2,
    /// <summary><c>Performable</c> enum value.</summary>
    Performable = 1 << 3,
}

/// <summary>Input action type (press, release, repeat).</summary>
public enum GhosttyInputAction
{
    /// <summary><c>Release</c> enum value.</summary>
    Release = 0,
    /// <summary><c>Press</c> enum value.</summary>
    Press,
    /// <summary><c>Repeat</c> enum value.</summary>
    Repeat,
}

/// <summary>Keyboard key codes based on W3C UI Events Code specification.</summary>
public enum GhosttyKey
{
    /// <summary><c>Unidentified</c> enum value.</summary>
    Unidentified = 0,

    // Writing System Keys § 3.1.1
    /// <summary><c>Backquote</c> enum value.</summary>
    Backquote,
    /// <summary><c>Backslash</c> enum value.</summary>
    Backslash,
    /// <summary><c>BracketLeft</c> enum value.</summary>
    BracketLeft,
    /// <summary><c>BracketRight</c> enum value.</summary>
    BracketRight,
    /// <summary><c>Comma</c> enum value.</summary>
    Comma,
    /// <summary><c>Digit0</c> enum value.</summary>
    Digit0,
    /// <summary><c>Digit1</c> enum value.</summary>
    Digit1,
    /// <summary><c>Digit2</c> enum value.</summary>
    Digit2,
    /// <summary><c>Digit3</c> enum value.</summary>
    Digit3,
    /// <summary><c>Digit4</c> enum value.</summary>
    Digit4,
    /// <summary><c>Digit5</c> enum value.</summary>
    Digit5,
    /// <summary><c>Digit6</c> enum value.</summary>
    Digit6,
    /// <summary><c>Digit7</c> enum value.</summary>
    Digit7,
    /// <summary><c>Digit8</c> enum value.</summary>
    Digit8,
    /// <summary><c>Digit9</c> enum value.</summary>
    Digit9,
    /// <summary><c>Equal</c> enum value.</summary>
    Equal,
    /// <summary><c>IntlBackslash</c> enum value.</summary>
    IntlBackslash,
    /// <summary><c>IntlRo</c> enum value.</summary>
    IntlRo,
    /// <summary><c>IntlYen</c> enum value.</summary>
    IntlYen,
    /// <summary><c>A</c> enum value.</summary>
    A,
    /// <summary><c>B</c> enum value.</summary>
    B,
    /// <summary><c>C</c> enum value.</summary>
    C,
    /// <summary><c>D</c> enum value.</summary>
    D,
    /// <summary><c>E</c> enum value.</summary>
    E,
    /// <summary><c>F</c> enum value.</summary>
    F,
    /// <summary><c>G</c> enum value.</summary>
    G,
    /// <summary><c>H</c> enum value.</summary>
    H,
    /// <summary><c>I</c> enum value.</summary>
    I,
    /// <summary><c>J</c> enum value.</summary>
    J,
    /// <summary><c>K</c> enum value.</summary>
    K,
    /// <summary><c>L</c> enum value.</summary>
    L,
    /// <summary><c>M</c> enum value.</summary>
    M,
    /// <summary><c>N</c> enum value.</summary>
    N,
    /// <summary><c>O</c> enum value.</summary>
    O,
    /// <summary><c>P</c> enum value.</summary>
    P,
    /// <summary><c>Q</c> enum value.</summary>
    Q,
    /// <summary><c>R</c> enum value.</summary>
    R,
    /// <summary><c>S</c> enum value.</summary>
    S,
    /// <summary><c>T</c> enum value.</summary>
    T,
    /// <summary><c>U</c> enum value.</summary>
    U,
    /// <summary><c>V</c> enum value.</summary>
    V,
    /// <summary><c>W</c> enum value.</summary>
    W,
    /// <summary><c>X</c> enum value.</summary>
    X,
    /// <summary><c>Y</c> enum value.</summary>
    Y,
    /// <summary><c>Z</c> enum value.</summary>
    Z,
    /// <summary><c>Minus</c> enum value.</summary>
    Minus,
    /// <summary><c>Period</c> enum value.</summary>
    Period,
    /// <summary><c>Quote</c> enum value.</summary>
    Quote,
    /// <summary><c>Semicolon</c> enum value.</summary>
    Semicolon,
    /// <summary><c>Slash</c> enum value.</summary>
    Slash,

    // Functional Keys § 3.1.2
    /// <summary><c>AltLeft</c> enum value.</summary>
    AltLeft,
    /// <summary><c>AltRight</c> enum value.</summary>
    AltRight,
    /// <summary><c>Backspace</c> enum value.</summary>
    Backspace,
    /// <summary><c>CapsLock</c> enum value.</summary>
    CapsLock,
    /// <summary><c>ContextMenu</c> enum value.</summary>
    ContextMenu,
    /// <summary><c>ControlLeft</c> enum value.</summary>
    ControlLeft,
    /// <summary><c>ControlRight</c> enum value.</summary>
    ControlRight,
    /// <summary><c>Enter</c> enum value.</summary>
    Enter,
    /// <summary><c>MetaLeft</c> enum value.</summary>
    MetaLeft,
    /// <summary><c>MetaRight</c> enum value.</summary>
    MetaRight,
    /// <summary><c>ShiftLeft</c> enum value.</summary>
    ShiftLeft,
    /// <summary><c>ShiftRight</c> enum value.</summary>
    ShiftRight,
    /// <summary><c>Space</c> enum value.</summary>
    Space,
    /// <summary><c>Tab</c> enum value.</summary>
    Tab,
    /// <summary><c>Convert</c> enum value.</summary>
    Convert,
    /// <summary><c>KanaMode</c> enum value.</summary>
    KanaMode,
    /// <summary><c>NonConvert</c> enum value.</summary>
    NonConvert,

    // Control Pad Section § 3.2
    /// <summary><c>Delete</c> enum value.</summary>
    Delete,
    /// <summary><c>End</c> enum value.</summary>
    End,
    /// <summary><c>Help</c> enum value.</summary>
    Help,
    /// <summary><c>Home</c> enum value.</summary>
    Home,
    /// <summary><c>Insert</c> enum value.</summary>
    Insert,
    /// <summary><c>PageDown</c> enum value.</summary>
    PageDown,
    /// <summary><c>PageUp</c> enum value.</summary>
    PageUp,

    // Arrow Pad Section § 3.3
    /// <summary><c>ArrowDown</c> enum value.</summary>
    ArrowDown,
    /// <summary><c>ArrowLeft</c> enum value.</summary>
    ArrowLeft,
    /// <summary><c>ArrowRight</c> enum value.</summary>
    ArrowRight,
    /// <summary><c>ArrowUp</c> enum value.</summary>
    ArrowUp,

    // Numpad Section § 3.4
    /// <summary><c>NumLock</c> enum value.</summary>
    NumLock,
    /// <summary><c>Numpad0</c> enum value.</summary>
    Numpad0,
    /// <summary><c>Numpad1</c> enum value.</summary>
    Numpad1,
    /// <summary><c>Numpad2</c> enum value.</summary>
    Numpad2,
    /// <summary><c>Numpad3</c> enum value.</summary>
    Numpad3,
    /// <summary><c>Numpad4</c> enum value.</summary>
    Numpad4,
    /// <summary><c>Numpad5</c> enum value.</summary>
    Numpad5,
    /// <summary><c>Numpad6</c> enum value.</summary>
    Numpad6,
    /// <summary><c>Numpad7</c> enum value.</summary>
    Numpad7,
    /// <summary><c>Numpad8</c> enum value.</summary>
    Numpad8,
    /// <summary><c>Numpad9</c> enum value.</summary>
    Numpad9,
    /// <summary><c>NumpadAdd</c> enum value.</summary>
    NumpadAdd,
    /// <summary><c>NumpadBackspace</c> enum value.</summary>
    NumpadBackspace,
    /// <summary><c>NumpadClear</c> enum value.</summary>
    NumpadClear,
    /// <summary><c>NumpadClearEntry</c> enum value.</summary>
    NumpadClearEntry,
    /// <summary><c>NumpadComma</c> enum value.</summary>
    NumpadComma,
    /// <summary><c>NumpadDecimal</c> enum value.</summary>
    NumpadDecimal,
    /// <summary><c>NumpadDivide</c> enum value.</summary>
    NumpadDivide,
    /// <summary><c>NumpadEnter</c> enum value.</summary>
    NumpadEnter,
    /// <summary><c>NumpadEqual</c> enum value.</summary>
    NumpadEqual,
    /// <summary><c>NumpadMemoryAdd</c> enum value.</summary>
    NumpadMemoryAdd,
    /// <summary><c>NumpadMemoryClear</c> enum value.</summary>
    NumpadMemoryClear,
    /// <summary><c>NumpadMemoryRecall</c> enum value.</summary>
    NumpadMemoryRecall,
    /// <summary><c>NumpadMemoryStore</c> enum value.</summary>
    NumpadMemoryStore,
    /// <summary><c>NumpadMemorySubtract</c> enum value.</summary>
    NumpadMemorySubtract,
    /// <summary><c>NumpadMultiply</c> enum value.</summary>
    NumpadMultiply,
    /// <summary><c>NumpadParenLeft</c> enum value.</summary>
    NumpadParenLeft,
    /// <summary><c>NumpadParenRight</c> enum value.</summary>
    NumpadParenRight,
    /// <summary><c>NumpadSubtract</c> enum value.</summary>
    NumpadSubtract,
    /// <summary><c>NumpadSeparator</c> enum value.</summary>
    NumpadSeparator,
    /// <summary><c>NumpadUp</c> enum value.</summary>
    NumpadUp,
    /// <summary><c>NumpadDown</c> enum value.</summary>
    NumpadDown,
    /// <summary><c>NumpadRight</c> enum value.</summary>
    NumpadRight,
    /// <summary><c>NumpadLeft</c> enum value.</summary>
    NumpadLeft,
    /// <summary><c>NumpadBegin</c> enum value.</summary>
    NumpadBegin,
    /// <summary><c>NumpadHome</c> enum value.</summary>
    NumpadHome,
    /// <summary><c>NumpadEnd</c> enum value.</summary>
    NumpadEnd,
    /// <summary><c>NumpadInsert</c> enum value.</summary>
    NumpadInsert,
    /// <summary><c>NumpadDelete</c> enum value.</summary>
    NumpadDelete,
    /// <summary><c>NumpadPageUp</c> enum value.</summary>
    NumpadPageUp,
    /// <summary><c>NumpadPageDown</c> enum value.</summary>
    NumpadPageDown,

    // Function Section § 3.5
    /// <summary><c>Escape</c> enum value.</summary>
    Escape,
    /// <summary><c>F1</c> enum value.</summary>
    F1,
    /// <summary><c>F2</c> enum value.</summary>
    F2,
    /// <summary><c>F3</c> enum value.</summary>
    F3,
    /// <summary><c>F4</c> enum value.</summary>
    F4,
    /// <summary><c>F5</c> enum value.</summary>
    F5,
    /// <summary><c>F6</c> enum value.</summary>
    F6,
    /// <summary><c>F7</c> enum value.</summary>
    F7,
    /// <summary><c>F8</c> enum value.</summary>
    F8,
    /// <summary><c>F9</c> enum value.</summary>
    F9,
    /// <summary><c>F10</c> enum value.</summary>
    F10,
    /// <summary><c>F11</c> enum value.</summary>
    F11,
    /// <summary><c>F12</c> enum value.</summary>
    F12,
    /// <summary><c>F13</c> enum value.</summary>
    F13,
    /// <summary><c>F14</c> enum value.</summary>
    F14,
    /// <summary><c>F15</c> enum value.</summary>
    F15,
    /// <summary><c>F16</c> enum value.</summary>
    F16,
    /// <summary><c>F17</c> enum value.</summary>
    F17,
    /// <summary><c>F18</c> enum value.</summary>
    F18,
    /// <summary><c>F19</c> enum value.</summary>
    F19,
    /// <summary><c>F20</c> enum value.</summary>
    F20,
    /// <summary><c>F21</c> enum value.</summary>
    F21,
    /// <summary><c>F22</c> enum value.</summary>
    F22,
    /// <summary><c>F23</c> enum value.</summary>
    F23,
    /// <summary><c>F24</c> enum value.</summary>
    F24,
    /// <summary><c>F25</c> enum value.</summary>
    F25,
    /// <summary><c>Fn</c> enum value.</summary>
    Fn,
    /// <summary><c>FnLock</c> enum value.</summary>
    FnLock,
    /// <summary><c>PrintScreen</c> enum value.</summary>
    PrintScreen,
    /// <summary><c>ScrollLock</c> enum value.</summary>
    ScrollLock,
    /// <summary><c>Pause</c> enum value.</summary>
    Pause,

    // Media Keys § 3.6
    /// <summary><c>BrowserBack</c> enum value.</summary>
    BrowserBack,
    /// <summary><c>BrowserFavorites</c> enum value.</summary>
    BrowserFavorites,
    /// <summary><c>BrowserForward</c> enum value.</summary>
    BrowserForward,
    /// <summary><c>BrowserHome</c> enum value.</summary>
    BrowserHome,
    /// <summary><c>BrowserRefresh</c> enum value.</summary>
    BrowserRefresh,
    /// <summary><c>BrowserSearch</c> enum value.</summary>
    BrowserSearch,
    /// <summary><c>BrowserStop</c> enum value.</summary>
    BrowserStop,
    /// <summary><c>Eject</c> enum value.</summary>
    Eject,
    /// <summary><c>LaunchApp1</c> enum value.</summary>
    LaunchApp1,
    /// <summary><c>LaunchApp2</c> enum value.</summary>
    LaunchApp2,
    /// <summary><c>LaunchMail</c> enum value.</summary>
    LaunchMail,
    /// <summary><c>MediaPlayPause</c> enum value.</summary>
    MediaPlayPause,
    /// <summary><c>MediaSelect</c> enum value.</summary>
    MediaSelect,
    /// <summary><c>MediaStop</c> enum value.</summary>
    MediaStop,
    /// <summary><c>MediaTrackNext</c> enum value.</summary>
    MediaTrackNext,
    /// <summary><c>MediaTrackPrevious</c> enum value.</summary>
    MediaTrackPrevious,
    /// <summary><c>Power</c> enum value.</summary>
    Power,
    /// <summary><c>Sleep</c> enum value.</summary>
    Sleep,
    /// <summary><c>AudioVolumeDown</c> enum value.</summary>
    AudioVolumeDown,
    /// <summary><c>AudioVolumeMute</c> enum value.</summary>
    AudioVolumeMute,
    /// <summary><c>AudioVolumeUp</c> enum value.</summary>
    AudioVolumeUp,
    /// <summary><c>WakeUp</c> enum value.</summary>
    WakeUp,

    // Legacy, Non-standard, and Special Keys § 3.7
    /// <summary><c>Copy</c> enum value.</summary>
    Copy,
    /// <summary><c>Cut</c> enum value.</summary>
    Cut,
    /// <summary><c>Paste</c> enum value.</summary>
    Paste,
}

/// <summary>Input trigger tag for key binding matching.</summary>
public enum GhosttyInputTriggerTag
{
    /// <summary><c>Physical</c> enum value.</summary>
    Physical = 0,
    /// <summary><c>Unicode</c> enum value.</summary>
    Unicode,
    /// <summary><c>CatchAll</c> enum value.</summary>
    CatchAll,
}

/// <summary>Build mode of the Ghostty library.</summary>
public enum GhosttyBuildMode
{
    /// <summary><c>Debug</c> enum value.</summary>
    Debug = 0,
    /// <summary><c>ReleaseSafe</c> enum value.</summary>
    ReleaseSafe,
    /// <summary><c>ReleaseFast</c> enum value.</summary>
    ReleaseFast,
    /// <summary><c>ReleaseSmall</c> enum value.</summary>
    ReleaseSmall,
}

/// <summary>Point tag for coordinate system selection.</summary>
public enum GhosttyPointTag
{
    /// <summary><c>Active</c> enum value.</summary>
    Active = 0,
    /// <summary><c>Viewport</c> enum value.</summary>
    Viewport,
    /// <summary><c>Screen</c> enum value.</summary>
    Screen,
    /// <summary><c>Surface</c> enum value.</summary>
    Surface,
}

/// <summary>Point coordinate mode.</summary>
public enum GhosttyPointCoord
{
    /// <summary><c>Exact</c> enum value.</summary>
    Exact = 0,
    /// <summary><c>TopLeft</c> enum value.</summary>
    TopLeft,
    /// <summary><c>BottomRight</c> enum value.</summary>
    BottomRight,
}

/// <summary>Surface context for new surfaces.</summary>
public enum GhosttySurfaceContext
{
    /// <summary><c>Window</c> enum value.</summary>
    Window = 0,
    /// <summary><c>Tab</c> enum value.</summary>
    Tab = 1,
    /// <summary><c>Split</c> enum value.</summary>
    Split = 2,
}

/// <summary>Action target type.</summary>
public enum GhosttyTargetTag
{
    /// <summary><c>App</c> enum value.</summary>
    App = 0,
    /// <summary><c>Surface</c> enum value.</summary>
    Surface,
}

/// <summary>Split direction for creating new splits.</summary>
public enum GhosttySplitDirection
{
    /// <summary><c>Right</c> enum value.</summary>
    Right = 0,
    /// <summary><c>Down</c> enum value.</summary>
    Down,
    /// <summary><c>Left</c> enum value.</summary>
    Left,
    /// <summary><c>Up</c> enum value.</summary>
    Up,
}

/// <summary>Direction for navigating between splits.</summary>
public enum GhosttyGotoSplit
{
    /// <summary><c>Previous</c> enum value.</summary>
    Previous = 0,
    /// <summary><c>Next</c> enum value.</summary>
    Next,
    /// <summary><c>Up</c> enum value.</summary>
    Up,
    /// <summary><c>Left</c> enum value.</summary>
    Left,
    /// <summary><c>Down</c> enum value.</summary>
    Down,
    /// <summary><c>Right</c> enum value.</summary>
    Right,
}

/// <summary>Direction for navigating between windows.</summary>
public enum GhosttyGotoWindow
{
    /// <summary><c>Previous</c> enum value.</summary>
    Previous = 0,
    /// <summary><c>Next</c> enum value.</summary>
    Next,
}

/// <summary>Direction for resizing splits.</summary>
public enum GhosttyResizeSplitDirection
{
    /// <summary><c>Up</c> enum value.</summary>
    Up = 0,
    /// <summary><c>Down</c> enum value.</summary>
    Down,
    /// <summary><c>Left</c> enum value.</summary>
    Left,
    /// <summary><c>Right</c> enum value.</summary>
    Right,
}

/// <summary>Tab navigation target.</summary>
public enum GhosttyGotoTab
{
    /// <summary><c>Previous</c> enum value.</summary>
    Previous = -1,
    /// <summary><c>Next</c> enum value.</summary>
    Next = -2,
    /// <summary><c>Last</c> enum value.</summary>
    Last = -3,
}

/// <summary>Fullscreen mode.</summary>
public enum GhosttyFullscreen
{
    /// <summary><c>Native</c> enum value.</summary>
    Native = 0,
    /// <summary><c>NonNative</c> enum value.</summary>
    NonNative,
    /// <summary><c>NonNativeVisibleMenu</c> enum value.</summary>
    NonNativeVisibleMenu,
    /// <summary><c>NonNativePaddedNotch</c> enum value.</summary>
    NonNativePaddedNotch,
}

/// <summary>Float window state.</summary>
public enum GhosttyFloatWindow
{
    /// <summary><c>On</c> enum value.</summary>
    On = 0,
    /// <summary><c>Off</c> enum value.</summary>
    Off,
    /// <summary><c>Toggle</c> enum value.</summary>
    Toggle,
}

/// <summary>Secure input state.</summary>
public enum GhosttySecureInput
{
    /// <summary><c>On</c> enum value.</summary>
    On = 0,
    /// <summary><c>Off</c> enum value.</summary>
    Off,
    /// <summary><c>Toggle</c> enum value.</summary>
    Toggle,
}

/// <summary>Inspector visibility state.</summary>
public enum GhosttyInspectorAction
{
    /// <summary><c>Toggle</c> enum value.</summary>
    Toggle = 0,
    /// <summary><c>Show</c> enum value.</summary>
    Show,
    /// <summary><c>Hide</c> enum value.</summary>
    Hide,
}

/// <summary>Quit timer state.</summary>
public enum GhosttyQuitTimer
{
    /// <summary><c>Start</c> enum value.</summary>
    Start = 0,
    /// <summary><c>Stop</c> enum value.</summary>
    Stop,
}

/// <summary>Terminal readonly state.</summary>
public enum GhosttyReadonly
{
    /// <summary><c>Off</c> enum value.</summary>
    Off = 0,
    /// <summary><c>On</c> enum value.</summary>
    On,
}

/// <summary>Title prompt target.</summary>
public enum GhosttyPromptTitle
{
    /// <summary><c>Surface</c> enum value.</summary>
    Surface = 0,
    /// <summary><c>Tab</c> enum value.</summary>
    Tab,
}

/// <summary>Terminal mouse cursor shape.</summary>
public enum GhosttyMouseShape
{
    /// <summary><c>Default</c> enum value.</summary>
    Default = 0,
    /// <summary><c>ContextMenu</c> enum value.</summary>
    ContextMenu,
    /// <summary><c>Help</c> enum value.</summary>
    Help,
    /// <summary><c>Pointer</c> enum value.</summary>
    Pointer,
    /// <summary><c>Progress</c> enum value.</summary>
    Progress,
    /// <summary><c>Wait</c> enum value.</summary>
    Wait,
    /// <summary><c>Cell</c> enum value.</summary>
    Cell,
    /// <summary><c>Crosshair</c> enum value.</summary>
    Crosshair,
    /// <summary><c>Text</c> enum value.</summary>
    Text,
    /// <summary><c>VerticalText</c> enum value.</summary>
    VerticalText,
    /// <summary><c>Alias</c> enum value.</summary>
    Alias,
    /// <summary><c>Copy</c> enum value.</summary>
    Copy,
    /// <summary><c>Move</c> enum value.</summary>
    Move,
    /// <summary><c>NoDrop</c> enum value.</summary>
    NoDrop,
    /// <summary><c>NotAllowed</c> enum value.</summary>
    NotAllowed,
    /// <summary><c>Grab</c> enum value.</summary>
    Grab,
    /// <summary><c>Grabbing</c> enum value.</summary>
    Grabbing,
    /// <summary><c>AllScroll</c> enum value.</summary>
    AllScroll,
    /// <summary><c>ColResize</c> enum value.</summary>
    ColResize,
    /// <summary><c>RowResize</c> enum value.</summary>
    RowResize,
    /// <summary><c>NResize</c> enum value.</summary>
    NResize,
    /// <summary><c>EResize</c> enum value.</summary>
    EResize,
    /// <summary><c>SResize</c> enum value.</summary>
    SResize,
    /// <summary><c>WResize</c> enum value.</summary>
    WResize,
    /// <summary><c>NeResize</c> enum value.</summary>
    NeResize,
    /// <summary><c>NwResize</c> enum value.</summary>
    NwResize,
    /// <summary><c>SeResize</c> enum value.</summary>
    SeResize,
    /// <summary><c>SwResize</c> enum value.</summary>
    SwResize,
    /// <summary><c>EwResize</c> enum value.</summary>
    EwResize,
    /// <summary><c>NsResize</c> enum value.</summary>
    NsResize,
    /// <summary><c>NeswResize</c> enum value.</summary>
    NeswResize,
    /// <summary><c>NwseResize</c> enum value.</summary>
    NwseResize,
    /// <summary><c>ZoomIn</c> enum value.</summary>
    ZoomIn,
    /// <summary><c>ZoomOut</c> enum value.</summary>
    ZoomOut,
}

/// <summary>Mouse cursor visibility.</summary>
public enum GhosttyMouseVisibility
{
    /// <summary><c>Visible</c> enum value.</summary>
    Visible = 0,
    /// <summary><c>Hidden</c> enum value.</summary>
    Hidden,
}

/// <summary>Renderer health status.</summary>
public enum GhosttyRendererHealth
{
    /// <summary><c>Ok</c> enum value.</summary>
    Ok = 0,
    /// <summary><c>Unhealthy</c> enum value.</summary>
    Unhealthy,
}

/// <summary>Color change kind (foreground, background, cursor, or palette index).</summary>
public enum GhosttyColorKind
{
    /// <summary><c>Foreground</c> enum value.</summary>
    Foreground = -1,
    /// <summary><c>Background</c> enum value.</summary>
    Background = -2,
    /// <summary><c>Cursor</c> enum value.</summary>
    Cursor = -3,
}

/// <summary>URL open kind.</summary>
public enum GhosttyOpenUrlKind
{
    /// <summary><c>Unknown</c> enum value.</summary>
    Unknown = 0,
    /// <summary><c>Text</c> enum value.</summary>
    Text,
    /// <summary><c>Html</c> enum value.</summary>
    Html,
}

/// <summary>Close tab mode.</summary>
public enum GhosttyCloseTabMode
{
    /// <summary><c>This</c> enum value.</summary>
    This = 0,
    /// <summary><c>Other</c> enum value.</summary>
    Other,
    /// <summary><c>Right</c> enum value.</summary>
    Right,
}

/// <summary>Progress report state for OSC progress indication.</summary>
public enum GhosttyProgressState
{
    /// <summary><c>Remove</c> enum value.</summary>
    Remove = 0,
    /// <summary><c>Set</c> enum value.</summary>
    Set,
    /// <summary><c>Error</c> enum value.</summary>
    Error,
    /// <summary><c>Indeterminate</c> enum value.</summary>
    Indeterminate,
    /// <summary><c>Pause</c> enum value.</summary>
    Pause,
}

/// <summary>Quick terminal size mode.</summary>
public enum GhosttyQuickTerminalSizeTag
{
    /// <summary><c>None</c> enum value.</summary>
    None = 0,
    /// <summary><c>Percentage</c> enum value.</summary>
    Percentage,
    /// <summary><c>Pixels</c> enum value.</summary>
    Pixels,
}

/// <summary>Key table action tag.</summary>
public enum GhosttyKeyTableTag
{
    /// <summary><c>Activate</c> enum value.</summary>
    Activate = 0,
    /// <summary><c>Deactivate</c> enum value.</summary>
    Deactivate,
    /// <summary><c>DeactivateAll</c> enum value.</summary>
    DeactivateAll,
}

/// <summary>Action tag identifying which action is being performed.</summary>
public enum GhosttyActionTag
{
    /// <summary><c>Quit</c> enum value.</summary>
    Quit = 0,
    /// <summary><c>NewWindow</c> enum value.</summary>
    NewWindow,
    /// <summary><c>NewTab</c> enum value.</summary>
    NewTab,
    /// <summary><c>CloseTab</c> enum value.</summary>
    CloseTab,
    /// <summary><c>NewSplit</c> enum value.</summary>
    NewSplit,
    /// <summary><c>CloseAllWindows</c> enum value.</summary>
    CloseAllWindows,
    /// <summary><c>ToggleMaximize</c> enum value.</summary>
    ToggleMaximize,
    /// <summary><c>ToggleFullscreen</c> enum value.</summary>
    ToggleFullscreen,
    /// <summary><c>ToggleTabOverview</c> enum value.</summary>
    ToggleTabOverview,
    /// <summary><c>ToggleWindowDecorations</c> enum value.</summary>
    ToggleWindowDecorations,
    /// <summary><c>ToggleQuickTerminal</c> enum value.</summary>
    ToggleQuickTerminal,
    /// <summary><c>ToggleCommandPalette</c> enum value.</summary>
    ToggleCommandPalette,
    /// <summary><c>ToggleVisibility</c> enum value.</summary>
    ToggleVisibility,
    /// <summary><c>ToggleBackgroundOpacity</c> enum value.</summary>
    ToggleBackgroundOpacity,
    /// <summary><c>MoveTab</c> enum value.</summary>
    MoveTab,
    /// <summary><c>GotoTab</c> enum value.</summary>
    GotoTab,
    /// <summary><c>GotoSplit</c> enum value.</summary>
    GotoSplit,
    /// <summary><c>GotoWindow</c> enum value.</summary>
    GotoWindow,
    /// <summary><c>ResizeSplit</c> enum value.</summary>
    ResizeSplit,
    /// <summary><c>EqualizeSplits</c> enum value.</summary>
    EqualizeSplits,
    /// <summary><c>ToggleSplitZoom</c> enum value.</summary>
    ToggleSplitZoom,
    /// <summary><c>PresentTerminal</c> enum value.</summary>
    PresentTerminal,
    /// <summary><c>SizeLimit</c> enum value.</summary>
    SizeLimit,
    /// <summary><c>ResetWindowSize</c> enum value.</summary>
    ResetWindowSize,
    /// <summary><c>InitialSize</c> enum value.</summary>
    InitialSize,
    /// <summary><c>CellSize</c> enum value.</summary>
    CellSize,
    /// <summary><c>Scrollbar</c> enum value.</summary>
    Scrollbar,
    /// <summary><c>Render</c> enum value.</summary>
    Render,
    /// <summary><c>Inspector</c> enum value.</summary>
    Inspector,
    /// <summary><c>ShowGtkInspector</c> enum value.</summary>
    ShowGtkInspector,
    /// <summary><c>RenderInspector</c> enum value.</summary>
    RenderInspector,
    /// <summary><c>DesktopNotification</c> enum value.</summary>
    DesktopNotification,
    /// <summary><c>SetTitle</c> enum value.</summary>
    SetTitle,
    /// <summary><c>PromptTitle</c> enum value.</summary>
    PromptTitle,
    /// <summary><c>Pwd</c> enum value.</summary>
    Pwd,
    /// <summary><c>MouseShape</c> enum value.</summary>
    MouseShape,
    /// <summary><c>MouseVisibility</c> enum value.</summary>
    MouseVisibility,
    /// <summary><c>MouseOverLink</c> enum value.</summary>
    MouseOverLink,
    /// <summary><c>RendererHealth</c> enum value.</summary>
    RendererHealth,
    /// <summary><c>OpenConfig</c> enum value.</summary>
    OpenConfig,
    /// <summary><c>QuitTimer</c> enum value.</summary>
    QuitTimer,
    /// <summary><c>FloatWindow</c> enum value.</summary>
    FloatWindow,
    /// <summary><c>SecureInput</c> enum value.</summary>
    SecureInput,
    /// <summary><c>KeySequence</c> enum value.</summary>
    KeySequence,
    /// <summary><c>KeyTable</c> enum value.</summary>
    KeyTable,
    /// <summary><c>ColorChange</c> enum value.</summary>
    ColorChange,
    /// <summary><c>ReloadConfig</c> enum value.</summary>
    ReloadConfig,
    /// <summary><c>ConfigChange</c> enum value.</summary>
    ConfigChange,
    /// <summary><c>CloseWindow</c> enum value.</summary>
    CloseWindow,
    /// <summary><c>RingBell</c> enum value.</summary>
    RingBell,
    /// <summary><c>Undo</c> enum value.</summary>
    Undo,
    /// <summary><c>Redo</c> enum value.</summary>
    Redo,
    /// <summary><c>CheckForUpdates</c> enum value.</summary>
    CheckForUpdates,
    /// <summary><c>OpenUrl</c> enum value.</summary>
    OpenUrl,
    /// <summary><c>ShowChildExited</c> enum value.</summary>
    ShowChildExited,
    /// <summary><c>ProgressReport</c> enum value.</summary>
    ProgressReport,
    /// <summary><c>ShowOnScreenKeyboard</c> enum value.</summary>
    ShowOnScreenKeyboard,
    /// <summary><c>CommandFinished</c> enum value.</summary>
    CommandFinished,
    /// <summary><c>StartSearch</c> enum value.</summary>
    StartSearch,
    /// <summary><c>EndSearch</c> enum value.</summary>
    EndSearch,
    /// <summary><c>SearchTotal</c> enum value.</summary>
    SearchTotal,
    /// <summary><c>SearchSelected</c> enum value.</summary>
    SearchSelected,
    /// <summary><c>Readonly</c> enum value.</summary>
    Readonly,
}

/// <summary>IPC target type.</summary>
public enum GhosttyIpcTargetTag
{
    /// <summary><c>Class</c> enum value.</summary>
    Class = 0,
    /// <summary><c>Detect</c> enum value.</summary>
    Detect,
}

/// <summary>IPC action type.</summary>
public enum GhosttyIpcActionTag
{
    /// <summary><c>NewWindow</c> enum value.</summary>
    NewWindow = 0,
}
