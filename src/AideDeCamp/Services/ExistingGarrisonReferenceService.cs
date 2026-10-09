using System.Globalization;
using System.IO;
using AideDeCamp.Models;

namespace AideDeCamp.Services;

/// <summary>
/// Resolves only fort links explicitly saved by the game. This is a read-only
/// relationship; it does not establish that a new combat-unit record is safe.
/// </summary>
public static class ExistingGarrisonReferenceService
{
    public sealed record Link(string FortName, float X, float Y, float Z, int GroupId, int Nation);

    public static IReadOnlyList<Link> Resolve(
        IReadOnlyList<string> lines,
        IReadOnlyDictionary<int, GroupNode> groups)
    {
        if (lines.Count == 0 ||
            !int.TryParse(lines[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
            count < 0 || count > (lines.Count - 1) / 8 || lines.Count != 1 + count * 8)
            throw new InvalidDataException("garrisonrefs.dat has an invalid count or record length.");

        var links = new List<Link>();
        var usedGroups = new HashSet<int>();
        for (var index = 0; index < count; index++)
        {
            var start = 1 + index * 8;
            var fortName = lines[start];
            if (string.IsNullOrWhiteSpace(fortName) ||
                !TryFinite(lines[start + 1], out var x) ||
                !TryFinite(lines[start + 2], out var y) ||
                !TryFinite(lines[start + 3], out var z) ||
                !int.TryParse(lines[start + 6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var runtimeType) ||
                !int.TryParse(lines[start + 7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var commander))
                throw new InvalidDataException($"garrisonrefs.dat fort record {index} is malformed.");

            var savedName = lines[start + 4];
            if (savedName == "none" && lines[start + 5] == "none" && runtimeType == -1 && commander == -1)
                continue; // The fort has no command attached.
            if (string.IsNullOrWhiteSpace(savedName) || savedName == "none" || runtimeType is not (14 or 15))
                throw new InvalidDataException($"garrisonrefs.dat fort record {index} has an unsupported command link.");

            // The game uses object name, abbreviation, runtime type and commander
            // for this link. groups.dat contains the saved name and commander, but
            // not the abbreviation, so the remaining pair must match uniquely.
            var matches = groups.Values.Where(group =>
                group.RawName == savedName && group.CommanderId == commander &&
                group.ParentId == -1 && group.UnitTier == 14).ToList();
            if (matches.Count != 1 || !usedGroups.Add(matches[0].GroupId))
                throw new InvalidDataException($"garrisonrefs.dat fort record {index} does not resolve to one distinct root command.");
            links.Add(new Link(fortName, x, y, z, matches[0].GroupId, matches[0].Nation));
        }
        return links;
    }

    private static bool TryFinite(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
}
