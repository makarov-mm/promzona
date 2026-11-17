#include "ui_canvas.hpp"
#include "promzona/text.hpp"
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdlib>
#include <fstream>
#include <stdexcept>
#include <unordered_map>

#define STB_TRUETYPE_IMPLEMENTATION
#define STBTT_STATIC
#if defined(__GNUC__)
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wunused-function"
#endif
#include "stb/stb_truetype.h"
#if defined(__GNUC__)
#pragma GCC diagnostic pop
#endif

namespace Promzona
{
namespace
{
    struct Glyph
    {
        int x0{}, y0{}, w{}, h{}, advance{};
        std::vector<unsigned char> coverage;
    };

    // Coverage is lifted a little, as GDI's grayscale smoothing did, so thin strokes keep
    // their weight on the dark HUD.
    const std::array<unsigned char, 256> &contrast()
    {
        static const auto table = []
        {
            std::array<unsigned char, 256> t{};
            for (int i = 0; i < 256; ++i)
                t[i] = static_cast<unsigned char>(std::lround(255. * std::pow(i / 255., 1 / 1.25)));
            return t;
        }();
        return table;
    }

    std::uint32_t blend(std::uint32_t under, Color color, unsigned coverage)
    {
        // `under` is 0x00RRGGBB, `color` is 0x00BBGGRR.
        const unsigned r = color & 0xFF, g = (color >> 8) & 0xFF, b = (color >> 16) & 0xFF;
        const unsigned ur = (under >> 16) & 0xFF, ug = (under >> 8) & 0xFF, ub = under & 0xFF;
        auto mix = [&](unsigned from, unsigned to) { return (from * (255 - coverage) + to * coverage + 127) / 255; };
        return (mix(ur, r) << 16) | (mix(ug, g) << 8) | mix(ub, b);
    }

    std::uint32_t pixel(Color color)
    {
        return ((color & 0xFF) << 16) | (color & 0xFF00) | ((color >> 16) & 0xFF);
    }

    std::vector<unsigned char> read_file(const std::filesystem::path &file)
    {
        std::ifstream in(file, std::ios::binary);
        if (!in)
            throw std::runtime_error("Missing font: " + file.string());
        return {std::istreambuf_iterator<char>(in), std::istreambuf_iterator<char>()};
    }

    unsigned read16(const unsigned char *p)
    {
        return unsigned(p[0]) << 8 | p[1];
    }
}

struct Font::Impl
{
    std::vector<unsigned char> file;
    stbtt_fontinfo info{};
    float scale{};
    int ascent{}, descent{};
    mutable std::unordered_map<char32_t, Glyph> glyphs;

    Impl(const std::filesystem::path &path, int em) : file(read_file(path))
    {
        const int offset = stbtt_GetFontOffsetForIndex(file.data(), 0);
        if (offset < 0 || !stbtt_InitFont(&info, file.data(), offset))
            throw std::runtime_error("Unreadable font: " + path.string());
        scale = stbtt_ScaleForMappingEmToPixels(&info, float(em));
        // GDI sizes the text cell by the Windows metrics of the OS/2 table.
        if (const stbtt_uint32 os2 = stbtt__find_table(file.data(), stbtt_uint32(offset), "OS/2"))
        {
            ascent = int(std::ceil(read16(file.data() + os2 + 74) * scale));
            descent = int(std::ceil(read16(file.data() + os2 + 76) * scale));
        }
        else
        {
            int a = 0, d = 0, gap = 0;
            stbtt_GetFontVMetrics(&info, &a, &d, &gap);
            ascent = int(std::ceil(a * scale));
            descent = int(std::ceil(-d * scale));
        }
    }

    const Glyph &glyph(char32_t c) const
    {
        if (auto found = glyphs.find(c); found != glyphs.end())
            return found->second;
        Glyph g;
        const int index = stbtt_FindGlyphIndex(&info, int(c));
        int advance = 0, bearing = 0;
        stbtt_GetGlyphHMetrics(&info, index, &advance, &bearing);
        g.advance = int(std::lround(advance * scale));
        int x1 = 0, y1 = 0;
        stbtt_GetGlyphBitmapBox(&info, index, scale, scale, &g.x0, &g.y0, &x1, &y1);
        g.w = std::max(0, x1 - g.x0);
        g.h = std::max(0, y1 - g.y0);
        if (g.w && g.h)
        {
            g.coverage.resize(size_t(g.w) * g.h);
            stbtt_MakeGlyphBitmap(&info, g.coverage.data(), g.w, g.h, g.w, scale, scale, index);
        }
        return glyphs.emplace(c, std::move(g)).first->second;
    }

