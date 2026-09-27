// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.App.Services.Links;

internal interface IHyperlinkPathResolver
{
    string ResolvePath(string path);
}

internal sealed class DesktopHyperlinkPathPreviewSource(IHyperlinkPathResolver paths) : ITerminalHyperlinkPathPreviewSource
{
    public async ValueTask<string> GetPathPreviewAsync(string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string display = TerminalHyperlinkSafety.SanitizeDisplay(target);
        try
        {
            if (!target.AsSpan().StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                int colon = target.IndexOf(':');
                // Web/custom spellings stay intact; relative blocked targets
                // are standardized but do not cause any filesystem inspection.
                return colon > 0 && Uri.CheckSchemeName(target[..colon]) ? display :
                    TerminalHyperlinkSafety.SanitizeDisplay(Path.GetFullPath(target));
            }

            TerminalHyperlinkRequest request = TerminalHyperlinkSafety.Classify(target);
            if (request.Disposition != TerminalHyperlinkDisposition.InspectFile) return display;
            Uri uri = request.Uri!.Host.Length == 0 ? request.Uri : new UriBuilder(request.Uri) { Host = string.Empty }.Uri;
            string path = uri.LocalPath;
            if (!HyperlinkFilePolicy.IsLocalPath(path, OperatingSystem.IsWindows())) return display;
            path = Path.GetFullPath(path);
            display = TerminalHyperlinkSafety.SanitizeDisplay(path);
            string canonical = await Task.Run(() => paths.ResolvePath(path), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return TerminalHyperlinkSafety.SanitizeDisplay(canonical);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or
            PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return display;
        }
    }
}
