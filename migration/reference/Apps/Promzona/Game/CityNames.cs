namespace Promzona.Game;

/// <summary>Street names for the HUD (29.10.2026). Every vertical road of the
/// grid (constant i) is a street, every horizontal road (constant j) an avenue,
/// so a job destination reads as a street name instead of a bare distance.
/// Names are plain nineties provincial stock, no real town is mapped.</summary>
public static class CityNames
{
    private static readonly (string Ru, string En)[] Streets =
    [
        ("Ленина", "Lenina"), ("Заводская", "Zavodskaya"), ("Советская", "Sovetskaya"), ("Мира", "Mira"),
        ("Гагарина", "Gagarina"), ("Комсомольская", "Komsomolskaya"), ("Октябрьская", "Oktyabrskaya"),
        ("Пролетарская", "Proletarskaya"), ("Кирова", "Kirova"), ("Калинина", "Kalinina"), ("Пушкина", "Pushkina"),
        ("Строителей", "Stroiteley"), ("Молодёжная", "Molodyozhnaya"), ("Школьная", "Shkolnaya"), ("Садовая", "Sadovaya"),
        ("Набережная", "Naberezhnaya"), ("Железнодорожная", "Zheleznodorozhnaya"), ("Первомайская", "Pervomayskaya"),
        ("Чапаева", "Chapaeva"), ("Дзержинского", "Dzerzhinskogo"), ("Профсоюзная", "Profsoyuznaya"),
        ("Энергетиков", "Energetikov"), ("Металлургов", "Metallurgov"), ("Космонавтов", "Kosmonavtov"), ("Победы", "Pobedy"),
        ("Чкалова", "Chkalova"), ("Лермонтова", "Lermontova"), ("Некрасова", "Nekrasova"), ("Горького", "Gorkogo"),
        ("Маяковского", "Mayakovskogo"), ("Фрунзе", "Frunze"), ("Куйбышева", "Kuybysheva"), ("Свердлова", "Sverdlova"),
        ("Карла Маркса", "Karla Marksa"), ("Красноармейская", "Krasnoarmeyskaya"), ("Рабочая", "Rabochaya"),
        ("Зелёная", "Zelyonaya"), ("Луговая", "Lugovaya"), ("Полевая", "Polevaya"), ("Северная", "Severnaya"),
        ("Южная", "Yuzhnaya"), ("Восточная", "Vostochnaya"), ("Западная", "Zapadnaya"), ("Центральная", "Tsentralnaya"),
        ("Новая", "Novaya"), ("Фабричная", "Fabrichnaya"), ("Шоссейная", "Shosseynaya"), ("Трудовая", "Trudovaya"),
        ("Юбилейная", "Yubileynaya")
    ];

    private static readonly (string Ru, string En)[] Avenues =
    [
        ("Коммунистическая", "Kommunisticheskaya"), ("Интернациональная", "Internatsionalnaya"), ("Парковая", "Parkovaya"),
        ("Речная", "Rechnaya"), ("Озёрная", "Ozyornaya"), ("Лесная", "Lesnaya"), ("Берёзовая", "Beryozovaya"),
        ("Сосновая", "Sosnovaya"), ("Вокзальная", "Vokzalnaya"), ("Мостовая", "Mostovaya"), ("Степная", "Stepnaya"),
        ("Привокзальная", "Privokzalnaya"), ("Транспортная", "Transportnaya"), ("Механизаторов", "Mekhanizatorov"),
        ("Нефтяников", "Neftyanikov"), ("Шахтёров", "Shakhtyorov"), ("Геологов", "Geologov"), ("Химиков", "Khimikov"),
        ("Текстильщиков", "Tekstilshchikov"), ("Машиностроителей", "Mashinostroiteley"), ("Автомобилистов", "Avtomobilistov"),
        ("Литейная", "Liteynaya"), ("Кузнечная", "Kuznechnaya"), ("Слесарная", "Slesarnaya"), ("Кирпичная", "Kirpichnaya"),
        ("Цементная", "Tsementnaya"), ("Бетонная", "Betonnaya"), ("Водопроводная", "Vodoprovodnaya"),
        ("Телеграфная", "Telegrafnaya"), ("Почтовая", "Pochtovaya"), ("Аптечная", "Aptechnaya"), ("Больничная", "Bolnichnaya"),
        ("Театральная", "Teatralnaya"), ("Спортивная", "Sportivnaya"), ("Стадионная", "Stadionnaya"), ("Пионерская", "Pionerskaya"),
        ("Матросова", "Matrosova"), ("Панфилова", "Panfilova"), ("Жукова", "Zhukova"), ("Суворова", "Suvorova"),
        ("Кутузова", "Kutuzova"), ("Нахимова", "Nakhimova"), ("Ушакова", "Ushakova"), ("Толстого", "Tolstogo"),
        ("Чехова", "Chekhova"), ("Тургенева", "Turgeneva"), ("Гоголя", "Gogolya"), ("Достоевского", "Dostoevskogo"),
        ("Есенина", "Esenina")
    ];

    /// <summary>The vertical road through node column i.</summary>
    public static string Street(int i, bool ru)
    {
        var name = Streets[((i % Streets.Length) + Streets.Length) % Streets.Length];
        return ru ? "ул. " + name.Ru : name.En + " St";
    }

    /// <summary>The horizontal road through node row j.</summary>
    public static string Avenue(int j, bool ru)
    {
        var name = Avenues[((j % Avenues.Length) + Avenues.Length) % Avenues.Length];
        return ru ? "ул. " + name.Ru : name.En + " St";
    }

    /// <summary>The road a point lies on or beside: the nearer of the vertical
    /// and horizontal roads of its nearest node, which is where every bus stop,
    /// kiosk and clinic entrance sits.</summary>
    public static string StreetAt(CityPoint p, bool ru)
    {
        int i = Math.Clamp((int)Math.Round((p.X - CityTraffic.Coordinate(0)) / CityTraffic.Spacing), 0, CityTraffic.Nodes - 1),
            j = Math.Clamp((int)Math.Round((p.Z - CityTraffic.Coordinate(0)) / CityTraffic.Spacing), 0, CityTraffic.Nodes - 1);
        double dx = Math.Abs(p.X - CityTraffic.Coordinate(i)), dz = Math.Abs(p.Z - CityTraffic.Coordinate(j));
        return dx <= dz ? Street(i, ru) : Avenue(j, ru);
    }

    /// <summary>The two streets meeting at a node, for radio-style messages.</summary>
    public static string Corner(int i, int j, bool ru)
    {
        var a = Streets[((i % Streets.Length) + Streets.Length) % Streets.Length];
        var b = Avenues[((j % Avenues.Length) + Avenues.Length) % Avenues.Length];
        return ru ? $"угол {a.Ru} и {b.Ru}" : $"{a.En} and {b.En}";
    }
}
