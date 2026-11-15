#include "vulkan_graphics_constants.h"
#include "vulkan_embedded_shaders.h"
#include "vulkan_bitmap_premul_shaders.h"
#include "vulkan_bitmap_lcd_shaders.h"
#include "vulkan_effect_builtin_shaders.h"
#include "vulkan_ink_composite_shaders.h"
#include "vulkan_impeller_shaders.h"
#include "vulkan_text_glyph_shaders.h"
#include "vulkan_triangle_vc_shaders.h"
#include "vulkan_vello_composite_shaders.h"
#include "vulkan_video_shaders.h"

#include <filesystem>
#include <fstream>
#include <iostream>

namespace {
template <size_t N>
bool CheckShader(const char* name, const uint32_t (&code)[N], const std::filesystem::path& directory)
{
    std::vector<uint32_t> relocated;
    if (!jalium::vulkan_graphics_constants::RemapToUniform(code, N, relocated)) {
        std::cerr << "FAILED: cannot relocate " << name << '\n';
        return false;
    }
    if (!directory.empty()) {
        std::ofstream output(directory / (std::string(name) + ".spv"), std::ios::binary);
        output.write(reinterpret_cast<const char*>(relocated.data()), relocated.size() * sizeof(uint32_t));
        if (!output) {
            std::cerr << "FAILED: cannot write relocated " << name << '\n';
            return false;
        }
    }
    return true;
}
} // namespace

int main(int argc, char** argv)
{
    const std::filesystem::path directory = argc > 1 ? argv[1] : "";
    if (!directory.empty()) std::filesystem::create_directories(directory);
    bool ok = true;
    unsigned checked = 0;
#define CHECK_SHADER(name) ok = CheckShader(#name, jalium::name, directory) && ok; ++checked
    CHECK_SHADER(kFrameCompositeVertexShaderSpv);
    CHECK_SHADER(kFrameCompositeFragmentShaderSpv);
    CHECK_SHADER(kSolidRectVertexShaderSpv);
    CHECK_SHADER(kSolidRectFragmentShaderSpv);
    CHECK_SHADER(kBitmapQuadVertexShaderSpv);
    CHECK_SHADER(kBitmapQuadFragmentShaderSpv);
    CHECK_SHADER(kBitmapQuadPremulFragSpv);
    CHECK_SHADER(kBitmapQuadLcdFragSpv);
    CHECK_SHADER(kBackdropQuadVertexShaderSpv);
    CHECK_SHADER(kBackdropQuadFragmentShaderSpv);
    CHECK_SHADER(kBlurQuadVertexShaderSpv);
    CHECK_SHADER(kBlurQuadFragmentShaderSpv);
    CHECK_SHADER(kGlowQuadVertexShaderSpv);
    CHECK_SHADER(kGlowQuadFragmentShaderSpv);
    CHECK_SHADER(kLiquidGlassQuadVertexShaderSpv);
    CHECK_SHADER(kLiquidGlassQuadFragmentShaderSpv);
    CHECK_SHADER(kTriangleFillVertexShaderSpv);
    CHECK_SHADER(kTriangleFillFragmentShaderSpv);
    CHECK_SHADER(kTransitionQuadVertexShaderSpv);
    CHECK_SHADER(kTransitionQuadFragmentShaderSpv);
    CHECK_SHADER(kEffectBuiltinCustomShaderVsSpv);
    CHECK_SHADER(kEffectBuiltinColorMatrixPsSpv);
    CHECK_SHADER(kEffectBuiltinEmbossPsSpv);
    CHECK_SHADER(kEffectBuiltinOuterGlowPsSpv);
    CHECK_SHADER(kEffectBuiltinFilterDropShadowPsSpv);
    CHECK_SHADER(kEffectBuiltinColorMatrixChainPsSpv);
    CHECK_SHADER(kInkCompositeFragSpv);
    CHECK_SHADER(kImpellerSolidFillVertShaderSpv);
    CHECK_SHADER(kImpellerSolidFillFragShaderSpv);
    CHECK_SHADER(kTextGlyphVertSpv);
    CHECK_SHADER(kTextGlyphFragSpv);
    CHECK_SHADER(kTextGlyphFragClearTypeSpv);
    CHECK_SHADER(kTriangleFillVcVertShaderSpv);
    CHECK_SHADER(kTriangleFillVcFragShaderSpv);
    CHECK_SHADER(kVelloCompositeVertSpv);
    CHECK_SHADER(kVelloCompositeFragSpv);
    CHECK_SHADER(kVideoQuadFragSpv);
    CHECK_SHADER(kVideoReadbackVertSpv);
    CHECK_SHADER(kVideoReadbackFragSpv);
#undef CHECK_SHADER
    if (ok) std::cout << "Relocated " << checked << " embedded graphics shader modules.\n";
    return ok ? 0 : 1;
}
