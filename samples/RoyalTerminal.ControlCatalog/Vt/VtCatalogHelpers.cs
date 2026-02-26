// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.ControlCatalog;

internal static class VtCatalogHelpers
{
    public static bool TryQueryMode(VtProcessorProbe probe, bool decPrivate, int mode, out string response)
    {
        string sequence = decPrivate
            ? $"\x1b[?{mode}$p"
            : $"\x1b[{mode}$p";

        probe.ClearResponses();
        probe.Send(sequence);
        return probe.TryTakeResponse(out response);
    }

    public static bool IsModeQueryResponse(string response, bool decPrivate, int mode, out int status)
    {
        status = -1;
        string prefix = decPrivate
            ? $"\x1b[?{mode};"
            : $"\x1b[{mode};";

        if (!response.StartsWith(prefix, StringComparison.Ordinal) ||
            !response.EndsWith("$y", StringComparison.Ordinal))
        {
            return false;
        }

        int semicolon = response.LastIndexOf(';');
        int suffix = response.LastIndexOf("$y", StringComparison.Ordinal);
        if (semicolon < 0 || suffix <= semicolon + 1)
        {
            return false;
        }

        return int.TryParse(response.AsSpan(semicolon + 1, suffix - semicolon - 1), out status);
    }

    public static bool TryToggleDecMode(
        VtProcessorProbe probe,
        int mode,
        out int setStatus,
        out int resetStatus,
        out string setResponse,
        out string resetResponse)
    {
        setStatus = -1;
        resetStatus = -1;
        setResponse = string.Empty;
        resetResponse = string.Empty;

        probe.Send($"\x1b[?{mode}h");
        bool setResponseOk = TryQueryMode(probe, decPrivate: true, mode, out setResponse) &&
                             IsModeQueryResponse(setResponse, decPrivate: true, mode, out setStatus);

        probe.Send($"\x1b[?{mode}l");
        bool resetResponseOk = TryQueryMode(probe, decPrivate: true, mode, out resetResponse) &&
                               IsModeQueryResponse(resetResponse, decPrivate: true, mode, out resetStatus);

        return setResponseOk && resetResponseOk && setStatus == 1 && resetStatus == 2;
    }

    public static bool TrySingleResponse(
        VtProcessorProbe probe,
        string sequence,
        Func<string, bool> validator,
        out string response)
    {
        probe.ClearResponses();
        probe.Send(sequence);
        if (!probe.TryTakeResponse(out response))
        {
            return false;
        }

        return validator(response);
    }
}
