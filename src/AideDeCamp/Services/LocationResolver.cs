using AideDeCamp.Models;

namespace AideDeCamp.Services;

/// <summary>
/// Future map/location foundation. It intentionally knows only game-space X/Z geometry;
/// save-file-specific anchor parsing will be added after we verify the relevant town/fort files.
/// </summary>
public sealed class LocationResolver
{
    private readonly List<LocationAnchor> _anchors = new();
    public IReadOnlyList<LocationAnchor> Anchors => _anchors;

    public void ReplaceAnchors(IEnumerable<LocationAnchor> anchors)
    {
        _anchors.Clear();
        _anchors.AddRange(anchors);
    }

    public ResolvedLocation? ResolveNearest(GamePosition position)
    {
        if (_anchors.Count == 0) return null;
        var nearest = _anchors
            .Select(anchor => (Anchor: anchor, Distance: Distance2D(position, anchor.Position)))
            .OrderBy(x => x.Distance)
            .First();
        var place = string.IsNullOrWhiteSpace(nearest.Anchor.State)
            ? nearest.Anchor.Name
            : $"{nearest.Anchor.Name}, {nearest.Anchor.State}";
        return new ResolvedLocation(nearest.Anchor, nearest.Distance, place);
    }

    private static double Distance2D(GamePosition a, GamePosition b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
