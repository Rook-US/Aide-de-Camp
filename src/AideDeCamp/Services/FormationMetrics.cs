using AideDeCamp.Models;

namespace AideDeCamp.Services;

public sealed record CardMetric(string Label, string Value);

// Shared card/roster/detail projection. Unknown game fields are never inferred from men.
public sealed record FormationMetrics(long Assigned, long Present, long Casualties, long Maximum,
    int DirectHeadquarters, int TotalHeadquarters, int CombatUnits, long Infantry, long Cavalry,
    int ArtilleryUnits, long? Guns)
{
    public string Strength => Maximum > 0 ? $"{Assigned * 100.0 / Maximum:0}%" : "—";
    public string StrengthBrush => Maximum <= 0 ? "#E8EDF2" : (Assigned * 100.0 / Maximum) switch {
        <= 30 => "#F05D5E", < 40 => "#F29E4C", < 50 => "#E9C46A", _ => "#E8EDF2" };
    public static FormationMetrics For(OobNode node)
    {
        var units = new List<CombatUnitNode>();
        var seen = new HashSet<OobNode>();
        var headquarters = 0;
        void Visit(OobNode n) {
            if (!seen.Add(n)) return;
            if (n is CombatUnitNode u) { if (u.IsLandAsset) units.Add(u); return; }
            if (n is GroupNode g && !g.IsLandCommand) return;
            if (n != node) headquarters++;
            foreach (var child in n.Children) Visit(child);
        }
        Visit(node);
        var artillery = units.Where(u => u.UnitType == 2).ToList();
        return new(units.Sum(u => (long)u.FieldStrength), units.Where(u => !u.IsInTransfer).Sum(u => (long)u.FieldStrength),
            units.Sum(u => (long)u.Casualties), units.Sum(u => (long)u.ConfiguredMaxStrength),
            node.Children.OfType<GroupNode>().Count(g => g.IsLandCommand), headquarters, units.Count,
            units.Where(u => u.UnitType == 0).Sum(u => (long)u.FieldStrength),
            units.Where(u => u.UnitType == 1).Sum(u => (long)u.FieldStrength), artillery.Count,
            artillery.Any(u => u.GunCount is null) ? null : artillery.Sum(u => (long)u.GunCount!.Value));
    }
    public IReadOnlyList<CardMetric> Lines(OobNode node)
    {
        var rows = new List<CardMetric>();
        if (node is GroupNode) {
            if (TotalHeadquarters > 0) rows.Add(new("Subordinate HQs", $"{DirectHeadquarters:N0} immediate / {TotalHeadquarters:N0} total"));
            if (CombatUnits > 0) rows.Add(new("Combat formations", CombatUnits.ToString("N0")));
            if (Infantry > 0) rows.Add(new("Infantry", $"{Infantry:N0} men"));
            if (Cavalry > 0) rows.Add(new("Cavalry", $"{Cavalry:N0} men"));
            if (ArtilleryUnits > 0) rows.Add(new("Artillery guns", Guns?.ToString("N0") ?? "Unmapped"));
            rows.Add(new("Assigned field strength", $"{Assigned:N0} / {Maximum:N0} ({Strength})"));
            if (Present != Assigned) rows.Add(new("Present / in transfer", $"{Present:N0} / {Assigned - Present:N0} men"));
            rows.Add(new("Casualties / unavailable", Casualties.ToString("N0")));
        } else if (node is CombatUnitNode u) {
            rows.Add(new(u.TypeName + " manpower", $"{u.FieldStrength:N0} / {u.ConfiguredMaxStrength:N0} ({u.StrengthPercent}% of maximum)"));
            rows.Add(new("Casualties / unavailable", u.Casualties.ToString("N0")));
            if (u.UnitType == 2) rows.Add(new("Guns", u.GunCount?.ToString("N0") ?? "Unmapped"));
            rows.Add(new("Weapon", u.WeaponName));
            rows.Add(new("Contract", $"{u.ContractMonths} months"));
            rows.Add(new("Remaining", u.ContractRemainingMonths is int remaining
                ? $"{remaining} months ({(u.ContractMonths > 0 ? (remaining * 100.0 / u.ContractMonths).ToString("0") + "%" : "—")})"
                : "Unknown — start/campaign date missing"));
            rows.Add(new("Transfer ETA", $"{u.TransferDays} days"));
        }
        return rows;
    }
}
