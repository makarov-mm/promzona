using MakarovPhysics.Base;
using Promzona.Game;
namespace Promzona;

public sealed class GameSettings
{
    private readonly UserRegistry _registry;
    public string Language;
    public int Quality;
    public int DrawDistance;
    public bool Effects, Bloom;
    public float Volume, MusicVolume;

    public GameSettings(string language, string branch = @"Software\Promzona")
    {
        _registry = new UserRegistry(branch);
        Language = Ukrainian.Normalize(_registry.ReadString("Language") ?? language);
        Quality = Math.Clamp(_registry.ReadInt("Quality") ?? 2, 0, 2);
        DrawDistance = CityLod.RestoreDistance(_registry.ReadInt("DrawDistance"), _registry.ReadInt("GraphicsRevision") ?? 0);
        MusicVolume = (float)Math.Clamp(_registry.ReadDouble("MusicVolume") ?? .20, 0, .5);
        Effects = _registry.ReadBool("Effects") ?? true;
        Bloom = _registry.ReadBool("Bloom") ?? true;
        Volume = (float)Math.Clamp(_registry.ReadDouble("Volume") ?? .65, 0, 1);
    }

    public void Save()
    {
        _registry.WriteString("Language", Language);
        _registry.WriteInt("Quality", Quality);
        _registry.WriteInt("DrawDistance", DrawDistance);
        _registry.WriteInt("GraphicsRevision", 2);
        _registry.WriteBool("Effects", Effects);
        _registry.WriteBool("Bloom", Bloom);
        _registry.WriteDouble("Volume", Volume);
        _registry.WriteDouble("MusicVolume", MusicVolume);
    }
}
