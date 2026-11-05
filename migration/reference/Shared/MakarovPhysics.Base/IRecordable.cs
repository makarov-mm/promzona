namespace MakarovPhysics.Base;

/// <summary>
/// A module whose state can be written down and put back exactly.
///
/// What gets recorded is the STATE, not the picture. Frames cannot be
/// re-measured, re-scaled or continued from; a state can. And the restore has
/// to be exact rather than a re-simulation from a nearby point: in a system
/// with any sensitivity at all, re-integrating would land somewhere the
/// experiment never actually was, and the reader would be scrubbing back into
/// a history that did not happen.
/// </summary>
public interface IRecordable
{
    /// <summary>Number of doubles one snapshot takes. Constant for the module's lifetime.</summary>
    int StateSize { get; }

    /// <summary>Writes the current state into the buffer (exactly StateSize values).</summary>
    void CaptureState(Span<double> state);

    /// <summary>
    /// Puts a recorded state back. Anything the module shows that is history
    /// rather than state (a trail, a chart tail) belongs to a future that has
    /// not happened from here, so this is also where the module drops it.
    /// </summary>
    void RestoreState(ReadOnlySpan<double> state);
}
