import Foundation
import Metal
import CoreText
import CoreGraphics
import simd

// C-aligned Structures matching native_rendering_engine_spec.md

public struct SwiftRenderTheme {
    public var defaultForegroundOriginal: UInt32
    public var defaultBackgroundOriginal: UInt32
    public var cursorOriginal: UInt32
}

public struct SwiftTerminalCell {
    public var codepoint: Int32
    public var foreground: UInt32
    public var background: UInt32
    public var attributes: UInt8          // Maps to CellAttributes
    public var underlineStyle: UInt8      // Maps to TerminalUnderlineStyle
    public var decorations: UInt8         // Maps to CellDecorations
    public var width: UInt8               // Col width (1 or 2)
}

public struct SwiftRenderTargetDesc {
    public var backend: Int32
    public var targetKind: Int32
    public var pixelFormat: Int32
    public var width: Int32
    public var height: Int32
    public var sampleCount: UInt32
    public var deviceHandle: UnsafeMutableRawPointer?
    public var contextHandle: UnsafeMutableRawPointer?
    public var commandQueueHandle: UnsafeMutableRawPointer?
    public var commandBufferHandle: UnsafeMutableRawPointer?
    public var targetHandle: UnsafeMutableRawPointer?
    public var targetViewHandle: UnsafeMutableRawPointer?
    public var frameId: UInt64
    public var debugNameUtf8: UnsafePointer<CChar>?
}

// Struct layout for Metal vertex buffers
struct MetalVertex {
    var x: Float
    var y: Float
    var r: Float
    var g: Float
    var b: Float
    var a: Float
}

struct MetalGlyphVertex {
    var x: Float
    var y: Float
    var u: Float
    var v: Float
    var r: Float
    var g: Float
    var b: Float
    var a: Float
}

// The native render context
public final class SwiftRenderContext {
    public let device: MTLDevice
    public let commandQueue: MTLCommandQueue
    
    public init?(device: MTLDevice? = nil) {
        if let dev = device {
            self.device = dev
        } else if let dev = MTLCreateSystemDefaultDevice() {
            self.device = dev
        } else {
            return nil
        }
        
        guard let queue = self.device.makeCommandQueue() else {
            return nil
        }
        self.commandQueue = queue
    }
}

// Dynamic Glyph Atlas for monospaced rendering
final class GlyphAtlas {
    let device: MTLDevice
    let texture: MTLTexture
    
    let fontName: String
    let fontSize: CGFloat
    
    let cellWidth: Int
    let cellHeight: Int
    
    // CoreText Font references
    let font: CTFont
    let boldFont: CTFont
    let italicFont: CTFont
    let boldItalicFont: CTFont
    
    // Atlas layout dimensions
    private let atlasWidth = 2048
    private let atlasHeight = 2048
    private let colsInAtlas: Int
    private let rowsInAtlas: Int
    
    // Mapping from (codepoint, style) to slot index
    private var cache: [UInt64: Int] = [:]
    private var nextFreeSlot = 0
    
    init?(device: MTLDevice, fontName: String, fontSize: CGFloat) {
        self.device = device
        self.fontName = fontName
        self.fontSize = fontSize
        
        // Setup primary and style fonts
        let fontRef = CTFontCreateWithName(fontName as CFString, fontSize, nil)
        self.font = fontRef
        
        let desc = CTFontCopyFontDescriptor(fontRef)
        
        if let boldDesc = CTFontDescriptorCreateCopyWithSymbolicTraits(desc, .traitBold, .traitBold) {
            self.boldFont = CTFontCreateWithFontDescriptor(boldDesc, fontSize, nil)
        } else {
            self.boldFont = fontRef
        }
        
        if let italicDesc = CTFontDescriptorCreateCopyWithSymbolicTraits(desc, .traitItalic, .traitItalic) {
            self.italicFont = CTFontCreateWithFontDescriptor(italicDesc, fontSize, nil)
        } else {
            self.italicFont = fontRef
        }
        
        if let boldItalicDesc = CTFontDescriptorCreateCopyWithSymbolicTraits(desc, [.traitBold, .traitItalic], [.traitBold, .traitItalic]) {
            self.boldItalicFont = CTFontCreateWithFontDescriptor(boldItalicDesc, fontSize, nil)
        } else {
            self.boldItalicFont = fontRef
        }
        
        // Measure cell size using CoreText metrics
        var glyph = CTFontGetGlyphWithName(fontRef, "0" as CFString)
        if glyph == 0 {
            glyph = CTFontGetGlyphWithName(fontRef, "M" as CFString)
        }
        
        var advance = CGSize.zero
        CTFontGetAdvancesForGlyphs(fontRef, .horizontal, &glyph, &advance, 1)
        
        let ascent = CTFontGetAscent(fontRef)
        let descent = CTFontGetDescent(fontRef)
        let leading = CTFontGetLeading(fontRef)
        
        let calculatedHeight = ceil(ascent + descent + leading)
        let calculatedWidth = ceil(advance.width > 0 ? advance.width : fontSize * 0.6)
        
        self.cellWidth = max(1, Int(calculatedWidth))
        self.cellHeight = max(1, Int(calculatedHeight))
        
        self.colsInAtlas = atlasWidth / self.cellWidth
        self.rowsInAtlas = atlasHeight / self.cellHeight
        
        // Allocate grayscale atlas texture
        let texDesc = MTLTextureDescriptor.texture2DDescriptor(
            pixelFormat: .r8Unorm,
            width: atlasWidth,
            height: atlasHeight,
            mipmapped: false
        )
        texDesc.usage = [.shaderRead]
        guard let tex = device.makeTexture(descriptor: texDesc) else {
            return nil
        }
        self.texture = tex
        
        // Clear atlas texture to 0
        let zeroBytes = [UInt8](repeating: 0, count: atlasWidth * atlasHeight)
        tex.replace(
            region: MTLRegionMake2D(0, 0, atlasWidth, atlasHeight),
            mipmapLevel: 0,
            withBytes: zeroBytes,
            bytesPerRow: atlasWidth
        )
    }
    
