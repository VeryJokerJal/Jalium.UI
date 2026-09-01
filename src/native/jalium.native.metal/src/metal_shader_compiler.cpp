#include "metal_shader_compiler.h"

#ifdef JALIUM_HAS_DXC_SPIRV_CROSS
#include <dxc/dxcapi.h>
#include <spirv_cross_c.h>

#include <cstdint>
#include <vector>
#endif

namespace jalium {

bool CompileMetalPixelShader(const char* hlsl, std::string& msl,
    std::string& entryPoint, std::string& error)
{
    msl.clear(); entryPoint.clear(); error.clear();
    if (!hlsl || !*hlsl) { error = "empty HLSL source"; return false; }
#ifndef JALIUM_HAS_DXC_SPIRV_CROSS
    error = "DXC/SPIRV-Cross compiler archive is not linked";
    return false;
#else
    IDxcUtils* utils = nullptr;
    IDxcCompiler3* compiler = nullptr;
    IDxcResult* result = nullptr;
    IDxcBlob* object = nullptr;
    IDxcBlobUtf8* diagnostics = nullptr;
    auto releaseAll = [&]() {
        if (diagnostics) diagnostics->Release();
        if (object) object->Release();
        if (result) result->Release();
        if (compiler) compiler->Release();
        if (utils) utils->Release();
    };
    if (FAILED(DxcCreateInstance(CLSID_DxcUtils, IID_PPV_ARGS(&utils))) ||
        FAILED(DxcCreateInstance(CLSID_DxcCompiler, IID_PPV_ARGS(&compiler)))) {
        error = "DxcCreateInstance failed"; releaseAll(); return false;
    }
    const wchar_t* args[] = {
        L"-E", L"main", L"-T", L"ps_6_0", L"-spirv", L"-O3",
        L"-fvk-use-dx-layout", L"-fvk-b-shift", L"0", L"all",
        L"-fvk-t-shift", L"16", L"all", L"-fvk-s-shift", L"32", L"all",
        L"-fvk-u-shift", L"48", L"all"
    };
    DxcBuffer source{};
    source.Ptr = hlsl; source.Size = std::char_traits<char>::length(hlsl);
    source.Encoding = DXC_CP_UTF8;
    HRESULT hr = compiler->Compile(&source, args,
        static_cast<uint32_t>(sizeof(args) / sizeof(args[0])), nullptr,
        IID_PPV_ARGS(&result));
    if (FAILED(hr) || !result) { error = "DXC invocation failed"; releaseAll(); return false; }
    result->GetOutput(DXC_OUT_ERRORS, IID_PPV_ARGS(&diagnostics), nullptr);
    HRESULT status = S_OK; result->GetStatus(&status);
    if (FAILED(status)) {
        if (diagnostics && diagnostics->GetStringLength())
            error.assign(diagnostics->GetStringPointer(), diagnostics->GetStringLength());
        else error = "HLSL compilation failed";
        releaseAll(); return false;
    }
    if (FAILED(result->GetOutput(DXC_OUT_OBJECT, IID_PPV_ARGS(&object), nullptr)) ||
        !object || object->GetBufferSize() % 4 != 0) {
        error = "DXC produced no SPIR-V"; releaseAll(); return false;
    }

    spvc_context context = nullptr;
    spvc_parsed_ir ir = nullptr;
    spvc_compiler mslCompiler = nullptr;
    spvc_compiler_options options = nullptr;
    if (spvc_context_create(&context) != SPVC_SUCCESS ||
        spvc_context_parse_spirv(context,
            static_cast<const SpvId*>(object->GetBufferPointer()),
            object->GetBufferSize() / 4, &ir) != SPVC_SUCCESS ||
        spvc_context_create_compiler(context, SPVC_BACKEND_MSL, ir,
            SPVC_CAPTURE_MODE_TAKE_OWNERSHIP, &mslCompiler) != SPVC_SUCCESS ||
        spvc_compiler_create_compiler_options(mslCompiler, &options) != SPVC_SUCCESS) {
        error = "SPIRV-Cross initialization failed";
        if (context) spvc_context_destroy(context); releaseAll(); return false;
    }
    spvc_compiler_options_set_uint(options, SPVC_COMPILER_OPTION_MSL_VERSION, 30000);
    spvc_compiler_options_set_bool(options,
        SPVC_COMPILER_OPTION_MSL_ENABLE_DECORATION_BINDING, SPVC_TRUE);
    spvc_compiler_install_compiler_options(mslCompiler, options);

    // The framework shader contract is b0/t0/s0. DXC's binding shifts above
    // keep register classes disjoint in SPIR-V; remap them to Metal's separate
    // buffer/texture/sampler namespaces.
    spvc_msl_resource_binding binding{};
    binding.stage = SpvExecutionModelFragment; binding.desc_set = 0;
    binding.binding = 0; binding.msl_buffer = 0;
    spvc_compiler_msl_add_resource_binding(mslCompiler, &binding);
    binding = {}; binding.stage = SpvExecutionModelFragment; binding.desc_set = 0;
    binding.binding = 16; binding.msl_texture = 0;
    spvc_compiler_msl_add_resource_binding(mslCompiler, &binding);
    binding = {}; binding.stage = SpvExecutionModelFragment; binding.desc_set = 0;
    binding.binding = 32; binding.msl_sampler = 0;
    spvc_compiler_msl_add_resource_binding(mslCompiler, &binding);

    const char* generated = nullptr;
    if (spvc_compiler_compile(mslCompiler, &generated) != SPVC_SUCCESS || !generated) {
        error = spvc_context_get_last_error_string(context);
        spvc_context_destroy(context); releaseAll(); return false;
    }
    msl = generated;
    // SPIRV-Cross consistently legalizes an HLSL main entry to main0 for MSL.
    entryPoint = "main0";
    spvc_context_destroy(context); releaseAll();
    return true;
#endif
}

} // namespace jalium
