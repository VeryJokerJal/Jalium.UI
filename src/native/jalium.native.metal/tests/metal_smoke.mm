#include "metal_backend.h"

#import <AppKit/AppKit.h>

#include <array>
#include <cstdint>
#include <cstdlib>
#include <cstdio>
#include <memory>
#include <vector>

int main()
{
    @autoreleasepool {
        [NSApplication sharedApplication];
        NSWindow* window = [[NSWindow alloc]
            initWithContentRect:NSMakeRect(0, 0, 128, 96)
            styleMask:NSWindowStyleMaskBorderless
            backing:NSBackingStoreBuffered defer:NO];
        [window orderFront:nil];

        std::unique_ptr<jalium::IRenderBackend> backend(jalium::CreateMetalBackend());
        if (!backend) return 10;
        std::unique_ptr<jalium::RenderTarget> target(
            backend->CreateRenderTarget((__bridge void*)window, 128, 96));
        std::unique_ptr<jalium::Brush> red(
            backend->CreateSolidBrush(1.0f, 0.0f, 0.0f, 1.0f));
        if (!target || !red || backend->CheckDeviceStatus() != JALIUM_OK) return 11;

        auto finishAndRead = [&](int errorBase) {
            if (target->RequestReadback() != JALIUM_OK) std::exit(errorBase);
            if (target->EndDraw() != JALIUM_OK) std::exit(errorBase + 1);
            int32_t width = 0, height = 0;
            if (target->FetchReadback(nullptr, 0, &width, &height) != JALIUM_OK ||
                width != 128 || height != 96) std::exit(errorBase + 2);
            std::vector<uint8_t> pixels(static_cast<size_t>(width) * height * 4);
            if (target->FetchReadback(pixels.data(), width * 4, &width, &height) != JALIUM_OK)
                std::exit(errorBase + 3);
            return pixels;
        };
        auto pixel = [](const std::vector<uint8_t>& pixels,int x,int y){
            const size_t offset=(static_cast<size_t>(y)*128+x)*4;
            return std::array<uint8_t,4>{pixels[offset],pixels[offset+1],
                pixels[offset+2],pixels[offset+3]};};

        // Impeller baseline.
        if (target->BeginDraw() != JALIUM_OK) return 12;
        target->Clear(0, 0, 0, 1);
        target->FillRectangle(0, 0, 128, 96, red.get());
        auto pixels=finishAndRead(13);
        auto center=pixel(pixels,64,48);
        if(center[2]<245||center[1]>8||center[0]>8||center[3]<245)return 17;

        // Damage clear must preserve the persistent scene outside the rect.
        target->AddDirtyRect(0,0,16,16);
        if(target->BeginDraw()!=JALIUM_OK)return 18;
        target->Clear(0,0,1,1);
        pixels=finishAndRead(19);
        auto damaged=pixel(pixels,8,8);center=pixel(pixels,64,48);
        if(damaged[0]<245||damaged[2]>8||center[2]<245||center[0]>8)return 23;

        // The packaged 19-stage compute graph must be loadable and paint in
        // painter order, not merely exist as a file in the app bundle.
        if(target->SetRenderingEngine(JALIUM_ENGINE_VELLO)!=JALIUM_OK)return 24;
        std::unique_ptr<jalium::Brush> green(
            backend->CreateSolidBrush(0,1,0,1));
        target->SetFullInvalidation();
        if(target->BeginDraw()!=JALIUM_OK)return 25;
        target->Clear(0,0,0,1);target->FillEllipse(64,48,28,24,green.get());
        pixels=finishAndRead(26);center=pixel(pixels,64,48);
        if(center[1]<220||center[2]>20||center[0]>20)return 30;

        // Runtime SourceHlsl: DXC -> SPIR-V -> MSL -> Metal pipeline.
        constexpr const char* invert = R"HLSL(
Texture2D content : register(t0);
SamplerState samp : register(s0);
struct PsIn { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
float4 main(PsIn i) : SV_Target {
    float4 c=content.Sample(samp,i.uv);
    return float4(c.a-c.r,c.a-c.g,c.a-c.b,c.a);
})HLSL";
        if(target->SetRenderingEngine(JALIUM_ENGINE_IMPELLER)!=JALIUM_OK)return 31;
        target->SetFullInvalidation();
        if(target->BeginDraw()!=JALIUM_OK)return 32;
        target->Clear(0,0,0,1);target->BeginEffectCapture(32,24,64,48);
        target->FillRectangle(32,24,64,48,red.get());target->EndEffectCapture();
        target->DrawShaderEffectFromSource(32,24,64,48,invert,nullptr,0);
        pixels=finishAndRead(33);center=pixel(pixels,64,48);
        if(center[0]<220||center[1]<220||center[2]>25||center[3]<245)return 37;
        [window orderOut:nil];
        return 0;
    }
}
