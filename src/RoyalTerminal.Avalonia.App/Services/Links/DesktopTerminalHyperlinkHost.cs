// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.App.Services.Links;

internal interface ITerminalHyperlinkPrompt
{
    ValueTask<bool> ShowAsync(TerminalHyperlinkRequest request, string? handler, CancellationToken cancellationToken);
}

internal interface ITerminalHyperlinkHandlerResolver
{
    ValueTask<string?> ResolveAsync(string scheme, CancellationToken cancellationToken);
}

internal interface ITerminalHyperlinkLauncher
{
    ValueTask LaunchAsync(Uri uri, CancellationToken cancellationToken);
}

// The host orchestrates decisions; the prompt, association query and launcher
// are separate boundaries. None may turn a denial or a failed operation into
// launch; an unknown handler is labeled explicitly and still needs consent.
internal sealed class DesktopTerminalHyperlinkHost(
    ITerminalHyperlinkPrompt prompt,
    ITerminalHyperlinkHandlerResolver handlers,
    ITerminalHyperlinkLauncher launcher,
    ITerminalHyperlinkFileInspector files,
    ITerminalHyperlinkPathPreviewSource previews) : ITerminalHyperlinkHost
{
    public async ValueTask HandleAsync(TerminalHyperlinkRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Treat even an externally constructed request as untrusted. Its flags
        // and display text cannot grant permission or bypass scalar validation.
        request = TerminalHyperlinkSafety.Classify(request.Target);
        if (request.Disposition == TerminalHyperlinkDisposition.InspectFile)
            request = await files.InspectAsync(request, cancellationToken);
        else if (request.Disposition == TerminalHyperlinkDisposition.Deny)
        {
            // In particular, scheme-less blocked targets are standardized for
            // display/copy. Preview failure cannot grant any launch permission.
            try { request = request with { DisplayText = await previews.GetPathPreviewAsync(request.Target, cancellationToken) }; }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { /* Keep the escaped classification fallback. */ }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Disposition == TerminalHyperlinkDisposition.Allow)
        {
            await launcher.LaunchAsync(request.Uri!, cancellationToken);
            return;
        }

        string? handler = request.Disposition == TerminalHyperlinkDisposition.Confirm
            ? await handlers.ResolveAsync(request.Uri!.Scheme, cancellationToken)
            : null;
        cancellationToken.ThrowIfCancellationRequested();
        bool accepted = await prompt.ShowAsync(request, handler, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // File permission comes only from inspection, never generic consent.
        if (accepted && request.Disposition == TerminalHyperlinkDisposition.Confirm)
            await launcher.LaunchAsync(request.Uri!, cancellationToken);
    }
}
