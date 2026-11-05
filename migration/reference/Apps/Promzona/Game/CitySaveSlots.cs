namespace Promzona.Game;
public static class CitySaveSlots
{
    public static string PathFor(string directory,int slot)
    {
        if(slot is <1 or >3)throw new ArgumentOutOfRangeException(nameof(slot));
        return Path.Combine(directory,slot==1?"save.json":$"save-{slot}.json");
    }
}
