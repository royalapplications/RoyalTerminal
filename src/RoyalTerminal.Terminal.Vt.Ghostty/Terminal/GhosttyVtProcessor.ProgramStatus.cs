// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor : ITerminalProgramStatusSource
{
    private readonly TerminalProgramStatusStore _programStatuses = new();
    private GhosttyVtNative.GhosttyTerminalProgramStatusCallback? _programStatusDelegate;
    private GhosttyVtNative.GhosttyTerminalSemanticPromptCallback? _semanticPromptDelegate;
    private GhosttyVtNative.GhosttyTerminalResetCallback? _resetDelegate;

    /// <inheritdoc />
    public IReadOnlyList<TerminalProgramStatus> ProgramStatuses => _programStatuses.Records;

    /// <inheritdoc />
    public Action? ProgramStatusChangedCallback { get; set; }

    /// <inheritdoc />
    public string GetProgramStatusApplication(string id) => _programStatuses.GetApplication(id);

    /// <inheritdoc />
    public void NotifyProgramStatusProcessExit()
    {
        if (_programStatuses.ClearTransient()) ProgramStatusChangedCallback?.Invoke();
    }

    private void ResetProgramStatuses()
    {
        if (_programStatuses.Reset()) ProgramStatusChangedCallback?.Invoke();
    }

    private unsafe void SetupProgramStatusEffects()
    {
        _programStatusDelegate ??= OnNativeProgramStatus;
        _semanticPromptDelegate ??= OnNativeSemanticPrompt;
        _resetDelegate ??= OnNativeReset;
        _terminal.SetProgramStatusCallback(Marshal.GetFunctionPointerForDelegate(_programStatusDelegate));
        _terminal.SetSemanticPromptCallback(Marshal.GetFunctionPointerForDelegate(_semanticPromptDelegate));
        _terminal.SetResetCallback(Marshal.GetFunctionPointerForDelegate(_resetDelegate));
    }

    private unsafe void OnNativeProgramStatus(nint terminal, nint userdata,
        GhosttyVtNative.GhosttyTerminalProgramStatus* report)
    {
        if (report is null) return;
        try
        {
            TerminalProgramStatus status = new((TerminalProgramStatusState)report->State,
                report->Id.ToUtf8String(), (TerminalProgramStatusKind)report->Kind,
                report->Progress < 0 ? null : (byte)report->Progress,
                report->App.ToUtf8String(), report->Title.ToUtf8String(), report->Message.ToUtf8String());
            if (_programStatuses.Apply(status)) ProgramStatusChangedCallback?.Invoke();
        }
        catch { /* Managed exceptions must not cross the native callback boundary. */ }
    }

    private unsafe void OnNativeSemanticPrompt(nint terminal, nint userdata,
        GhosttyVtNative.GhosttyTerminalSemanticPrompt* prompt)
    {
        if (prompt is null) return;
        try
        {
            if (prompt->Kind == GhosttyVtNative.GhosttySemanticPromptKind.PromptStart)
                NotifyProgramStatusProcessExit();
        }
        catch { /* Managed exceptions must not cross the native callback boundary. */ }
    }

    private void OnNativeReset(nint terminal, nint userdata)
    {
        try { ResetProgramStatuses(); }
        catch { /* Managed exceptions must not cross the native callback boundary. */ }
    }
}
