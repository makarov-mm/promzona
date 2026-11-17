#pragma once
#include "math.hpp"
#include <span>
#include <string>
#include <string_view>

namespace Promzona
{
    // Port of CityNames: every vertical road of the grid (constant column) is a street
    // and every horizontal road (constant row) an avenue, so a destination reads as a
    // name instead of a bare distance. Plain nineties provincial stock; no real town.
    struct StreetName
    {
        std::wstring_view russian, english, ukrainian;
    };

    std::span<const StreetName> street_names();
    std::span<const StreetName> avenue_names();
    const StreetName &street_name(int column);
    const StreetName &avenue_name(int row);

    // World text is Russian ("ул. Ленина"); localize() renders the other languages.
    std::wstring street_label(int column);
    std::wstring avenue_label(int row);

    // The road a point lies on or beside: the nearer of the two roads through its nearest node.
    std::wstring street_at(Vec2 point, int nodes = 49, double spacing = 36);
}
