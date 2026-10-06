namespace AideDeCamp.Models;

public sealed record BatchEditRequest(
    int? EtaDays,
    int? ContractMonths,
    int? ContractRemainingMonths,
    double? Experience,
    WeaponOption? Weapon,
    bool AllowUnknownWeaponCompatibility);

public sealed class BatchUnitPlan
{
    public required CombatUnitNode Unit { get; init; }
    public int? EtaDays { get; set; }
    public int? ContractMonths { get; set; }
    public double? Experience { get; set; }
    public WeaponOption? Weapon { get; set; }
    public List<string> Skips { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool HasChange => EtaDays.HasValue || ContractMonths.HasValue || Experience.HasValue || Weapon is not null;
}

public sealed class BatchEditPlan
{
    public required BatchEditRequest Request { get; init; }
    public List<BatchUnitPlan> Units { get; } = new();
    public List<string> Errors { get; } = new();
    public int ChangedUnitCount => Units.Count(u => u.HasChange);
    public int SkippedUnitCount => Units.Count(u => u.Skips.Count > 0);
    public int WarningUnitCount => Units.Count(u => u.Warnings.Count > 0);
    public IEnumerable<CombatUnitNode> ChangedUnits => Units.Where(u => u.HasChange).Select(u => u.Unit);

    public string BuildTooltip()
    {
        var lines = new List<string>();
        if (Errors.Count > 0)
        {
            lines.Add("ERRORS");
            lines.AddRange(Errors);
        }
        var skipped = Units.Where(u => u.Skips.Count > 0).ToList();
        if (skipped.Count > 0)
        {
            if (lines.Count > 0) lines.Add(string.Empty);
            lines.Add("SKIPPED FIELDS");
            foreach (var item in skipped)
            {
                lines.Add($"{item.Unit.Name}");
                lines.AddRange(item.Skips.Select(x => $"  • {x}"));
            }
        }
        var warnings = Units.Where(u => u.Warnings.Count > 0).ToList();
        if (warnings.Count > 0)
        {
            if (lines.Count > 0) lines.Add(string.Empty);
            lines.Add("WARNINGS");
            foreach (var item in warnings)
            {
                lines.Add($"{item.Unit.Name}");
                lines.AddRange(item.Warnings.Select(x => $"  • {x}"));
            }
        }
        return lines.Count == 0 ? "No preview warnings." : string.Join("\n", lines);
    }
}
