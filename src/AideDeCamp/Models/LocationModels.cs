namespace AideDeCamp.Models;

public readonly record struct GamePosition(double X, double Y, double Z);
public sealed record LocationAnchor(string Name, string? State, GamePosition Position, string Source);
public sealed record ResolvedLocation(LocationAnchor Anchor, double MapDistance, string DisplayText);