    // Returns (U_min, V_min, U_max, V_max) for the glyph
    func getGlyphTexCoords(codepoint: Int32, attributes: UInt8) -> simd_float4 {
        let bold = (attributes & 1) != 0
        let italic = (attributes & 2) != 0
        
        let styleKey: UInt64 = (bold ? 1 : 0) | (italic ? 2 : 0)
        let cacheKey: UInt64 = (UInt64(codepoint) << 8) | styleKey
        
        if let slot = cache[cacheKey] {
            return getSlotTexCoords(slot: slot)
        }
        
        let slot = nextFreeSlot
        nextFreeSlot = (nextFreeSlot + 1) % (colsInAtlas * rowsInAtlas)
        cache[cacheKey] = slot
        
        rasterizeGlyph(codepoint: codepoint, bold: bold, italic: italic, into: slot)
        return getSlotTexCoords(slot: slot)
    }
    
    private func getSlotTexCoords(slot: Int) -> simd_float4 {
        let col = slot % colsInAtlas
        let row = slot / colsInAtlas
        
        let x = Float(col * cellWidth)
        let y = Float(row * cellHeight)
        
        let uMin = x / Float(atlasWidth)
        let vMin = y / Float(atlasHeight)
        let uMax = (x + Float(cellWidth)) / Float(atlasWidth)
        let vMax = (y + Float(cellHeight)) / Float(atlasHeight)
        
        return simd_make_float4(uMin, vMin, uMax, vMax)
    }
    
    private func rasterizeGlyph(codepoint: Int32, bold: Bool, italic: Bool, into slot: Int) {
        let col = slot % colsInAtlas
        let row = slot / colsInAtlas
        
        let x = col * cellWidth
        let y = row * cellHeight
        
        let byteCount = cellWidth * cellHeight
        var bitmapBytes = [UInt8](repeating: 0, count: byteCount)
        
        let colorSpace = CGColorSpaceCreateDeviceGray()
        guard let context = CGContext(
            data: &bitmapBytes,
            width: cellWidth,
            height: cellHeight,
            bitsPerComponent: 8,
            bytesPerRow: cellWidth,
            space: colorSpace,
            bitmapInfo: CGImageAlphaInfo.none.rawValue
        ) else {
            return
        }
        
        context.translateBy(x: 0, y: CGFloat(cellHeight))
        context.scaleBy(x: 1.0, y: -1.0)
        
        let activeFont: CTFont
        if bold && italic {
            activeFont = boldItalicFont
        } else if bold {
            activeFont = boldFont
        } else if italic {
            activeFont = italicFont
        } else {
            activeFont = font
        }
        
        if let scalar = UnicodeScalar(UInt32(codepoint)) {
            let str = String(scalar)
            let attributes: [NSAttributedString.Key: Any] = [
                NSAttributedString.Key(kCTFontAttributeName as String): activeFont,
                NSAttributedString.Key(kCTForegroundColorAttributeName as String): CGColor(gray: 1.0, alpha: 1.0)
            ]
            let attrStr = NSAttributedString(string: str, attributes: attributes)
            let line = CTLineCreateWithAttributedString(attrStr)
            
            let ascent = CTFontGetAscent(activeFont)
            let yOffset = CGFloat(cellHeight) - ascent
            
            context.textPosition = CGPoint(x: 0, y: yOffset)
            CTLineDraw(line, context)
        }
        
        texture.replace(
            region: MTLRegionMake2D(x, y, cellWidth, cellHeight),
            mipmapLevel: 0,
            withBytes: bitmapBytes,
            bytesPerRow: cellWidth
        )
    }
}

