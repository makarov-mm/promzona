#pragma once
#include "assets.hpp"
#include "world.hpp"

namespace Promzona
{
    // What the renderer needs to dress one person. Port of the managed rules: build, skin
    // tone and everyday clothes follow from the stable id (CityPedestrian, CityActorModels);
    // a persona, a habit or a uniform replaces the clothes (Actor.frag choices 9..18).
    struct Appearance
    {
        enum Outfit
        {
            everyday, // 0..8
            medic = 9,
            militia,
            rosguard,
            nightWorker,
            bagElder,
            drunk,
            homeless,
            beach,
            fsb,
            army,
            hero // native: the player is told from the crowd at a glance
        };

        int model{};  // 0 man, 1 woman
        int outfit{}; // Actor.frag clothing choice
        int tone{};   // 0 dark .. 5 light

        // The man of the people skin wears shorts and a T-shirt; most men of the city do not.
        bool longTrousers{}, longSleeves{};

        // a negative red keeps the colour of the palette
        Vec3 hair {
            .x = -1, 
            .y = 0, 
            .z = 0
        };

        // Scale of the .51 m wide, 1.72 m tall skin.
        float width{1}, height{1}, depth{1};

        bool officer() const
        {
            return outfit == militia || outfit == rosguard || outfit == fsb || outfit == army;
        }

        // Cap of the force, as in the managed CityPeopleDetail.
        Vec3 cap() const;

        // Bit 1: trouser legs, bit 2: sleeves; the shader reads them from uOutfit.z.
        int flags() const
        {
            return (longTrousers ? 1 : 0) + (longSleeves ? 2 : 0);
        }
    };

    Appearance appearance(std::uint32_t id, std::uint32_t style, Person::Uniform uniform = Person::Uniform::none, Person::Role role = Person::Role::none);

    // A paramedic on a call wears the coat before the crew is ever set down.
    Appearance appearance(const Person &person, bool medic_on_call = false);

    Appearance hero_appearance();

    // Clothes keep their place in any pose: every vertex of a human skin remembers how high
    // it stands in the first idle frame (half metres in uv[1]) and whether it belongs to an
    // arm (plus one). Both rigs share the skeleton: joints 8..23 are the arms.
    void mark_standing_pose(SkinData &skin);
}
