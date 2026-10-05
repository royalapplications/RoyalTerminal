// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.App.Services.Notifications;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class FreedesktopNotificationResourceTests
{
    // Kitty requires first-available name ordering before supplied image data.
    // Use freedesktop icon/sound theme inheritance, not GTK initialization, on
    // the owned notification worker. All filesystem/settings sources are fakes.
    [Fact]
    public void IconLookupPrefersNameOrderThenCurrentThemeBeforeParentSize()
    {
        Files files = new();
        files.Put("/icons/custom/index.theme", IconTheme("parent", "16/apps", 16));
        files.Put("/icons/parent/index.theme", IconTheme("", "64/apps", 64));
        files.Put("/icons/custom/16/apps/first.png");
        files.Put("/icons/custom/16/apps/second.png");
        files.Put("/icons/parent/64/apps/first.png");
        FreedesktopNotificationResources resources = Resources(files);
        Assert.Equal("/icons/custom/16/apps/first.png", resources.ResolveIcon(["missing", "first", "second"], null));
        files.Remove("/icons/custom/16/apps/first.png");
        resources = Resources(files);
        Assert.Equal("/icons/parent/64/apps/first.png", resources.ResolveIcon(["first", "second"], null));
    }

    [Fact]
    public void IconExactScaleNearestSizeInheritanceAndUnthemedFallbackAreOrdered()
    {
        Files files = new();
        files.Put("/icons/custom/index.theme", """
            [Icon Theme]
            Directories=large,small,retina,exact
            Inherits=loop,hicolor
            [large]
            Size=128
            Type=Fixed
            [small]
            Size=32
            Type=Fixed
            [retina]
            Size=32
            Scale=2
            Type=Fixed
            [exact]
            Size=64
            Type=Fixed
            """);
        files.Put("/icons/loop/index.theme", IconTheme("custom", "64/apps", 64));
        files.Put("/icons/hicolor/index.theme", IconTheme("", "64/apps", 64));
        foreach (string directory in new[] { "large", "small", "retina", "exact" }) files.Put($"/icons/custom/{directory}/item.png");
        Assert.Equal("/icons/custom/exact/item.png", Resources(files).ResolveIcon(["item"], null));
        files.Remove("/icons/custom/exact/item.png");
        Assert.Equal("/icons/custom/retina/item.png", Resources(files).ResolveIcon(["item"], null));
        files.Remove("/icons/custom/retina/item.png");
        Assert.Equal("/icons/custom/small/item.png", Resources(files).ResolveIcon(["item"], null));
        files.Put("/icons/hicolor/64/apps/fallback.svg");
        Assert.Equal("/icons/hicolor/64/apps/fallback.svg", Resources(files).ResolveIcon(["fallback"], null));
        files.Put("/icons/flat.xpm");
        Assert.Equal("/icons/flat.xpm", Resources(files).ResolveIcon(["flat"], null));
    }

    [Theory]
    [InlineData("error", "dialog-error")]
    [InlineData("warn", "dialog-warning")]
    [InlineData("warning", "dialog-warning")]
    [InlineData("info", "dialog-information")]
    [InlineData("question", "dialog-question")]
    [InlineData("help", "help-browser")]
    [InlineData("file-manager", "system-file-manager")]
    [InlineData("system-monitor", "utilities-system-monitor")]
    [InlineData("text-editor", "accessories-text-editor")]
    public void AllStandardIconNamesMapToDesktopSymbols(string name, string symbol)
    {
        Files files = new();
        files.Put($"/icons/{symbol}.png");
        Assert.Equal($"/icons/{symbol}.png", Resources(files).ResolveIcon([name], null));
    }

    [Fact]
    public void DesktopEntriesAndImplicitApplicationNamesAreLocalMetadataOnly()
    {
        Files files = new();
        files.Put("/apps/vendor/editor.desktop", "[Desktop Entry]\nIcon=editor-logo\nExec=must-not-run");
        files.Put("/icons/editor-logo.svg");
        files.Put("/apps/absolute.desktop", "[Desktop Entry]\nIcon=/installed/logo.png\nExec=must-not-run");
        files.Put("/installed/logo.png");
        FreedesktopNotificationResources resources = Resources(files);
        Assert.Equal("/icons/editor-logo.svg", resources.ResolveIcon([], "vendor-editor"));
        Assert.Equal("/icons/editor-logo.svg", resources.ResolveIcon(["vendor-editor.desktop"], null));
        Assert.Equal("/installed/logo.png", resources.ResolveIcon(["absolute"], null));
        Assert.Empty(resources.ResolveIcon(["missing"], "vendor-editor"));
        Assert.Empty(resources.ResolveIcon(["/installed/logo.png"], null));
        Assert.DoesNotContain(files.Reads, path => path.Contains("Exec", StringComparison.Ordinal));
    }

    [Fact]
    public void XdgRootAndFirstIndexOrderingArePreserved()
    {
        Files files = new();
        files.Put("/user/custom/index.theme", IconTheme("", "user-directory", 64));
        files.Put("/system/custom/index.theme", IconTheme("", "system-directory", 64));
        files.Put("/system/custom/user-directory/icon.png");
        files.Put("/system/custom/system-directory/icon.png");
        DesktopThemeEnvironment environment = Environment() with { IconRoots = ["/user", "/system"] };
        Assert.Equal("/system/custom/user-directory/icon.png", Resources(files, environment).ResolveIcon(["icon"], null));
        files.Put("/user/custom/user-directory/icon.png");
        Assert.Equal("/user/custom/user-directory/icon.png", Resources(files, environment).ResolveIcon(["icon"], null));
    }

    [Fact]
    public void HiddenDesktopEntryMasksLowerPriorityInstalledEntry()
    {
        Files files = new();
        files.Put("/user/app.desktop", "[Desktop Entry]\nHidden=true");
        files.Put("/system/app.desktop", "[Desktop Entry]\nIcon=app-logo");
        files.Put("/icons/app-logo.png");
        DesktopThemeEnvironment environment = Environment() with { ApplicationRoots = ["/user", "/system"] };
        Assert.Empty(Resources(files, environment).ResolveIcon(["app"], null));
        Assert.DoesNotContain("/system/app.desktop", files.Reads);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("../outside")]
    [InlineData("file:///tmp/payload")]
    [InlineData("https://example.com/icon")]
    [InlineData("a\\b")]
    [InlineData("a\0b")]
    public void RemoteResourceFieldsCannotBecomePaths(string name)
    {
        Files files = new();
        FreedesktopNotificationResources resources = Resources(files);
        resources.Refresh(); files.Probes.Clear(); files.Reads.Clear();
        Assert.Empty(resources.ResolveIcon([name], null));
        Assert.Empty(resources.ResolveSound(name).File);
        Assert.Empty(files.Probes);
        Assert.Empty(files.Reads);
    }

    [Fact]
    public void MaliciousThemeDirectoriesAndFlattenedDesktopIdsCannotEscapeRoots()
    {
        Files files = new();
        files.Put("/icons/custom/index.theme", IconTheme("../escape", "../escape", 64));
        FreedesktopNotificationResources resources = Resources(files);
        Assert.Empty(resources.ResolveIcon(["-escape", "app-..-escape", "a--b"], null));
        foreach (string path in files.Probes)
        {
            Assert.DoesNotContain("/../", path, StringComparison.Ordinal);
            Assert.True(path.StartsWith("/icons/", StringComparison.Ordinal) ||
                path.StartsWith("/apps/", StringComparison.Ordinal) || path.StartsWith("/sounds/", StringComparison.Ordinal), path);
        }
    }

    [Fact]
    public void IconAndSoundMissesAreCachedAndThemeChangesInvalidateThem()
    {
        Files files = new();
        Clock clock = new();
        FreedesktopNotificationResources resources = Resources(files, clock: clock);
        resources.Refresh();
        Assert.False(resources.SupportsNamedSounds);
        Assert.Empty(resources.ResolveIcon(["new"], null));
        files.Probes.Clear();
        Assert.Empty(resources.ResolveIcon(["new"], null));
        Assert.Empty(files.Probes);
        files.Put("/icons/new.png"); files.Put("/sounds/dialog.wav");
        resources.Refresh();
        Assert.Empty(resources.ResolveIcon(["new"], null));
        clock.Advance(); resources.Refresh();
        Assert.Equal("/icons/new.png", resources.ResolveIcon(["new"], null));
        Assert.True(resources.SupportsNamedSounds);
        files.Probes.Clear();
        Assert.Equal("/sounds/dialog.wav", resources.ResolveSound("error").File);
        Assert.Empty(files.Probes);
    }

    [Theory]
    [InlineData("error", "dialog-error")]
    [InlineData("warning", "dialog-warning")]
    [InlineData("warn", "dialog-warning")]
    [InlineData("info", "dialog-information")]
    [InlineData("question", "dialog-question")]
    [InlineData("message-new-instant", "message-new-instant")]
    public void NamedSoundsSupplyFileAsWellAsOptionalNameHint(string name, string mapped)
    {
        Files files = new();
        files.Put("/sounds/custom/index.theme", SoundTheme("freedesktop"));
        files.Put($"/sounds/custom/stereo/{mapped}.oga");
        Assert.Equal(new NotificationSound(mapped, $"/sounds/custom/stereo/{mapped}.oga", false), Resources(files).ResolveSound(name));
    }

    [Fact]
    public void SoundDisabledOverridesLocaleInheritanceAndGenericFallback()
    {
        Files files = new();
        files.Put("/sounds/__custom/index.theme", "[Sound Theme]\nDirectories=.\n[.]\n");
        files.Put("/sounds/__custom/./dialog-error.disabled");
        files.Put("/sounds/custom/index.theme", SoundTheme("parent"));
        files.Put("/sounds/parent/index.theme", SoundTheme("custom"));
        files.Put("/sounds/custom/stereo/fr/dialog-error.wav");
        files.Put("/sounds/parent/stereo/fr/dialog-question.ogg");
        files.Put("/sounds/parent/stereo/dialog-warning.oga");
        files.Put("/sounds/parent/stereo/fr/dialog-information.disabled");
        DesktopThemeEnvironment environment = Environment() with { Locale = "fr_CA.UTF-8@variant" };
        FreedesktopNotificationResources resources = Resources(files, environment);
        resources.Refresh();
        Assert.True(resources.SupportsNamedSounds); // Disabled by user is an honored choice.
        Assert.True(resources.ResolveSound("error").Silent);
        Assert.True(resources.ResolveSound("info").Silent);
        Assert.Equal("/sounds/parent/stereo/fr/dialog-question.ogg", resources.ResolveSound("question").File);
        Assert.Equal("/sounds/parent/stereo/dialog-warning.oga", resources.ResolveSound("warn").File);
        Assert.True(resources.ResolveSound("silent").Silent);
        Assert.Equal(new NotificationSound("", "", false), resources.ResolveSound("system"));
        Assert.Equal(new[] { "fr_CA@variant", "fr_CA", "fr", "C", "" }, FreedesktopNotificationResources.Locales(environment.Locale));
    }

    [Fact]
    public void PathologicalNameAndThemeSearchesHaveFiniteWorkAndDoNotCacheBudgetMisses()
    {
        Files files = new();
        files.Put("/icons/custom/index.theme", IconTheme("custom", "64/apps", 64));
        FreedesktopNotificationResources resources = Resources(files);
        resources.Refresh(); files.Probes.Clear();
        Assert.Empty(resources.ResolveIcon([string.Join('-', Enumerable.Repeat("a", 50))], null));
        Assert.InRange(files.Probes.Count, 1, 8192);
        files.Put("/icons/found.png");
        Assert.Equal("/icons/found.png", resources.ResolveIcon(["found"], null));
        Assert.Empty(resources.ResolveIcon([new string('a', 257)], null));
    }

    [Fact]
    public void EnvironmentReadsKdeOrGnomeAndUsesOnlyAbsoluteXdgRoots()
    {
        Files files = new();
        Dictionary<string, string> variables = new()
        {
            ["HOME"] = "/home/user", ["XDG_DATA_HOME"] = "relative-ignored", ["XDG_DATA_DIRS"] = "/data:relative:/data:/other",
            ["XDG_CONFIG_HOME"] = "/config", ["XDG_CURRENT_DESKTOP"] = "KDE", ["LANG"] = "en_GB.UTF-8",
        };
        files.Put("/config/kdeglobals", "[Icons]\nTheme=Breeze");
        LinuxDesktopThemeEnvironment source = new(files, key => variables.GetValueOrDefault(key), (_, key) => key == "icon-theme" ? "Adwaita" : "freedesktop");
        DesktopThemeEnvironment environment = source.Read();
        Assert.Equal("Breeze", environment.IconTheme);
        Assert.Equal(new[] { "/home/user/.local/share/applications", "/data/applications", "/other/applications" }, environment.ApplicationRoots);
        Assert.Equal("/home/user/.icons", environment.IconRoots[0]);
        variables["XDG_CURRENT_DESKTOP"] = "GNOME";
        Assert.Equal("Adwaita", source.Read().IconTheme);
        source = new(files, key => variables.GetValueOrDefault(key), (_, _) => null);
        files.Put("/config/gtk-3.0/settings.ini", "[Settings]\ngtk-icon-theme-name=\"Fallback\"\ngtk-sound-theme-name=Alerts");
        Assert.Equal(("Fallback", "Alerts"), (source.Read().IconTheme, source.Read().SoundTheme));
    }

    [Fact]
    public void MissingOptionalNativeSettingsDoNotPreventResourceLookup()
        => Assert.Null(LinuxDesktopThemeSettings.Read("org.royalapps.RoyalTerminal.Tests.MissingSchema", "icon-theme"));

    [Fact]
    public void ThemeDocumentsAndPhysicalReadsAreBounded()
    {
        DesktopThemeDocument document = new("[Icon Theme]\nDirectories=one,two\nSize=64\nSize=99\nBad=-1\nHuge=2147483647\n");
        Assert.Equal(new[] { "one", "two" }, document.List("Icon Theme", "Directories"));
        Assert.Equal(64, document.Number("Icon Theme", "Size", 0));
        Assert.Equal(2, document.Number("Icon Theme", "Bad", 2));
        Assert.Equal(2, document.Number("Icon Theme", "Huge", 2));
        Assert.Empty(new DesktopThemeDocument(new string('x', 256 * 1024 + 1)).Get("Icon Theme", "Directories"));
        string directory = Path.Combine(Path.GetTempPath(), "royalterminal-notification-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "index.theme");
            DesktopThemeFiles files = new();
            Assert.Null(files.ReadText(path));
            File.WriteAllText(path, "[Icon Theme]\nName=é\n");
            Assert.Equal("[Icon Theme]\nName=é\n", files.ReadText(path));
            File.WriteAllText(path, new string('x', 256 * 1024 + 1));
            Assert.Null(files.ReadText(path));
            Assert.Null(files.ReadText(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(true, "XID", true)]
    [InlineData(true, "wl_surface", false)]
    [InlineData(true, null, false)]
    [InlineData(false, "HWND", true)]
    public void FocusCapabilityUsesActualWindowBackendNotSessionEnvironment(bool linux, string? handle, bool expected)
        => Assert.Equal(expected, DesktopNotificationHost.SupportsWindowFocus(linux, handle));

    private static FreedesktopNotificationResources Resources(Files files, DesktopThemeEnvironment? environment = null, Clock? clock = null)
        => new(files, () => environment ?? Environment(), clock ?? new Clock());

    private static DesktopThemeEnvironment Environment() => new("custom", "custom", ["/icons"], ["/sounds"], ["/apps"], "C");
    private static string IconTheme(string parents, string directory, int size)
        => $"[Icon Theme]\nInherits={parents}\nDirectories={directory}\n[{directory}]\nSize={size}\nType=Fixed\n";
    private static string SoundTheme(string parents) => $"[Sound Theme]\nInherits={parents}\nDirectories=stereo\n[stereo]\nOutputProfile=stereo\n";

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance() => _ticks += TimeSpan.FromSeconds(6).Ticks;
    }

    private sealed class Files : IDesktopThemeFiles
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
        internal readonly List<string> Probes = new(), Reads = new();
        internal void Put(string path, string text = "") => _files[path] = text;
        internal void Remove(string path) => _files.Remove(path);
        public bool Exists(string path) { Probes.Add(path); return _files.ContainsKey(path); }
        public string? ReadText(string path) { Reads.Add(path); return _files.GetValueOrDefault(path); }
    }
}
