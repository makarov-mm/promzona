#pragma once
#include <string>
#include <vector>

namespace Promzona
{
    struct World;

    struct ControlHint
    {
        std::wstring key, action;
    };

    std::vector<ControlHint> control_hints(const World &world);
}
