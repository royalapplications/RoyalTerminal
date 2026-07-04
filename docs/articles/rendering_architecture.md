# RoyalTerminal Rendering Architecture

This document details the pipeline of RoyalTerminal, tracing how raw data moves from the PTY/VT sequence parser all the way to GPU-accelerated frames on the screen. It compares the standard **SkiaSharp** rendering pipeline with the high-performance **Swift/Metal** native rendering pipeline.

---

## 1. The Core Pipeline: VT to Screen

The rendering process starts with external terminal processes sending raw VT escape sequences and ends with Avalonia compositing the final frames.

```
+---------------+      +--------------+      +----------------+
|  PTY / VtNet  | ---> |   VT Parser  | ---> | TerminalScreen |
|  (Raw Bytes)  |      |  (VT100/OSC) |      | (Logical Grid) |
+---------------+      +--------------+      +----------------+
                                                      |
                                                      v
                                             +------------------+
                                             |  Presenter Loop  |
                                             | (Dirtiness Sync) |
                                             +------------------+
                                                      |
                             +------------------------+------------------------+
                             |                                                 |
                             v                                                 v
                  [ Skia Rendering Path ]                           [ Swift/Metal Rendering Path ]
                             |                                                 |
                             v                                                 v
                  +--------------------+                            +---------------------+
                  | SkiaTerminalRender |                            | SwiftRenderSurface  |
                  | (C# Draw Canvas)   |                            | (C# Wrapper)        |
                  +--------------------+                            +---------------------+
                             |                                                 |
                             v                                                 v
                  +--------------------+                            +---------------------+
                  |   HarfBuzz Shaper  |                            | libswift_terminal_  |
                  | (CPU Font Shaping) |                            | renderer (Swift/MTL)|
                  +--------------------+                            +---------------------+
                             |                                                 |
                             |                                                 v
                             |                                      +---------------------+
                             |                                      |     GlyphAtlas      |
                             |                                      | (CoreText Cache GPU)|
                             |                                      +---------------------+
                             |                                                 |
                             v                                                 v
                  +--------------------+                            +---------------------+
                  |   Skia GPU Frame   | <------------------------- |  Shared MTLTexture  |
                  |   (Compositor)     |      (Skia GPU Binding)    |  (Direct Draw GPU)  |
                  +--------------------+                            +---------------------+
                             |
                             v
                  +--------------------+
                  |    macOS Screen    |
                  +--------------------+
```

---

## 2. Shared Initialization Stage (VT to Logical Grid)

1. **PTY/Transport Ingestion**: 
   Raw bytes from shell processes (e.g., `zsh`, `ssh`) are read by the transport layer (`PtyTransport` or `SshNetTransport`).
2. **VT Parsing**:
   The parser decodes ANSI escape codes (e.g., SGR parameters for foreground/background colors, cursor controls, screen erases).
3. **Logical Screen Update (`TerminalScreen`)**:
   Updates the logical grid, which consists of individual terminal lines. Each cell contains metadata:
   - Unicode codepoint (character)
   - Foreground and background ARGB colors
   - Cell attributes (Bold, Italic, Underline, Sixel, etc.)
   - Column width (1 or 2 columns for CJK characters)

---

## 3. Engine Comparison

### Pipeline A: Standard Skia Rendering Engine

```
[Logical Grid] ---> [Calculate Layout] ---> [Background Passes] ---> [HarfBuzz Text Shaping] ---> [Draw Glyphs to SKCanvas]
```

* **Text Shaping (CPU Bound)**:
  - For every line, cells are batched into runs of identical styles.
  - The C# presenter queries `HarfBuzzTextShaper` to shape the Unicode codepoint runs into positioned glyph indices.
  - HarfBuzz measures font metrics, kerning, and ligatures on the CPU, returning glyph clusters.
* **Layout and Drawing**:
  - The presenter invokes `SkiaTerminalRenderer` using an Avalonia-provided `SKCanvas`.
  - **Background Pass**: Draws rectangular shapes for cell backgrounds.
  - **Text Pass**: Uses `SKCanvas.DrawGlyphs` to draw text clusters using cached font textures.
* **Pros & Cons**:
  - *Pros*: Cross-platform; handles complex ligatures and international scripts accurately via HarfBuzz.
  - *Cons*: High CPU-side allocation overhead during text shaping on fast-flowing text; frequent garbage collections.

---

### Pipeline B: Swift/Metal Native Rendering Engine

```
[Logical Grid] ---> [Marshal raw cells to C] ---> [Swift Vertex Assembly] ---> [Metal GPU Render] ---> [Direct GPU Texture Share]
```

