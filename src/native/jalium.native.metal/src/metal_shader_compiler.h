#pragma once

#include <string>
#include <cstdint>

namespace jalium {

/// Compiles the framework's SM6 pixel-shader contract to MSL. Returns false
/// when the optional pinned compiler archive is not part of this build.
bool CompileMetalPixelShader(const char* hlsl, std::string& msl,
    std::string& entryPoint, std::string& error, uint32_t* constantBytes = nullptr);

/// Compiles a BrushMain body against the canonical D3D12/Vulkan/Metal Ink ABI.
bool CompileMetalBrushShader(const char* brushMainHlsl, std::string& msl,
    std::string& entryPoint, std::string& error);

} // namespace jalium