    // Visits every glyph with its pen position, from the left edge of the run.
    template <class F> int run(std::wstring_view s, F &&visit) const
    {
        int pen = 0;
        char32_t previous = 0;
        for (size_t i = 0; i < s.size();)
        {
            const char32_t c = next_code_point(s, i);
            if (previous)
                pen += int(std::lround(stbtt_GetCodepointKernAdvance(&info, int(previous), int(c)) * scale));
            const Glyph &g = glyph(c);
            visit(g, pen);
            pen += g.advance;
            previous = c;
        }
        return pen;
    }
};

Font::Font(const std::filesystem::path &file, int emPixels) : impl_(std::make_unique<Impl>(file, emPixels)) {}
Font::~Font() = default;
Font::Font(Font &&) noexcept = default;
Font &Font::operator=(Font &&) noexcept = default;

int Font::height() const
{
    return impl_->ascent + impl_->descent;
}

int Font::ascent() const
{
    return impl_->ascent;
}

int Font::width(std::wstring_view text) const
{
    return impl_->run(text, [](const Glyph &, int) {});
}

void HudCanvas::resize(int width, int height)
{
    width_ = std::max(0, width);
    height_ = std::max(0, height);
    pixels_.assign(size_t(width_) * height_, 0);
    clips_.clear();
}

HudCanvas::Clip HudCanvas::clip() const
{
    return clips_.empty() ? Clip{0, 0, width_, height_} : clips_.back();
}

void HudCanvas::push_clip(int x, int y, int w, int h)
{
    const Clip c = clip();
    clips_.push_back({std::max(c.left, x), std::max(c.top, y), std::min(c.right, x + w), std::min(c.bottom, y + h)});
}

void HudCanvas::pop_clip()
{
    if (!clips_.empty())
        clips_.pop_back();
}

void HudCanvas::fill(int x, int y, int w, int h, Color color)
{
    const Clip c = clip();
    const int left = std::max(c.left, x), top = std::max(c.top, y);
    const int right = std::min(c.right, x + w), bottom = std::min(c.bottom, y + h);
    const std::uint32_t value = pixel(color);
    for (int row = top; row < bottom; ++row)
        std::fill(pixels_.begin() + ptrdiff_t(row) * width_ + left, pixels_.begin() + ptrdiff_t(row) * width_ + right,
                  value);
}

void HudCanvas::blit(int x, int y, int w, int h, const std::uint32_t *source)
{
    const Clip c = clip();
    for (int row = std::max(0, c.top - y); row < h && y + row < c.bottom; ++row)
    {
        const int from = std::max(0, c.left - x), to = std::min(w, c.right - x);
        if (from < to)
            std::copy(source + size_t(row) * w + from, source + size_t(row) * w + to,
                      pixels_.begin() + ptrdiff_t(y + row) * width_ + x + from);
    }
}

void HudCanvas::text(const Font &font, int x, int y, std::wstring_view s, Color color)
{
    const Clip c = clip();
    const int baseline = y + font.ascent();
    const auto &lift = contrast();
    font.impl_->run(s,
                    [&](const Glyph &g, int pen)
                    {
                        const int left = x + pen + g.x0, top = baseline + g.y0;
                        for (int row = std::max(0, c.top - top); row < g.h && top + row < c.bottom; ++row)
                        {
                            std::uint32_t *line = pixels_.data() + size_t(top + row) * width_;
                            for (int column = std::max(0, c.left - left); column < g.w && left + column < c.right;
                                 ++column)
                                if (const unsigned a = lift[g.coverage[size_t(row) * g.w + column]])
                                    line[left + column] = blend(line[left + column], color, a);
                        }
                    });
}

void HudCanvas::text_box(const Font &font, int x, int y, int w, int h, std::wstring_view s, Color color, bool centred,
                      bool ellipsis)
{
    std::wstring shown(s);
    int textWidth = font.width(shown);
    if (ellipsis && textWidth > w)
    {
        // The longest start of the text that leaves room for the dots, as GDI's end ellipsis.
        size_t keep = shown.size();
        do
        {
            --keep;
            if constexpr (sizeof(wchar_t) == 2)
                if (keep && shown[keep] >= 0xDC00 && shown[keep] <= 0xDFFF)
                    --keep;
            shown = std::wstring(s.substr(0, keep)) + L"...";
            textWidth = font.width(shown);
        } while (keep && textWidth > w);
    }
    push_clip(x, y, w, h);
    text(font, centred ? x + (w - textWidth) / 2 : x, y + (h - font.height()) / 2, shown, color);
    pop_clip();
}

namespace
{
    std::filesystem::path system_font(const char *name)
    {
#ifdef _WIN32
        wchar_t *windows = nullptr;
        size_t length = 0;
        if (_wdupenv_s(&windows, &length, L"WINDIR") == 0 && windows)
        {
            std::filesystem::path file = std::filesystem::path(windows) / "Fonts" / name;
            std::free(windows);
            std::error_code error;
            if (std::filesystem::is_regular_file(file, error))
                return file;
        }
#else
        (void)name;
#endif
        return {};
    }
}

std::filesystem::path ui_font_file(const std::filesystem::path &content, bool bold)
{
    if (auto file = system_font(bold ? "segoeuib.ttf" : "segoeui.ttf"); !file.empty())
        return file;
    return content / "Fonts" / (bold ? "LiberationSans-Bold.ttf" : "LiberationSans-Regular.ttf");
}

std::filesystem::path lettering_font_file(const std::filesystem::path &content)
{
    if (auto file = system_font("arialbd.ttf"); !file.empty())
        return file;
    return content / "Fonts" / "LiberationSans-Bold.ttf";
}
} // namespace Promzona
