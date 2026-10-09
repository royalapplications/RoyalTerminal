// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor : ITerminalProgramStatusSource
{
    private readonly TerminalProgramStatusStore _programStatuses = new();

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

    private void HandleProgramStatus(ReadOnlySpan<byte> body, bool bellTerminator)
    {
        if (body.SequenceEqual("?"u8))
        {
            ResponseCallback?.Invoke(bellTerminator ? "\u001b]7501;?\a"u8.ToArray() : "\u001b]7501;?\u001b\\"u8.ToArray());
            return;
        }
        if (ProgramStatusParser.TryParse(body, out TerminalProgramStatus? report) &&
            _programStatuses.Apply(report!)) ProgramStatusChangedCallback?.Invoke();
    }
}
