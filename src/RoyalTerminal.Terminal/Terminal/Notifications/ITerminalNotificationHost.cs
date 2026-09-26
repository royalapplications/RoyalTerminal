// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Desktop features actually supported by the installed notification host.</summary>
[Flags]
public enum TerminalNotificationCapabilities
{
    /// <summary>No desktop backend is available; OSC 99 queries receive no response.</summary>
    None = 0,
    /// <summary>Plain-text titles and bodies can be displayed.</summary>
    Display = 1,
    /// <summary>Activation events can be reported.</summary>
    Activation = 2,
    /// <summary>The originating terminal can be focused.</summary>
    Focus = 4,
    /// <summary>All close events are reported by the backend.</summary>
    CloseEvents = 8,
    /// <summary>Notifications can be closed programmatically (also enables expiry).</summary>
    Close = 16,
    /// <summary>The backend maintains an authoritative, nonblocking alive-state cache.</summary>
    Alive = 32,
    /// <summary>Encoded image data and ordered icon names are supported.</summary>
    Icons = 64,
    /// <summary>Additional buttons and their one-based activation indices are supported.</summary>
    Buttons = 128,
    /// <summary>Low, normal and high urgency are supported.</summary>
    Urgency = 256,
    /// <summary>The standard system and silent sound choices are supported.</summary>
    Sound = 512,
    /// <summary>All standard protocol sound names are supported.</summary>
    NamedSounds = 1024,
}

/// <summary>An owned, plain-text desktop request; never interpret its strings as markup or commands.</summary>
/// <param name="Token">Unique host identity for this revision; never use the client ID as a native identity or path.</param>
/// <param name="ReplacesToken">Previous revision to replace, if it is still alive.</param>
/// <param name="Title">Plain-text title.</param>
/// <param name="Body">Plain-text body.</param>
/// <param name="ApplicationName">Client application name, for display/filtering only.</param>
/// <param name="Types">Client classification labels, for filtering only.</param>
/// <param name="IconNames">Ordered local icon names; do not treat these as file paths.</param>
/// <param name="IconData">Owned encoded image bytes; decode with bounded dimensions/allocation.</param>
/// <param name="Buttons">Button labels, with one-based activation indices.</param>
/// <param name="Sound">Requested sound name; unknown names use the system default.</param>
/// <param name="Urgency">0 low, 1 normal, 2 high.</param>
/// <param name="ExpireMilliseconds">-1 OS default, 0 no expiry if possible, positive terminal-enforced expiry.</param>
public sealed record TerminalNotificationRequest(Guid Token, Guid? ReplacesToken,
    string Title, string Body, string? ApplicationName, IReadOnlyList<string> Types,
    IReadOnlyList<string> IconNames, ReadOnlyMemory<byte> IconData, IReadOnlyList<string> Buttons,
    string Sound, int Urgency, int ExpireMilliseconds);

/// <summary>Backend event associated with exactly one notification revision.</summary>
public enum TerminalNotificationEvent
{
    /// <summary>The body or a button was activated.</summary>
    Activated,
    /// <summary>The notification was dismissed or expired.</summary>
    Closed,
    /// <summary>The backend could not deliver the request.</summary>
    Failed,
}

/// <summary>Thread-safe backend feedback; button zero denotes the body, 1..N denotes a button.</summary>
/// <param name="Event">Event kind.</param>
/// <param name="Button">Activated button index; ignored for other events.</param>
public readonly record struct TerminalNotificationFeedback(TerminalNotificationEvent Event, int Button = 0);

/// <summary>
/// Desktop notification integration. Methods run under terminal serialization and must
/// never wait for the UI thread. Queue UI/native work and return promptly. Properties
/// and IsAlive read cached state. Hosts must bound their queue and retained requests.
/// Caller owns host lifetime; processor detachment closes its owned notifications.
/// </summary>
public interface ITerminalNotificationHost
{
    /// <summary>Capabilities currently usable, not merely compiled into the host.</summary>
    TerminalNotificationCapabilities Capabilities { get; }
    /// <summary>Cached keyboard-focus state of the originating terminal.</summary>
    bool IsFocused { get; }
    /// <summary>Cached visibility of the originating terminal in an active OS window.</summary>
    bool IsVisible { get; }
    /// <summary>Queues a request. False rejects it; feedback may arrive on any thread, including synchronously.</summary>
    bool Show(TerminalNotificationRequest request, Action<TerminalNotificationFeedback> feedback);
    /// <summary>Queues closure of this revision. Unknown tokens are harmless.</summary>
    void Close(Guid token);
    /// <summary>Reads cached alive state, including accepted requests awaiting OS delivery; only called when Alive is advertised.</summary>
    bool IsAlive(Guid token);
    /// <summary>Queues activation/focus of the originating terminal.</summary>
    void Focus();
}

/// <summary>Optional OSC 99 integration. Serialize configuration with all processor access.</summary>
public interface ITerminalNotificationSource
{
    /// <summary>Notification host; null disables the protocol and closes this processor's notifications.</summary>
    ITerminalNotificationHost? NotificationHost { get; set; }
}
