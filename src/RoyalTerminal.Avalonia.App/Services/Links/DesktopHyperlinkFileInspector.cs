// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.App.Services.Links;

internal sealed class DesktopHyperlinkFileInspector(IHyperlinkFileProbe probe) : ITerminalHyperlinkFileInspector
{
    public async ValueTask<TerminalHyperlinkRequest> InspectAsync(TerminalHyperlinkRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request = TerminalHyperlinkSafety.Classify(request.Target);
        if (request.Disposition != TerminalHyperlinkDisposition.InspectFile) return request;
        // Treat localhost as this machine without introducing a UNC path on Windows.
        Uri uri = request.Uri!.Host.Length == 0 ? request.Uri : new UriBuilder(request.Uri) { Host = string.Empty }.Uri;
        string path = uri.LocalPath;
        if (!HyperlinkFilePolicy.IsLocalPath(path, OperatingSystem.IsWindows()))
            return Denied(TerminalHyperlinkDenialReason.Malformed, TerminalHyperlinkSafety.SanitizeDisplay(path));
        string display = TerminalHyperlinkSafety.SanitizeDisplay(path);
        try
        {
            path = Path.GetFullPath(path);
            display = TerminalHyperlinkSafety.SanitizeDisplay(path);
            HyperlinkFileFacts facts = await Task.Run(() => probe.Read(path), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return HyperlinkFilePolicy.Apply(request, facts, OperatingSystem.IsWindows());
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or DllNotFoundException or
            EntryPointNotFoundException or BadImageFormatException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Denied(TerminalHyperlinkDenialReason.FileInspectionUnavailable, display);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Denied(TerminalHyperlinkDenialReason.InaccessibleFile, display);
        }

        TerminalHyperlinkRequest Denied(TerminalHyperlinkDenialReason reason, string preview)
            => request with { Uri = null, DisplayText = preview, Disposition = TerminalHyperlinkDisposition.Deny, DenialReason = reason };
    }
}
