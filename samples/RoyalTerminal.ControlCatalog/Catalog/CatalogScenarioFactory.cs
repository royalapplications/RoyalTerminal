// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.ControlCatalog;

internal static class CatalogScenarioFactory
{
    public static List<ICatalogScenario> Create()
    {
        List<ICatalogScenario> scenarios =
        [
            new VtModeCatalogScenario(),
            new VtQueryCatalogScenario(),
            new OscCatalogScenario(),
            new DcsAndKittyCatalogScenario(),
            new TrackerCatalogScenario(),
            new InteractiveInputWindowCatalogScenario(),
            new InteractiveLiveInputPlaygroundScenario(),
            new RenderingModelCatalogScenario(),
            new ComplexGlyphAndVtCatalogScenario(),
            new VisualColorAndStyleGalleryScenario(),
            new VisualHyperlinkAndInlineGalleryScenario(),
            new VisualUnicodeAndSpriteGalleryScenario(),
            new VisualAttributeExtensionGalleryScenario(),
            new VisualDecSpecialGraphicsScenario(),
            new VisualWrappingAndTabStopsScenario(),
            new VisualVtEditingMechanicsScenario(),
            new VisualOscThemeMutationScenario(),
            new VisualSpriteFrameGalleryScenario(),
            new VisualTuiLayoutGalleryScenario(),
            new VisualVtStateDashboardScenario(),
            new InteractiveInputWindowBoardScenario(),
            new VisualPtyTranscriptScenario(),
            new TuiAppCompatibilityCatalogScenario(),
            new PtyLifecycleCatalogScenario(),
        ];

        scenarios.Add(new DelegateCatalogScenario(
            "Run full catalog sweep",
            "Execute every catalog entry and print combined summary.",
            includeInFullSweep: false,
            () => CatalogSweepRunner.Run(scenarios)));

        return scenarios;
    }
}
