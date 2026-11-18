#include "promzona/appearance.hpp"
#include <stdexcept>

namespace Promzona
{
namespace
{
constexpr int firstArmJoint = 8, lastArmJoint = 23;
constexpr Vec3 hairShades[]{
    {.10f, .09f, .085f}, {-1, 0, 0}, {.33f, .23f, .15f}, {.45f, .30f, .17f}, {.62f, .50f, .30f}};
constexpr Vec3 greyHair{.52f, .49f, .43f};
} // namespace

Appearance appearance(std::uint32_t id, std::uint32_t style, Person::Uniform uniform, Person::Role habit)
{
    using U = Person::Uniform;
    Person type;
    type.style = style;
    // A uniform hides the habit and the persona of whoever wears it.
    const bool civilian = uniform == U::none;
    const Person::Role role = civilian ? habit : Person::Role::none;
    const bool persona = civilian && role == Person::Role::none;
    Appearance a;
    a.outfit = uniform == U::medical         ? Appearance::medic
               : uniform == U::police        ? Appearance::militia
               : uniform == U::rosguard      ? Appearance::rosguard
               : uniform == U::fsb           ? Appearance::fsb
               : uniform == U::army          ? Appearance::army
               : role == Person::Role::night ? Appearance::nightWorker
               : role == Person::Role::beach ? Appearance::beach
               : type.bagElder()             ? Appearance::bagElder
               : type.drunk()                ? Appearance::drunk
               : type.homeless()             ? Appearance::homeless
                                             : int(id % 9);
    // Managed models: officers are men, the night shift and the pensioners with bags are women.
    a.model = a.officer()                                                   ? 0
              : role == Person::Role::night || (persona && type.bagElder()) ? 1
                                                                            : int(style % 2);
    a.tone = int(id % 6);
    a.height = float((1.60 + (id % 11) * .022) / 1.72);
    a.width = float((.40 + (id % 7) * .018) / .51);
    a.depth = float(.94 + (id % 5) * .035);
    const bool old = civilian && type.elder();
    // The managed palette greys the two oldest everyday outfits; here the elders too.
    a.hair = old || (persona && id % 9 >= 7) ? greyHair : hairShades[(id / 3) % std::size(hairShades)];
    a.long_trousers =
        role != Person::Role::beach && (old || !persona || type.drunk() || type.homeless() || id % 4 != 0);
    a.long_sleeves = a.officer() || a.outfit == Appearance::medic || (persona && (old || id % 3 == 0));
    return a;
}

Appearance appearance(const Person &p, bool medic_on_call)
{
    return appearance(
        p.body.id, p.style,
        medic_on_call && p.uniform == Person::Uniform::none ? Person::Uniform::medical : p.uniform, p.role);
}

Appearance hero_appearance()
{
    Appearance a;
    a.outfit = Appearance::hero;
    a.tone = 4;
    a.long_trousers = a.long_sleeves = true;
    a.hair = hairShades[0];
    a.width = .96f;
    return a;
}

Vec3 Appearance::cap() const
{
    return outfit == militia ? Vec3{.10f, .17f, .23f}
           : outfit == fsb   ? Vec3{.055f, .055f, .06f}
                             : Vec3{.12f, .18f, .13f};
}

void mark_standing_pose(SkinData &skin)
{
    if (skin.joints <= lastArmJoint || skin.poses < 1 || skin.bones.size() < size_t(skin.joints) * 16)
        throw std::runtime_error("Human rig has no arm joints");
    for (auto &v : skin.mesh.vertices)
    {
        double height = 0;
        int lead = 0;
        for (int k = 0; k < 4; ++k)
        {
            const int joint = int(v.joints[k]);
            if (joint < 0 || joint >= skin.joints)
                throw std::runtime_error("Invalid skin influences");
            // Column-major bone matrix of the first pose; only its second row is needed.
            const float *m = skin.bones.data() + size_t(joint) * 16;
            height +=
                v.weights[k] * (m[1] * v.position.x + m[5] * v.position.y + m[9] * v.position.z + m[13]);
            if (v.weights[k] > v.weights[lead])
                lead = k;
        }
        const int joint = int(v.joints[lead]);
        v.uv[1] = float(clamp(height, 0., 1.98) * .5) +
                  (joint >= firstArmJoint && joint <= lastArmJoint ? 1.f : 0.f);
    }
}
} // namespace Promzona