// Active Rendering Surface
public final class SwiftRenderSurface {
    public let context: SwiftRenderContext
    public let backend: Int32
    
    public var width: Int32 = 1
    public var height: Int32 = 1
    public var scaleX: Double = 1.0
    public var scaleY: Double = 1.0
    public var focused: Bool = true
    public var theme = SwiftRenderTheme(
        defaultForegroundOriginal: 0xFFD4D4D4,
        defaultBackgroundOriginal: 0xFF1E1E1E,
        cursorOriginal: 0xFFD4D4D4
    )
    
    private let renderPipelineState: MTLRenderPipelineState
    private let glyphPipelineState: MTLRenderPipelineState
    private let samplerState: MTLSamplerState
    
    private var atlas: GlyphAtlas?

    private var customCellWidth: Double?
    private var customCellHeight: Double?
    private var customCellBaseline: Double?
    private var currentFontName: String = "Courier"
    private var currentFontSize: CGFloat = 14.0

    public func setFont(fontName: String, fontSize: CGFloat) {
        self.currentFontName = fontName
        self.currentFontSize = fontSize
        recreateAtlas()
    }
    
    public func setCellSize(width: Double, height: Double, baseline: Double) {
        self.customCellWidth = width
        self.customCellHeight = height
        self.customCellBaseline = baseline
    }
    
    private func recreateAtlas() {
        let scaledSize = self.currentFontSize * CGFloat(self.scaleY)
        if let newAtlas = GlyphAtlas(device: context.device, fontName: self.currentFontName, fontSize: scaledSize) {
            self.atlas = newAtlas
        }
    }
    
    public init?(context: SwiftRenderContext, backend: Int32) {
        self.context = context
        self.backend = backend
        
        let shaderSource = """
        #include <metal_stdlib>
        using namespace metal;

        struct QuadVertexInput {
            float2 position [[attribute(0)]];
            float4 color    [[attribute(1)]];
        };

        struct QuadVertexOutput {
            float4 position [[position]];
            float4 color;
        };

        vertex QuadVertexOutput quad_vertex(QuadVertexInput in [[stage_in]],
                                            constant float2& viewportSize [[buffer(1)]]) {
            QuadVertexOutput out;
            float x = 2.0 * (in.position.x / viewportSize.x) - 1.0;
            float y = 1.0 - 2.0 * (in.position.y / viewportSize.y);
            out.position = float4(x, y, 0.0, 1.0);
            out.color = in.color;
            return out;
        }

        fragment float4 quad_fragment(QuadVertexOutput in [[stage_in]]) {
            return in.color;
        }

        struct GlyphVertexInput {
            float2 position  [[attribute(0)]];
            float2 texCoords [[attribute(1)]];
            float4 color     [[attribute(2)]];
        };

        struct GlyphVertexOutput {
            float4 position [[position]];
            float2 texCoords;
            float4 color;
        };

        vertex GlyphVertexOutput glyph_vertex(GlyphVertexInput in [[stage_in]],
                                              constant float2& viewportSize [[buffer(1)]]) {
            GlyphVertexOutput out;
            float x = 2.0 * (in.position.x / viewportSize.x) - 1.0;
            float y = 1.0 - 2.0 * (in.position.y / viewportSize.y);
            out.position = float4(x, y, 0.0, 1.0);
            out.texCoords = in.texCoords;
            out.color = in.color;
            return out;
        }

        fragment float4 glyph_fragment(GlyphVertexOutput in [[stage_in]],
                                       texture2d<float> texture [[texture(0)]],
                                       sampler textureSampler [[sampler(0)]]) {
            float intensity = texture.sample(textureSampler, in.texCoords).r;
            return float4(in.color.rgb, in.color.a * intensity);
        }
        """
        
        do {
            let library = try context.device.makeLibrary(source: shaderSource, options: nil)
            
            let quadVertex = library.makeFunction(name: "quad_vertex")
            let quadFragment = library.makeFunction(name: "quad_fragment")
            
            let quadDescriptor = MTLRenderPipelineDescriptor()
            quadDescriptor.vertexFunction = quadVertex
            quadDescriptor.fragmentFunction = quadFragment
            quadDescriptor.colorAttachments[0].pixelFormat = .bgra8Unorm
            
            let attachment = quadDescriptor.colorAttachments[0]!
            attachment.isBlendingEnabled = true
            attachment.rgbBlendOperation = .add
            attachment.alphaBlendOperation = .add
            attachment.sourceRGBBlendFactor = .sourceAlpha
            attachment.sourceAlphaBlendFactor = .sourceAlpha
            attachment.destinationRGBBlendFactor = .oneMinusSourceAlpha
            attachment.destinationAlphaBlendFactor = .oneMinusSourceAlpha
            
            let quadVertexDesc = MTLVertexDescriptor()
            quadVertexDesc.attributes[0].format = .float2
            quadVertexDesc.attributes[0].offset = 0
            quadVertexDesc.attributes[0].bufferIndex = 0
            quadVertexDesc.attributes[1].format = .float4
            quadVertexDesc.attributes[1].offset = MemoryLayout<simd_float2>.stride
            quadVertexDesc.attributes[1].bufferIndex = 0
            quadVertexDesc.layouts[0].stride = MemoryLayout<MetalVertex>.stride
            quadDescriptor.vertexDescriptor = quadVertexDesc
            
            self.renderPipelineState = try context.device.makeRenderPipelineState(descriptor: quadDescriptor)
            
            let glyphVertex = library.makeFunction(name: "glyph_vertex")
            let glyphFragment = library.makeFunction(name: "glyph_fragment")
            
            let glyphDescriptor = MTLRenderPipelineDescriptor()
            glyphDescriptor.vertexFunction = glyphVertex
            glyphDescriptor.fragmentFunction = glyphFragment
            glyphDescriptor.colorAttachments[0].pixelFormat = .bgra8Unorm
            
            let glyphAttachment = glyphDescriptor.colorAttachments[0]!
            glyphAttachment.isBlendingEnabled = true
            glyphAttachment.rgbBlendOperation = .add
            glyphAttachment.alphaBlendOperation = .add
            glyphAttachment.sourceRGBBlendFactor = .sourceAlpha
            glyphAttachment.sourceAlphaBlendFactor = .sourceAlpha
            glyphAttachment.destinationRGBBlendFactor = .oneMinusSourceAlpha
            glyphAttachment.destinationAlphaBlendFactor = .oneMinusSourceAlpha
            
            let glyphVertexDesc = MTLVertexDescriptor()
            glyphVertexDesc.attributes[0].format = .float2
            glyphVertexDesc.attributes[0].offset = 0
            glyphVertexDesc.attributes[0].bufferIndex = 0
            
            glyphVertexDesc.attributes[1].format = .float2
            glyphVertexDesc.attributes[1].offset = MemoryLayout<simd_float2>.stride
            glyphVertexDesc.attributes[1].bufferIndex = 0
            
            glyphVertexDesc.attributes[2].format = .float4
            glyphVertexDesc.attributes[2].offset = MemoryLayout<simd_float2>.stride * 2
            glyphVertexDesc.attributes[2].bufferIndex = 0
            
            glyphVertexDesc.layouts[0].stride = MemoryLayout<MetalGlyphVertex>.stride
            glyphDescriptor.vertexDescriptor = glyphVertexDesc
            
            self.glyphPipelineState = try context.device.makeRenderPipelineState(descriptor: glyphDescriptor)
            
            let samplerDesc = MTLSamplerDescriptor()
            samplerDesc.minFilter = .nearest
            samplerDesc.magFilter = .nearest
            self.samplerState = context.device.makeSamplerState(descriptor: samplerDesc)!
            
        } catch {
            print("Failed to initialize Metal rendering pipelines: \(error)")
            return nil
        }
        
        self.atlas = GlyphAtlas(device: context.device, fontName: "Courier", fontSize: 14.0)
    }
    
