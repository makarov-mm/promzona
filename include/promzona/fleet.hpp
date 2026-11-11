#pragma once
#include "core.hpp"

namespace pz
{
// Stable indices are stored in native saves. New models belong at the end.
struct VehicleSpec
{
    const char *folder;
    int kind;
    double width, height, length, mass, wheelbase;
};
inline constexpr VehicleSpec fleet[] = {{"old_car_-_vaz_2107", 0, 1.78, 1.48, 4.16, 1060, 2.42},
                                        {"luaz", 0, 1.72, 1.81, 3.42, 970, 1.8},
                                        {"azlh-2140_sl_moskvich", 0, 1.60, 1.44, 4.17, 1080, 2.4},
                                        {"militia_sedan", 3, 1.82, 1.65, 4.65, 1600, 2.7},
                                        {"rosguard_van", 7, 2.24, 2.85, 5.85, 4200, 3.65},
                                        {"fsb_sedan", 13, 1.88, 1.57, 4.95, 1900, 2.9},
                                        {"army_btr", 14, 2.9, 2.65, 7.3, 13600, 4.3},
                                        {"promzona_helicopter", 12, 2.4, 3.1, 8.6, 2100, 3.2},
                                        {"free_soviet_tram_x-series", 9, 2.533, 4.061, 10.4, 17000, 3.8},
                                        {"ziu_9", 10, 2.962, 3.36, 11.9, 10500, 6},
                                        {"promzona_tank", 11, 3.35, 2.45, 6.3, 32000, 3.7},
                                        {"u469", 0, 1.92, 1.92, 4.02, 1650, 2.38},
                                        {"ukrainer_79_-_low_poly_model", 0, 1.51, 1.32, 3.75, 840, 2.16},
                                        {"1970_soviet_car_vaz_2101", 0, 1.63, 1.33, 4.17, 955, 2.42},
                                        {"free_zuk_3d_model", 1, 2.03, 2.05, 4.31, 1850, 2.7},
                                        {"pirozhok_scan_retopo_free_scan", 1, 1.77, 1.78, 4.1, 1200, 2.4},
                                        {"gaz-52_truck_old_version", 2, 2.93, 2.41, 6.4, 6500, 3.7},
                                        {"maz_500", 2, 3.14, 3.21, 6.67, 7400, 3.9},
                                        {"ambulance", 4, 2.26, 2.86, 4.97, 2760, 2.7}};
inline constexpr unsigned ambulanceModel = unsigned(std::size(fleet) - 1);
inline void fitVehicle(Car &car, unsigned model)
{
    const auto &spec = fleet[model];
    car.model = model;
    car.kind = spec.kind;
    car.body.halfW = spec.width / 2;
    car.body.halfH = spec.length / 2;
    car.body.verticalExtent = spec.height;
    car.mass = spec.mass;
    car.wheelbase = spec.wheelbase;
    car.body.mass(spec.mass);
}
} // namespace pz