* **Zero-Copy Memory Marshalling**:
  - C# skips text shaping entirely. It locks the screen grid and passes a direct memory pointer of the raw cells (`SwiftTerminalCellNative*`) across the C-interop boundary.
* **Native Context and Sizing**:
  - The Swift renderer uses the Avalonia compositor's direct `MTLDevice` pointer, ensuring both frameworks operate on the same GPU context.
  - Scaled grid layout metrics (cell width, height, baseline, and font size scaled by the DPI factor) are synchronized via interop.
* **CoreText GPU Glyph Atlas**:
  - On startup or font/DPI change, Swift rasterizes the character set into a grayscale GPU texture atlas (`GlyphAtlas`) using macOS CoreText.
  - At render time, Swift queries the atlas to obtain texture coordinates (UVs) for each character.
* **Metal Rendering Pipeline**:
  - Swift structures cell data into sequentially packed float vertex arrays (`MetalVertex` for backgrounds/cursor, `MetalGlyphVertex` for text).
  - Background cells are batched by color and drawn as rectangular triangles using the `quad_vertex` and `quad_fragment` shaders.
  - Text cells are rendered using `glyph_vertex` and `glyph_fragment` shaders, mapping characters to texture atlas positions.
* **GPU Texture Binding**:
  - Rather than copying pixels back to C#, the rendering target is a shared Metal texture (`MTLTexture`).
  - C# wraps this texture handle into a Skia `GRBackendTexture` (without any CPU allocations) and tells the compositor to draw it directly on screen.
* **Pros & Cons**:
  - *Pros*: Near-zero CPU overhead; extremely high framerates; zero-copy texture sharing; highly responsive interop.
  - *Cons*: Limited to macOS/Metal-capable devices.

---

## 4. Key Data Structure Layouts (Swift/Metal Interop)

To maintain consistent data flow across the C# and Swift boundary, structures are aligned to 4-byte boundaries with zero padding:

### Cell Transfer Structure
```swift
struct SwiftTerminalCell {
    var codepoint: Int32        // 4 bytes
    var foreground: UInt32      // 4 bytes
    var background: UInt32      // 4 bytes
    var attributes: UInt8       // 1 byte  (Bold, Italic, etc.)
    var underlineStyle: UInt8   // 1 byte
    var decorations: UInt8      // 1 byte
    var width: UInt8            // 1 byte  (Grid width)
} // Total: 16 bytes. No padding.
```

### Background Vertex Structure (MetalVertex)
```swift
struct MetalVertex {
    var x: Float                // 4 bytes
    var y: Float                // 4 bytes
    var r: Float                // 4 bytes
    var g: Float                // 4 bytes
    var b: Float                // 4 bytes
    var a: Float                // 4 bytes
} // Total: 24 bytes. No padding. Matches shader attributes alignment.
```

### Text Vertex Structure (MetalGlyphVertex)
```swift
struct MetalGlyphVertex {
    var x: Float                // 4 bytes
    var y: Float                // 4 bytes
    var u: Float                // 4 bytes
    var v: Float                // 4 bytes
    var r: Float                // 4 bytes
    var g: Float                // 4 bytes
    var b: Float                // 4 bytes
    var a: Float                // 4 bytes
} // Total: 32 bytes. No padding.
```

---

## 5. Feature Support, Limitations & Graphics Protocols

### Text Layout and Formatting Support

| Feature | Skia Renderer | Swift/Metal Renderer |
| :--- | :--- | :--- |
| **Colors & Style Attributes** | Fully Supported | Fully Supported |
| **Underline / Strikethrough** | Drawn via Skia Canvas | Vector lines drawn via Metal Shaders |
| **Complex Script Shaping** | Supported via HarfBuzz (CPU) | Unsupported (cell-by-cell drawing) |
| **Programming Ligatures** | Supported via HarfBuzz (CPU) | Unsupported |
| **Fallback Fonts & Emojis** | Handled dynamically via Skia | Rasterized on-demand via CoreText into GlyphAtlas |

### Sixel & Kitty Graphics Protocols

* **Skia Renderer**: Fully supported. Decoded raster bitmaps (`TerminalRasterGraphics`) are cached as `SKBitmap` blocks and composited directly onto the screen grid.
* **Swift/Metal Renderer**: Unsupported. The C# interop transfer data structure (`SwiftTerminalCellNative`) is optimized exclusively for cell text, attributes, and colors. The Swift dynamic library does not handle image texture descriptors or compile-time raster image pipelines. Users requiring sixel/kitty graphics must use the Skia rendering engine.

