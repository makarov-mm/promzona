namespace Promzona.Game;

public sealed record CitySound(string Name, CityPoint Position, double Gain, int Variant, int SpeakerId = -1, int Revision = 0, bool Ui = false);
