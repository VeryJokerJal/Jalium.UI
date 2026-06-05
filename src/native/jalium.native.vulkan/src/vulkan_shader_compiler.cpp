#include "vulkan_shader_compiler.h"

#include <cstdio>
#include <cstring>
#include <mutex>

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
// dxcapi.h relies on the full COM/OLE type set (IUnknown, IStream, BSTR).
// WIN32_LEAN_AND_MEAN strips those out of <windows.h>, so pull them in
// explicitly before the DXC header or it fails with C2504/C2061.
#include <unknwn.h>   // IUnknown
#include <objidl.h>   // IStream
#include <oleauto.h>  // BSTR
#include <dxc/dxcapi.h>
#endif

namespace jalium {

namespace {

// All Compile()/EnsureLoaded() work is serialized: DXC interface instances are
// created per call (cheap relative to a compile) and we never assume the
// compiler is thread-safe.
std::mutex g_compilerMutex;

#if defined(_WIN32)
std::wstring WidenUtf8(const char* s) {
    if (!s || !*s) return std::wstring();
    int len = ::MultiByteToWideChar(CP_UTF8, 0, s, -1, nullptr, 0);
    if (len <= 1) return std::wstring();
    std::wstring w(static_cast<size_t>(len - 1), L'\0');
    ::MultiByteToWideChar(CP_UTF8, 0, s, -1, w.data(), len);
    return w;
}

template <typename T>
void SafeRelease(T*& p) {
    if (p) { p->Release(); p = nullptr; }
}
#endif

} // namespace

VulkanShaderCompiler::~VulkanShaderCompiler() {
#if defined(_WIN32)
    if (dll_) {
        ::FreeLibrary(reinterpret_cast<HMODULE>(dll_));
        dll_ = nullptr;
    }
#endif
}

bool VulkanShaderCompiler::EnsureLoaded() {
#if defined(_WIN32)
    if (attempted_) return available_;
    attempted_ = true;

    HMODULE mod = ::LoadLibraryW(L"dxcompiler.dll");
    if (!mod) {
        ::OutputDebugStringA(
            "[Jalium Vulkan] dxcompiler.dll not found — runtime HLSL "
            "compilation unavailable; brush / custom shaders fall back.\n");
        return false;
    }
    auto proc = reinterpret_cast<DxcCreateInstanceProc>(
        ::GetProcAddress(mod, "DxcCreateInstance"));
    if (!proc) {
        ::OutputDebugStringA(
            "[Jalium Vulkan] dxcompiler.dll missing DxcCreateInstance export.\n");
        ::FreeLibrary(mod);
        return false;
    }
    dll_        = mod;
    createProc_ = reinterpret_cast<void*>(proc);
    available_  = true;
    return true;
#else
    attempted_ = true;
    available_ = false;
    return false;
#endif
}

bool VulkanShaderCompiler::Available() {
    std::lock_guard<std::mutex> lk(g_compilerMutex);
    return EnsureLoaded();
}

std::vector<uint32_t> VulkanShaderCompiler::Compile(const std::string& source,
                                                    const char* entryPoint,
                                                    const char* profile,
                                                    std::string& errorOut) {
    std::vector<uint32_t> spirv;
    errorOut.clear();

#if defined(_WIN32)
    std::lock_guard<std::mutex> lk(g_compilerMutex);
    if (!EnsureLoaded()) {
        errorOut = "dxcompiler.dll unavailable";
        return spirv;
    }
    auto createProc = reinterpret_cast<DxcCreateInstanceProc>(createProc_);

    IDxcUtils*     utils    = nullptr;
    IDxcCompiler3* compiler = nullptr;
    if (FAILED(createProc(CLSID_DxcUtils, __uuidof(IDxcUtils),
                          reinterpret_cast<void**>(&utils))) || !utils) {
        errorOut = "DxcCreateInstance(DxcUtils) failed";
        return spirv;
    }
    if (FAILED(createProc(CLSID_DxcCompiler, __uuidof(IDxcCompiler3),
                          reinterpret_cast<void**>(&compiler))) || !compiler) {
        SafeRelease(utils);
        errorOut = "DxcCreateInstance(DxcCompiler) failed";
        return spirv;
    }

    const std::wstring wEntry   = WidenUtf8(entryPoint ? entryPoint : "main");
    const std::wstring wProfile = WidenUtf8(profile ? profile : "ps_6_0");

    // Register-class → binding shifts (see header). Space 0 only.
    wchar_t tShift[16], sShift[16], uShift[16];
    swprintf_s(tShift, L"%u", VulkanShaderCompiler::kTShift);
    swprintf_s(sShift, L"%u", VulkanShaderCompiler::kSShift);
    swprintf_s(uShift, L"%u", VulkanShaderCompiler::kUShift);

    std::vector<LPCWSTR> args;
    args.push_back(L"-spirv");
    args.push_back(L"-T"); args.push_back(wProfile.c_str());
    args.push_back(L"-E"); args.push_back(wEntry.c_str());
    args.push_back(L"-fvk-t-shift"); args.push_back(tShift); args.push_back(L"0");
    args.push_back(L"-fvk-s-shift"); args.push_back(sShift); args.push_back(L"0");
    args.push_back(L"-fvk-u-shift"); args.push_back(uShift); args.push_back(L"0");
    // Keep matrices column-major to match the float[16] row-major data we feed
    // through push-constants / cbuffers consistently with the rest of the
    // Vulkan pipeline set (which uses the same convention as D3D12 HLSL).
    args.push_back(L"-Zpc");
#ifndef _DEBUG
    args.push_back(L"-O3");
#else
    args.push_back(L"-Od");
#endif

    DxcBuffer srcBuf{};
    srcBuf.Ptr      = source.data();
    srcBuf.Size     = source.size();
    srcBuf.Encoding = DXC_CP_UTF8;

    IDxcResult* result = nullptr;
    HRESULT hr = compiler->Compile(
        &srcBuf, args.data(), static_cast<UINT32>(args.size()),
        nullptr, __uuidof(IDxcResult), reinterpret_cast<void**>(&result));
    if (FAILED(hr) || !result) {
        SafeRelease(compiler);
        SafeRelease(utils);
        errorOut = "IDxcCompiler3::Compile invocation failed";
        return spirv;
    }

    // Diagnostics first so we capture warnings even on success.
    IDxcBlobUtf8* errBlob = nullptr;
    result->GetOutput(DXC_OUT_ERRORS, __uuidof(IDxcBlobUtf8),
                      reinterpret_cast<void**>(&errBlob), nullptr);
    if (errBlob && errBlob->GetStringLength() > 0) {
        errorOut.assign(errBlob->GetStringPointer(), errBlob->GetStringLength());
    }

    HRESULT status = E_FAIL;
    result->GetStatus(&status);
    if (FAILED(status)) {
        if (!errorOut.empty()) {
            ::OutputDebugStringA("[Jalium Vulkan] HLSL→SPIR-V compile error:\n");
            ::OutputDebugStringA(errorOut.c_str());
            ::OutputDebugStringA("\n");
        }
        SafeRelease(errBlob);
        SafeRelease(result);
        SafeRelease(compiler);
        SafeRelease(utils);
        return spirv;
    }

    IDxcBlob* obj = nullptr;
    result->GetOutput(DXC_OUT_OBJECT, __uuidof(IDxcBlob),
                      reinterpret_cast<void**>(&obj), nullptr);
    if (obj && obj->GetBufferSize() >= sizeof(uint32_t)) {
        const size_t words = obj->GetBufferSize() / sizeof(uint32_t);
        spirv.resize(words);
        std::memcpy(spirv.data(), obj->GetBufferPointer(), words * sizeof(uint32_t));
    } else {
        errorOut = errorOut.empty() ? "DXC produced an empty SPIR-V object" : errorOut;
    }

    SafeRelease(obj);
    SafeRelease(errBlob);
    SafeRelease(result);
    SafeRelease(compiler);
    SafeRelease(utils);
#else
    (void)source; (void)entryPoint; (void)profile;
    errorOut = "runtime HLSL compilation not supported on this platform";
#endif
    return spirv;
}

} // namespace jalium
