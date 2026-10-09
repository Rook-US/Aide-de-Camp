using AideDeCamp.Models;

namespace AideDeCamp.Services;

public enum CommandCategory
{
    FieldCommand,
    Garrison,
    Fleet,
    Unknown
}

/// <summary>
/// Conservative root-command classifier. Unknown land roots remain visible as
/// Field Commands instead of being silently hidden or guessed into Garrisons.
/// </summary>
public sealed class CommandClassificationService
{
    public CommandCategory ClassifyRoot(GroupNode group)
    {
        if (group.SavedCategory is { } saved) return saved;
        var name = group.Name ?? string.Empty;
        if (group.UnitTier == 17 || ContainsAny(name, "fleet", "navy", "naval", "flotilla"))
            return CommandCategory.Fleet;

        if (ContainsAny(name, "garrison", "fort ", "fortress", "arsenal", "citadel"))
            return CommandCategory.Garrison;

        if (group.UnitTier >= 15 || ContainsAny(name, "army", "corps", "division", "department", "district", "theater", "theatre", "command"))
            return CommandCategory.FieldCommand;

        // Tier 14 used to be hard-coded as Garrison. That is unsafe for regimental
        // campaigns and mods where a Division can legitimately be a top-level field command.
        return CommandCategory.Unknown;
    }

    public CommandCategory CategoryForGroup(GroupNode group, IReadOnlyDictionary<int, GroupNode> groups)
    {
        var seen = new HashSet<int>(); var current = group;
        while (seen.Add(current.GroupId))
        {
            if (ClassifyRoot(current) == CommandCategory.Fleet) return CommandCategory.Fleet;
            if (!groups.TryGetValue(current.ParentId, out var parent) || parent.Nation != group.Nation)
                return ClassifyRoot(current);
            current = parent;
        }
        return CommandCategory.Unknown;
    }
    public CommandCategory CategoryForUnit(CombatUnitNode unit, IReadOnlyDictionary<int, GroupNode> groups)
    {
        var category = groups.TryGetValue(unit.ParentId, out var parent) && parent.Nation == unit.Nation ? CategoryForGroup(parent, groups) : CommandCategory.Unknown;
        return category == CommandCategory.Fleet ? category : unit.UnitType is < 0 or > 2 ? CommandCategory.Unknown : category;
    }
    public bool IsLandEditable(CombatUnitNode unit, IReadOnlyDictionary<int, GroupNode> groups) =>
        unit.UnitType is >= 0 and <= 2 && CategoryForUnit(unit, groups) is CommandCategory.FieldCommand or CommandCategory.Garrison;
    public bool IsFieldVisible(GroupNode group) => ClassifyRoot(group) == CommandCategory.FieldCommand;
    public bool IsGarrison(GroupNode group) => ClassifyRoot(group) == CommandCategory.Garrison;
    private static bool ContainsAny(string source, params string[] values) =>
        values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));
}
