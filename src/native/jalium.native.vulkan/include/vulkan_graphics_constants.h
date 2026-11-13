#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

namespace jalium::vulkan_graphics_constants {

// The largest built-in graphics block is LiquidGlass (240 bytes). Vulkan
// guarantees only 128 push-constant bytes, so smaller devices use a dynamic
// uniform buffer at set 2, binding 0. Sets 0/1 retain textures/elliptical clips.
inline constexpr uint32_t kRequiredPushConstantBytes = 240;
inline constexpr uint32_t kUniformRangeBytes = 256;
inline constexpr uint32_t kDescriptorSet = 2;

constexpr bool NeedsUniformBuffer(uint32_t maxPushConstantsSize) noexcept
{
    return maxPushConstantsSize < kRequiredPushConstantBytes;
}

// The embedded HLSL blocks contain std140-compatible scalars, vectors and
// matrices. Relocate their PushConstant pointers/variable to Uniform and add
// descriptor decorations, preserving member offsets and every result ID.
// This avoids a second, potentially divergent copy of every embedded shader.
inline bool RemapToUniform(const uint32_t* code, size_t wordCount,
                           std::vector<uint32_t>& result)
{
    result.clear();
    if (!code || wordCount < 5 || code[0] != 0x07230203u) return false;
    std::vector<uint32_t> variables;
    size_t typesBegin = wordCount;
    for (size_t i = 5; i < wordCount;) {
        const uint32_t length = code[i] >> 16;
        const uint32_t opcode = code[i] & 0xffffu;
        if (length == 0 || length > wordCount - i) return false;
        if (opcode >= 19 && opcode <= 39 && typesBegin == wordCount) typesBegin = i;
        // OpVariable: result type, result ID, storage class.
        if (opcode == 59 && length >= 4 && code[i + 3] == 9)
            variables.push_back(code[i + 2]);
        i += length;
    }
    result.assign(code, code + wordCount);
    if (variables.empty()) return true;
    if (typesBegin == wordCount) { result.clear(); return false; }
    for (size_t i = 5; i < result.size(); i += result[i] >> 16) {
        const uint32_t opcode = result[i] & 0xffffu;
        const uint32_t length = result[i] >> 16;
        // OpTypePointer: result ID, storage class, pointee type.
        if (opcode == 32 && length >= 4 && result[i + 2] == 9) result[i + 2] = 2;
        if (opcode == 59 && length >= 4 && result[i + 3] == 9) result[i + 3] = 2;
    }
    std::vector<uint32_t> decorations;
    for (uint32_t id : variables) {
        // OpDecorate DescriptorSet / Binding, in the annotation section.
        decorations.insert(decorations.end(), {
            (4u << 16) | 71u, id, 34u, kDescriptorSet,
            (4u << 16) | 71u, id, 33u, 0u});
    }
    result.insert(result.begin() + typesBegin, decorations.begin(), decorations.end());
    return true;
}

} // namespace jalium::vulkan_graphics_constants
