#include "jalium_api.h"
#include "jalium_platform.h"
#include "jalium_video_surface.h"
#include <vulkan/vulkan.h>
#include <fcntl.h>
#include <sys/mman.h>
#include <unistd.h>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <thread>
#include <string>
#include <vector>

extern "C" void jalium_vulkan_init();

namespace {
int failures = 0;

bool Check(bool condition, const char* message)
{
    if (!condition) {
        std::cerr << "FAILED: " << message << '\n';
        ++failures;
    }
    return condition;
}

void PumpEvents()
{
    for (int i = 0; i < 40; ++i) {
        (void)jalium_platform_poll_events();
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
    }
}

std::vector<uint8_t> Capture(JaliumRenderTarget* target, int width, int height)
{
    int32_t capturedWidth = 0, capturedHeight = 0;
    if (!Check(jalium_render_target_fetch_readback(
            target, nullptr, 0, &capturedWidth, &capturedHeight) == JALIUM_OK &&
            capturedWidth == width && capturedHeight == height,
            "GPU capture reports the current target dimensions")) return {};
    std::vector<uint8_t> pixels(static_cast<size_t>(width) * height * 4);
    if (!Check(jalium_render_target_fetch_readback(target, pixels.data(), width * 4,
            &capturedWidth, &capturedHeight) == JALIUM_OK,
            "GPU capture completes after submission")) return {};
    return pixels;
}

void CheckPixel(const std::vector<uint8_t>& pixels, int width,
                int x, int y, int r, int g, int b, const char* message)
{
    const size_t i = (static_cast<size_t>(y) * width + x) * 4;
    if (pixels.size() < i + 4) return;
    if (!Check(std::abs(int(pixels[i]) - b) <= 3 &&
               std::abs(int(pixels[i + 1]) - g) <= 3 &&
               std::abs(int(pixels[i + 2]) - r) <= 3 && pixels[i + 3] >= 252,
               message)) {
        std::cerr << "  pixel " << x << ',' << y << " BGRA="
                  << int(pixels[i]) << ',' << int(pixels[i + 1]) << ','
                  << int(pixels[i + 2]) << ',' << int(pixels[i + 3]) << '\n';
    }
}

#include "vulkan_video_render_fixture.h"
#include "vulkan_drawing_parity_fixture.h"

bool DrawFrame(JaliumRenderTarget* target, JaliumBrush* red, JaliumBrush* blue, bool stress = false)
{
    if (!Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "begin Vulkan frame"))
        return false;
    jalium_render_target_set_full_invalidation(target);
    jalium_render_target_clear(target, 1, 1, 1, 1);
    jalium_draw_fill_rectangle(target, 16, 16, 64, 48, red);
    jalium_draw_fill_ellipse(target, 144, 48, 24, 24, blue);
    jalium_draw_line(target, 16, 96, 176, 96, blue, 3);
    const float triangle[] = {0, 240, 16, 0, 216, 72, 5};
    jalium_fill_path(target, 192, 16, triangle, 7, red, 1, -1);
    if (stress) {
        // More than two 64 KiB constant chunks, on both in-flight slots.
        for (int i = 0; i < 600; ++i)
            jalium_draw_fill_rectangle(target, 16 + (i % 100) * 2,
                112 + (i / 100) * 8, 2, 4, i % 2 == 0 ? red : blue);
    }
    Check(jalium_render_target_request_readback(target) == JALIUM_OK, "request GPU readback");
    return Check(jalium_render_target_end_draw(target) == JALIUM_OK, "submit and present Vulkan frame");
}

