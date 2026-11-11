#pragma once
#include <string>
#include <vector>
namespace pz
{
struct World;
struct ControlHint
{
    std::wstring key, action;
};
std::vector<ControlHint> controlHints(const World &world);
} // namespace pz
