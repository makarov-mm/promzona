#include "promzona/control_hints.hpp"
#include "promzona/world.hpp"
namespace pz
{
std::vector<ControlHint> controlHints(const World &world)
{
    using Hints = std::vector<ControlHint>;
    if (world.drone.active)
        return {{L"WASD", L"Полёт"},
                {L"Space", L"Выше"},
                {L"Ctrl", L"Ниже"},
                {L"ЛКМ", L"Осмотреть крышу"},
                {L"K", L"Вернуться"}};

    Hints hints;
    int site = world.counterInReach(true);
    if (world.playerScooter >= 0)
        hints = {{L"WASD", L"Ехать"}, {L"Space", L"Тормоз"}, {L"F", L"Сойти"}};
    else if (world.controlledCar >= 0)
    {
        const auto &car = world.cars[world.controlledCar];
        if (car.kind == 12)
            hints = {{L"W/S", L"Тяга"}, {L"A/D", L"Разворот"}, {L"Space", L"Взлёт"}, {L"Ctrl", L"Снижение"}};
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
        hints.push_back({L"K", L"Дрон"});
    }
    const auto &job = world.job;
    if (job.passenger() && !job.acquired && world.controlledCar >= 0 &&
        (world.position() - world.jobTarget()).length() <= 10 && job.ride.car < 0)
        hints.push_back({L"E", L"Посадить пассажира"});
    else if (job.kind == JobKind::broadcast && !job.escaping() && world.controlledCar == job.actor)
        hints.push_back(
            {L"E", job.route.enabled ? L"Выключить громкоговоритель" : L"Включить громкоговоритель"});
    else if (job.kind == JobKind::leaflets && !job.escaping() && world.controlledCar < 0 &&
             (world.position() - world.jobTarget()).length() < 2.2)
        hints.push_back({L"E", L"Раздать листовки"});
    else if (site >= 0 && job.kind == JobKind::none)
        hints.push_back({L"E", L"Взять посылку"});
    else if (site >= 0 && job.kind == JobKind::courier && job.destination == site)
        hints.push_back({L"E", L"Передать посылку"});
    hints.push_back({L"J", L"Заказы"});
    return hints;
}
} // namespace pz
