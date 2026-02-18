/*
 * ghostty_renderer.h — C API for Ghostty GPU Rendering Interop
 *
 * Current scope:
 *   - Common render target descriptors
 *   - Backend/target validation API
 *   - Direct-target render API for Metal/Vulkan/D3D11/D3D12/OpenGL/Software
 *   - macOS Metal texture write path for external MTLTexture targets
 *   - CPU RGBA fallback
 *
 * Thread safety: callers must externally synchronize access per surface handle.
 */

#ifndef GHOSTTY_RENDERER_H
#define GHOSTTY_RENDERER_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct ghostty_render_context* ghostty_render_context_t;
typedef struct ghostty_render_surface* ghostty_render_surface_t;

typedef enum ghostty_gpu_backend {
    GHOSTTY_GPU_BACKEND_UNKNOWN = 0,
    GHOSTTY_GPU_BACKEND_SOFTWARE = 1,
    GHOSTTY_GPU_BACKEND_METAL = 2,
    GHOSTTY_GPU_BACKEND_VULKAN = 3,
    GHOSTTY_GPU_BACKEND_D3D11 = 4,
    GHOSTTY_GPU_BACKEND_D3D12 = 5,
    GHOSTTY_GPU_BACKEND_OPENGL = 6,
} ghostty_gpu_backend;

typedef enum ghostty_render_target_kind {
    GHOSTTY_RENDER_TARGET_UNKNOWN = 0,
    GHOSTTY_RENDER_TARGET_TEXTURE_2D = 1,
    GHOSTTY_RENDER_TARGET_FRAMEBUFFER = 2,
} ghostty_render_target_kind;

typedef enum ghostty_render_pixel_format {
    GHOSTTY_RENDER_PIXEL_UNKNOWN = 0,
    GHOSTTY_RENDER_PIXEL_BGRA8_UNORM = 1,
    GHOSTTY_RENDER_PIXEL_BGRA8_SRGB = 2,
    GHOSTTY_RENDER_PIXEL_RGBA8_UNORM = 3,
    GHOSTTY_RENDER_PIXEL_RGBA8_SRGB = 4,
    GHOSTTY_RENDER_PIXEL_RGBA16_FLOAT = 5,
} ghostty_render_pixel_format;

typedef enum ghostty_render_result {
    GHOSTTY_RENDER_RESULT_OK = 0,
    GHOSTTY_RENDER_RESULT_INVALID_ARGUMENT = 1,
    GHOSTTY_RENDER_RESULT_UNSUPPORTED_BACKEND = 2,
    GHOSTTY_RENDER_RESULT_UNSUPPORTED_PLATFORM = 3,
    GHOSTTY_RENDER_RESULT_INVALID_TARGET = 4,
    GHOSTTY_RENDER_RESULT_RENDER_FAILED = 5,
    GHOSTTY_RENDER_RESULT_OUT_OF_MEMORY = 6,
} ghostty_render_result;

typedef struct ghostty_render_target_desc {
    ghostty_gpu_backend backend;
    ghostty_render_target_kind target_kind;
    ghostty_render_pixel_format pixel_format;

    int32_t width;
    int32_t height;
    uint32_t sample_count;

    /* Backend-specific opaque handles */
    void* device_handle;
    void* context_handle;
    void* command_queue_handle;
    void* command_buffer_handle;
    void* target_handle;
    void* target_view_handle;

    uint64_t frame_id;
    const char* debug_name_utf8;
} ghostty_render_target_desc;

ghostty_render_context_t ghostty_render_context_new(void);
void ghostty_render_context_free(ghostty_render_context_t context);

ghostty_render_surface_t ghostty_render_surface_new(
    ghostty_render_context_t context,
    ghostty_gpu_backend backend);

void ghostty_render_surface_free(ghostty_render_surface_t surface);

int32_t ghostty_render_surface_set_size(
    ghostty_render_surface_t surface,
    int32_t width,
    int32_t height);

int32_t ghostty_render_surface_set_scale(
    ghostty_render_surface_t surface,
    double scale_x,
    double scale_y);

int32_t ghostty_render_surface_set_focus(
    ghostty_render_surface_t surface,
    uint8_t focused);

int32_t ghostty_render_surface_set_color_scheme(
    ghostty_render_surface_t surface,
    uint32_t color_scheme);

int32_t ghostty_render_surface_begin_frame(
    ghostty_render_surface_t surface,
    uint64_t* out_frame_token);

int32_t ghostty_render_surface_end_frame(
    ghostty_render_surface_t surface,
    uint64_t frame_token);

int32_t ghostty_render_surface_validate_target(
    ghostty_render_surface_t surface,
    const ghostty_render_target_desc* target,
    const char** out_error_utf8);

int32_t ghostty_render_surface_render_to_target(
    ghostty_render_surface_t surface,
    const ghostty_render_target_desc* target,
    uint64_t* out_sync_token);

int32_t ghostty_render_surface_render_to_rgba(
    ghostty_render_surface_t surface,
    uint8_t* dst_rgba,
    uint32_t dst_len,
    int32_t width,
    int32_t height,
    int32_t stride);

const char* ghostty_render_result_message(int32_t result_code);

#ifdef __cplusplus
}
#endif

#endif /* GHOSTTY_RENDERER_H */
