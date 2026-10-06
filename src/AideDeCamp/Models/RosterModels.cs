using System.Windows;
using System.Windows.Media;

namespace AideDeCamp.Models;

public sealed class RosterRow
{
    public bool LandMetrics { get; init; } = true;
    public required OobNode Node { get; init; }
    public int Depth { get; init; }
    public bool IsGroup => Node is GroupNode;
    public bool IsUnit => Node is CombatUnitNode;
    public CombatUnitNode? Unit => Node as CombatUnitNode;
    public GroupNode? Group => Node as GroupNode;
    public string Name => Node.Name;
    public string TreeGlyph => Group is null ? string.Empty : Group.IsExpanded ? "−" : "+";
    public static double HierarchyIndent { get; set; } = 18;
    public Thickness IndentMargin => new(Math.Max(0, Depth) * HierarchyIndent, 0, 0, 0);
    public string TypeName => !LandMetrics && Unit is not null ? $"Raw type {Unit.UnitType}" : Unit?.TypeName ?? Group?.TierName ?? string.Empty;
    public string HomeStateName => Unit?.HomeStateName ?? "Unmapped";
    public string CommandPath => Unit?.CommandPath ?? Group?.CommandPath ?? string.Empty;
    public string MaximumText => LandMetrics ? Node.Metrics.Maximum.ToString("N0") : "—";
    public string GunsText => !LandMetrics || Node.Metrics.ArtilleryUnits == 0 ? "—" : Node.Metrics.Guns?.ToString("N0") ?? "Unmapped";
    public string CommanderName => Node.IdentityCommander;
    public string FieldStrengthText => !LandMetrics ? "—" : Node.Metrics.Assigned.ToString("N0");
    public string CasualtiesText => !LandMetrics ? "—" : Node.Metrics.Casualties.ToString("N0");
    public string StrengthText => !LandMetrics ? "—" : Node.Metrics.Strength;
    public string WeaponName => !LandMetrics ? "—" : Unit?.WeaponName ?? "—";
    public string RaisedText => Unit?.RaisedText ?? "—";
    public string ContractText => !LandMetrics ? "—" : Unit is null ? "—" : $"{Unit.ContractMonths} mo";
    public string ContractRemainingText => !LandMetrics ? "—" : Unit?.ContractRemainingText ?? "—";
    public string ExperienceText => Unit is null ? "Unmapped" : Unit.ExperienceRaw.ToString("0.###");
    public string TransferText => !LandMetrics ? "—" : Unit is null ? "—" : Unit.TransferDays.ToString();
    public string ReadinessText => Unit is null ? "—" : "—";
    public FontWeight FontWeight => IsGroup ? FontWeights.SemiBold : FontWeights.Normal;

    public string StrengthForeground => !LandMetrics ? "#C5D0D9" : Node.Metrics.StrengthBrush;
    public string ContractForeground => !LandMetrics || Unit is null ? "#DCE4EB" : Unit.ContractRiskBrush;
    public string RemainingForeground => !LandMetrics || Unit is null ? "#DCE4EB" : Unit.ContractRiskBrush;
    public string ContractRiskTooltip => Unit?.ContractRisk switch
    {
        ContractRiskLevel.Critical => "Contract has one quarter or less remaining.",
        ContractRiskLevel.Warning => "Contract has one half or less remaining.",
        _ => string.Empty
    };
    public string ReadinessForeground => "#71808E";

    public string RowEditMarker
    {
        get
        {
            if (Unit is null) return string.Empty;
            if (TrackedFields.Any(f => Unit.GetEditState(f) == EditState.Unsaved)) return "●";
            if (TrackedFields.Any(f => Unit.GetEditState(f) == EditState.SavedThisSession)) return "✓";
            return string.Empty;
        }
    }
    public string RowEditMarkerBrush => Unit is not null && TrackedFields.Any(f => Unit.GetEditState(f) == EditState.Unsaved) ? "#E9B85C" : "#72B38B";
    public string RowChangeTooltip
    {
        get
        {
            if (Unit is null) return string.Empty;
            var lines = new List<string>();
            foreach (var (field, label) in TrackedFieldLabels)
            {
                var tip = Unit.GetEditTooltip(field, label);
                if (!string.IsNullOrWhiteSpace(tip)) lines.Add(tip.Replace("\n", "  "));
            }
            return lines.Count == 0 ? "No edits in this session." : string.Join("\n", lines);
        }
    }

    public string NameCellBackground => EditBackground("Name");
    public string HomeStateCellBackground => EditBackground("HomeState");
    public string FieldCellBackground => EditBackground("FieldStrength");
    public string CasualtiesCellBackground => EditBackground("Casualties");
    public string WeaponCellBackground => EditBackground("Weapon");
    public string ContractCellBackground => EditBackground("Contract");
    public string RemainingCellBackground => EditBackground("ContractRemaining");
    public string ExperienceCellBackground => EditBackground("Experience");
    public string EtaCellBackground => EditBackground("ETA");
    public string CommandPathCellBackground => EditBackground("Parent");

    public string NameChangeTooltip => EditTooltip("Name", "Unit name");
    public string HomeStateChangeTooltip => EditTooltip("HomeState", "Home state");
    public string FieldChangeTooltip => EditTooltip("FieldStrength", "Field strength");
    public string CasualtiesChangeTooltip => EditTooltip("Casualties", "Casualties");
    public string WeaponChangeTooltip => EditTooltip("Weapon", "Weapon");
    public string ContractChangeTooltip => EditTooltip("Contract", "Contract length");
    public string RemainingChangeTooltip => EditTooltip("ContractRemaining", "Contract remaining");
    public string ExperienceChangeTooltip => EditTooltip("Experience", "Experience");
    public string EtaChangeTooltip => EditTooltip("ETA", "Transfer ETA");
    public string CommandPathChangeTooltip => EditTooltip("Parent", "Command assignment");

    private string EditBackground(string field) => Unit?.GetEditState(field) switch
    {
        EditState.Unsaved => "#4A3B22",
        EditState.SavedThisSession => "#213A2C",
        _ => "Transparent"
    };

    private string EditTooltip(string field, string label) => Unit?.GetEditTooltip(field, label) ?? string.Empty;

    private static readonly string[] TrackedFields = { "Name", "HomeState", "FieldStrength", "Casualties", "Weapon", "Contract", "ContractRemaining", "Experience", "ETA", "Parent" };
    private static readonly (string Field, string Label)[] TrackedFieldLabels =
    {
        ("Name", "Unit name"), ("HomeState", "Home state"), ("FieldStrength", "Field strength"), ("Casualties", "Casualties"),
        ("Weapon", "Weapon"), ("Contract", "Contract length"), ("ContractRemaining", "Contract remaining"), ("Experience", "Experience"),
        ("ETA", "Transfer ETA"), ("Parent", "Command assignment")
    };
}
