/// GPU shader pipeline stub implementations.
/// These functions are declared via P/Invoke in Jalium.UI.Gpu but are not yet
/// implemented. Stubs are required so that the NativeAOT static linker can
/// resolve all DirectPInvoke symbols. Each stub is a no-op that returns a
/// safe default value (0 / nullptr).

#include "jalium_api.h"
#include <cstdint>

extern "C" {

// ===== Initialization =====

JALIUM_API int jalium_pipeline_init(void* context) { return 0; }
JALIUM_API void jalium_pipeline_shutdown(void* context) {}

// ===== Buffers =====

JALIUM_API void* jalium_buffer_create(void* context, const void* data, int size, int usage) { return nullptr; }
JALIUM_API void* jalium_buffer_create_empty(void* context, int size, int usage) { return nullptr; }
JALIUM_API void jalium_buffer_update(void* context, void* buffer, int offset, const void* data, int size) {}
JALIUM_API void jalium_buffer_destroy(void* context, void* buffer) {}

// ===== Textures =====

JALIUM_API void* jalium_texture_load(void* context, const char* path, int format) { return nullptr; }
JALIUM_API void* jalium_glyph_atlas_create(void* context, const char* fontId, float fontSize, int width, int height) { return nullptr; }
JALIUM_API void* jalium_texture_create_rt(void* context, int width, int height, int format) { return nullptr; }
JALIUM_API void* jalium_texture_create_2d(void* context, int width, int height, int format, int usage) { return nullptr; }
JALIUM_API void jalium_texture_destroy(void* context, void* texture) {}

// ===== Binding =====

JALIUM_API void jalium_bind_vertex_buffer(void* context, void* buffer) {}
JALIUM_API void jalium_bind_index_buffer(void* context, void* buffer) {}
JALIUM_API void jalium_bind_instance_buffer(void* context, void* buffer) {}
JALIUM_API void jalium_bind_uniform_buffer(void* context, void* buffer) {}
JALIUM_API void jalium_bind_texture(void* context, int slot, void* texture) {}

// ===== State =====

JALIUM_API void jalium_set_scissor(void* context, int x, int y, int width, int height) {}
JALIUM_API void jalium_set_viewport(void* context, int x, int y, int width, int height) {}

// ===== Draw =====

JALIUM_API void jalium_draw_indexed_instanced(void* context, uint32_t indexCount, uint32_t instanceCount, uint32_t firstIndex, int baseVertex, uint32_t firstInstance) {}
JALIUM_API void jalium_draw(void* context, uint32_t vertexCount, uint32_t instanceCount, uint32_t startVertex, uint32_t startInstance) {}
JALIUM_API void jalium_draw_glyphs(void* context, uint32_t offset, uint32_t count) {}
JALIUM_API void jalium_dispatch(void* context, uint32_t x, uint32_t y, uint32_t z) {}

// ===== Effects =====

JALIUM_API void jalium_apply_effect(void* context, int effectType, uint32_t srcTex, uint32_t dstTex, const void* parameters, int paramSize) {}
JALIUM_API void jalium_capture_backdrop(void* context, float x, float y, float w, float h, uint32_t targetTexIndex) {}
JALIUM_API void jalium_apply_backdrop_filter(void* context, uint32_t srcTexIndex, uint32_t dstTexIndex) {}
JALIUM_API void jalium_composite_layer(void* context, uint32_t srcTexIndex, float x, float y, float w, float h, int blendMode, uint8_t opacity) {}
JALIUM_API void jalium_submit(void* context) {}

// ===== Shader Pipeline =====

JALIUM_API int jalium_shader_compile(const void* sourceData, int sourceSize, const char* entryPoint, const char* target, int flags, void** bytecodePtr, int* bytecodeSize, void** errorPtr, int* errorSize) {
    if (bytecodePtr) *bytecodePtr = nullptr;
    if (bytecodeSize) *bytecodeSize = 0;
    if (errorPtr) *errorPtr = nullptr;
    if (errorSize) *errorSize = 0;
    return -1;
}
JALIUM_API void jalium_shader_free_blob(void* blob) {}

JALIUM_API void* jalium_pso_create_graphics(void* context, const void* vsBytecode, int vsSize, const void* psBytecode, int psSize, int inputLayout, int blendMode, int cullMode, int depthEnable, int rtFormat, int sampleCount, int rootSigType) { return nullptr; }
JALIUM_API void* jalium_pso_create_compute(void* context, const void* csBytecode, int csSize, int rootSigType) { return nullptr; }
JALIUM_API void jalium_pso_destroy(void* context, void* pso) {}

JALIUM_API void* jalium_root_signature_create(void* context, int type) { return nullptr; }
JALIUM_API void jalium_root_signature_destroy(void* context, void* rootSig) {}

// ===== Descriptors =====

JALIUM_API int jalium_descriptor_create_srv(void* context, void* resource) { return 0; }
JALIUM_API int jalium_descriptor_create_cbv(void* context, void* buffer, int offset, int size) { return 0; }
JALIUM_API int jalium_descriptor_create_uav(void* context, void* resource) { return 0; }
JALIUM_API void jalium_descriptor_free(void* context, int index) {}

// ===== Commands =====

JALIUM_API void jalium_cmd_set_pso(void* context, void* pso) {}
JALIUM_API void jalium_cmd_set_root_signature(void* context, void* rootSig) {}
JALIUM_API void jalium_cmd_resource_barrier(void* context, void* resource, int stateBefore, int stateAfter) {}
JALIUM_API void jalium_cmd_clear_rt(void* context, uint32_t rtId, float r, float g, float b, float a) {}

// ===== Sync =====

JALIUM_API void jalium_fence_signal(void* context, uint64_t fenceValue) {}
JALIUM_API void jalium_fence_wait(void* context, uint64_t fenceValue) {}

// ===== Device Info =====

JALIUM_API void* jalium_get_device(void* context) { return nullptr; }
JALIUM_API void* jalium_get_command_queue(void* context) { return nullptr; }

} // extern "C"
