#pragma once

#include <string>

namespace jalium {

/// Compiles the framework's SM6 pixel-shader contract to MSL. Returns false
/// when the optional pinned compiler archive is not part of this build.
bool CompileMetalPixelShader(const char* hlsl, std::string& msl,
    std::string& entryPoint, std::string& error);

} // namespace jalium
