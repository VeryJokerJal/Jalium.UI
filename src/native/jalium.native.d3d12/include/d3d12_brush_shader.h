#pragma once

#include "d3d12_backend.h"
#include <d3d12.h>
#include <wrl/client.h>
#include <string>
#include <memory>

namespace jalium {

using Microsoft::WRL::ComPtr;

// Blend mode selector for brush-shader PSOs. Values match the managed
// BrushBlendMode enum so managed → native pass-through is a direct cast.
enum class BrushBlendMode : int
{
    SourceOver = 0,
    Additive   = 1,
    Erase      = 2,
};

// A compiled brush pixel shader + its PSO. Lifetime owned by the caller
// via the JaliumBrushShader* C API handle; D3D12 destroys all PSO /
// blob refs when this object dies.
class D3D12BrushShader
{
public:
    D3D12BrushShader(
        ComPtr<ID3DBlob> psBlob,
        ComPtr<ID3D12PipelineState> pso,
        BrushBlendMode blendMode,
        std::string shaderKey);
    ~D3D12BrushShader() = default;

    // Non-copyable, non-movable (held via unique_ptr / raw pointer handle).
    D3D12BrushShader(const D3D12BrushShader&) = delete;
    D3D12BrushShader& operator=(const D3D12BrushShader&) = delete;

    BrushBlendMode BlendMode() const { return blendMode_; }
    const std::string& Key() const { return shaderKey_; }
    ID3D12PipelineState* Pso() const { return pso_.Get(); }
    ID3DBlob* PsBlob() const { return psBlob_.Get(); }

private:
    ComPtr<ID3DBlob>            psBlob_;
    ComPtr<ID3D12PipelineState> pso_;
    BrushBlendMode              blendMode_;
    std::string                 shaderKey_;
};

// Owns the shared GPU-side pieces of the brush-shader pipeline:
//  * Root signature (CBV b0 + SRV t0)
//  * Fullscreen-triangle vertex shader (compiled once)
//  * Blend states per BrushBlendMode — baked into each shader's PSO
//
// Instances are held by the D3D12 backend / context; individual
// D3D12BrushShader objects reuse this pipeline's VS + root signature to
// avoid redundant compilation and keep PSO equality tight.
class D3D12BrushShaderPipeline
{
public:
    explicit D3D12BrushShaderPipeline(ID3D12Device* device);
    ~D3D12BrushShaderPipeline() = default;

    // Compiles the shared VS + ensures the root signature is ready.
    // Returns false on compile / CreateRootSignature failure.
    bool Initialize();

    // Compiles a user brush HLSL + creates its PSO. Returns nullptr on
    // compilation failure (error string is written to OutputDebugString).
    // shaderKey is stored verbatim on the resulting object — used by
    // managed code to key a cache if it wants to.
    std::unique_ptr<D3D12BrushShader> CreateBrushShader(
        const char* shaderKey,
        const char* brushMainHlsl,
        BrushBlendMode blendMode);

    ID3D12RootSignature* RootSignature() const { return rootSig_.Get(); }
    ID3DBlob*            VsBlob()        const { return vsBlob_.Get(); }

private:
    ID3D12Device*              device_;
    ComPtr<ID3D12RootSignature> rootSig_;
    ComPtr<ID3DBlob>            vsBlob_;
    bool                        initialized_ = false;
};

} // namespace jalium
