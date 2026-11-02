# System font names and monospace fallback

RoyalTerminal accepts exact installed family names first. If a name is not a
family, it also accepts a family followed by an advertised style, for example
`Source Code Pro Light` or `Cascadia Code Light`. The longest matching family
prefix wins, and the suffix must match a real advertised style (case-insensitive
in the Skia matcher). A genuine family whose name ends in `Light` wins over an
alias. Unknown suffixes never silently select the base family's regular face.

Aliases behave like explicitly named faces: they may override the logical style
category. An explicit `RegularStyle`/`BoldStyle`/etc. overrides an alias's style;
a missing explicit style remains unavailable. Legacy glyph caches derive their
bold/italic variants from the resolved family, so an unresolved requested name
cannot send styled text to an unrelated platform default. Glyph coverage uses
the same cache selection as rendering.

On an unavailable primary family, prefer a verified fixed-pitch Consolas face,
then common macOS/Linux monospace families, then another installed fixed-pitch
face with basic ASCII coverage. Fixed-pitch emoji/symbol fonts and LastResort
placeholder fonts (including those with ASCII glyphs) are rejected. Never accept
Skia's proportional default merely because it is non-null. If no installed
monospace font is usable, load the embedded JetBrains Mono Regular fallback.
Default control construction must succeed before hosts can apply a bundled font
through `FontSource.File`/`FontFilePath` or `TypefaceCollection`. The embedded face
is separately owned by each cache; styled faces use the existing synthesis policy
when no installed variants exist. The font and its OFL-1.1 copyright/license are
embedded in the renderer, and the license is also shipped in its NuGet package.
See [bundled font provenance](../../src/RoyalTerminal.Rendering.Skia/Fonts/README.md).
Explicitly requested, available proportional fonts remain supported. File-loaded
fonts and synthetic-style policy retain their existing behavior.

Reference decisions:

- [Windows Terminal](https://github.com/microsoft/terminal/blob/main/src/renderer/atlas/AtlasEngine.api.cpp)
  checks family availability and explicitly falls back to Consolas. RoyalTerminal
  follows that fallback policy, with additional platform fallbacks. Resolving
  advertised-style aliases is a deliberate compatibility extension for names
  stored by Windows font pickers; Windows Terminal instead exposes font weight.
- [Ghostty](https://github.com/ghostty-org/ghostty/blob/main/src/font/discovery.zig)
  separates family and advertised style. RoyalTerminal resolves legacy combined
  names into that same distinction without changing the native VT processor.
- [xterm.js](https://github.com/xtermjs/xterm.js/blob/master/typings/xterm.d.ts)
  exposes family and weight separately; its browser font fallback is not used as
  the native renderer's primary-font policy.

`SystemFontNameResolverTests` covers metadata-based aliases independently of
installed fonts, exact-family precedence, explicit style overrides, unknown
suffixes, fallback pitch/priority and public cache/renderer integration. Native
Skia integration also checks Cascadia aliases when those fonts are installed.

## Installed Windows reproduction

With Adobe Source Code Pro 2.042 static TTF Regular and Light installed per-user,
Windows GDI/GDI+ `InstalledFontCollection` lists both `Source Code Pro` and
`Source Code Pro Light`. Royal TS uses those names in `DisplayOptionsPage`.
Skia's system font list contains only `Source Code Pro`; `Light` is an advertised
style under that family, at weight 300. The same distinction is visible for
Cascadia Code/Mono Light and other weights in the GDI list.

The old raw Skia lookup of the actual picker value `Source Code Pro Light`
selects Segoe UI (400, 15x21 cells at 16 DIP). The resolver selects Source Code Pro
Light (300, 10x20 cells). Its regular-text output matches loading that installed
Light TTF directly, pixel-for-pixel. Installed-font regression cases now cover
Source Code Pro as well as Cascadia, with shaping enabled and disabled.

The DirectWrite selection sequence used by Windows Terminal selects the same
Light face with family `Source Code Pro` and weight 300. The combined GDI name
is unavailable in that font collection and falls back to Consolas. The forum
report does not identify the customer's exact font binary, version, or format;
this reproduction uses the same family and style from Adobe's static TTF release.
