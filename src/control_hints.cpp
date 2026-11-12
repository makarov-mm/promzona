#include "promzona/control_hints.hpp"
#include "promzona/world.hpp"
namespace pz
{
std::vector<ControlHint> controlHints(const World &world)
{
    using Hints = std::vector<ControlHint>;
    if (world.drone.active)
        return {{L"WASD", L"Полёт"},
                {L"Q / Space", L"Выше"},
                {L"Z / Ctrl", L"Ниже"},
                {L"ЛКМ", L"Осмотреть крышу"},
                {L"K", L"Вернуться"}};

    if (world.openMarket >= 0)
        return {{L"1–4", L"Купить"}, {L"E", L"Закрыть прилавок"}};
    if (world.vehicleEntry.car >= 0)
        return {{L"F / WASD", L"Отменить посадку"}};
    if (world.transitPassenger())
        return {{L"F / B", world.transitExitRequested ? L"Выход заказан" : L"Выйти на остановке"},
                {L"Мышь", L"Осмотреться"},
                {L"V", L"Камера"}};
    Hints hints;
    int site = world.counterInReach(true);
    if (world.playerScooter >= 0)
        hints = {{L"WASD", L"Ехать"}, {L"Space", L"Тормоз"}, {L"F", L"Сойти"}};
    else if (world.controlledCar >= 0)
    {
        const auto &car = world.cars[world.controlledCar];
        if (car.kind == 12)
            hints = {{L"W/S", L"Тяга"},
                     {L"A/D", L"Разворот"},
                     {L"Q / Space", L"Взлёт"},
                     {L"Z / Ctrl", L"Снижение"},
                     {L"L", world.helicopterLanding ? L"Отменить автопосадку" : L"Автопосадка"}};
        else if (car.kind == 9)
            hints = {{L"W/S", L"Вперёд / назад"}, {L"Space", L"Тормоз"}};
        else
            hints = {{L"W/S", L"Газ / тормоз"}, {L"A/D", L"Руль"}, {L"Space", L"Ручник"}};
        if (car.mountedGun())
            hints.insert(hints.end(), {{L"Мышь", L"Башня"}, {L"ЛКМ", L"Огонь"}});
        else if (world.inventory.active())
            hints.push_back({L"ЛКМ", L"Огонь вперёд"});
        hints.push_back({L"F", L"Выйти"});
        hints.push_back({L"R", L"Эвакуация"});
    }
    else
    {
        hints = {{L"WASD", L"Идти"},
                 {L"Shift", L"Бежать"},
                 {L"ЛКМ", world.inventory.active() ? L"Огонь" : L"Удар"}};
        if (int food = world.counterInReach(false); food >= 0)
            hints.push_back({L"F", L"Перекус $" + std::to_wstring(world.sites[food].price())});
        else if (world.nearbyScooter() >= 0)
            hints.push_back({L"F", L"На самокат"});
        else if (world.conversation.offer != World::StreetOffer::none &&
                 world.time < world.conversation.offerUntil)
            hints.push_back({L"F", L"Принять предложение"});
        else
            hints.push_back({L"F", L"Сесть / поговорить"});
        if (world.boardableTransit() >= 0)
            hints.push_back({L"B", L"Ехать пассажиром"});
        hints.push_back({L"K", L"Дрон"});
    }
    // Weapons are switched on foot and in ordinary vehicles; mounted guns are fixed.
    if (world.playerScooter < 0 && world.inventory.hasWeapons() &&
        (world.controlledCar < 0 || !world.cars[world.controlledCar].mountedGun()))
        hints.push_back({L"[ ]", L"Оружие"});
    const auto &job = world.job;
    const int garage = world.garageInReach();
    if (job.passenger() && !job.acquired && world.controlledCar >= 0 &&
        (world.position() - world.jobTarget()).length() <= 10 && job.ride.car < 0)
        hints.push_back({L"E", L"Посадить пассажира"});
    else if (job.passenger() && job.acquired && job.ride.car == world.controlledCar &&
             world.controlledCar >= 0 && world.cars[world.controlledCar].body.v.length() < .8)
        hints.push_back(
            {L"E", (world.jobTarget() - world.position()).length() < (job.kind == JobKind::patient ? 14 : 9)
                       ? L"Завершить поездку"
                       : L"Высадить без оплаты"});
    else if (world.hailerInReach() >= 0)
        hints.push_back({L"E", L"Взять пассажира"});
    else if (job.kind == JobKind::broadcast && !job.escaping() && world.controlledCar == job.actor)
        hints.push_back(
            {L"E", job.route.enabled ? L"Выключить громкоговоритель" : L"Включить громкоговоритель"});
    else if (job.kind == JobKind::leaflets && !job.escaping() && world.controlledCar < 0 &&
             (world.position() - world.jobTarget()).length() < 2.2)
        hints.push_back({L"E", L"Раздать листовки"});
    else if (world.marketInReach() >= 0)
        hints.push_back({L"E", L"Торговый прилавок"});
    else if (world.controlledCar >= 0 && !job.peace() && garage >= 0)
    {
        const int price = world.garageQuote();
        if (price > 0)
            hints.push_back({L"E", (world.wantedLevel() ? L"Ремонт и покраска $" : L"Ремонт $") +
                                       std::to_wstring(price)});
    }
    else if (site >= 0 && job.kind == JobKind::none)
        hints.push_back({L"E", L"Взять посылку"});
    else if (site >= 0 && job.kind == JobKind::courier && job.destination == site)
        hints.push_back({L"E", L"Передать посылку"});
    if (garage >= 0)
    {
        if (world.ownedGarage < 0)
            hints.push_back({L"G", L"Купить гараж $" + std::to_wstring(World::garagePrice)});
        else if (world.ownedGarage == garage && world.controlledCar >= 0 && job.kind == JobKind::none &&
                 world.cars[world.controlledCar].kind <= 1 && !world.cars[world.controlledCar].fireTime)
            hints.push_back({L"G", L"Припарковать (" + std::to_wstring(world.ownedVehicleCount()) + L"/3)"});
    }
    hints.push_back({L"J", L"Заказы"});
    return hints;
}
} // namespace pz