    public func setSize(width: Int32, height: Int32) {
        self.width = width
        self.height = height
    }
    
    public func setScale(scaleX: Double, scaleY: Double) {
        let changed = self.scaleX != scaleX || self.scaleY != scaleY
        self.scaleX = scaleX
        self.scaleY = scaleY
        if changed {
            recreateAtlas()
        }
    }
    
    public func setTheme(theme: SwiftRenderTheme) {
        self.theme = theme
    }
    
    public func setFocus(focused: Bool) {
        self.focused = focused
    }
    
    private func setVertexBufferOrBytes(
        encoder: MTLRenderCommandEncoder,
        bytes: UnsafeRawPointer,
        length: Int,
        index: Int
    ) {
        if length <= 4096 {
            encoder.setVertexBytes(bytes, length: length, index: index)
        } else if let buffer = context.device.makeBuffer(bytes: bytes, length: length, options: .storageModeShared) {
            encoder.setVertexBuffer(buffer, offset: 0, index: index)
        }
    }
    
    public func render(
        targetTexture: MTLTexture,
        cells: UnsafePointer<SwiftTerminalCell>,
        cellCount: Int,
        cols: Int,
        rows: Int,
        cursorCol: Int,
        cursorRow: Int,
        cursorVisible: Bool,
        cursorStyle: Int32
    ) -> Int32 {
        guard let atlas = self.atlas else {
            return 5
        }
        
        guard let commandBuffer = context.commandQueue.makeCommandBuffer() else {
            return 5
        }
        
        let renderPassDescriptor = MTLRenderPassDescriptor()
        let colorAttachment = renderPassDescriptor.colorAttachments[0]!
        colorAttachment.texture = targetTexture
        colorAttachment.loadAction = .clear
        
        let bgArgb = theme.defaultBackgroundOriginal
        let bgR = Float((bgArgb >> 16) & 0xFF) / 255.0
        let bgG = Float((bgArgb >> 8) & 0xFF) / 255.0
        let bgB = Float(bgArgb & 0xFF) / 255.0
        colorAttachment.clearColor = MTLClearColor(red: Double(bgR), green: Double(bgG), blue: Double(bgB), alpha: 1.0)
        colorAttachment.storeAction = .store
        
        guard let encoder = commandBuffer.makeRenderCommandEncoder(descriptor: renderPassDescriptor) else {
            return 5
        }
        
        let cellW = customCellWidth != nil ? Float(customCellWidth!) : Float(atlas.cellWidth)
        let cellH = customCellHeight != nil ? Float(customCellHeight!) : Float(atlas.cellHeight)
        
        var quadVertices: [MetalVertex] = []
        
        for r in 0..<rows {
            var col = 0
            while col < cols {
                let cellIndex = r * cols + col
                if cellIndex >= cellCount { break }
                let cell = cells[cellIndex]
                
                var batchLen = 1
                while (col + batchLen) < cols {
                    let nextIndex = r * cols + (col + batchLen)
                    if nextIndex >= cellCount { break }
                    if cells[nextIndex].background == cell.background {
                        batchLen += 1
                    } else {
                        break
                    }
                }
                
                let argb = cell.background
                let red = Float((argb >> 16) & 0xFF) / 255.0
                let green = Float((argb >> 8) & 0xFF) / 255.0
                let blue = Float(argb & 0xFF) / 255.0
                let alpha = Float((argb >> 24) & 0xFF) / 255.0
                
                let x = Float(col) * cellW
                let y = Float(r) * cellH
                let w = Float(batchLen) * cellW
                let h = cellH
                
                quadVertices.append(MetalVertex(x: x, y: y, r: red, g: green, b: blue, a: alpha))
                quadVertices.append(MetalVertex(x: x + w, y: y, r: red, g: green, b: blue, a: alpha))
                quadVertices.append(MetalVertex(x: x, y: y + h, r: red, g: green, b: blue, a: alpha))
                
                quadVertices.append(MetalVertex(x: x + w, y: y, r: red, g: green, b: blue, a: alpha))
                quadVertices.append(MetalVertex(x: x + w, y: y + h, r: red, g: green, b: blue, a: alpha))
                quadVertices.append(MetalVertex(x: x, y: y + h, r: red, g: green, b: blue, a: alpha))
                
                col += batchLen
            }
        }
        
        if !quadVertices.isEmpty {
            encoder.setRenderPipelineState(renderPipelineState)
            
            var viewportSize = simd_make_float2(Float(width), Float(height))
            encoder.setVertexBytes(&viewportSize, length: MemoryLayout<simd_float2>.stride, index: 1)
            
            self.setVertexBufferOrBytes(encoder: encoder, bytes: quadVertices, length: quadVertices.count * MemoryLayout<MetalVertex>.stride, index: 0)
            encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: quadVertices.count)
        }
        
