#pragma once
#include <cstdint>
#include <filesystem>
#include <memory>
#include <string_view>
#include <vector>

// The HUD is painted on the CPU into a 32-bit canvas that is uploaded as a texture.
// Pixels keep the layout of a Windows DIB section (0x00RRGGBB, top row first), which the
// upload and the minimap already use; colours keep the COLORREF layout (0x00BBGGRR).
namespace Promzona
{
    using Color = std::uint32_t;

    constexpr Color rgb(int r, int g, int b)
    {
        return Color(r & 0xFF) | (Color(g & 0xFF) << 8) | (Color(b & 0xFF) << 16);
    }

    // A TrueType face at one size, measured the way GDI measured its fonts: the size is
    // the em height, the cell spans the Windows ascent and descent.
    class Font
    {
        struct Impl;
        std::unique_ptr<Impl> impl_;

    public:
        Font(const std::filesystem::path &file, int emPixels);
        ~Font();
        Font(Font &&) noexcept;
        Font &operator=(Font &&) noexcept;

        int height() const;
        int ascent() const;
        int width(std::wstring_view text) const;

        friend class HudCanvas;
    };

    class HudCanvas
    {
        std::vector<std::uint32_t> pixels_;
        int width_{}, height_{};
        struct Clip
        {
            int left, top, right, bottom;
        };
        std::vector<Clip> clips_;
        Clip clip() const;

    public:
        void resize(int width, int height);
        int width() const
        {
            return width_;
        }
        int height() const
        {
            return height_;
        }
        std::uint32_t *data()
        {
            return pixels_.data();
        }
        const std::uint32_t *data() const
        {
            return pixels_.data();
        }

        void fill(int x, int y, int w, int h, Color color);
        // Text whose cell has its top-left corner at (x, y), blended over the canvas.
        void text(const Font &font, int x, int y, std::wstring_view s, Color color);
        // Single line in a box: vertically centred, left-aligned or centred, cut with "..."
        // when it does not fit, and clipped to the box.
        void text_box(const Font &font, int x, int y, int w, int h, std::wstring_view s, Color color,
                      bool centred, bool ellipsis);
        // Copies DIB-ordered pixels.
        void blit(int x, int y, int w, int h, const std::uint32_t *source);
        void push_clip(int x, int y, int w, int h);
        void pop_clip();
    };

    // The regular and bold faces of the HUD. On Windows the system's Segoe UI and Arial are
    // used when present, so the game keeps its look; elsewhere the bundled Liberation Sans.
    std::filesystem::path ui_font_file(const std::filesystem::path &content, bool bold);
    std::filesystem::path lettering_font_file(const std::filesystem::path &content);
}
