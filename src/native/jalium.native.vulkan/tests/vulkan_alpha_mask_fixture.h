#pragma once
#include "jalium_alpha_mask.h"
#include <array>

void TestAlphaMasks(JaliumRenderTarget* target, JaliumContext* context,
                    JaliumBrush* red, JaliumBrush* blue, JaliumBrush* green)
{
    if (!Check(jalium_render_target_supports_alpha_masks(target) == 1,
               "Vulkan exposes grouped alpha masks")) return;
    int cases = 0;
    const uint8_t half[] = {128}, opaque[] = {255};
    const auto begin = [&] {
        Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "begin alpha frame");
        jalium_render_target_set_full_invalidation(target);
        jalium_render_target_clear(target, 1, 1, 1, 1);
    };
    const auto end = [&] {
        jalium_render_target_request_readback(target);
        Check(jalium_render_target_end_draw(target) == JALIUM_OK, "submit alpha Vulkan frame");
        ++cases;
        auto pixels = Capture(target, 256, 192);
        if (const char* directory = std::getenv("JALIUM_VK_ALPHA_CAPTURE_DIR")) {
            const std::string name = std::string(directory) + "/alpha-" + std::to_string(cases) + ".ppm";
            FILE* file = std::fopen(name.c_str(), "wb");
            if (Check(file != nullptr, "open alpha-mask readback evidence")) {
                std::fprintf(file, "P6\n256 192\n255\n");
                for (size_t i = 0; i + 3 < pixels.size(); i += 4) {
                    const uint8_t rgb[] = {pixels[i + 2], pixels[i + 1], pixels[i]};
                    std::fwrite(rgb, 1, 3, file);
                }
                Check(std::fclose(file) == 0, "save alpha-mask readback evidence");
            }
        }
        return pixels;
    };
    const auto open = [&](float x, float y, float w, float h) {
        Check(jalium_alpha_mask_begin_capture(target,x,y,w,h) == JALIUM_OK,"open isolated mask group");
    };
    const auto close = [&](const uint8_t* a, uint32_t width, uint32_t height,
                           float x, float y, float w, float h) {
        jalium_effect_end_capture(target);
        Check(jalium_draw_alpha_mask_effect(target,a,width*height,width,height,width,
            x,y,w,h,x,y,w,h) == JALIUM_OK,"composite completed mask group");
    };
    for (auto engine : {JALIUM_ENGINE_IMPELLER,JALIUM_ENGINE_VELLO}) {
        Check(jalium_render_target_set_engine(target,engine)==JALIUM_OK,"select alpha path engine");
        Check(jalium_render_target_get_engine(target)==engine,"alpha path uses requested engine");
        for (int dpi : {1,2}) {
            jalium_render_target_set_dpi(target,float(96*dpi),float(96*dpi));
            begin();
            // Lazy vector-engine work before the group and bitmap work after it
            // must survive the temporary CPU subframe.
            const float beforePath[] = {0,8,0,0,8,8,0,0,8,5};
            jalium_fill_path(target,0,0,beforePath,10,blue,1,-1);
            open(12,12,100,64);
            jalium_draw_fill_rectangle(target,16,16,64,48,red);
            jalium_draw_fill_rectangle(target,48,16,64,48,blue);
            close(half,1,1,12,12,100,64);
            const float afterPath[] = {0,12,76,0,12,84,0,4,84,5};
            jalium_fill_path(target,4,76,afterPath,10,green,1,-1);
            auto image=end();
            CheckPixel(image,256,4*dpi,4*dpi,0,0,255,"preceding GPU work survives mask group");
            CheckPixel(image,256,24*dpi,32*dpi,255,127,127,"mask is applied once to red coverage");
            CheckPixel(image,256,56*dpi,32*dpi,127,127,255,"overlap is composited before mask");
            CheckPixel(image,256,8*dpi,80*dpi,0,255,0,"following GPU work survives mask group");

            begin();jalium_push_opacity(target,.5f);jalium_push_clip(target,24,16,64,48);
            open(12,12,100,64);jalium_draw_fill_rectangle(target,12,12,100,64,red);close(half,1,1,12,12,100,64);
            jalium_pop_clip(target);jalium_pop_opacity(target);image=end();
            CheckPixel(image,256,40*dpi,32*dpi,255,191,191,"parent opacity is applied once");
            CheckPixel(image,256,16*dpi,32*dpi,255,255,255,"parent clip is restored for composite");

            begin();open(12,12,100,64);jalium_draw_fill_rectangle(target,16,16,24,32,red);
            open(40,12,64,64);jalium_draw_fill_rectangle(target,48,16,40,32,blue);close(half,1,1,40,12,64,64);
            close(half,1,1,12,12,100,64);image=end();
            CheckPixel(image,256,24*dpi,32*dpi,255,127,127,"outer mask retains its earlier content");
            CheckPixel(image,256,56*dpi,32*dpi,191,191,255,"nested masks multiply completed coverage once");

            begin();jalium_effect_begin_capture(target,8,8,104,72);
            jalium_draw_fill_rectangle(target,16,16,24,32,blue);
            open(40,12,64,64);jalium_draw_fill_rectangle(target,48,16,40,32,red);close(half,1,1,40,12,64,64);
            jalium_effect_end_capture(target);
            const float identity[] = {1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1, 0,0,0,0};
            Check(jalium_draw_color_matrix_chain_effect(target,8,8,104,72,identity,1)==JALIUM_OK,"effect around mask retains its own capture");
            image=end();
            CheckPixel(image,256,24*dpi,32*dpi,0,0,255,"parent GPU effect retains preceding content");
            CheckPixel(image,256,56*dpi,32*dpi,255,127,127,"parent GPU effect includes masked child");

            for (float childOpacity : {1.0f,.5f}) {
                begin();open(8,8,104,72);jalium_effect_begin_capture(target,12,12,80,48);
                jalium_push_opacity(target,childOpacity);jalium_draw_fill_rectangle(target,16,16,64,32,red);jalium_pop_opacity(target);
                jalium_effect_end_capture(target);
                Check(jalium_draw_color_matrix_chain_effect(target,12,12,80,48,identity,1)==JALIUM_OK,"nested CPU effect inside mask");
                close(half,1,1,8,8,104,72);image=end();
                const int remaining = childOpacity==1 ? 127 : 191;
                CheckPixel(image,256,32*dpi,32*dpi,255,remaining,remaining,"nested translucent effect retains premultiplied color");
            }

            const std::array<std::array<float,6>,4> matrices = {{{1,0,0,1,12,10},{.85f,.12f,-.08f,.9f,18,6},{-1,0,0,1,104,8},{.9f,.08f,.13f,.8f,10,10}}};
            for (auto matrix : matrices) {
                begin();jalium_push_transform(target,matrix.data());open(0,0,80,64);
                jalium_draw_fill_rectangle(target,0,0,80,64,red);
                uint8_t coverage[] = {255,0,64,128};close(coverage,2,2,0,0,80,64);
                // Pixels are copied, not retained from borrowed A8 storage.
                std::fill(std::begin(coverage),std::end(coverage),0);
                jalium_pop_transform(target);image=end();
                for(float& value:matrix)value*=dpi;
                const uint8_t expectedCoverage[] = {255,0,64,128};
                jalium::AlphaMaskSampler sample(expectedCoverage,2,2,2,0,0,80,64,matrix.data());
                for (int y=12*dpi;y<64*dpi;y+=7*dpi) for(int x=16*dpi;x<96*dpi;x+=9*dpi) {
                    const auto a=sample.At(x+.5,y+.5);
                    if(a>8&&a<247)CheckPixel(image,256,x,y,255,255-a,255-a,"affine/DPI mask maps back to original local space");
                }
            }

            begin();open(12,12,80,64);jalium_draw_fill_rectangle(target,16,16,64,48,red);
            jalium_effect_end_capture(target);
            Check(jalium_draw_alpha_mask_effect(target,opaque,1,1,1,0,12,12,80,64,12,12,80,64)==JALIUM_ERROR_INVALID_ARGUMENT,"invalid A8 stride is rejected");
            Check(jalium_draw_alpha_mask_effect(target,half,1,1,1,1,12,12,80,64,12,12,80,64)==JALIUM_OK,"invalid descriptor does not consume pending group");
            Check(jalium_draw_alpha_mask_effect(target,half,1,1,1,1,12,12,80,64,12,12,80,64)==JALIUM_ERROR_INVALID_STATE,"completed group is consumed exactly once");
            image=end();CheckPixel(image,256,32*dpi,32*dpi,255,127,127,"invalid/repeated calls do not duplicate composite");
            Check(jalium_alpha_mask_begin_capture(target,0,0,10,10)==JALIUM_ERROR_INVALID_STATE,"capture outside a draw session is rejected");
        }
    }
    jalium_render_target_set_dpi(target,96,96);
    std::cout<<"{\"count\":"<<cases<<",\"failed\":"<<failures<<",\"backend\":\"Vulkan\",\"maskSubframe\":\"CPU\",\"parentComposite\":\"Vulkan bitmap or parent CPU capture\"}\n";
}
