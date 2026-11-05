namespace Promzona.Game;

/// <summary>One actor in a save: boxed field values (JsonElements once read from a file),
/// the body pose and either the four Bezier handles of its route (format 3) or the sampled points.</summary>
public sealed record ActorSave(Dictionary<string, object?> State, BodySave? Body, CityPoint[]? Path, bool Bezier = false);
