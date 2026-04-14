#pragma once

#include "jalium_rendering_engine.h"
#include "d3d12_vello.h"

namespace jalium {

/// VelloD3D12Engine — adapts the existing D3D12VelloRenderer to IRenderingEngine.
/// This is a thin adapter; all the real work is in D3D12VelloRenderer.
class VelloD3D12Engine : public IRenderingEngine {
public:
    explicit VelloD3D12Engine(ID3D12Device* device, ShaderBlobCache* shaderCache = nullptr);
    ~VelloD3D12Engine() override;

    JaliumRenderingEngine GetType() const override { return JALIUM_ENGINE_VELLO; }
    bool Initialize() override;

    void BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight) override;
    void SetScissorRect(float left, float top, float right, float bottom) override;
    void ClearScissorRect() override;

    bool EncodeFillPath(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        const EngineBrushData& brush,
        FillRule fillRule,
        const EngineTransform& transform) override;

    bool EncodeStrokePath(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        const EngineBrushData& brush,
        float strokeWidth, bool closed,
        int32_t lineJoin, float miterLimit,
        int32_t lineCap,
        const float* dashPattern, uint32_t dashCount, float dashOffset,
        const EngineTransform& transform) override;

    bool EncodeFillPolygon(
        const float* points, uint32_t pointCount,
        const EngineBrushData& brush,
        FillRule fillRule,
        const EngineTransform& transform) override;

    bool EncodeFillEllipse(
        float cx, float cy, float rx, float ry,
        const EngineBrushData& brush,
        const EngineTransform& transform) override;

    bool Execute(void* commandList, void* renderTarget, uint32_t width, uint32_t height) override;
    bool HasPendingWork() const override;
    uint32_t GetEncodedPathCount() const override;

    /// Access the underlying VelloRenderer for advanced features (clip, blur, etc.)
    D3D12VelloRenderer* GetVelloRenderer() { return &vello_; }

private:
    D3D12VelloRenderer vello_;
};

} // namespace jalium
