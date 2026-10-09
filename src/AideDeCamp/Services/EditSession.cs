using AideDeCamp.Models;

namespace AideDeCamp.Services;

/// <summary>
/// Records semantic working-state edits as reversible transactions. A batch edit or
/// multi-field detail apply becomes one undo step instead of many unrelated mutations.
/// </summary>
public sealed class EditSession
{
    private readonly Stack<EditTransaction> _undo = new();
    private readonly Stack<EditTransaction> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void ExecuteCreation(string description, Action apply, Action undo)
    {
        apply();
        _undo.Push(new EditTransaction(description, new(), new(), UndoAction: undo, RedoAction: apply));
        _redo.Clear();
    }
    public bool ExecuteManagement(string description,ManagementDocument document,Action action)
    {
        var before=document.Capture();try{action();}catch{document.Restore(before);throw;}
        var after=document.Capture();if(before.Count==after.Count && before.All(p=>after.TryGetValue(p.Key,out var v)&&v==p.Value))return false;
        _undo.Push(new EditTransaction(description,new(),new(),UndoAction:()=>document.Restore(before),RedoAction:()=>document.Restore(after)));_redo.Clear();return true;
    }
    public string UndoLabel => _undo.Count == 0 ? string.Empty : _undo.Peek().Description;
    public string RedoLabel => _redo.Count == 0 ? string.Empty : _redo.Peek().Description;

    public bool Execute(string description, IEnumerable<CombatUnitNode> units, Action action)
        => Execute(description, units, Array.Empty<GroupNode>(), action);

    public bool Execute(string description, IEnumerable<CombatUnitNode> units, IEnumerable<GroupNode> groups, Action action)
    {
        var distinct = units.Distinct().ToList();
        var before = distinct.ToDictionary(u => u, UnitEditSnapshot.Capture);
        var groupBefore = groups.Distinct().ToDictionary(g => g, GroupEditSnapshot.Capture);
        try
        {
            action();
        }
        catch
        {
            // A semantic edit is atomic in working memory too: never leave half of a
            // batch/detail transaction applied if a future edit action throws.
            foreach (var pair in before) pair.Value.ApplyTo(pair.Key);
            foreach (var pair in groupBefore) pair.Value.ApplyTo(pair.Key);
            throw;
        }
        var after = distinct.ToDictionary(u => u, UnitEditSnapshot.Capture);
        var groupAfter = groupBefore.Keys.ToDictionary(g => g, GroupEditSnapshot.Capture);
        if (before.All(pair => after[pair.Key] == pair.Value) && groupBefore.All(pair => groupAfter[pair.Key] == pair.Value)) return false;
        _undo.Push(new EditTransaction(description, before, after, groupBefore, groupAfter));
        _redo.Clear();
        return true;
    }

    public Dictionary<CombatUnitNode, UnitEditSnapshot> Capture(IEnumerable<CombatUnitNode> units) =>
        units.Distinct().ToDictionary(u => u, UnitEditSnapshot.Capture);

    public bool RecordExternalChange(string description, IReadOnlyDictionary<CombatUnitNode, UnitEditSnapshot> before)
    {
        var after = before.Keys.ToDictionary(u => u, UnitEditSnapshot.Capture);
        if (before.All(pair => after.TryGetValue(pair.Key, out var value) && pair.Value == value)) return false;
        _undo.Push(new EditTransaction(description, new Dictionary<CombatUnitNode, UnitEditSnapshot>(before), after));
        _redo.Clear();
        return true;
    }

    public bool Undo(out string description)
    {
        description = string.Empty;
        if (_undo.Count == 0) return false;
        var tx = _undo.Peek();
        tx.UndoAction?.Invoke();
        foreach (var pair in tx.Before) pair.Value.ApplyTo(pair.Key);
        if (tx.GroupBefore is not null) foreach (var pair in tx.GroupBefore) pair.Value.ApplyTo(pair.Key);
        _undo.Pop(); _redo.Push(tx);
        description = tx.Description;
        return true;
    }

    public bool Redo(out string description)
    {
        description = string.Empty;
        if (_redo.Count == 0) return false;
        var tx = _redo.Peek();
        tx.RedoAction?.Invoke();
        foreach (var pair in tx.After) pair.Value.ApplyTo(pair.Key);
        if (tx.GroupAfter is not null) foreach (var pair in tx.GroupAfter) pair.Value.ApplyTo(pair.Key);
        _redo.Pop(); _undo.Push(tx);
        description = tx.Description;
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private sealed record EditTransaction(
        string Description,
        Dictionary<CombatUnitNode, UnitEditSnapshot> Before,
        Dictionary<CombatUnitNode, UnitEditSnapshot> After,
        Dictionary<GroupNode, GroupEditSnapshot>? GroupBefore = null,
        Dictionary<GroupNode, GroupEditSnapshot>? GroupAfter = null,
        Action? UndoAction=null,Action? RedoAction=null);
}

public sealed record GroupEditSnapshot(string Name, int ParentId, int Order, string CommandPath)
{
    public static GroupEditSnapshot Capture(GroupNode g) => new(g.Name, g.ParentId, g.EditorOrder, g.CommandPath);
    public void ApplyTo(GroupNode g) { g.Name = Name; g.ParentId = ParentId; g.EditorOrder = Order; g.CommandPath = CommandPath; g.RefreshAggregates(); }
}

public sealed record UnitEditSnapshot(
    string Name,
    int EditorOrder,
    int ParentId,
    int Nation,
    int StateId,
    string HomeStateName,
    int WeaponId,
    string WeaponName,
    string EnlistDateRaw,
    DateTime? EnlistDate,
    int ContractMonths,
    double ExperienceRaw,
    int TotalMenRaw,
    double CasualtyRatioRaw,
    double TransferTimeRaw,
    string CommandPath,
    double? Stock0,
    double? Stock1,
    double? Stock2,
    double? Stock3)
{
    public static UnitEditSnapshot Capture(CombatUnitNode unit) => new(
        unit.Name, unit.EditorOrder, unit.ParentId, unit.Nation, unit.StateId, unit.HomeStateName,
        unit.WeaponId, unit.WeaponName, unit.EnlistDateRaw, unit.EnlistDate, unit.ContractMonths, unit.ExperienceRaw,
        unit.TotalMenRaw, unit.CasualtyRatioRaw, unit.TransferTimeRaw, unit.CommandPath,
        unit.SupplyStockAt(0), unit.SupplyStockAt(1), unit.SupplyStockAt(2), unit.SupplyStockAt(3));

    public void ApplyTo(CombatUnitNode unit)
    {
        unit.Name = Name;
        unit.EditorOrder = EditorOrder;
        unit.ParentId = ParentId;
        unit.Nation = Nation;
        unit.StateId = StateId;
        unit.HomeStateName = HomeStateName;
        unit.WeaponId = WeaponId;
        unit.WeaponName = WeaponName;
        unit.EnlistDateRaw = EnlistDateRaw;
        unit.EnlistDate = EnlistDate;
        unit.ContractMonths = ContractMonths;
        unit.ExperienceRaw = ExperienceRaw;
        unit.TotalMenRaw = TotalMenRaw;
        unit.CasualtyRatioRaw = CasualtyRatioRaw;
        unit.TransferTimeRaw = TransferTimeRaw;
        unit.CommandPath = CommandPath;
        unit.RestoreSupplyStock(Stock0, Stock1, Stock2, Stock3);
        unit.RefreshDisplay();
    }
}
