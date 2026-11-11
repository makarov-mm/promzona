#pragma once
#include <algorithm>
#include <vector>

namespace pz
{
// Track horizontal strips so the previous footprint is also erased when a panel disappears.
class HudDamage
{
    struct Span
    {
        int left, right;
    };
    static constexpr int band = 32;
    int width_{}, height_{};
    std::vector<Span> painted_, previous_;

  public:
    void resize(int width, int height)
    {
        width_ = width;
        height_ = height;
        painted_.assign((height + band - 1) / band, {width, 0});
        previous_ = painted_;
    }
    void begin()
    {
        painted_.swap(previous_);
        std::fill(painted_.begin(), painted_.end(), Span{width_, 0});
    }
    void touch(int x, int y, int width, int height)
    {
        int l = std::max(x, 0), r = std::min(x + width, width_);
        int top = std::max(y, 0), bottom = std::min(y + height, height_);
        if (l >= r || top >= bottom)
            return;
        for (int i = top / band; i <= (bottom - 1) / band; ++i)
        {
            painted_[i].left = std::min(painted_[i].left, l);
            painted_[i].right = std::max(painted_[i].right, r);
        }
    }
    template <class F> void regions(bool includeCurrent, F consume) const
    {
        int top = 0;
        Span active{width_, 0};
        for (size_t i = 0; i <= previous_.size(); ++i)
        {
            const int y = std::min(int(i) * band, height_);
            Span next{width_, 0};
            if (i < previous_.size())
            {
                next = previous_[i];
                if (includeCurrent)
                {
                    next.left = std::min(next.left, painted_[i].left);
                    next.right = std::max(next.right, painted_[i].right);
                }
            }
            if (next.left == active.left && next.right == active.right)
                continue;
            if (active.right > active.left)
                consume(active.left, top, active.right - active.left, y - top);
            top = y;
            active = next;
        }
    }
};
} // namespace pz
