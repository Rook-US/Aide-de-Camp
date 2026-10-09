using System.Globalization;
using System.IO;
using AideDeCamp.Models;

namespace AideDeCamp.Services;

// Value-only design: no file offsets, live nodes or UI controls. Future templates
// can reuse this specification while resolving placement against the current save.
public sealed record CreationPerk(int Id = -1, int Level = 0, double Progress = 0);
public sealed record CreationUniform(int Faction, string Coat, string Trousers, string Variation);
public sealed record UnitBlueprint
{
    public bool Headquarters { get; init; }
    public int Faction { get; init; }
    public string Name { get; init; } = "";
    public int NativeTier { get; init; } = 13;
    public int UnitType { get; init; }
    public int CommanderId { get; init; } = -1;
    public int Strength { get; init; }
    public int WeaponId { get; init; } = -1;
    public int HomeStateId { get; init; } = -1;
    public int ContractMonths { get; init; } = 12;
    public int RecruitingType { get; init; }
    public double Training { get; init; }
    public double Experience { get; init; }
    public bool HorseArtillery { get; init; }
    public CreationPerk[] Perks { get; init; } = [new()];
    public double[] SupplyPercent { get; init; } = [100, 100, 100, 100];
    public string Coat { get; init; } = "000-049-083";
    public string Trousers { get; init; } = "135-206-235";
    public string ColorVariation { get; init; } = "000-000-000";
}

// Placement belongs to an invocation, never implicitly to a reusable template.
public sealed record CreationPlacement(int? ParentId, TownStateMap.MappedTown? Town = null);
public sealed record CreationRequest(UnitBlueprint Blueprint, CreationPlacement Placement);
public sealed record CreationOfficer(int Id, string Name, int Faction, int Branch, int Rank, int Status, bool Active);
public sealed record CreationPerkOption(int Id, int Level, string Name, string Description);
public sealed record CreationAssessment(IReadOnlyList<string> Errors, IReadOnlyList<string> Confirmations)
{
    public bool CanCreate => Errors.Count == 0;
}

/// <summary>Named 1.142 record construction. Evidence: docs/CREATE_TOOL.md.</summary>
public static class CreationRecords
{
    // GameVars.experiencetext, indexed by floor(Regiment.Experience() * 5).
    public static readonly string[] ExperienceLabels = ["Green", "Inexperienced", "Battle Experienced", "Veterans", "Elite", "Crack"];
    // PerkRow.PerksShown*Single arrays, also used by AIBattle.CheckPerkSelection.
    public static bool IsStandardCombatPerk(int branch, int perk) => perk < 0 || branch switch { 0 => perk is >= 0 and <= 7, 1 => perk is >= 8 and <= 13, 2 => perk is >= 14 and <= 18, _ => false };
    private static string N(double n) => n.ToString("0.#########", CultureInfo.InvariantCulture);
    public static List<string> Regiment(int id, UnitBlueprint b, int parent, DateTime date,
        (float X, float Y, float Z) transfer)
    {
        var p = b.Perks.Single();
        return [N(id), b.Name, b.Name, N(parent), N(b.UnitType), N(b.CommanderId), N(b.Strength),
            "0", "0", "0", N(b.Experience), N(b.Training), "True", N(b.WeaponId),
            b.Coat, b.Trousers, b.ColorVariation, N(p.Id), N(p.Level), N(p.Progress), "-1", "-1", "0",
            b.HorseArtillery.ToString(), N(b.NativeTier), "0", date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            N(b.ContractMonths), "0", N(b.HomeStateId), N(b.RecruitingType),
            $"Raised on {date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}", "0", "0",
            N(transfer.X), N(transfer.Y), N(transfer.Z), "0", "1"];
    }
    public static List<string> Group(int id, UnitBlueprint b, int? parent)
    {
        // Initial no-loss, full-condition HQ. Stores are runtime aggregates, not
        // editable combat stock. Test 10's game resave normalizes them on load.
        var lines = new List<string> {N(id), b.Name, N(parent ?? -1), N(b.Faction), N(b.CommanderId), "100",
            "100", "100", "100", "100", "100", "100", "100", "-1", "0", b.Coat, b.Trousers,
            N(b.NativeTier), "0", "0"};
        foreach (var p in b.Perks) lines.AddRange([N(p.Id), N(p.Level), N(p.Progress)]);
        return lines;
    }
    public static List<string> Path(UnitBlueprint b, string history)
    {
        var p = new List<string> { b.Name, b.Name, N(b.UnitType), N(b.CommanderId),
            "0", // cover history count
            "0", // rotation; parent provides placement
            "0", // movement path count
            "-1", "-1", "0", // timed movement, order delay, arrival
            "False", "False", "False", "False", // battle participation
            "0", "0", "0", "0", "0", "0", "0", // transfer and patrol
            "False", "0", // basic-garrison flag, blockade
            "0", "0", "0", "0", "0", // stance and branch orders
            "0", "0", "0", "1", // idle time, entrenchment, stats update, morale
            "150" };
        p.AddRange(Enumerable.Repeat("False", 150));
        p.AddRange(["", "", "-1", "-1", "0", "0", // no advised source, no queued orders
            "0", "0", "False", "False", // upkeep/recruitment cache, retreat flags
            "3", "1", "1", "1", // tactical ammunition fractions
            "4"]); // four supply-consumption triples
        p.AddRange(Enumerable.Repeat("0", 12));
        p.AddRange(["1", "0", "0", "1", "0"]); // flow, reinforcements, ratio, retreat angle
        p.AddRange(b.SupplyPercent.Select(v => N(b.Strength * v / 100)));
        p.AddRange(["True", "-1", "False", "0", "False", "0", "0", // recruitment, flag, reset, missing, reserved/discarded, battle-until, wounded
            "False", "False", "False", "False", "True", // transport permissions
            "0", "0", "0", "2", "0", "-1", history, "0", "-1", // theater, reinforcement, history, prestige, combat zone
            "", "-1", "-1", // army group references
            "0", "0", "False", "0", "False", "True", "0"]); // embarkation/DLC/coordination
        if (p.Count != 250) throw new InvalidDataException("New no-order path has an incorrect width.");
        PathRecordParser.Parse(new[] { "1" }.Concat(p).ToArray(), strictFields: true);
        return p;
    }
    public static List<string> Deployment(int id, TownLocationParser.Town town) =>
        [N(id), "1", N(town.X), N(-town.Z), "0", "0", "0", "-1", "0", "0", "0", "0", "0", "True", "100"];