void TestTexturesAndEffects(JaliumRenderTarget* target, JaliumContext* context,
                           JaliumBrush* red, JaliumBrush* blue)
{
    const uint8_t greenPixel[] = {0, 255, 0, 255};
    const JaliumGradientStop stops[] = {{0, 1, 0, 0, 1}, {1, 0, 0, 1, 1}};
    auto* bitmap = jalium_bitmap_create_from_pixels(context, greenPixel, 1, 1, 4);
    auto* gradient = jalium_brush_create_linear_gradient(context, 16, 16, 80, 16, stops, 2, 0);
    auto* text = jalium_text_format_create(context, L"sans-serif", 18, 400, 0);
    if (Check(bitmap && gradient && text, "create texture, gradient and text resources") &&
        Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "begin textured/effect frame")) {
        jalium_render_target_set_full_invalidation(target);
        jalium_render_target_clear(target, 1, 1, 1, 1);
        jalium_draw_fill_rectangle(target, 16, 16, 64, 48, gradient);
        JaliumEllipticalRectClip clip{};
        clip.structSize = sizeof(clip);
        clip.edges = 15;
        clip.x = 112; clip.y = 16; clip.width = 64; clip.height = 64;
        for (int corner = 0; corner < 4; ++corner) {
            clip.radiusX[corner] = 16;
            clip.radiusY[corner] = 24;
        }
        Check(jalium_push_elliptical_rect_clip(target, &clip) == JALIUM_OK,
              "bind the elliptical clip alongside texture and uniform descriptors");
        jalium_draw_bitmap(target, bitmap, 112, 16, 64, 64, 1);
        jalium_pop_clip(target);
        jalium_draw_line(target, 16, 88, 80, 88, blue, 3);
        jalium_draw_text(target, L"Vulkan", 6, text, 16, 104, 88, 40, blue);

        jalium_effect_begin_capture(target, 112, 104, 64, 48);
        jalium_draw_fill_rectangle(target, 128, 116, 32, 24, red);
        jalium_effect_end_capture(target);
        const float redToGreen[] = {
            0, 0, 0, 0, 0,
            1, 0, 0, 0, 0,
            0, 0, 0, 0, 0,
            0, 0, 0, 1, 0};
        jalium_draw_color_matrix_effect(target, 112, 104, 64, 48, redToGreen);
        jalium_effect_begin_capture(target, 192, 104, 48, 48);
        jalium_draw_fill_rectangle(target, 200, 112, 32, 32, red);
        jalium_effect_end_capture(target);
        jalium_draw_blur_effect(target, 192, 104, 48, 48, 2, 0, 0);
        Check(jalium_render_target_request_readback(target) == JALIUM_OK, "capture textures and effects");
        Check(jalium_render_target_end_draw(target) == JALIUM_OK, "present textures and effects");
        auto pixels = Capture(target, 256, 192);
        CheckPixel(pixels, 256, 144, 48, 0, 255, 0, "textured bitmap keeps its green color");
        CheckPixel(pixels, 256, 113, 17, 255, 255, 255, "elliptical clip removes the bitmap corner");
        CheckPixel(pixels, 256, 48, 88, 0, 0, 255, "encoded AA line survives a later CPU capture transition");
        CheckPixel(pixels, 256, 144, 128, 0, 255, 0, "embedded color-matrix shader transforms red to green");
        CheckPixel(pixels, 256, 216, 128, 255, 0, 0, "blur keeps the isolated red interior");
        if (!pixels.empty()) {
            const size_t left = (32u * 256 + 20) * 4;
            const size_t right = (32u * 256 + 76) * 4;
            if (!Check(pixels[left + 2] > 220 && pixels[left] < 35 &&
                  pixels[right] > 220 && pixels[right + 2] < 35,
                  "gradient endpoints retain distinct red and blue values")) {
                std::cerr << "  gradient endpoints BGRA=" << int(pixels[left]) << ',' << int(pixels[left + 1]) << ',' << int(pixels[left + 2])
                          << " / " << int(pixels[right]) << ',' << int(pixels[right + 1]) << ',' << int(pixels[right + 2]) << '\n';
            }
            unsigned textPixels = 0;
            for (int y = 104; y < 144; ++y)
                for (int x = 16; x < 104; ++x) {
                    const size_t i = (y * 256u + x) * 4;
                    if (pixels[i] > 128 && pixels[i + 1] < 180 && pixels[i + 2] < 180)
                        ++textPixels;
                }
            Check(textPixels > 8, "text glyphs survive texture/uniform pipeline switches");
        }
    }
    // The next frame can keep a warm CPU mirror while selecting GPU replay.
    // Gradients must be present in both representations after CPU captures.
    if (gradient && Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "begin post-capture gradient frame")) {
        jalium_render_target_set_full_invalidation(target);
        jalium_render_target_clear(target, 1, 1, 1, 1);
        jalium_draw_fill_rectangle(target, 16, 16, 64, 48, gradient);
        jalium_render_target_request_readback(target);
        Check(jalium_render_target_end_draw(target) == JALIUM_OK, "submit post-capture gradient frame");
        const auto pixels = Capture(target, 256, 192);
        CheckPixel(pixels, 256, 20, 32, 237, 0, 18, "post-capture gradient retains its red endpoint");
        CheckPixel(pixels, 256, 76, 32, 14, 0, 241, "post-capture gradient retains its blue endpoint");
    }
    if (text) jalium_text_format_destroy(text);
    if (gradient) jalium_brush_destroy(gradient);
    if (bitmap) jalium_bitmap_destroy(bitmap);
}
#include "vulkan_alpha_mask_fixture.h"
void TestLongFilteredText(JaliumRenderTarget* target, JaliumContext* context, JaliumBrush* brush)
{
    const std::u16string source(100000, u'A'), reference(64, u'A'), prefixedReference(576, u'A');
    auto* format = jalium_text_format_create(context, reinterpret_cast<const wchar_t*>(u"DejaVu Sans"), 20, 400, 0);
    if (!Check(format != nullptr, "create long-text format")) return;
    jalium_text_format_set_word_wrapping(format, JALIUM_WORD_WRAP_NONE);
    JaliumTextHitTestResult caret{}, prefix{};
    Check(jalium_text_format_hit_test_text_position(format, reinterpret_cast<const wchar_t*>(source.data()),
        source.size(), 10000000, 40, 1024, 0, &caret) == JALIUM_OK, "query the scrolled source origin");
    Check(jalium_text_format_hit_test_text_position(format, reinterpret_cast<const wchar_t*>(source.data()),
        source.size(), 10000000, 40, 512, 0, &prefix) == JALIUM_OK, "query the independent reference prefix");
    int frames = 0;
    for (auto engine : {JALIUM_ENGINE_IMPELLER, JALIUM_ENGINE_VELLO})
    for (int transform : {0, 1, 2}) for (bool scroll : {false, true}) {
        Check(jalium_render_target_set_engine(target, engine) == JALIUM_OK, "select long-text engine");
        auto draw = [&](const std::u16string& text, bool large) {
            Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "begin bounded long-text frame");
            jalium_render_target_set_full_invalidation(target);
            jalium_render_target_clear(target, 1, 1, 1, 1);
            const uint32_t range[]{0, static_cast<uint32_t>(text.size())};
            Check(jalium_render_target_set_text_glyph_ranges(target, range, 1, 1) == JALIUM_OK, "filter the complete logical line");
            const float skew[]{.91f, .08f, -.06f, .92f, 9, 3}, scale[]{2, 0, 0, 2, 0, 0};
            if (transform) jalium_push_transform(target, transform == 1 ? skew : scale);
            jalium_draw_text(target, reinterpret_cast<const wchar_t*>(text.data()), text.size(), format,
                scroll ? -(large ? caret.caretX : prefix.caretX) : 0, 10, large || scroll ? 10000000 : 512, 40, brush);
            if (transform) jalium_pop_transform(target);
            Check(jalium_render_target_set_text_glyph_ranges(target, nullptr, 0, 0) == JALIUM_OK, "reset source filtering");
            Check(jalium_render_target_request_readback(target) == JALIUM_OK, "capture long text");
            Check(jalium_render_target_end_draw(target) == JALIUM_OK, "submit viewport-bounded text");
            return Capture(target, 256, 192);
        };
        auto actual = draw(source, true), expected = draw(scroll ? prefixedReference : reference, false);
        int largest = 0;
        if (Check(actual.size() == expected.size() && !actual.empty(), "long-text readback dimensions match"))
            for (size_t i = 0; i < actual.size(); ++i) largest = std::max(largest, std::abs(int(actual[i]) - int(expected[i])));
        std::cout << "long engine=" << engine << " transform=" << transform << " scroll=" << scroll << " largest=" << largest << "\n";
        Check(largest <= 2, "very long filtered/scrolled text equals the visible independent short reference");
        ++frames;
    }
    jalium_text_format_destroy(format);
    std::cout << "{\"count\":" << frames << ",\"failed\":" << failures << ",\"sourceLength\":100000}\n";
}

