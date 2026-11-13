#pragma once
// Host-only ABI shim. Real Android builds use the NDK header and libraries.
#include <stdint.h>
struct AHardwareBuffer;
typedef struct AHardwareBuffer_Desc {
    uint32_t width, height, layers, format;
    uint64_t usage;
    uint32_t stride, rfu0;
    uint64_t rfu1;
} AHardwareBuffer_Desc;
#define AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE (1ULL << 8)
#define AHARDWAREBUFFER_USAGE_PROTECTED_CONTENT (1ULL << 14)
#define AHARDWAREBUFFER_USAGE_GPU_MIPMAP_COMPLETE (1ULL << 26)
extern "C" void AHardwareBuffer_describe(const AHardwareBuffer*, AHardwareBuffer_Desc*);
