using AideDeCamp.Models;

namespace AideDeCamp.Services;

/// <summary>
/// Translates the canonical parsed game model into a UI-only military display model.
/// The translation is intentionally one-way. UI edits are applied to Source objects
/// in the canonical model, and the display projection is rebuilt afterward.
/// </summary>
public sealed class OobDisplayModelService
{
    public DisplayOobModel Build(
        IReadOnlyDictionary<int, GroupNode> groups,
        IReadOnlyList<CombatUnitNode> units,
        int nation,
        IEnumerable<int> rootGroupIds, Func<GroupNode, bool>? includeGroup = null, Func<CombatUnitNode, bool>? includeUnit = null)
    {
        var model = new DisplayOobModel();
        var nationGroups = groups.Values.Where(g => g.Nation == nation && (includeGroup?.Invoke(g) ?? true)).ToDictionary(g => g.GroupId);
        var childrenByParent = nationGroups.Values
            .Where(g => g.ParentId >= 0)
            .GroupBy(g => g.ParentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.EditorOrder).ThenBy(x => x.GroupLineStart).ToList());
        var unitsByParent = units
            .Where(u => u.Nation == nation && nationGroups.ContainsKey(u.ParentId) && (includeUnit?.Invoke(u) ?? true))
            .GroupBy(u => u.ParentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.EditorOrder).ThenBy(x => x.RegimentLineStart).ToList());

        DisplayCommandNode BuildCommand(GroupNode source, int depth, HashSet<int> ancestry)
        {
            var node = new DisplayCommandNode { Source = source, Depth = depth };
            model.CommandsById[source.GroupId] = node;

            // A malformed/modded save should not be allowed to recurse forever.
            if (!ancestry.Add(source.GroupId)) return node;

            if (childrenByParent.TryGetValue(source.GroupId, out var childGroups))
            {
                foreach (var child in childGroups)
                {
                    if (ancestry.Contains(child.GroupId)) continue;
                    node.Subcommands.Add(BuildCommand(child, depth + 1, new HashSet<int>(ancestry)));
                }
            }

            if (unitsByParent.TryGetValue(source.GroupId, out var attached))
                node.AttachedUnits.AddRange(attached);

            return node;
        }

        foreach (var id in rootGroupIds.Distinct())
        {
            if (!nationGroups.TryGetValue(id, out var root)) continue;
            model.Roots.Add(BuildCommand(root, 0, new HashSet<int>()));
        }

        model.Roots.Sort((a, b) =>
        {
            var tier = a.Source.EditorOrder.CompareTo(b.Source.EditorOrder);
            return tier != 0 ? tier : string.Compare(a.Source.Name, b.Source.Name, StringComparison.OrdinalIgnoreCase);
        });
        return model;
    }
}