void TestSourceTextRasterGrid(JaliumRenderTarget* target, JaliumContext* context)
{
    auto* format=jalium_text_format_create(context,reinterpret_cast<const wchar_t*>(u"DejaVu Sans"),28,400,0);
    if(!Check(format!=nullptr,"create source-text raster format"))return;
    int comparisons=0;
    for(const std::u16string text:{u"ABCD",u"אב ABCD"}) {
        const auto length=static_cast<uint32_t>(text.size());
        JaliumTextSpan span{format,0,length,1,0,0,1};
        std::vector<JaliumTextAdvanceUnit> units;
        for(uint32_t i=0;i<length;i++)units.push_back({i,1,0,0,0,0});
        auto* paragraph=jalium_text_paragraph_create_context_advanced(format,reinterpret_cast<const uint16_t*>(text.data()),length,
            &span,1,units.data(),length,10000000,1,0);
        if(!Check(paragraph!=nullptr,"retain source-text paragraph"))continue;
        for(auto engine:{JALIUM_ENGINE_IMPELLER,JALIUM_ENGINE_VELLO})
        for(int dpi:{1,2})for(bool affine:{false,true})for(bool fractional:{false,true}) {
            Check(jalium_render_target_set_engine(target,engine)==JALIUM_OK,"select source-text raster engine");
            jalium_render_target_set_dpi(target,96.0f*dpi,96.0f*dpi);
            auto draw=[&](int split) {
                Check(jalium_render_target_begin_draw(target)==JALIUM_OK,"begin source-text raster frame");
                jalium_render_target_set_full_invalidation(target);jalium_render_target_clear(target,0,0,0,0);
                const float transform[]{.91f,.08f,-.06f,.92f,9,3};
                if(affine)jalium_push_transform(target,transform);
                const uint32_t step=split==0?length:split==1?2u:1u;
                for(uint32_t first=0;first<length;first+=step)
                    Check(jalium_render_target_draw_paragraph_slice(target,paragraph,0,
                        fractional?8.125f:8.0f,fractional?11.703125f:12.0f,first,std::min(step,length-first),.85f,.2f,.35f,1)==JALIUM_OK,
                        "draw retained source-text slice");
                if(affine)jalium_pop_transform(target);
                Check(jalium_render_target_request_readback(target)==JALIUM_OK,"capture source-text raster frame");
                Check(jalium_render_target_end_draw(target)==JALIUM_OK,"submit source-text raster frame");
                return Capture(target,256,192);
            };
            const auto expected=draw(0);
            Check(!expected.empty()&&std::any_of(expected.begin(),expected.end(),[](uint8_t value){return value!=0;}),"source-text reference contains ink");
            for(int split:{1,2}) {
                const auto actual=draw(split);
                if(actual!=expected) {
                    size_t different=0,first=0;int largest=0;
                    for(size_t i=0;i<std::min(actual.size(),expected.size());++i) {
                        const int difference=std::abs(int(actual[i])-int(expected[i]));
                        if(difference){if(!different)first=i;++different;largest=std::max(largest,difference);}
                    }
                    std::cerr<<"  source-raster length="<<length<<" engine="<<engine<<" dpi="<<dpi
                             <<" affine="<<affine<<" fractional="<<fractional<<" split="<<split
                             <<" differentBytes="<<different<<" largest="<<largest
                             <<" first="<<(first/4)%256<<','<<(first/4)/256<<','<<first%4<<'\n';
                    if(const char* directory=std::getenv("JALIUM_VK_SOURCE_TEXT_DIAGNOSTICS")) {
                        const std::string stem=std::string(directory)+"/raster-"+std::to_string(length)+"-"+
                            std::to_string(engine)+"-"+std::to_string(dpi)+"-"+std::to_string(affine)+"-"+
                            std::to_string(fractional)+"-"+std::to_string(split);
                        for(const auto& [suffix,pixels]:{std::pair{"actual",&actual},std::pair{"expected",&expected}})
                            if(auto* file=std::fopen((stem+"-"+suffix+".bgra").c_str(),"wb")) {
                                std::fwrite(pixels->data(),1,pixels->size(),file);std::fclose(file);
                            }
                    }
                }
                Check(actual==expected,"source slicing preserves the exact retained glyph raster grid");++comparisons;
            }
        }
        jalium_text_paragraph_destroy(paragraph);
    }
    jalium_render_target_set_dpi(target,96,96);jalium_text_format_destroy(format);
    std::cout<<"{\"sourceTextRasterComparisons\":"<<comparisons<<",\"failed\":"<<failures<<"}\n";
}

} // namespace

