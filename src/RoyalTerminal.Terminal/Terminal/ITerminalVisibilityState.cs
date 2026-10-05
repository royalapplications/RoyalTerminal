// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Optional host-owned visibility metadata for a VT processor.</summary>
public interface ITerminalVisibilityState
{
    /// <summary>
    /// Gets or sets whether the terminal may be visible. Unknown visibility is true.
    /// Changes emit a response through the processor's response callback only while
    /// DEC mode 2033 is enabled. Duplicate assignments do not emit responses.
    /// This state survives terminal/session resets and is not snapshot content.
    /// Serialize access with all other processor operations.
    /// </summary>
    bool PotentiallyVisible { get; set; }
}

/// <summary>Optional external endpoint capability for host visibility updates.</summary>
public interface ITerminalVisibilitySink
{
    /// <summary>
    /// Reports whether the attached host may be visible, independently of focus.
    /// Unknown desktop occlusion is potentially visible. Called on the host UI thread;
    /// implementations serialize the update with their own terminal operations.
    /// </summary>
    void SetVisibility(bool potentiallyVisible);
}
