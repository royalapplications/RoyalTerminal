// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Headless.XUnit;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Avalonia.Settings;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalSettingsPanelStateTests
{
    [AvaloniaFact]
    public void CategoryStates_ForwardValuesAndPropertyChanges()
    {
        TerminalSettingsPanelState state = new();
        string? changedProperty = null;
        state.Session.PropertyChanged += (_, args) => changedProperty = args.PropertyName;

        state.SessionName = "Profile A";
        Assert.Equal("Profile A", state.Session.SessionName);
        Assert.Equal(nameof(TerminalSettingsPanelState.SessionName), changedProperty);

        state.Connection.WorkingDirectory = "/tmp/royalterminal";
        Assert.Equal("/tmp/royalterminal", state.WorkingDirectory);

        state.Logging.EventLogEnabled = false;
        Assert.False(state.EventLogEnabled);
    }

    [AvaloniaFact]
    public void UpdateFromRuntime_UpdatesWrappersWithoutDirtyingState()
    {
        TerminalSettingsPanelState state = new();
        state.MarkSaved();
        state.SessionName = "Edited";
        Assert.True(state.IsDirty);

        state.MarkSaved();
        state.UpdateFromRuntime(current =>
        {
            current.SessionName = "Runtime";
            current.EnableLigatures = true;
            current.ReflowOnResize = false;
            current.SixelGraphicsEnabled = false;
            current.EventLogEnabled = false;
            current.FontSize = 16;
            current.SelectedPasteSafetyPolicy = TerminalPasteSafetyPolicy.BlockUnsafe;
        });

        Assert.False(state.IsDirty);
        Assert.Equal("Runtime", state.Session.SessionName);
        Assert.True(state.TerminalBehavior.EnableLigatures);
        Assert.False(state.Logging.EventLogEnabled);
        Assert.Equal(16, state.Appearance.FontSize);
        Assert.False(state.TerminalBehavior.ReflowOnResize);
        Assert.False(state.TerminalBehavior.SixelGraphicsEnabled);
        Assert.Equal(TerminalPasteSafetyPolicy.BlockUnsafe, state.TerminalBehavior.SelectedPasteSafetyPolicy);

        TerminalSessionProfilesDocument document = state.BuildDocument();
        TerminalSessionProfile profile = Assert.Single(document.Profiles);
        Assert.Equal("BlockUnsafe", profile.Behavior.PasteSafetyPolicy);
        Assert.False(profile.Behavior.ReflowOnResize);
        Assert.False(profile.Behavior.SixelGraphicsEnabled);
    }

    [AvaloniaFact]
    public void DefaultProfile_EnablesSixelGraphics()
    {
        TerminalSettingsPanelState state = new();

        Assert.True(state.SixelGraphicsEnabled);

        TerminalSessionProfilesDocument document = state.BuildDocument();
        TerminalSessionProfile profile = Assert.Single(document.Profiles);
        Assert.True(profile.Behavior.SixelGraphicsEnabled);
    }

    [AvaloniaFact]
    public void FontSettings_LoadFontFile_UpdatesStateAndPersistsProfile()
    {
        TerminalSettingsPanelState state = new();
        state.MarkSaved();

        string fontPath = Path.Combine(Path.GetTempPath(), "RoyalTerminal.CustomFont.otf");
        state.LoadFontFile(fontPath);

        Assert.True(state.IsDirty);
        Assert.Equal(TerminalFontSource.File, state.SelectedFontSource);
        Assert.True(state.Appearance.IsFileFontSourceSelected);
        Assert.False(state.Appearance.IsSystemFontSourceSelected);
        Assert.Equal(fontPath, state.Appearance.FontFilePath);
        Assert.Equal("RoyalTerminal.CustomFont", state.Appearance.FontFamilyName);
        Assert.NotEmpty(state.Appearance.SystemFontFamilies);

        TerminalSessionProfilesDocument document = state.BuildDocument();
        TerminalSessionProfile profile = Assert.Single(document.Profiles);
        Assert.Equal(TerminalFontSource.File, profile.Appearance.FontSource);
        Assert.Equal(fontPath, profile.Appearance.FontFilePath);
        Assert.Equal("RoyalTerminal.CustomFont", profile.Appearance.FontFamilyName);
    }

    [AvaloniaFact]
    public void TextHighlightRules_CanBeEditedAndPersisted()
    {
        TerminalSettingsPanelState state = new();
        state.MarkSaved();

        state.Appearance.AddTextHighlightRuleCommand.Execute(null);
        TerminalSettingsTextHighlightingModeOption realtimeMode = Assert.Single(
            state.Appearance.TextHighlightingModes,
            mode => mode.Mode == TerminalTextHighlightingMode.Realtime);
        state.Appearance.SelectedTextHighlightingMode = realtimeMode;
        TerminalSettingsHighlightRuleState rule = Assert.Single(state.Appearance.TextHighlightRules);
        rule.Name = "Errors";
        rule.Pattern = "ERROR";
        rule.IsForegroundEnabled = true;
        rule.ForegroundColor = "#FF4DFF";
        rule.IsBackgroundEnabled = true;
        rule.BackgroundColor = "#3B003B";

        Assert.True(state.IsDirty);

        TerminalSessionProfilesDocument document = state.BuildDocument();
        TerminalSessionProfile profile = Assert.Single(document.Profiles);
        Assert.Equal(TerminalTextHighlightingMode.Realtime, profile.Appearance.TextHighlightingMode);
        TerminalSessionTextHighlightRule persisted = Assert.Single(profile.Appearance.TextHighlightRules);
        Assert.Equal("Errors", persisted.Name);
        Assert.Equal("ERROR", persisted.Pattern);
        Assert.Equal("#FF4DFF", persisted.ForegroundColor);
        Assert.Equal("#3B003B", persisted.BackgroundColor);
        Assert.Null(persisted.DarkForegroundColor);
        Assert.Null(persisted.DarkBackgroundColor);
    }
}
