// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.ControlCatalog;

internal sealed class CatalogApp
{
    private readonly CatalogCliRenderer _renderer;
    private readonly IReadOnlyList<ICatalogScenario> _scenarios;
    private readonly IReadOnlyList<ICatalogScenario> _validationScenarios;
    private readonly IReadOnlyList<ICatalogScenario> _visualScenarios;
    private readonly IReadOnlyList<ICatalogScenario> _interactiveScenarios;
    private readonly IReadOnlyList<ICatalogScenario> _rootMenuEntries;

    public CatalogApp(CatalogCliRenderer renderer, IReadOnlyList<ICatalogScenario> scenarios)
    {
        _renderer = renderer;
        _scenarios = scenarios;
        _validationScenarios = [.. scenarios.Where(static scenario => scenario.IncludeInFullSweep && !IsVisualScenario(scenario) && !IsInteractiveScenario(scenario))];
        _visualScenarios = [.. scenarios.Where(static scenario => IsVisualScenario(scenario))];
        _interactiveScenarios = [.. scenarios.Where(static scenario => IsInteractiveScenario(scenario))];
        _rootMenuEntries =
        [
            CreateRootEntry(
                "Validation scenarios",
                "Protocol/state checks for VT, PTY, trackers, and compatibility."),
            CreateRootEntry(
                "Visual scenarios",
                "Rich rendering galleries and realistic TUI screens for manual inspection."),
            CreateRootEntry(
                "Interactive scenarios",
                "Live rendered playgrounds for manual mouse/cursor/keyboard/window interaction."),
            CreateRootEntry(
                "Run full catalog sweep",
                "Execute validation, visual, and non-manual interactive scenario sets."),
        ];
    }

    public int Run()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            CatalogScenarioResult result = CatalogSweepRunner.Run(_scenarios);
            PlainResultWriter.Write(result);
            return result.Success ? 0 : 1;
        }

        int selectedIndex = 0;
        string statusLine = "Select a screen: validation, visual, or interactive.";

        while (true)
        {
            _renderer.RenderMenu(
                "RoyalTerminal VT/PTy Control Catalog",
                "Three main groups: validation checks, visual galleries, and interactive playgrounds.",
                _rootMenuEntries,
                selectedIndex,
                statusLine,
                exitLabel: "Exit",
                footerHint: "Enter: open selection  |  R: full sweep  |  Q/Esc: quit");

            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    selectedIndex = selectedIndex <= 0 ? _rootMenuEntries.Count : selectedIndex - 1;
                    continue;

                case ConsoleKey.DownArrow:
                    selectedIndex = selectedIndex >= _rootMenuEntries.Count ? 0 : selectedIndex + 1;
                    continue;

                case ConsoleKey.R:
                {
                    CatalogScenarioResult result = CatalogSweepRunner.Run(_scenarios);
                    _renderer.RenderResult(result);
                    _ = Console.ReadKey(intercept: true);
                    statusLine = $"Full sweep: {(result.Success ? "PASS" : "FAIL")}";
                    continue;
                }

                case ConsoleKey.Enter:
                {
                    if (selectedIndex == _rootMenuEntries.Count)
                    {
                        return 0;
                    }

                    if (selectedIndex == 0)
                    {
                        statusLine = RunScenarioScreen(
                            title: "Validation scenario screen",
                            subtitle: "Focused protocol/state verification scenarios.",
                            scenarios: _validationScenarios,
                            emptyMessage: "No validation scenarios available.");
                        continue;
                    }

                    if (selectedIndex == 1)
                    {
                        statusLine = RunScenarioScreen(
                            title: "Visual scenario screen",
                            subtitle: "Rich manual visual inspection scenarios and realistic TUI layouts.",
                            scenarios: _visualScenarios,
                            emptyMessage: "No visual scenarios available.");
                        continue;
                    }

                    if (selectedIndex == 2)
                    {
                        statusLine = RunScenarioScreen(
                            title: "Interactive scenario screen",
                            subtitle: "Rendered scenarios where you can interact with live mouse/cursor/keyboard/window controls.",
                            scenarios: _interactiveScenarios,
                            emptyMessage: "No interactive scenarios available.");
                        continue;
                    }

                    CatalogScenarioResult fullSweep = CatalogSweepRunner.Run(_scenarios);
                    _renderer.RenderResult(fullSweep);
                    _ = Console.ReadKey(intercept: true);
                    statusLine = $"Full sweep: {(fullSweep.Success ? "PASS" : "FAIL")}";
                    continue;
                }

                case ConsoleKey.Escape:
                case ConsoleKey.Q:
                    return 0;
            }
        }
    }

    private string RunScenarioScreen(
        string title,
        string subtitle,
        IReadOnlyList<ICatalogScenario> scenarios,
        string emptyMessage)
    {
        if (scenarios.Count == 0)
        {
            return emptyMessage;
        }

        int selectedIndex = 0;
        string statusLine = "Use Up/Down to choose, Enter to run, R for full sweep, Q/Esc to go back.";

        while (true)
        {
            _renderer.RenderMenu(
                title,
                subtitle,
                scenarios,
                selectedIndex,
                statusLine,
                exitLabel: "Back",
                footerHint: "Enter: run selection  |  R: full sweep  |  Q/Esc: back");

            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    selectedIndex = selectedIndex <= 0 ? scenarios.Count : selectedIndex - 1;
                    continue;

                case ConsoleKey.DownArrow:
                    selectedIndex = selectedIndex >= scenarios.Count ? 0 : selectedIndex + 1;
                    continue;

                case ConsoleKey.R:
                {
                    CatalogScenarioResult result = CatalogSweepRunner.Run(_scenarios);
                    _renderer.RenderResult(result);
                    _ = Console.ReadKey(intercept: true);
                    statusLine = $"Full sweep: {(result.Success ? "PASS" : "FAIL")}";
                    continue;
                }

                case ConsoleKey.Enter:
                {
                    if (selectedIndex == scenarios.Count)
                    {
                        return $"{title}: back";
                    }

                    ICatalogScenario scenario = scenarios[selectedIndex];
                    CatalogScenarioResult result = scenario.Execute();
                    _renderer.RenderResult(result);
                    _ = Console.ReadKey(intercept: true);
                    statusLine = $"{scenario.Title}: {(result.Success ? "PASS" : "FAIL")}";
                    continue;
                }

                case ConsoleKey.Escape:
                case ConsoleKey.Q:
                    return $"{title}: back";
            }
        }
    }

    private static bool IsVisualScenario(ICatalogScenario scenario)
    {
        return scenario.Title.StartsWith("Visual ", StringComparison.Ordinal);
    }

    private static bool IsInteractiveScenario(ICatalogScenario scenario)
    {
        return scenario.Title.StartsWith("Interactive ", StringComparison.Ordinal);
    }

    private static ICatalogScenario CreateRootEntry(string title, string description)
    {
        return new DelegateCatalogScenario(
            title,
            description,
            includeInFullSweep: false,
            static () => new CatalogScenarioResult("Root menu", true, Array.Empty<string>()));
    }
}
