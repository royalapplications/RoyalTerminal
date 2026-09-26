// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.App.Services.Notifications;

internal sealed record DesktopThemeEnvironment(string IconTheme, string SoundTheme,
    string[] IconRoots, string[] SoundRoots, string[] ApplicationRoots, string Locale);

internal sealed class LinuxDesktopThemeEnvironment(IDesktopThemeFiles files,
    Func<string, string?> environment, Func<string, string, string?> settings)
{
    internal DesktopThemeEnvironment Read()
    {
        string home = Absolute(environment("HOME"), string.Empty);
        string dataHome = Absolute(environment("XDG_DATA_HOME"), home.Length > 0 ? Path.Combine(home, ".local/share") : string.Empty);
        string configHome = Absolute(environment("XDG_CONFIG_HOME"), home.Length > 0 ? Path.Combine(home, ".config") : string.Empty);
        List<string> data = Roots(dataHome, environment("XDG_DATA_DIRS"), "/usr/local/share:/usr/share");
        List<string> configs = Roots(configHome, environment("XDG_CONFIG_DIRS"), "/etc/xdg");
        string desktop = environment("XDG_CURRENT_DESKTOP") ?? string.Empty;
        bool kde = desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase);
        string? schema = desktop.Contains("Cinnamon", StringComparison.OrdinalIgnoreCase) ? "org.cinnamon.desktop"
            : desktop.Contains("MATE", StringComparison.OrdinalIgnoreCase) ? "org.mate"
            : desktop.Contains("GNOME", StringComparison.OrdinalIgnoreCase) || desktop.Contains("Unity", StringComparison.OrdinalIgnoreCase)
                ? "org.gnome.desktop" : null;
        string icon = string.Empty;
        if (kde) icon = Config(configs, "kdeglobals", "Icons", "Theme");
        if (icon.Length == 0 && schema is not null) icon = settings(schema + ".interface", "icon-theme") ?? string.Empty;
        if (icon.Length == 0) icon = Config(configs, "gtk-4.0/settings.ini", "Settings", "gtk-icon-theme-name");
        if (icon.Length == 0) icon = Config(configs, "gtk-3.0/settings.ini", "Settings", "gtk-icon-theme-name");
        // On an unknown desktop prefer its explicit GTK settings over a GNOME
        // schema merely installed by another application.
        if (icon.Length == 0 && !kde && schema is null) icon = settings("org.gnome.desktop.interface", "icon-theme") ?? string.Empty;
        string sound = schema is null ? string.Empty : settings(schema + ".sound", "theme-name") ?? string.Empty;
        if (sound.Length == 0) sound = Config(configs, "gtk-3.0/settings.ini", "Settings", "gtk-sound-theme-name");
        List<string> icons = new();
        if (home.Length > 0) icons.Add(Path.Combine(home, ".icons"));
        List<string> sounds = new(), applications = new();
        foreach (string root in data)
        { icons.Add(Path.Combine(root, "icons")); sounds.Add(Path.Combine(root, "sounds")); applications.Add(Path.Combine(root, "applications")); }
        icons.Add("/usr/share/pixmaps");
        string locale = environment("LC_ALL") ?? string.Empty;
        if (locale.Length == 0) locale = environment("LC_MESSAGES") ?? string.Empty;
        if (locale.Length == 0) locale = environment("LANG") ?? "C";
        return new(FreedesktopNotificationResources.IsName(icon) ? icon : "hicolor",
            FreedesktopNotificationResources.IsName(sound) ? sound : "freedesktop",
            icons.ToArray(), sounds.ToArray(), applications.ToArray(), locale);
    }

    private string Config(List<string> roots, string relative, string section, string key)
    {
        foreach (string root in roots)
        {
            string value = new DesktopThemeDocument(files.ReadText(Path.Combine(root, relative))).Get(section, key).Trim('"');
            if (FreedesktopNotificationResources.IsName(value)) return value;
        }
        return string.Empty;
    }

    private static List<string> Roots(string first, string? value, string fallback)
    {
        List<string> roots = new();
        if (first.Length > 0) roots.Add(first);
        foreach (string root in (string.IsNullOrEmpty(value) ? fallback : value).Split(':'))
            if (roots.Count < 32 && Path.IsPathFullyQualified(root) && !roots.Contains(root, StringComparer.Ordinal)) roots.Add(root);
        return roots;
    }

    private static string Absolute(string? value, string fallback)
        => !string.IsNullOrEmpty(value) && Path.IsPathFullyQualified(value) ? value : fallback;
}
