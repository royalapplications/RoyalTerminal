# Bundled terminal fallback

`JetBrainsMono-Regular.ttf` is an unchanged copy of
`external/ghostty/src/font/res/JetBrainsMonoNoNF-Regular.ttf` from the pinned
Ghostty submodule. Keeping a copy here makes the renderer build independent of
the submodule. It is used only when no installed monospace font is usable.

JetBrains Mono is copyright 2020 The JetBrains Mono Project Authors
(https://github.com/JetBrains/JetBrainsMono), licensed under OFL-1.1. The copyright
notice and full license are in `JetBrainsMono-OFL.txt`, embedded beside the font
and included under `licenses/` in the renderer NuGet package. Renderer code
remains licensed under MIT.
