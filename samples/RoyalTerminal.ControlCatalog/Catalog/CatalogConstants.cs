// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.ControlCatalog;

internal static class CatalogConstants
{
    public static readonly int[] AnsiModes = [2, 4, 12, 20];

    public static readonly int[] DecModes =
    [
        1, 3, 4, 5, 6, 7, 8, 9, 12, 25, 40, 45, 47, 66, 69,
        1000, 1002, 1003, 1004, 1005, 1006, 1007, 1015, 1016,
        1035, 1036, 1039, 1045, 1047, 1048, 1049,
        2004, 2026, 2027, 2031, 2048, 9001,
    ];

    public static readonly int[] ToggleModes = [1, 25, 66, 1004, 1006, 1049, 2004, 9001];

    public const string ManagedProbeName = "Managed VT";
    public const string GhosttyProbeName = "Ghostty VT";
    public const string PtySmokeToken = "__RT_CONTROL_CATALOG_PTY_OK__";
}
