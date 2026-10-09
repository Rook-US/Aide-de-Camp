using System.Globalization;
using AideDeCamp.Models;

namespace AideDeCamp.Services;

/// <summary>Stages only relevant combat stock slots; percentages use active strength.</summary>
public sealed class SupplyStockEditPlan
{
    public sealed record Change(CombatUnitNode Unit, int Slot, double Amount);
    public IReadOnlyList<Change> Changes { get; private init; } = Array.Empty<Change>();
    public IReadOnlyList<string> Errors { get; private init; } = Array.Empty<string>();
    public bool CanApply => Changes.Count > 0 && Errors.Count == 0;

    public static bool IsRelevant(int type, int slot) => slot switch
    {
        0 => type is 0 or 1,
        1 => type == 2,
        2 => type is 0 or 1 or 2,
        3 => type is 1 or 2,
        _ => false
    };

    public static SupplyStockEditPlan Build(IEnumerable<CombatUnitNode> units,
        IReadOnlyDictionary<int, string> entered, bool rawValues)
    {
        var changes = new List<Change>();
        var errors = new List<string>();
        foreach (var slot in entered.Keys)
            if (slot is < 0 or > 3) errors.Add($"Unsupported supply slot {slot}.");
        foreach (var unit in units.Distinct())
        {
            if (unit.UnitType is not (0 or 1 or 2) || !unit.IsLandAsset ||
                unit.PathLinkStatus != PathLinkStatus.Confirmed ||
                unit.PathSupplyStockLineIndex is null || !unit.HasSupplyStock)
            {
                errors.Add($"{unit.Name}: a unique, complete 1.142 combat stock record is required.");
                continue;
            }
            if (unit.HasUnsavedChange("FieldStrength") || unit.HasUnsavedChange("Casualties"))
            {
                errors.Add($"{unit.Name}: save the strength change before editing stock.");
                continue;
            }
            for (var slot = 0; slot < 4; slot++)
            {
                if (!IsRelevant(unit.UnitType, slot) || !entered.TryGetValue(slot, out var text) ||
                    string.IsNullOrWhiteSpace(text)) continue;
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var input) ||
                    !double.IsFinite(input) || input < 0)
                {
                    errors.Add($"{unit.Name}: supply {slot} needs a finite nonnegative number.");
                    continue;
                }
                double amount;
                if (rawValues) amount = input;
                else
                {
                    if (input > 100 || unit.FieldStrength <= 0)
                    {
                        errors.Add($"{unit.Name}: supply {slot} percentage must be 0–100 with positive active strength.");
                        continue;
                    }
                    amount = unit.FieldStrength * input / 100;
                }
                var saved = unit.SupplyStockAt(slot)!.Value;
                var refillTarget = (long)unit.TotalMenRaw + unit.WoundedRaw;
                if (!double.IsFinite(amount) || amount > Math.Max(refillTarget, saved))
                {
                    errors.Add($"{unit.Name}: supply {slot} exceeds the observed campaign refill target or saved amount.");
                    continue;
                }
                if (amount != saved) changes.Add(new Change(unit, slot, amount));
            }
        }
        return new SupplyStockEditPlan { Changes = changes, Errors = errors };
    }

    public void Apply()
    {
        if (!CanApply) throw new InvalidOperationException("No validated supply stock changes are staged.");
        foreach (var change in Changes) change.Unit.SetSupplyStock(change.Slot, change.Amount);
    }
}
