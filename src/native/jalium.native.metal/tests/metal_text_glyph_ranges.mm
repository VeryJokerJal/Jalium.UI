#include "jalium_api.h"
#import <AppKit/AppKit.h>
#import <QuartzCore/CAMetalLayer.h>
#include <algorithm>
#include <cstdio>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
int checks = 0;
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); ++checks; }
const wchar_t* Managed(const char16_t* text) { return reinterpret_cast<const wchar_t*>(text); }
template<class T, void (*Destroy)(T*)> using Handle = std::unique_ptr<T, decltype(Destroy)>;
struct Surface {
    Handle<JaliumContext, jalium_context_destroy> context{jalium_context_create(JALIUM_BACKEND_METAL), jalium_context_destroy};
    Handle<JaliumRenderTarget, jalium_render_target_destroy> target{nullptr, jalium_render_target_destroy};
    Handle<JaliumTextFormat, jalium_text_format_destroy> font{nullptr, jalium_text_format_destroy};
    Handle<JaliumBrush, jalium_brush_destroy> brush{nullptr, jalium_brush_destroy};
    NSView* view;
    int width, height;
    explicit Surface(int scale) : width(420 * scale), height(140 * scale) {
        Check(context != nullptr, "Metal context");
        view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 420, 140)];
        view.wantsLayer = YES; view.layer = [CAMetalLayer layer];
        JaliumSurfaceDescriptor descriptor{}; descriptor.platform = JALIUM_PLATFORM_MACOS;
        descriptor.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
        descriptor.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
        target.reset(jalium_render_target_create_for_surface(context.get(), &descriptor, width, height));
        Check(target != nullptr, "Own windowless text target");
        jalium_render_target_set_dpi(target.get(), 96 * scale, 96 * scale);
        font.reset(jalium_text_format_create(context.get(), Managed(u"Helvetica Neue"), 40, 400, 1));
        brush.reset(jalium_brush_create_solid(context.get(), 1, 0, 0, 1));
        Check(font && brush, "Italic font and brush");
        jalium_text_format_set_word_wrapping(font.get(), 1);
    }
    void Filter(const std::vector<uint32_t>& pairs, int mode) {
        Check(jalium_render_target_set_text_glyph_ranges(target.get(), pairs.data(), pairs.size() / 2, mode) == JALIUM_OK, "Set glyph ranges");
    }
    std::vector<uint8_t> Draw(const std::u16string& text, bool paragraph = false) {
        Handle<JaliumTextParagraph, jalium_text_paragraph_destroy> layout{nullptr, jalium_text_paragraph_destroy};
        if (paragraph) {
            JaliumTextSpan span{font.get(), 0, static_cast<uint32_t>(text.size()), 1, 0, 0, 1};
            layout.reset(jalium_text_paragraph_create_with_wrapping(font.get(), reinterpret_cast<const uint16_t*>(text.data()),
                text.size(), &span, 1, 380, 1, 0, 0, 1));
            Check(layout && jalium_text_paragraph_line_count(layout.get()) == 1, "Shaped paragraph");
        }
        Check(jalium_render_target_request_readback(target.get()) == JALIUM_OK, "Request glyph pixels");
        Check(jalium_render_target_begin_draw(target.get()) == JALIUM_OK, "Begin glyph draw");
        jalium_render_target_clear(target.get(), 0, 0, 0, 0);
        if (layout) Check(jalium_render_target_draw_paragraph_line(target.get(), layout.get(), 0, 20, 20, 1) == JALIUM_OK, "Draw retained paragraph");
        else jalium_draw_text(target.get(), Managed(text.c_str()), text.size(), font.get(), 20, 20, 380, 80, brush.get());
        Check(jalium_render_target_end_draw(target.get()) == JALIUM_OK, "End glyph draw");
        int32_t w = 0, h = 0; std::vector<uint8_t> pixels(width * height * 4);
        Check(jalium_render_target_fetch_readback(target.get(), pixels.data(), width * 4, &w, &h) == JALIUM_OK && w == width && h == height, "Fetch glyph pixels");
        return pixels;
    }
};
bool Empty(const std::vector<uint8_t>& pixels) {
    for (size_t i = 3; i < pixels.size(); i += 4) if (pixels[i]) return false;
    return true;
}
void Basic(Surface& s, bool paragraph, int scale) {
    const std::u16string text = u"fff      Miii";
    s.Filter({}, 0); auto whole = s.Draw(text, paragraph); Check(!Empty(whole), "Normal glyph ink");
    s.Filter({0, static_cast<uint32_t>(text.size())}, 1); Check(s.Draw(text, paragraph) == whole, "Including all preserves exact pixels and italic overhang");
    s.Filter({0, static_cast<uint32_t>(text.size())}, 2); Check(Empty(s.Draw(text, paragraph)), "Excluding all leaves no glyph fragments");
    s.Filter({}, 1); Check(Empty(s.Draw(text, paragraph)), "Empty include is transparent");
    s.Filter({}, 2); Check(s.Draw(text, paragraph) == whole, "Empty exclude preserves glyphs");
    s.Filter({0, 3}, 1); auto left = s.Draw(text, paragraph);
    s.Filter({0, 3}, 2); auto right = s.Draw(text, paragraph);
    Check(!Empty(left) && !Empty(right), "Partial ranges keep selected and neighboring glyphs");
    // The groups are separated by six spaces. This independent partition of the
    // original bitmap checks exact placement and ink, without using caret boxes.
    int cut = 100 * scale;
    for (int y = 0; y < s.height; ++y) for (int x = 0; x < s.width; ++x) for (int c = 0; c < 4; ++c) {
        size_t i = (static_cast<size_t>(y) * s.width + x) * 4 + c;
        if (left[i] != (x < cut ? whole[i] : 0) || right[i] != (x >= cut ? whole[i] : 0))
            throw std::runtime_error("Partial range changed adjacent glyph ink or shaping");
    }
    ++checks;
    s.Filter({2, 1, 0, 2, 1, 1}, 1); Check(s.Draw(text, paragraph) == left, "Unsorted overlapping ranges merge");
    uint32_t overflow[] = {0xffffffffu, 2};
    Check(jalium_render_target_set_text_glyph_ranges(s.target.get(), overflow, 1, 1) == JALIUM_ERROR_INVALID_ARGUMENT, "Reject overflow");
    Check(jalium_render_target_set_text_glyph_ranges(s.target.get(), nullptr, 1, 1) == JALIUM_ERROR_INVALID_ARGUMENT, "Reject missing pairs");
    Check(jalium_render_target_set_text_glyph_ranges(s.target.get(), nullptr, 0, 3) == JALIUM_ERROR_INVALID_ARGUMENT, "Reject unknown mode");
    Check(s.Draw(text, paragraph) == left, "Rejected filters preserve prior state");
    s.Filter({}, 0); Check(s.Draw(text, paragraph) == whole, "Reset restores normal cached rendering");
}
void Clusters(Surface& s, bool paragraph) {
    for (const std::u16string text : {u"🙂", u"a\u0301", u"👩‍👩‍👧‍👦"}) {
        s.Filter({}, 0); auto whole = s.Draw(text, paragraph); Check(!Empty(whole), "Cluster has real glyphs");
        for (uint32_t index = 0; index < text.size(); ++index) {
            s.Filter({index, 1}, 1); Check(s.Draw(text, paragraph) == whole, "Cluster selection preserves color and combining glyphs");
            s.Filter({index, 1}, 2); Check(Empty(s.Draw(text, paragraph)), "Cluster exclusion leaves no surrogate or mark fragments");
        }
    }
}
}
int main() {
    @autoreleasepool {
        try {
            [NSApplication sharedApplication];
            Check(jalium_render_target_set_text_glyph_ranges(nullptr, nullptr, 0, 0) == JALIUM_ERROR_INVALID_ARGUMENT, "Reject null target");
            for (int scale : {1, 2}) {
                Surface s(scale);
                for (bool paragraph : {false, true}) { Basic(s, paragraph, scale); Clusters(s, paragraph); }
                Surface other(scale); s.Filter({0, 3}, 1); auto expected = s.Draw(u"fff      Miii");
                other.Filter({}, 1); Check(Empty(other.Draw(u"fff      Miii")), "Independent target filter");
                Check(s.Draw(u"fff      Miii") == expected, "Interleaving targets preserves visibility");
            }
            std::printf("PASS shaped glyph ranges: %d checks at 1x/2x; real Metal readback; no desktop input\n", checks); return 0;
        } catch (const std::exception& error) { std::fprintf(stderr,"FAIL shaped glyph ranges: %s\n",error.what()); return 1; }
    }
}
