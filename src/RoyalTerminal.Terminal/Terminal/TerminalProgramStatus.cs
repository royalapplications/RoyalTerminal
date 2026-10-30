// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>State reported by OSC 7501. Clear removes records and is never stored.</summary>
public enum TerminalProgramStatusState
{
    /// <summary>Waiting for another instruction.</summary>
    Idle,
    /// <summary>Running independently.</summary>
    Working,
    /// <summary>Finished successfully; retained across prompts and process exit.</summary>
    Done,
    /// <summary>Waiting for user action.</summary>
    Blocked,
    /// <summary>Stopped with an error; retained across prompts and process exit.</summary>
    Error,
    /// <summary>Remove the addressed record and its descendants.</summary>
    Clear,
}

/// <summary>Action needed by a blocked program.</summary>
public enum TerminalProgramStatusKind
{
    /// <summary>No recognized action was specified.</summary>
    None,
    /// <summary>Approval is needed.</summary>
    Permission,
    /// <summary>An answer is needed.</summary>
    Question,
    /// <summary>Authentication is needed.</summary>
    Auth,
}

/// <summary>An immutable OSC 7501 report. Text is untrusted plain text, never markup.</summary>
/// <param name="State">Program state, or a subtree clear request.</param>
/// <param name="Id">Slash-separated record path; empty identifies the root.</param>
/// <param name="Kind">Reason for a blocked state.</param>
/// <param name="Progress">Percentage, or null for indeterminate work.</param>
/// <param name="App">Declared program name; empty means inherit from ancestors.</param>
/// <param name="Title">Decoded record label.</param>
/// <param name="Message">Decoded status message.</param>
public sealed record TerminalProgramStatus(
    TerminalProgramStatusState State,
    string Id = "",
    TerminalProgramStatusKind Kind = TerminalProgramStatusKind.None,
    byte? Progress = null,
    string App = "",
    string Title = "",
    string Message = "");

/// <summary>Optional per-terminal OSC 7501 state. Access under the processor's input lock.</summary>
public interface ITerminalProgramStatusSource
{
    /// <summary>Current immutable records, oldest update first; copy before crossing threads.</summary>
    IReadOnlyList<TerminalProgramStatus> ProgramStatuses { get; }

    /// <summary>Runs synchronously after records change.</summary>
    Action? ProgramStatusChangedCallback { get; set; }

    /// <summary>Resolves a record's declared or nearest inherited application name.</summary>
    string GetProgramStatusApplication(string id);

    /// <summary>Drops working and blocked records after the attached process exits.</summary>
    void NotifyProgramStatusProcessExit();
}