int main()
{
    if (!std::getenv("DISPLAY") && !std::getenv("WAYLAND_DISPLAY")) {
        std::cout << "SKIP: Vulkan rendering needs an X11 or Wayland display.\n";
        return 77;
    }
    if (!Check(jalium_platform_init() == JALIUM_OK, "initialize Linux platform")) return 1;
    if (const char* requestedPlatform = std::getenv("JALIUM_WINDOW_SYSTEM")) {
        if (std::strcmp(requestedPlatform, "x11") == 0)
            Check(jalium_platform_get_current() == JALIUM_PLATFORM_LINUX_X11,
                  "the X11 fixture uses a real X11 platform window");
        if (std::strcmp(requestedPlatform, "wayland") == 0)
            Check(jalium_platform_get_current() == JALIUM_PLATFORM_LINUX_WAYLAND,
                  "the Wayland fixture cannot silently fall back to X11");
    }
    jalium_vulkan_init();
    JaliumWindowParams parameters{};
    parameters.title = reinterpret_cast<const JaliumUtf16Char*>(u"Jalium Vulkan regression");
    parameters.x = JALIUM_DEFAULT_POS;
    parameters.y = JALIUM_DEFAULT_POS;
    parameters.width = 256;
    parameters.height = 192;
    parameters.style = JALIUM_WINDOW_STYLE_DEFAULT;
    auto* window = jalium_window_create(&parameters);
    auto* context = jalium_context_create(JALIUM_BACKEND_VULKAN);
    JaliumRenderTarget* target = nullptr;
    JaliumBrush *red = nullptr, *blue = nullptr, *green = nullptr;
    int videoStatus = 0;
    if (Check(window && context, "create native window and Vulkan context")) {
        Check(jalium_context_get_backend(context) == JALIUM_BACKEND_VULKAN,
              "the rendering regression runs on Vulkan");
        jalium_window_show(window);
        PumpEvents();
        const auto surface = jalium_window_get_surface(window);
        target = jalium_render_target_create_for_surface(context, &surface, 256, 192);
        red = jalium_brush_create_solid(context, 1, 0, 0, 1);
        blue = jalium_brush_create_solid(context, 0, 0, 1, 1);
        green = jalium_brush_create_solid(context, 0, 1, 0, 1);
    }
    if (Check(target && red && blue && green, "create Vulkan swapchain and brushes")) {
        if (std::getenv("JALIUM_VK_TEST_SOURCE_TEXT")) {
            TestSourceTextRasterGrid(target,context);
        } else if (std::getenv("JALIUM_VK_TEST_LONG_TEXT")) {
            TestLongFilteredText(target, context, blue);
        } else if (std::getenv("JALIUM_VK_TEST_ALPHA_MASKS")) {
            TestAlphaMasks(target,context,red,blue,green);
        } else {
        if (std::getenv("JALIUM_VK_TEST_VIDEO"))
            videoStatus = TestVideoFdRendering(target, context);
        for (auto engine : {JALIUM_ENGINE_IMPELLER, JALIUM_ENGINE_VELLO}) {
            Check(jalium_render_target_set_engine(target, engine) == JALIUM_OK,
                  "switch the rendering engine");
            Check(jalium_render_target_get_engine(target) == engine,
                  "the requested rendering engine is active");
            for (int frame = 0; frame < 6; ++frame) {
                if (!DrawFrame(target, red, blue, frame >= 4)) break;
                auto pixels = Capture(target, 256, 192);
                CheckPixel(pixels, 256, 4, 4, 255, 255, 255, "background survives frame-slot reuse");
                CheckPixel(pixels, 256, 32, 32, 255, 0, 0, "rectangle is opaque red");
                CheckPixel(pixels, 256, 144, 48, 0, 0, 255, "ellipse is opaque blue");
                CheckPixel(pixels, 256, 216, 32, 255, 0, 0, "engine-encoded path is opaque red");
                if (frame >= 4) {
                    CheckPixel(pixels, 256, 17, 114, 255, 0, 0, "constant growth retains the first draw");
                    CheckPixel(pixels, 256, 19, 114, 0, 0, 255, "consecutive draws retain independent colors");
                    CheckPixel(pixels, 256, 129, 130, 255, 0, 0, "constant growth retains the second chunk");
                    CheckPixel(pixels, 256, 215, 154, 0, 0, 255, "constant growth retains the final draw");
                }
            }
        }

        // Partial replay must preserve pixels outside damage across acquired images.
        Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "begin partial Vulkan frame");
        jalium_render_target_add_dirty_rect(target, 112, 16, 64, 64);
        jalium_draw_fill_rectangle(target, 112, 16, 64, 64, green);
        Check(jalium_render_target_request_readback(target) == JALIUM_OK, "capture partial repaint");
        Check(jalium_render_target_end_draw(target) == JALIUM_OK, "present partial repaint");
        auto pixels = Capture(target, 256, 192);
        CheckPixel(pixels, 256, 32, 32, 255, 0, 0, "partial repaint retains the red rectangle");
        CheckPixel(pixels, 256, 144, 48, 0, 255, 0, "partial repaint updates the damaged region");

        TestTexturesAndEffects(target, context, red, blue);
        TestBitmapTransformsAndUpdates(target, context);
        TestTextOpacity(target, context, blue);

        for (int cycle = 0; cycle < 4; ++cycle) {
            const int width = cycle % 2 == 0 ? 320 : 256;
            const int height = cycle % 2 == 0 ? 224 : 192;
            jalium_window_resize(window, width, height);
            PumpEvents();
            jalium_render_target_set_vsync(target, cycle % 2);
            Check(jalium_render_target_resize(target, width, height) == JALIUM_OK,
                  "recreate swapchain after resize and VSync change");
            Check(jalium_render_target_resize(target, width, height) == JALIUM_OK,
                  "same-size resize is harmless");
            if (!DrawFrame(target, red, blue)) break;
            pixels = Capture(target, width, height);
            CheckPixel(pixels, width, 32, 32, 255, 0, 0, "resized swapchain preserves drawing colors");
            CheckPixel(pixels, width, width - 4, height - 4, 255, 255, 255,
                       "resized capture includes the new bottom-right extent");
        }
        Check(jalium_context_check_device_status(context) == JALIUM_OK,
              "device remains healthy after engine switches and swapchain rebuilds");

        // PPM is dependency-free evidence of the actual GPU readback.
        if (const char* path = std::getenv("JALIUM_VK_TEST_CAPTURE")) {
            if (pixels.size() == 256u * 192u * 4u) {
                if (FILE* output = std::fopen(path, "wb")) {
                    std::fprintf(output, "P6\n256 192\n255\n");
                    for (size_t i = 0; i < pixels.size(); i += 4) {
                        const uint8_t rgb[] = {pixels[i + 2], pixels[i + 1], pixels[i]};
                        std::fwrite(rgb, 1, 3, output);
                    }
                    std::fclose(output);
                } else Check(false, "write requested GPU capture");
            } else Check(false, "requested GPU capture contains a completed frame");
        }
    }
    }
    if (green) jalium_brush_destroy(green);
    if (blue) jalium_brush_destroy(blue);
    if (red) jalium_brush_destroy(red);
    if (target) jalium_render_target_destroy(target);
    if (context) jalium_context_destroy(context);
    if (window) jalium_window_destroy(window);
    jalium_platform_shutdown();
    if (failures == 0) std::cout << "Vulkan Linux rendering tests passed.\n";
    if (!failures && videoStatus == 77) return 77;
    return failures == 0 ? 0 : 1;
}