        if cursorVisible && cursorCol >= 0 && cursorCol < cols && cursorRow >= 0 && cursorRow < rows {
            var cursorVertices: [MetalVertex] = []
            
            let cursorColor = theme.cursorOriginal
            let cRed = Float((cursorColor >> 16) & 0xFF) / 255.0
            let cGreen = Float((cursorColor >> 8) & 0xFF) / 255.0
            let cBlue = Float(cursorColor & 0xFF) / 255.0
            let cAlpha = Float((cursorColor >> 24) & 0xFF) / 255.0
            
            let x = Float(cursorCol) * cellW
            let y = Float(cursorRow) * cellH
            
            if cursorStyle == 0 {
                cursorVertices.append(MetalVertex(x: x, y: y, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
                cursorVertices.append(MetalVertex(x: x + cellW, y: y, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
                cursorVertices.append(MetalVertex(x: x, y: y + cellH, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
                cursorVertices.append(MetalVertex(x: x + cellW, y: y, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
                cursorVertices.append(MetalVertex(x: x + cellW, y: y + cellH, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
                cursorVertices.append(MetalVertex(x: x, y: y + cellH, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
            } else if cursorStyle == 1 {
                let lineH: Float = 2.0
                let lineY = y + cellH - lineH
                cursorVertices.append(MetalVertex(x: x, y: lineY, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
                cursorVertices.append(MetalVertex(x: x + cellW, y: lineY, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
                cursorVertices.append(MetalVertex(x: x, y: y + cellH, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
                cursorVertices.append(MetalVertex(x: x + cellW, y: lineY, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
                cursorVertices.append(MetalVertex(x: x + cellW, y: y + cellH, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
                cursorVertices.append(MetalVertex(x: x, y: y + cellH, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
            } else if cursorStyle == 2 {
                let lineW: Float = 2.0
                cursorVertices.append(MetalVertex(x: x, y: y, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
                cursorVertices.append(MetalVertex(x: x + lineW, y: y, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
                cursorVertices.append(MetalVertex(x: x, y: y + cellH, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
                cursorVertices.append(MetalVertex(x: x + lineW, y: y, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
                cursorVertices.append(MetalVertex(x: x + lineW, y: y + cellH, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
                cursorVertices.append(MetalVertex(x: x, y: y + cellH, r: cRed, g: cGreen, b: cBlue, a: cAlpha))
            }
            
            if !cursorVertices.isEmpty {
                encoder.setRenderPipelineState(renderPipelineState)
                var viewportSize = simd_make_float2(Float(width), Float(height))
                encoder.setVertexBytes(&viewportSize, length: MemoryLayout<simd_float2>.stride, index: 1)
                encoder.setVertexBytes(cursorVertices, length: cursorVertices.count * MemoryLayout<MetalVertex>.stride, index: 0)
                encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: cursorVertices.count)
            }
        }
        
        var glyphVertices: [MetalGlyphVertex] = []
        
        for r in 0..<rows {
            for c in 0..<cols {
                let cellIndex = r * cols + c
                if cellIndex >= cellCount { break }
                let cell = cells[cellIndex]
                
                if cell.codepoint == 0 || cell.codepoint == 32 {
                    continue
                }
                
                let texCoords = atlas.getGlyphTexCoords(codepoint: cell.codepoint, attributes: cell.attributes)
                let uMin = texCoords.x
                let vMin = texCoords.y
                let uMax = texCoords.z
                let vMax = texCoords.w
                
                let argb = cell.foreground
                let red = Float((argb >> 16) & 0xFF) / 255.0
                let green = Float((argb >> 8) & 0xFF) / 255.0
                let blue = Float(argb & 0xFF) / 255.0
                let alpha = Float((argb >> 24) & 0xFF) / 255.0
                
                let cellW = customCellWidth != nil ? Float(customCellWidth!) : Float(atlas.cellWidth)
                let cellH = customCellHeight != nil ? Float(customCellHeight!) : Float(atlas.cellHeight)
                
                let atlasW = Float(atlas.cellWidth)
                let atlasH = Float(atlas.cellHeight)
                
                let xOffset = (cellW - atlasW) * 0.5
                let x = Float(c) * cellW + xOffset
                
                let cellY = Float(r) * cellH
                let qY: Float
                if let customBaseline = self.customCellBaseline {
                    let activeFont = (cell.attributes & 1) != 0 ? atlas.boldFont : atlas.font
                    let ascent = Float(CTFontGetAscent(activeFont))
                    qY = cellY + Float(customBaseline) - ascent
                } else {
                    qY = cellY + (cellH - atlasH) * 0.5
                }
                
                glyphVertices.append(MetalGlyphVertex(
                    x: x, y: qY,
                    u: uMin, v: vMax,
                    r: red, g: green, b: blue, a: alpha
                ))
                glyphVertices.append(MetalGlyphVertex(
                    x: x + atlasW, y: qY,
                    u: uMax, v: vMax,
                    r: red, g: green, b: blue, a: alpha
                ))
                glyphVertices.append(MetalGlyphVertex(
                    x: x, y: qY + atlasH,
                    u: uMin, v: vMin,
                    r: red, g: green, b: blue, a: alpha
                ))
                
                glyphVertices.append(MetalGlyphVertex(
                    x: x + atlasW, y: qY,
                    u: uMax, v: vMax,
                    r: red, g: green, b: blue, a: alpha
                ))
                glyphVertices.append(MetalGlyphVertex(
                    x: x + atlasW, y: qY + atlasH,
                    u: uMax, v: vMin,
                    r: red, g: green, b: blue, a: alpha
                ))
                glyphVertices.append(MetalGlyphVertex(
                    x: x, y: qY + atlasH,
                    u: uMin, v: vMin,
                    r: red, g: green, b: blue, a: alpha
                ))
            }
        }
        
        if !glyphVertices.isEmpty {
            encoder.setRenderPipelineState(glyphPipelineState)
            
            var viewportSize = simd_make_float2(Float(width), Float(height))
            encoder.setVertexBytes(&viewportSize, length: MemoryLayout<simd_float2>.stride, index: 1)
            
            encoder.setFragmentTexture(atlas.texture, index: 0)
            encoder.setFragmentSamplerState(samplerState, index: 0)
            
            self.setVertexBufferOrBytes(encoder: encoder, bytes: glyphVertices, length: glyphVertices.count * MemoryLayout<MetalGlyphVertex>.stride, index: 0)
            encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: glyphVertices.count)
        }
        
        encoder.endEncoding()
        commandBuffer.commit()
        commandBuffer.waitUntilCompleted()
        
        return 0
    }
}

// C-Linkage Exports

@_cdecl("swift_render_context_new_with_device")
public func swift_render_context_new_with_device(
    _ deviceHandle: UnsafeMutableRawPointer?
) -> UnsafeMutableRawPointer? {
    guard let devPtr = deviceHandle else { return nil }
    let device = Unmanaged<AnyObject>.fromOpaque(devPtr).takeUnretainedValue() as! MTLDevice
    guard let ctx = SwiftRenderContext(device: device) else {
        return nil
    }
    return Unmanaged.passRetained(ctx).toOpaque()
}

@_cdecl("swift_render_create_texture")
public func swift_render_create_texture(
    _ devicePtr: UnsafeMutableRawPointer?,
    _ width: Int32,
    _ height: Int32
) -> UnsafeMutableRawPointer? {
    guard let devPtr = devicePtr else { return nil }
    let device = Unmanaged<AnyObject>.fromOpaque(devPtr).takeUnretainedValue() as! MTLDevice
    let texDesc = MTLTextureDescriptor.texture2DDescriptor(
        pixelFormat: .bgra8Unorm,
        width: Int(width),
        height: Int(height),
        mipmapped: false
    )
    texDesc.usage = [.shaderRead, .renderTarget]
    guard let texture = device.makeTexture(descriptor: texDesc) else { return nil }
    return Unmanaged<AnyObject>.passRetained(texture as AnyObject).toOpaque()
}

@_cdecl("swift_render_free_texture")
public func swift_render_free_texture(
    _ texturePtr: UnsafeMutableRawPointer?
) {
    guard let ptr = texturePtr else { return }
    Unmanaged<AnyObject>.fromOpaque(ptr).release()
}

@_cdecl("swift_render_context_new")
public func swift_render_context_new() -> UnsafeMutableRawPointer? {
    guard let ctx = SwiftRenderContext() else {
        return nil
    }
    return Unmanaged.passRetained(ctx).toOpaque()
}

@_cdecl("swift_render_context_free")
public func swift_render_context_free(_ context: UnsafeMutableRawPointer?) {
    guard let ctxPtr = context else { return }
    let _ = Unmanaged<SwiftRenderContext>.fromOpaque(ctxPtr).takeRetainedValue()
}

@_cdecl("swift_render_surface_new")
public func swift_render_surface_new(
    _ context: UnsafeMutableRawPointer?,
    _ backend: Int32
) -> UnsafeMutableRawPointer? {
    guard let ctxPtr = context else { return nil }
    let ctx = Unmanaged<SwiftRenderContext>.fromOpaque(ctxPtr).takeUnretainedValue()
    
    guard let surface = SwiftRenderSurface(context: ctx, backend: backend) else {
        return nil
    }
    return Unmanaged.passRetained(surface).toOpaque()
}

@_cdecl("swift_render_surface_free")
public func swift_render_surface_free(_ surface: UnsafeMutableRawPointer?) {
    guard let surfPtr = surface else { return }
    let _ = Unmanaged<SwiftRenderSurface>.fromOpaque(surfPtr).takeRetainedValue()
}

@_cdecl("swift_render_surface_set_size")
public func swift_render_surface_set_size(
    _ surface: UnsafeMutableRawPointer?,
    _ width: Int32,
    _ height: Int32
) -> Int32 {
    guard let surfPtr = surface else { return 1 }
    let surface = Unmanaged<SwiftRenderSurface>.fromOpaque(surfPtr).takeUnretainedValue()
    surface.setSize(width: width, height: height)
    return 0
}

@_cdecl("swift_render_surface_set_scale")
public func swift_render_surface_set_scale(
    _ surface: UnsafeMutableRawPointer?,
    _ scaleX: Double,
    _ scaleY: Double
) -> Int32 {
    guard let surfPtr = surface else { return 1 }
    let surface = Unmanaged<SwiftRenderSurface>.fromOpaque(surfPtr).takeUnretainedValue()
    surface.setScale(scaleX: scaleX, scaleY: scaleY)
    return 0
}

@_cdecl("swift_render_surface_set_theme")
public func swift_render_surface_set_theme(
    _ surface: UnsafeMutableRawPointer?,
    _ theme: UnsafeRawPointer?
) -> Int32 {
    guard let surfPtr = surface, let tPtr = theme else { return 1 }
    let surface = Unmanaged<SwiftRenderSurface>.fromOpaque(surfPtr).takeUnretainedValue()
    let themeValue = tPtr.assumingMemoryBound(to: SwiftRenderTheme.self).pointee
    surface.setTheme(theme: themeValue)
    return 0
}

@_cdecl("swift_render_surface_set_focus")
public func swift_render_surface_set_focus(
    _ surface: UnsafeMutableRawPointer?,
    _ focused: UInt8
) -> Int32 {
    guard let surfPtr = surface else { return 1 }
    let surface = Unmanaged<SwiftRenderSurface>.fromOpaque(surfPtr).takeUnretainedValue()
    surface.setFocus(focused: focused != 0)
    return 0
}

@_cdecl("swift_render_surface_render_to_target")
public func swift_render_surface_render_to_target(
    _ surface: UnsafeMutableRawPointer?,
    _ targetDesc: UnsafeRawPointer?,
    _ flatCells: UnsafeRawPointer?,
    _ cellCount: Int32,
    _ cols: Int32,
    _ rows: Int32,
    _ cursorCol: Int32,
    _ cursorRow: Int32,
    _ cursorVisible: UInt8,
    _ cursorStyle: Int32
) -> Int32 {
    guard let surfPtr = surface, let descPtr = targetDesc, let cellsPtr = flatCells else {
        return 1
    }
    
    let surface = Unmanaged<SwiftRenderSurface>.fromOpaque(surfPtr).takeUnretainedValue()
    let desc = descPtr.assumingMemoryBound(to: SwiftRenderTargetDesc.self).pointee
    
    guard let textureHandle = desc.targetHandle else {
        return 4 // Invalid target
    }
    let targetTexture = Unmanaged<AnyObject>.fromOpaque(textureHandle).takeUnretainedValue() as! MTLTexture
    let cells = cellsPtr.assumingMemoryBound(to: SwiftTerminalCell.self)
    
    return surface.render(
        targetTexture: targetTexture,
        cells: cells,
        cellCount: Int(cellCount),
        cols: Int(cols),
        rows: Int(rows),
        cursorCol: Int(cursorCol),
        cursorRow: Int(cursorRow),
        cursorVisible: cursorVisible != 0,
        cursorStyle: cursorStyle
    )
}

@_cdecl("swift_render_surface_render_to_rgba")
public func swift_render_surface_render_to_rgba(
    _ surface: UnsafeMutableRawPointer?,
    _ dstRgba: UnsafeMutableRawPointer?,
    _ dstLength: UInt32,
    _ width: Int32,
    _ height: Int32,
    _ stride: Int32,
    _ flatCells: UnsafeRawPointer?,
    _ cellCount: Int32,
    _ cols: Int32,
    _ rows: Int32,
    _ cursorCol: Int32,
    _ cursorRow: Int32,
    _ cursorVisible: UInt8,
    _ cursorStyle: Int32
) -> Int32 {
    guard let surfPtr = surface, let dst = dstRgba, let cellsPtr = flatCells else {
        return 1
    }
    
    let surface = Unmanaged<SwiftRenderSurface>.fromOpaque(surfPtr).takeUnretainedValue()
    
    let texDesc = MTLTextureDescriptor.texture2DDescriptor(
        pixelFormat: .bgra8Unorm,
        width: Int(width),
        height: Int(height),
        mipmapped: false
    )
    texDesc.usage = [.shaderRead, .renderTarget]
    
    guard let tempTexture = surface.context.device.makeTexture(descriptor: texDesc) else {
        return 5
    }
    
    let cells = cellsPtr.assumingMemoryBound(to: SwiftTerminalCell.self)
    
    let renderResult = surface.render(
        targetTexture: tempTexture,
        cells: cells,
        cellCount: Int(cellCount),
        cols: Int(cols),
        rows: Int(rows),
        cursorCol: Int(cursorCol),
        cursorRow: Int(cursorRow),
        cursorVisible: cursorVisible != 0,
        cursorStyle: cursorStyle
    )
    
    if renderResult != 0 {
        return renderResult
    }
    
    let region = MTLRegionMake2D(0, 0, Int(width), Int(height))
    tempTexture.getBytes(dst, bytesPerRow: Int(stride), from: region, mipmapLevel: 0)
    
    return 0
}

@_cdecl("swift_render_surface_set_font")
public func swift_render_surface_set_font(
    _ surface: UnsafeMutableRawPointer?,
    _ fontNameUtf8: UnsafePointer<CChar>?,
    _ fontSize: Double
) -> Int32 {
    guard let surfPtr = surface else { return 1 }
    let surface = Unmanaged<SwiftRenderSurface>.fromOpaque(surfPtr).takeUnretainedValue()
    let name: String
    if let namePtr = fontNameUtf8 {
        name = String(cString: namePtr)
    } else {
        name = "Courier"
    }
    surface.setFont(fontName: name, fontSize: CGFloat(fontSize))
    return 0
}

@_cdecl("swift_render_surface_set_cell_size")
public func swift_render_surface_set_cell_size(
    _ surface: UnsafeMutableRawPointer?,
    _ cellWidth: Double,
    _ cellHeight: Double,
    _ baseline: Double
) -> Int32 {
    guard let surfPtr = surface else { return 1 }
    let surface = Unmanaged<SwiftRenderSurface>.fromOpaque(surfPtr).takeUnretainedValue()
    surface.setCellSize(width: cellWidth, height: cellHeight, baseline: baseline)
    return 0
}

