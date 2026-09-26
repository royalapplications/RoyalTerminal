// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.App.Services.Notifications;

internal readonly record struct NotificationSound(string Name, string File, bool Silent);

internal interface ILinuxNotificationResources
{
    void Refresh();
    bool SupportsNamedSounds { get; }
    string ResolveIcon(IReadOnlyList<string> names, string? application);
    NotificationSound ResolveSound(string name);
}

// Worker-confined, bounded caches. Theme/file changes are observed on the next
// request after five seconds; notification conversion does no filesystem work
// under terminal serialization or on the UI thread.
internal sealed class FreedesktopNotificationResources(IDesktopThemeFiles files,
    Func<DesktopThemeEnvironment> readEnvironment, TimeProvider clock) : ILinuxNotificationResources
{
    private readonly Dictionary<string, DesktopThemeDocument> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _icons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NotificationSound> _sounds = new(StringComparer.Ordinal);
    private DesktopThemeEnvironment? _environment;
    private long _refreshed;
    private int _remainingProbes;
    public bool SupportsNamedSounds { get; private set; }

    public void Refresh()
    {
        if (_environment is not null && clock.GetElapsedTime(_refreshed) < TimeSpan.FromSeconds(5)) return;
        _environment = readEnvironment();
        _refreshed = clock.GetTimestamp();
        _documents.Clear(); _icons.Clear(); _sounds.Clear();
        SupportsNamedSounds = true;
        foreach (string name in new[] { "error", "warning", "info", "question" })
        {
            NotificationSound sound = ResolveSound(name);
            SupportsNamedSounds &= sound.File.Length > 0 || sound.Silent;
        }
    }

    public string ResolveIcon(IReadOnlyList<string> names, string? application)
    {
        if (_environment is null) Refresh();
        _remainingProbes = 8192;
        // Kitty specifies name ordering across the entire theme hierarchy, not
        // freedesktop FindBestIcon's preference for any name in the current theme.
        for (int i = 0; i < Math.Min(names.Count, 32); i++)
        {
            string icon = NamedIcon(names[i]);
            if (icon.Length > 0) return icon;
        }
        return names.Count == 0 && application is not null ? NamedIcon(application) : string.Empty;
    }

    private string NamedIcon(string name)
    {
        if (!IsName(name)) return string.Empty;
        if (_icons.TryGetValue(name, out string? cached)) return cached;
        string mapped = name switch
        {
            "error" => "dialog-error", "warn" or "warning" => "dialog-warning",
            "info" => "dialog-information", "question" => "dialog-question", "help" => "help-browser",
            "file-manager" => "system-file-manager", "system-monitor" => "utilities-system-monitor",
            "text-editor" => "accessories-text-editor", _ => name,
        };
        string result = FindIcon(mapped);
        if (result.Length == 0) result = ApplicationIcon(name);
        if (_icons.Count >= 256) _icons.Clear();
        // A request exhausting its lookup budget must not poison future lookups.
        if (_remainingProbes > 0) _icons[name] = result;
        return result;
    }

    private string FindIcon(string name)
    {
        DesktopThemeEnvironment environment = _environment!;
        HashSet<string> visited = new(StringComparer.Ordinal);
        string result = InTheme(environment.IconTheme, 0);
        if (result.Length == 0) result = InTheme("hicolor", 0);
        if (result.Length > 0) return result;
        foreach (string root in environment.IconRoots)
            if (IconFile(root, name) is { Length: > 0 } fallback) return fallback;
        return string.Empty;

        string InTheme(string theme, int depth)
        {
            if (!IsName(theme) || depth >= 16 || visited.Count >= 32 || !visited.Add(theme)) return string.Empty;
            DesktopThemeDocument document = Theme(environment.IconRoots, theme);
            string[] directories = document.List("Icon Theme", "Directories");
            string[] scaled = document.List("Icon Theme", "ScaledDirectories");
            string best = string.Empty;
            long bestDistance = long.MaxValue;
            // Exact size/scale precedes nearest-size fallback, preserving directory
            // and XDG-root ordering for ties. Parent themes are only tried after
            // every size in this theme, as required by the icon theme spec.
            for (int pass = 0; pass < 2; pass++)
            {
                int count = Math.Min(directories.Length + scaled.Length, 1024);
                for (int i = 0; i < count && _remainingProbes > 0; i++)
                {
                    string directory = i < directories.Length ? directories[i] : scaled[i - directories.Length];
                    if (!IsSubdirectory(directory)) continue;
                    int size = document.Number(directory, "Size", 0), scale = document.Number(directory, "Scale", 1);
                    if (size == 0 || scale == 0) continue;
                    int minimum = size, maximum = size;
                    switch (document.Get(directory, "Type"))
                    {
                        case "Scalable": minimum = document.Number(directory, "MinSize", size); maximum = document.Number(directory, "MaxSize", size); break;
                        case "Fixed": break;
                        default: int threshold = document.Number(directory, "Threshold", 2); minimum = Math.Max(0, size - threshold); maximum = size + threshold; break;
                    }
                    long distance = Math.Max(0, Math.Max((long)minimum * scale - 64, 64 - (long)maximum * scale));
                    if (pass == 0 && (scale != 1 || distance != 0) || pass == 1 && distance >= bestDistance) continue;
                    foreach (string root in environment.IconRoots)
                    {
                        string path = IconFile(Path.Combine(root, theme, directory), name);
                        if (path.Length == 0) continue;
                        if (pass == 0) return path;
                        best = path; bestDistance = distance; break;
                    }
                }
                if (best.Length > 0) return best;
            }
            foreach (string parent in document.List("Icon Theme", "Inherits"))
            {
                // hicolor is always the final inherited fallback.
                if (parent == "hicolor") continue;
                string inherited = InTheme(parent, depth + 1);
                if (inherited.Length > 0) return inherited;
            }
            return string.Empty;
        }
    }

    private string IconFile(string directory, string name)
    {
        foreach (string extension in new[] { ".png", ".svg", ".xpm" })
        {
            string path = Path.Combine(directory, name + extension);
            if (Exists(path)) return path;
        }
        return string.Empty;
    }

    private string ApplicationIcon(string name)
    {
        // Only consult locally installed desktop entries. Never execute Exec,
        // launch an app, or pass the caller's name as a desktop-entry hint.
        string id = name.EndsWith(".desktop", StringComparison.Ordinal) ? name : name + ".desktop";
        foreach (string root in _environment!.ApplicationRoots)
        {
            string? result = Entry(id, 0, 0);
            if (result is not null) return result;

            string? Entry(string relative, int offset, int depth)
            {
                if (_remainingProbes <= 0 || depth > 8 || !IsSubdirectory(relative)) return null;
                string path = Path.Combine(root, relative);
                if (Exists(path))
                {
                    DesktopThemeDocument document = Document(path);
                    if (document.Get("Desktop Entry", "Hidden") == "true") return string.Empty;
                    string icon = document.Get("Desktop Entry", "Icon").Replace("\\s", " ", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
                    if (IsName(icon)) return FindIcon(icon);
                    // Absolute paths are allowed ONLY from a local desktop file,
                    // never directly from OSC fields. The daemon handles decoding.
                    if (Path.IsPathFullyQualified(icon) && Exists(icon)) return icon;
                    return string.Empty;
                }
                // Desktop file IDs flatten subdirectories using '-'. Try the
                // direct name first, then bounded alternatives in root order.
                for (int i = offset; i < relative.Length && _remainingProbes > 0; i++)
                {
                    if (relative[i] != '-') continue;
                    string nested = relative[..i] + "/" + relative[(i + 1)..];
                    string? found = Entry(nested, i + 1, depth + 1);
                    if (found is not null) return found;
                }
                return null;
            }
        }
        return string.Empty;
    }

    public NotificationSound ResolveSound(string name)
    {
        if (_environment is null) Refresh();
        if (name == "silent") return new(string.Empty, string.Empty, true);
        if (name == "system" || !IsName(name)) return new(string.Empty, string.Empty, false);
        string mapped = name switch
        {
            "error" => "dialog-error", "warn" or "warning" => "dialog-warning",
            "info" => "dialog-information", "question" => "dialog-question", _ => name,
        };
        if (_sounds.TryGetValue(mapped, out NotificationSound cached)) return cached;
        _remainingProbes = 8192;
        DesktopThemeEnvironment environment = _environment!;
        HashSet<string> visited = new(StringComparer.Ordinal);
        string[] locales = Locales(environment.Locale);
        string file = InTheme("__custom", 0);
        if (file.Length == 0) file = InTheme(environment.SoundTheme, 0);
        if (file.Length == 0) file = InTheme("freedesktop", 0);
        if (file.Length == 0)
            foreach (string root in environment.SoundRoots)
                if ((file = SoundFile(root)).Length > 0) break;
        bool silent = file.EndsWith(".disabled", StringComparison.Ordinal);
        NotificationSound result = new(file.Length > 0 && !silent ? mapped : string.Empty, silent ? string.Empty : file, silent);
        if (_sounds.Count >= 256) _sounds.Clear();
        if (_remainingProbes > 0) _sounds[mapped] = result;
        return result;

        string InTheme(string theme, int depth)
        {
            if (!IsName(theme) || depth >= 16 || visited.Count >= 32 || !visited.Add(theme)) return string.Empty;
            DesktopThemeDocument document = Theme(environment.SoundRoots, theme);
            string[] directories = document.List("Sound Theme", "Directories");
            foreach (string profile in new[] { "stereo", string.Empty })
                for (int i = 0; i < Math.Min(directories.Length, 1024) && _remainingProbes > 0; i++)
                {
                    string directory = directories[i];
                    if (!IsSubdirectory(directory) || document.Get(directory, "OutputProfile") != profile) continue;
                    foreach (string root in environment.SoundRoots)
                        if (SoundFile(Path.Combine(root, theme, directory)) is { Length: > 0 } found) return found;
                }
            foreach (string parent in document.List("Sound Theme", "Inherits"))
            {
                if (parent == "freedesktop") continue;
                string inherited = InTheme(parent, depth + 1);
                if (inherited.Length > 0) return inherited;
            }
            return string.Empty;
        }

        string SoundFile(string directory)
        {
            string candidate = mapped;
            while (_remainingProbes > 0)
            {
                foreach (string locale in locales)
                    foreach (string extension in new[] { ".disabled", ".oga", ".ogg", ".wav" })
                    {
                        string path = Path.Combine(directory, locale, candidate + extension);
                        if (Exists(path)) return path;
                    }
                int hyphen = candidate.LastIndexOf('-');
                if (hyphen <= 0) break;
                candidate = candidate[..hyphen];
            }
            return string.Empty;
        }
    }

    private DesktopThemeDocument Theme(string[] roots, string theme)
    {
        foreach (string root in roots)
        {
            string path = Path.Combine(root, theme, "index.theme");
            if (_documents.TryGetValue(path, out DesktopThemeDocument? cached)) return cached;
            if (Exists(path)) return Document(path);
        }
        return new(null);
    }

    private DesktopThemeDocument Document(string path)
    {
        if (_documents.TryGetValue(path, out DesktopThemeDocument? cached)) return cached;
        DesktopThemeDocument document = new(files.ReadText(path));
        if (_documents.Count >= 64) _documents.Clear();
        _documents[path] = document;
        return document;
    }

    private bool Exists(string path) => _remainingProbes-- > 0 && files.Exists(path);

    internal static bool IsName(string value)
    {
        if (value.Length is 0 or > 256 || value is "." or "..") return false;
        foreach (char character in value)
            if (!char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.') return false;
        return true;
    }

    private static bool IsSubdirectory(string value)
    {
        if (value == ".") return true; // __custom sound overrides
        if (value.Length is 0 or > 512) return false;
        foreach (string part in value.Split('/')) if (!IsName(part)) return false;
        return true;
    }

    internal static string[] Locales(string value)
    {
        int encoding = value.IndexOf('.');
        if (encoding >= 0)
        {
            int modifier = value.IndexOf('@', encoding);
            value = value[..encoding] + (modifier >= 0 ? value[modifier..] : string.Empty);
        }
        List<string> result = new();
        Add(value);
        int at = value.IndexOf('@');
        if (at >= 0) { value = value[..at]; Add(value); }
        int underscore = value.IndexOf('_');
        if (underscore >= 0) Add(value[..underscore]);
        Add("C"); result.Add(string.Empty);
        return result.ToArray();

        void Add(string locale)
        {
            // @ is only a locale modifier, never a path delimiter.
            if (IsName(locale.Replace('@', '-')) && !result.Contains(locale, StringComparer.Ordinal)) result.Add(locale);
        }
    }
}
