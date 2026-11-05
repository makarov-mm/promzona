namespace MakarovPhysics.Base;

/// <summary>
/// The names of what a scene draws, shared by every module that draws more
/// than one thing at once.
///
/// The visual audit (02.09) asked for switches - setup, particles, trails,
/// field - and the point is NOT the switches. Every module already hides
/// something behind a checkbox of its own naming: Tokamak calls it "Field
/// lines", the mass spectrometer "Upper magnet", and a reader who learns one
/// panel learns nothing about the next. The point is that four words mean the
/// same four things everywhere and always appear in the same order, so the
/// second module costs nothing to read.
///
///   Setup      the apparatus: vessel, coils, bench, magnets, screen - what
///              would still be in the room with the power off
///   Particles  what the model moves: ions, electrons, molecules, agents
///   Trails     the record of where they have been: tracks, hits, heat marks
///   Field      what is drawn to show the invisible: field lines, grids,
///              equipotentials, the wave picture
///
/// A module declares only the layers it HAS. Ordering lives here rather than
/// in the widget because it is a contract, not a look, and because Base is
/// compiled by every test harness while the UI layer is stubbed out.
/// </summary>
public static class ViewLayers
{
    public const string Setup = "View.Setup";
    public const string Particles = "View.Particles";
    public const string Trails = "View.Trails";
    public const string Field = "View.Field";

    /// <summary>The four, in the order a panel must show them.</summary>
    public static readonly string[] Canonical = [Setup, Particles, Trails, Field];

    public static bool IsKnown(string key) => Array.IndexOf(Canonical, key) >= 0;

    /// <summary>
    /// The caller's keys sorted into <see cref="Canonical"/> order, with any
    /// key outside the vocabulary kept - after the known ones, in the order it
    /// was given.
    ///
    /// Unknown keys are kept rather than dropped or thrown on: a module that
    /// has a fifth thing to hide (the Doppler cone, the microscope) must not
    /// have to choose between a wrong word and no switch, and a checker can
    /// count the strays later. Duplicates are kept too - two switches with one
    /// label is a module's bug to see, not something to silently swallow.
    /// </summary>
    public static string[] Order(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var sorted = new List<string>(keys.Count);

        foreach (string canonical in Canonical)
        {
            sorted.AddRange(keys.Where(key => key == canonical));
        }

        sorted.AddRange(keys.Where(t => !IsKnown(t)));
        return [.. sorted];
    }
}
