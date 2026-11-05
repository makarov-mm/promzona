using Promzona.Game;

namespace Promzona;

public sealed record CitySave(int Format, int Seed, double X, double Z, double Heading, double Hour, int Money, bool Raining, CityWorldSave? World = null);
