#pragma once

void TestBitmapTransformsAndUpdates(JaliumRenderTarget* target, JaliumContext* context)
{
    const uint8_t horizontal[] = {0, 0, 255, 255, 255, 0, 0, 255};
    const uint8_t vertical[] = {0, 0, 255, 255, 0, 255, 0, 255};
    const uint8_t redPixel[] = {0, 0, 255, 255}, bluePixel[] = {255, 0, 0, 255};
    const uint8_t translucentPixel[] = {0, 0, 255, 128};
    auto* h = jalium_bitmap_create_from_pixels(context, horizontal, 2, 1, 8);
    auto* v = jalium_bitmap_create_from_pixels(context, vertical, 1, 2, 4);
    auto* changing = jalium_bitmap_create_from_pixels(context, redPixel, 1, 1, 4);
    auto* translucent = jalium_bitmap_create_from_pixels(context, translucentPixel, 1, 1, 4);
    if (Check(h && v && changing && translucent, "create bitmap transform/COW resources") &&
        Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "begin bitmap parity frame")) {
        jalium_render_target_set_full_invalidation(target);
        jalium_render_target_clear(target, 1, 1, 1, 1);
        jalium_draw_bitmap_ex(target, h, 16, 16, 64, 32, 1, 3);
        const float mirrorX[] = {-1, 0, 0, 1, 176, 0};
        jalium_push_transform(target, mirrorX);
        jalium_draw_bitmap_ex(target, h, 16, 16, 64, 32, 1, 3);
        jalium_pop_transform(target);
        const float mirrorY[] = {1, 0, 0, -1, 0, 176};
        jalium_push_transform(target, mirrorY);
        jalium_draw_bitmap_ex(target, v, 176, 16, 32, 64, 1, 3);
        jalium_pop_transform(target);
        jalium_draw_bitmap(target, changing, 16, 96, 32, 32, 1);
        Check(jalium_bitmap_update_pixels(changing, bluePixel, 1, 1, 4) != 0,
              "update a bitmap after its earlier draw was recorded");
        jalium_draw_bitmap(target, changing, 96, 96, 32, 32, 1);
        jalium_draw_bitmap(target, translucent, 16, 144, 32, 32, 1);
        jalium_push_opacity(target, .5f);
        jalium_draw_bitmap(target, translucent, 96, 144, 32, 32, 1);
        jalium_pop_opacity(target);
        if (std::getenv("JALIUM_VK_EFFECT_CPU_CAPTURE")) {
            jalium_effect_begin_capture(target, 224, 160, 16, 16);
            jalium_effect_end_capture(target);
        }
        jalium_render_target_request_readback(target);
        Check(jalium_render_target_end_draw(target) == JALIUM_OK, "submit bitmap parity frame");
        const auto pixels = Capture(target, 256, 192);
        CheckPixel(pixels, 256, 24, 32, 255, 0, 0, "original texture left is red");
        CheckPixel(pixels, 256, 72, 32, 0, 0, 255, "original texture right is blue");
        CheckPixel(pixels, 256, 104, 32, 0, 0, 255, "negative X scale mirrors texture coordinates");
        CheckPixel(pixels, 256, 152, 32, 255, 0, 0, "negative X scale keeps the opposite endpoint");
        CheckPixel(pixels, 256, 192, 104, 0, 255, 0, "negative Y scale mirrors texture coordinates");
        CheckPixel(pixels, 256, 192, 152, 255, 0, 0, "negative Y scale keeps the opposite endpoint");
        CheckPixel(pixels, 256, 32, 112, 255, 0, 0, "recorded bitmap keeps its pre-update pixels");
        CheckPixel(pixels, 256, 112, 112, 0, 0, 255, "later draw sees updated bitmap pixels");
        CheckPixel(pixels, 256, 32, 160, 255, 127, 127, "straight alpha is premultiplied exactly once");
        CheckPixel(pixels, 256, 112, 160, 255, 191, 191, "bitmap opacity stack is applied exactly once");
    }
    if (h) jalium_bitmap_destroy(h);
    if (v) jalium_bitmap_destroy(v);
    if (changing) jalium_bitmap_destroy(changing);
    if (translucent) jalium_bitmap_destroy(translucent);
}

void TestTextOpacity(JaliumRenderTarget* target, JaliumContext* context, JaliumBrush* brush)
{
    auto* format = jalium_text_format_create(context, L"sans-serif", 22, 400, 0);
    if (!Check(format != nullptr, "create text opacity format")) return;
    if (Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "begin text opacity frame")) {
        jalium_render_target_set_full_invalidation(target);
        jalium_render_target_clear(target, 1, 1, 1, 1);
        jalium_draw_text(target, L"Vulkan", 6, format, 0, 24, 120, 48, brush);
        jalium_push_opacity(target, .5f);
        jalium_draw_text(target, L"Vulkan", 6, format, 128, 24, 120, 48, brush);
        jalium_pop_opacity(target);
        if (std::getenv("JALIUM_VK_EFFECT_CPU_CAPTURE")) {
            jalium_effect_begin_capture(target, 16, 104, 16, 16);
            jalium_effect_end_capture(target);
        }
        jalium_render_target_request_readback(target);
        Check(jalium_render_target_end_draw(target) == JALIUM_OK, "submit text opacity frame");
        const auto pixels = Capture(target, 256, 192);
        unsigned glyphPixels = 0, mismatches = 0, maxError = 0;
        if (!pixels.empty()) {
            for (int y = 24; y < 72; ++y) for (int x = 0; x < 120; ++x) {
                const size_t a = (y * 256u + x) * 4, b = a + 128 * 4;
                if (pixels[a] < 250 || pixels[a + 1] < 250 || pixels[a + 2] < 250) ++glyphPixels;
                for (int c = 0; c < 3; ++c)
                    if (const int error = std::abs(int(pixels[b + c]) - (int(pixels[a + c]) + 255) / 2); error > 3) {
                        ++mismatches; maxError = std::max(maxError, static_cast<unsigned>(error));
                    }
            }
        }
        if (!Check(glyphPixels > 30 && mismatches == 0,
              "cached text coverage, tint and opacity agree at identical subpixel phases"))
            std::cerr << "  glyph pixels=" << glyphPixels << " mismatches=" << mismatches << " max error=" << maxError << '\n';
    }
    jalium_text_format_destroy(format);
}
