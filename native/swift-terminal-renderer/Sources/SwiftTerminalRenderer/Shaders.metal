#include <metal_stdlib>
using namespace metal;

// 1. Background Quads Shaders

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
    // Map screen coordinates (0..width, 0..height) to normalized device coordinates (-1..1, -1..1)
    // In Metal, Y goes down in viewport space, but normalized device coordinates have Y going up.
    // So we map Y: NDC_Y = 1.0 - 2.0 * (pixel_Y / viewportHeight)
    // NDC_X = 2.0 * (pixel_X / viewportWidth) - 1.0
    float x = 2.0 * (in.position.x / viewportSize.x) - 1.0;
    float y = 1.0 - 2.0 * (in.position.y / viewportSize.y);
    out.position = float4(x, y, 0.0, 1.0);
    out.color = in.color;
    return out;
}

fragment float4 quad_fragment(QuadVertexOutput in [[stage_in]]) {
    return in.color;
}

// 2. Text/Glyph Shaders

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
    // The texture atlas is grayscale (alpha only or single channel).
    // Sample texture red channel (or alpha) and multiply by character foreground color.
    float intensity = texture.sample(textureSampler, in.texCoords).r;
    return float4(in.color.rgb, in.color.a * intensity);
}