    public static int FixedCount(IReadOnlyList<string> lines, int width, string file)
    {
        if (lines.Count == 0 || !int.TryParse(lines[0], out var count) || count < 0 || lines.Count != 1L + (long)count * width)
            throw new InvalidDataException($"{file}: unknown record boundary or count.");
        var ids = Enumerable.Range(0, count).Select(i => int.Parse(lines[1 + i * width], CultureInfo.InvariantCulture)).Order().ToArray();
        if (!ids.SequenceEqual(Enumerable.Range(0, count))) throw new InvalidDataException($"{file}: IDs must be unique contiguous saved IDs.");
        return count;
    }
    public static int DeploymentTail(IReadOnlyList<string> lines)
    {
        if (lines.Count < 44 || !int.TryParse(lines[42], out var count) || count < 0 || 43L + 15L * count >= lines.Count)
            throw new InvalidDataException("battledata.dat: unresolved deployment section.");
        int tail = 43 + 15 * count;
        if (!int.TryParse(lines[tail], out var economy) || economy < 0 || lines.Count != tail + 1L + 2L * economy)
            throw new InvalidDataException("battledata.dat: unresolved economy tail.");
        return tail;
    }
    public static void ValidateFieldTypes(IReadOnlyList<string> lines, bool headquarters)
    {
        int width = headquarters ? 32 : 39, count = FixedCount(lines, width, headquarters ? "groups.dat" : "regiments.dat");
        int[] text = headquarters ? [1, 15, 16] : [1, 2, 14, 15, 16, 26, 31];
        int[] booleans = headquarters ? [] : [12, 23];
        int[] integers = headquarters ? [0, 2, 3, 4, 13, 14, 17, 19, 20, 21, 23, 24, 26, 27, 29, 30] : [0, 3, 4, 5, 6, 7, 8, 13, 17, 18, 20, 21, 22, 24, 25, 27, 28, 29, 30, 32];
        for (int record = 0; record < count; record++) for (int offset = 0; offset < width; offset++) {
            var value = lines[1 + record * width + offset];
            bool valid = !value.Any(char.IsControl) && (text.Contains(offset) || (booleans.Contains(offset) ? bool.TryParse(value, out _) : integers.Contains(offset) ? int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)));
            if (!valid) throw new InvalidDataException($"{(headquarters ? "groups.dat" : "regiments.dat")} record {record}, field {offset} is malformed.");
        }
    }
}
