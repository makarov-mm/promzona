#include "promzona/names.hpp"
#include <array>
#include <cmath>

namespace Promzona
{
namespace
{
constexpr std::array<StreetName, 49> streets{{{L"Ленина", L"Lenina", L"Леніна"},
                                              {L"Заводская", L"Zavodskaya", L"Заводська"},
                                              {L"Советская", L"Sovetskaya", L"Радянська"},
                                              {L"Мира", L"Mira", L"Миру"},
                                              {L"Гагарина", L"Gagarina", L"Гагаріна"},
                                              {L"Комсомольская", L"Komsomolskaya", L"Комсомольська"},
                                              {L"Октябрьская", L"Oktyabrskaya", L"Жовтнева"},
                                              {L"Пролетарская", L"Proletarskaya", L"Пролетарська"},
                                              {L"Кирова", L"Kirova", L"Кірова"},
                                              {L"Калинина", L"Kalinina", L"Калініна"},
                                              {L"Пушкина", L"Pushkina", L"Пушкіна"},
                                              {L"Строителей", L"Stroiteley", L"Будівельників"},
                                              {L"Молодёжная", L"Molodyozhnaya", L"Молодіжна"},
                                              {L"Школьная", L"Shkolnaya", L"Шкільна"},
                                              {L"Садовая", L"Sadovaya", L"Садова"},
                                              {L"Набережная", L"Naberezhnaya", L"Набережна"},
                                              {L"Железнодорожная", L"Zheleznodorozhnaya", L"Залізнична"},
                                              {L"Первомайская", L"Pervomayskaya", L"Першотравнева"},
                                              {L"Чапаева", L"Chapaeva", L"Чапаєва"},
                                              {L"Дзержинского", L"Dzerzhinskogo", L"Дзержинського"},
                                              {L"Профсоюзная", L"Profsoyuznaya", L"Профспілкова"},
                                              {L"Энергетиков", L"Energetikov", L"Енергетиків"},
                                              {L"Металлургов", L"Metallurgov", L"Металургів"},
                                              {L"Космонавтов", L"Kosmonavtov", L"Космонавтів"},
                                              {L"Победы", L"Pobedy", L"Перемоги"},
                                              {L"Чкалова", L"Chkalova", L"Чкалова"},
                                              {L"Лермонтова", L"Lermontova", L"Лермонтова"},
                                              {L"Некрасова", L"Nekrasova", L"Некрасова"},
                                              {L"Горького", L"Gorkogo", L"Горького"},
                                              {L"Маяковского", L"Mayakovskogo", L"Маяковського"},
                                              {L"Фрунзе", L"Frunze", L"Фрунзе"},
                                              {L"Куйбышева", L"Kuybysheva", L"Куйбишева"},
                                              {L"Свердлова", L"Sverdlova", L"Свердлова"},
                                              {L"Карла Маркса", L"Karla Marksa", L"Карла Маркса"},
                                              {L"Красноармейская", L"Krasnoarmeyskaya", L"Червоноармійська"},
                                              {L"Рабочая", L"Rabochaya", L"Робітнича"},
                                              {L"Зелёная", L"Zelyonaya", L"Зелена"},
                                              {L"Луговая", L"Lugovaya", L"Лугова"},
                                              {L"Полевая", L"Polevaya", L"Польова"},
                                              {L"Северная", L"Severnaya", L"Північна"},
                                              {L"Южная", L"Yuzhnaya", L"Південна"},
                                              {L"Восточная", L"Vostochnaya", L"Східна"},
                                              {L"Западная", L"Zapadnaya", L"Західна"},
                                              {L"Центральная", L"Tsentralnaya", L"Центральна"},
                                              {L"Новая", L"Novaya", L"Нова"},
                                              {L"Фабричная", L"Fabrichnaya", L"Фабрична"},
                                              {L"Шоссейная", L"Shosseynaya", L"Шосейна"},
                                              {L"Трудовая", L"Trudovaya", L"Трудова"},
                                              {L"Юбилейная", L"Yubileynaya", L"Ювілейна"}}};
constexpr std::array<StreetName, 49> avenues{
    {{L"Коммунистическая", L"Kommunisticheskaya", L"Комуністична"},
     {L"Интернациональная", L"Internatsionalnaya", L"Інтернаціональна"},
     {L"Парковая", L"Parkovaya", L"Паркова"},
     {L"Речная", L"Rechnaya", L"Річкова"},
     {L"Озёрная", L"Ozyornaya", L"Озерна"},
     {L"Лесная", L"Lesnaya", L"Лісова"},
     {L"Берёзовая", L"Beryozovaya", L"Березова"},
     {L"Сосновая", L"Sosnovaya", L"Соснова"},
     {L"Вокзальная", L"Vokzalnaya", L"Вокзальна"},
     {L"Мостовая", L"Mostovaya", L"Мостова"},
     {L"Степная", L"Stepnaya", L"Степова"},
     {L"Привокзальная", L"Privokzalnaya", L"Привокзальна"},
     {L"Транспортная", L"Transportnaya", L"Транспортна"},
     {L"Механизаторов", L"Mekhanizatorov", L"Механізаторів"},
     {L"Нефтяников", L"Neftyanikov", L"Нафтовиків"},
     {L"Шахтёров", L"Shakhtyorov", L"Шахтарів"},
     {L"Геологов", L"Geologov", L"Геологів"},
     {L"Химиков", L"Khimikov", L"Хіміків"},
     {L"Текстильщиков", L"Tekstilshchikov", L"Текстильників"},
     {L"Машиностроителей", L"Mashinostroiteley", L"Машинобудівників"},
     {L"Автомобилистов", L"Avtomobilistov", L"Автомобілістів"},
     {L"Литейная", L"Liteynaya", L"Ливарна"},
     {L"Кузнечная", L"Kuznechnaya", L"Ковальська"},
     {L"Слесарная", L"Slesarnaya", L"Слюсарна"},
     {L"Кирпичная", L"Kirpichnaya", L"Цегляна"},
     {L"Цементная", L"Tsementnaya", L"Цементна"},
     {L"Бетонная", L"Betonnaya", L"Бетонна"},
     {L"Водопроводная", L"Vodoprovodnaya", L"Водогінна"},
     {L"Телеграфная", L"Telegrafnaya", L"Телеграфна"},
     {L"Почтовая", L"Pochtovaya", L"Поштова"},
     {L"Аптечная", L"Aptechnaya", L"Аптечна"},
     {L"Больничная", L"Bolnichnaya", L"Лікарняна"},
     {L"Театральная", L"Teatralnaya", L"Театральна"},
     {L"Спортивная", L"Sportivnaya", L"Спортивна"},
     {L"Стадионная", L"Stadionnaya", L"Стадіонна"},
     {L"Пионерская", L"Pionerskaya", L"Піонерська"},
     {L"Матросова", L"Matrosova", L"Матросова"},
     {L"Панфилова", L"Panfilova", L"Панфілова"},
     {L"Жукова", L"Zhukova", L"Жукова"},
     {L"Суворова", L"Suvorova", L"Суворова"},
     {L"Кутузова", L"Kutuzova", L"Кутузова"},
     {L"Нахимова", L"Nakhimova", L"Нахімова"},
     {L"Ушакова", L"Ushakova", L"Ушакова"},
     {L"Толстого", L"Tolstogo", L"Толстого"},
     {L"Чехова", L"Chekhova", L"Чехова"},
     {L"Тургенева", L"Turgeneva", L"Тургенєва"},
     {L"Гоголя", L"Gogolya", L"Гоголя"},
     {L"Достоевского", L"Dostoevskogo", L"Достоєвського"},
     {L"Есенина", L"Esenina", L"Єсеніна"}}};
template <size_t N> const StreetName &pick(const std::array<StreetName, N> &names, int index)
{
    const int count = int(N);
    return names[size_t(((index % count) + count) % count)];
}
} // namespace

std::span<const StreetName> streetNames()
{
    return streets;
}
std::span<const StreetName> avenueNames()
{
    return avenues;
}
const StreetName &streetName(int column)
{
    return pick(streets, column);
}
const StreetName &avenueName(int row)
{
    return pick(avenues, row);
}
std::wstring streetLabel(int column)
{
    return L"ул. " + std::wstring(streetName(column).russian);
}
std::wstring avenueLabel(int row)
{
    return L"ул. " + std::wstring(avenueName(row).russian);
}
std::wstring streetAt(Vec2 point, int nodes, double spacing)
{
    const double low = -(nodes - 1) * spacing * .5;
    const int i = clamp(int(std::lround((point.x - low) / spacing)), 0, nodes - 1);
    const int j = clamp(int(std::lround((point.z - low) / spacing)), 0, nodes - 1);
    const double dx = std::abs(point.x - (low + i * spacing)), dz = std::abs(point.z - (low + j * spacing));
    return dx <= dz ? streetLabel(i) : avenueLabel(j);
}
} // namespace Promzona
