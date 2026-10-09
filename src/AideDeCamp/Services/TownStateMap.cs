using System.Globalization;
using System.IO;

namespace AideDeCamp.Services;

/// <summary>
/// Read-only state filter for the installed 1861 campaign map. Entries were sampled
/// from the game's dated state texture at each game's saved town world position.
/// </summary>
public static class TownStateMap
{
    public sealed record MappedTown(TownLocationParser.Town Location, int StateId);

    public static IReadOnlyList<MappedTown> Map1861(IReadOnlyList<TownLocationParser.Town> towns, DateTime gameDate)
    {
        if (gameDate < new DateTime(1861, 3, 2) || gameDate >= new DateTime(1863, 6, 20))
            throw new NotSupportedException("A dated town-to-state map is not validated for this game date.");

        var assembly = typeof(TownStateMap).Assembly;
        using var stream = assembly.GetManifestResourceStream("AideDeCamp.Data.TownStates1861.csv")
            ?? throw new InvalidDataException("The verified 1861 town-state map is missing.");
        using var reader = new StreamReader(stream);
        if (reader.ReadLine() != "town,world_x,world_y,world_z,pixel_x,pixel_y,r,g,b,texture_state_id")
            throw new InvalidDataException("The verified town-state map has an unexpected header.");

        var entries = new Dictionary<string, List<(float X, float Y, float Z, int State)>>(StringComparer.Ordinal);
        string? line;
        var entryCount = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            var parts = line.Split(',');
            if (parts.Length != 10 || string.IsNullOrWhiteSpace(parts[0]) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ||
                !int.TryParse(parts[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var state) ||
                !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || state is < 0 or > 52)
                throw new InvalidDataException("The verified town-state map contains an invalid entry.");
            if (!entries.TryGetValue(parts[0], out var matches)) entries[parts[0]] = matches = new();
            matches.Add((x, y, z, state));
            entryCount++;
        }
        if (entryCount != 103 || towns.Count != entryCount)
            throw new InvalidDataException("The town count differs from the verified 1861 map.");

        var mapped = new List<MappedTown>(towns.Count);
        var used = new HashSet<(string Name, float X, float Y, float Z)>();
        foreach (var town in towns)
        {
            if (!entries.TryGetValue(town.Name, out var candidates))
                throw new InvalidDataException($"Town '{town.Name}' is absent from the verified map.");
            var match = candidates.Where(candidate =>
                Math.Abs(candidate.X - town.X) < 0.02f &&
                Math.Abs(candidate.Y - town.Y) < 0.02f &&
                Math.Abs(candidate.Z - town.Z) < 0.02f).ToList();
            if (match.Count != 1 || !used.Add((town.Name, match[0].X, match[0].Y, match[0].Z)))
                throw new InvalidDataException($"Town '{town.Name}' has no unique verified state/position match.");
            mapped.Add(new MappedTown(town, match[0].State));
        }
        return mapped;
    }
}
