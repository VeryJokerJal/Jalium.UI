#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace jalium {

// Runtime HLSL → SPIR-V compiler backed by a dynamically-loaded DXC
// (dxcompiler.dll on Windows). This is the Vulkan analogue of the D3D12
// backend's runtime D3DCompile path (d3d12_brush_shader.cpp): the *same*
// authored HLSL — the InkCanvas brush preamble + user BrushMain, or a custom
// ShaderEffect body — drives both backends, so there is one shader source of
// truth and no DXBC/SPIR-V duplication.
//
// The DXC library is loaded lazily on first use and never required: when it is
// absent (or fails to resolve DxcCreateInstance) Available() reports false and
// every Compile() returns an empty word stream. Callers degrade exactly like
// they do today — brush dispatch returns nullptr so the managed side falls back
// to CPU rasterization, and custom shader effects fall back to a plain blur.
//
// Register → binding convention. HLSL register classes share the b/t/s/u
// number space, but SPIR-V bindings are flat, so DXC must shift each class into
// a disjoint range or b0/t0/s0 collide. We pass -fvk-{t,s,u}-shift so the
// mapping is deterministic, and the Vulkan descriptor-set layouts that consume
// these shaders MUST declare bindings using the same offsets:
//     b<N>  →  binding N            (kBShift = 0, no shift needed)
//     t<N>  →  binding N + 16       (kTShift = 16)  [StructuredBuffer / Texture]
//     s<N>  →  binding N + 32       (kSShift = 32)  [SamplerState]
//     u<N>  →  binding N + 48       (kUShift = 48)  [RWStructuredBuffer]
//
// DXC is only wired up on Windows for now (the D3D12 backend it mirrors is
// Windows-only, and dxcapi.h's cross-platform COM shim needs extra plumbing on
// POSIX). On other platforms Available() is always false and the file still
// compiles cleanly so the Linux/Android Vulkan build is unaffected.
class VulkanShaderCompiler {
public:
    static constexpr uint32_t kBShift = 0;
    static constexpr uint32_t kTShift = 16;
    static constexpr uint32_t kSShift = 32;
    static constexpr uint32_t kUShift = 48;

    VulkanShaderCompiler() = default;
    ~VulkanShaderCompiler();

    VulkanShaderCompiler(const VulkanShaderCompiler&)            = delete;
    VulkanShaderCompiler& operator=(const VulkanShaderCompiler&) = delete;

    // True once dxcompiler is loaded and DxcCreateInstance resolved. Triggers
    // the lazy load on first call. Thread-safe.
    bool Available();

    // Compile HLSL → SPIR-V words. entryPoint e.g. "main"/"BrushPsMain";
    // profile e.g. "ps_6_0" / "vs_6_0". On failure returns an empty vector and
    // writes a human-readable diagnostic to errorOut (also emitted via
    // OutputDebugString). Thread-safe (calls are serialized internally).
    std::vector<uint32_t> Compile(const std::string& source,
                                  const char* entryPoint,
                                  const char* profile,
                                  std::string& errorOut);

private:
    bool EnsureLoaded();

    void* dll_        = nullptr;  // HMODULE (Windows)
    void* createProc_ = nullptr;  // DxcCreateInstanceProc
    bool  attempted_  = false;
    bool  available_  = false;
};

} // namespace jalium
