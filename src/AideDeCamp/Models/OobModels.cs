using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AideDeCamp.Services;

namespace AideDeCamp.Models;

// Presentation only: GTCW can present the same native hierarchy at brigade or
// regiment scale. Never use this mapping while parsing, validating, or saving.
public static class OobPresentation
{
    public static bool RegimentalScale { get; set; }
    public static int DisplayTier(int nativeTier) => !RegimentalScale ? nativeTier : nativeTier switch
    {
        16 => 15, // Army → Corps
        15 => 14, // Corps → Division
        14 => 13, // Division → Brigade
        13 => 12, // Brigade → Regiment
        _ => nativeTier
    };
    public static string TierName(int nativeTier, bool headquarters) => DisplayTier(nativeTier) switch
    {
        18 => "Army Group", 17 => "Fleet", 16 => "Army", 15 => "Corps", 14 => "Division", 13 => "Brigade",
        12 => "Regiment", 11 => "Battalion", 10 => "Battery", _ => headquarters ? $"HQ Tier {nativeTier}" : $"Tier {nativeTier}"
    };
}

public abstract class OobNode : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private double _canvasX;
    private double _canvasY;
    private bool _isExpanded;

    public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChanged(); } } }
    public ObservableCollection<OobNode> Children { get; } = new();
    public abstract bool IsGroup { get; }
    public double CanvasX { get => _canvasX; set { _canvasX = value; OnPropertyChanged(); } }
    public double CanvasY { get => _canvasY; set { _canvasY = value; OnPropertyChanged(); } }
    public bool IsExpanded { get => _isExpanded; set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(CollapseGlyph)); } } }
    public string CollapseGlyph => IsExpanded ? "−" : "+";
    public int Depth { get; set; }
    public FormationMetrics Metrics => this is GroupNode g ? g.DisplayMetrics : FormationMetrics.For(this);
    public IReadOnlyList<CardMetric> CardMetrics => Metrics.Lines(this);
    public string IdentityCommander => this switch {
        GroupNode g => string.IsNullOrWhiteSpace(g.CommanderDisplayName) ? "Commander unassigned" : g.CommanderDisplayName,
        CombatUnitNode u => string.IsNullOrWhiteSpace(u.CommanderDisplayName) ? "Commander unassigned" : u.CommanderDisplayName, _ => "—" };
    public string IdentitySecondary => this is CombatUnitNode u ? $"{u.HomeStateName} • {u.TypeName} • {u.TierName}" : $"Home State: unmapped • {((GroupNode)this).TierName} HQ";
    public void NotifyPresentationChanged() { OnPropertyChanged(nameof(GroupNode.TierName)); OnPropertyChanged(nameof(CombatUnitNode.TierName)); OnPropertyChanged(nameof(GroupNode.PresentationTier)); OnPropertyChanged(nameof(CombatUnitNode.PresentationTier)); }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GroupNode : OobNode
{
    private FormationMetrics? _displayMetrics;
    public FormationMetrics DisplayMetrics => _displayMetrics ??= FormationMetrics.For(this);
    public int EditorOrder { get; set; }
    private int _savedOrder;
    private AggregateCache? _aggregate;
    private int _parentId;
    private int _originalParentId;
    private int _savedParentId;

    public override bool IsGroup => true;
    public bool IsLandCommand { get; set; } = true;
    public int GroupId { get; init; }
    public int GroupLineStart { get; set; }
    public string RawName { get; set; } = string.Empty;
    public string CommandPath { get; set; } = string.Empty;
    public int ParentId { get => _parentId; set { if (_parentId != value) { _parentId = value; OnPropertyChanged(); } } }
    public bool HasUnsavedParent => ParentId != _savedParentId || Name != RawName || EditorOrder != _savedOrder;
    public bool HasUnsavedOrder => EditorOrder != _savedOrder;
    public void CaptureOriginalState() { _originalParentId = ParentId; _savedParentId = ParentId; _savedOrder = EditorOrder; }
    public void MarkSavedState() { _savedParentId = ParentId; RawName = Name; _savedOrder = EditorOrder; }
    public void RestoreParent(int parentId) { ParentId = parentId; }
    public int Nation { get; init; }
    public int CommanderId { get; init; }
    public int UnitTier { get; init; }
    public string CommanderName { get; set; } = string.Empty;
    public string CommanderDisplayName { get; set; } = string.Empty;
    public int PresentationTier => OobPresentation.DisplayTier(UnitTier);
    public string TierName => OobPresentation.TierName(UnitTier, headquarters: true);

    private AggregateCache Aggregate => _aggregate ??= BuildAggregate();

    // HQ NATO branch symbol follows the majority of combat units recursively beneath
    // this command. Infantry is the deterministic default in every tie/empty/unknown case.
    public int NatoUnitType => Aggregate.Cavalry > Aggregate.Infantry && Aggregate.Cavalry > Aggregate.Artillery ? 1
        : Aggregate.Artillery > Aggregate.Infantry && Aggregate.Artillery > Aggregate.Cavalry ? 2 : 0;

    public int AttachedCurrentUnitCount => Aggregate.AttachedCurrentUnitCount;
    public int AttachedExpectedUnitCount => Aggregate.AttachedExpectedUnitCount;
    public int AttachedCurrentFieldStrength => Aggregate.AttachedCurrentFieldStrength;
    public int AttachedExpectedFieldStrength => Aggregate.AttachedExpectedFieldStrength;
    public int SubordinateFormationCount => Aggregate.SubordinateFormationCount;
    public int SubordinateCurrentUnitCount => Aggregate.SubordinateCurrentUnitCount;
    public int SubordinateExpectedUnitCount => Aggregate.SubordinateExpectedUnitCount;
    public int SubordinateCurrentFieldStrength => Aggregate.SubordinateCurrentFieldStrength;
    public int SubordinateExpectedFieldStrength => Aggregate.SubordinateExpectedFieldStrength;
    public int CommandCurrentUnitCount => Aggregate.CommandCurrentUnitCount;
    public int CommandExpectedUnitCount => Aggregate.CommandExpectedUnitCount;
    public int CommandCurrentFieldStrength => Aggregate.CommandCurrentFieldStrength;
    public int CommandExpectedFieldStrength => Aggregate.CommandExpectedFieldStrength;
    public int CommandCasualties => Aggregate.CommandCasualties;
    public int CommandCurrentMaximum => Aggregate.CommandCurrentMaximum;
    public int CommandExpectedMaximum => Aggregate.CommandExpectedMaximum;
    public int CurrentStrengthPercent => CommandCurrentMaximum <= 0 ? 0 : (int)Math.Round(CommandCurrentFieldStrength * 100.0 / CommandCurrentMaximum);
    public int ExpectedStrengthPercent => CommandExpectedMaximum <= 0 ? 0 : (int)Math.Round(CommandExpectedFieldStrength * 100.0 / CommandExpectedMaximum);
    public int TransferringUnitCount => Aggregate.TransferringUnitCount;
    public int HighestTransferDays => Aggregate.HighestTransferDays;
    public bool HasTransfers => TransferringUnitCount > 0;
    public string TransferSummary => HasTransfers ? $"⇢ {TransferringUnitCount:N0} in transfer  •  max ETA {HighestTransferDays:N0}d" : string.Empty;
    public int YellowAlertCount => Aggregate.YellowAlertCount;
    public int OrangeAlertCount => Aggregate.OrangeAlertCount;
    public int RedAlertCount => Aggregate.RedAlertCount;
    public bool HasYellowAlerts => YellowAlertCount > 0;
    public bool HasOrangeAlerts => OrangeAlertCount > 0;
    public bool HasRedAlerts => RedAlertCount > 0;
    public string YellowAlertDisplay => HasYellowAlerts ? $"● {YellowAlertCount}" : string.Empty;
    public string OrangeAlertDisplay => HasOrangeAlerts ? $"● {OrangeAlertCount}" : string.Empty;
    public string RedAlertDisplay => HasRedAlerts ? $"● {RedAlertCount}" : string.Empty;
    public string YellowAlertTooltip => BuildAlertTooltip(ReadinessLevel.Yellow, "DEPLETED — BELOW 50% STRENGTH");
    public string OrangeAlertTooltip => BuildAlertTooltip(ReadinessLevel.Orange, "SEVERE — BELOW 40% STRENGTH");
    public string RedAlertTooltip => BuildAlertTooltip(ReadinessLevel.Red, "CRITICAL — 30% STRENGTH OR LESS");
    public bool AttachedExpectedDiffers => AttachedCurrentUnitCount != AttachedExpectedUnitCount || AttachedCurrentFieldStrength != AttachedExpectedFieldStrength;
    public bool SubordinateExpectedDiffers => SubordinateCurrentUnitCount != SubordinateExpectedUnitCount || SubordinateCurrentFieldStrength != SubordinateExpectedFieldStrength;
    public bool CommandExpectedDiffers => CommandCurrentFieldStrength != CommandExpectedFieldStrength || CurrentStrengthPercent != ExpectedStrengthPercent;
    public string AttachedExpectedBrush => AttachedExpectedDiffers ? "#7FD3DF" : "#66727E";
    public string SubordinateExpectedBrush => SubordinateExpectedDiffers ? "#7FD3DF" : "#66727E";
    public string CommandExpectedBrush => CommandExpectedDiffers ? "#7FD3DF" : "#66727E";

    public void InvalidateAggregateCache() { _aggregate = null; _displayMetrics = null; }

    public void NotifyAggregateChanged()
    {
        OnPropertyChanged(nameof(Metrics)); OnPropertyChanged(nameof(CardMetrics));
        OnPropertyChanged(nameof(IdentityCommander)); OnPropertyChanged(nameof(IdentitySecondary));
        foreach (var name in AggregatePropertyNames) OnPropertyChanged(name);
    }

    public void RefreshAggregates()
    {
        InvalidateAggregateCache();
        NotifyAggregateChanged();
    }

    private AggregateCache BuildAggregate()
    {
        var attachedCurrentUnitCount = 0;
        var attachedExpectedUnitCount = 0;
        var attachedCurrentFieldStrength = 0;
        var attachedExpectedFieldStrength = 0;
        var subordinateFormationCount = 0;
        var subordinateCurrentUnitCount = 0;
        var subordinateExpectedUnitCount = 0;
        var subordinateCurrentFieldStrength = 0;
        var subordinateExpectedFieldStrength = 0;
        var commandCasualties = 0;
        var commandCurrentMaximum = 0;
        var commandExpectedMaximum = 0;
        var transferringUnitCount = 0;
        var highestTransferDays = 0;
        var yellow = 0;
        var orange = 0;
        var red = 0;
        var infantry = 0;
        var cavalry = 0;
        var artillery = 0;

        foreach (var child in Children)
        {
            if (child is CombatUnitNode unit)
            {
                if (!unit.IsLandAsset) continue;
                attachedExpectedUnitCount++;
                attachedExpectedFieldStrength += unit.FieldStrength;
                commandCasualties += unit.Casualties;
                commandExpectedMaximum += unit.ConfiguredMaxStrength;
                if (!unit.IsInTransfer)
                {
                    attachedCurrentUnitCount++;
                    attachedCurrentFieldStrength += unit.FieldStrength;
                    commandCurrentMaximum += unit.ConfiguredMaxStrength;
                }
                else
                {
                    transferringUnitCount++;
                    highestTransferDays = Math.Max(highestTransferDays, unit.TransferDays);
                }

                switch (unit.ReadinessStatus)
                {
                    case ReadinessLevel.Yellow: yellow++; break;
                    case ReadinessLevel.Orange: orange++; break;
                    case ReadinessLevel.Red: red++; break;
                }
                switch (unit.UnitType)
                {
                    case 1: cavalry++; break;
                    case 2: artillery++; break;
                    default: infantry++; break;
                }
                continue;
            }

            if (child is not GroupNode group || !group.IsLandCommand) continue;
            var nested = group.Aggregate;
            subordinateFormationCount += 1 + nested.SubordinateFormationCount;
            subordinateCurrentUnitCount += nested.CommandCurrentUnitCount;
            subordinateExpectedUnitCount += nested.CommandExpectedUnitCount;
            subordinateCurrentFieldStrength += nested.CommandCurrentFieldStrength;
            subordinateExpectedFieldStrength += nested.CommandExpectedFieldStrength;
            commandCasualties += nested.CommandCasualties;
            commandCurrentMaximum += nested.CommandCurrentMaximum;
            commandExpectedMaximum += nested.CommandExpectedMaximum;
            transferringUnitCount += nested.TransferringUnitCount;
            highestTransferDays = Math.Max(highestTransferDays, nested.HighestTransferDays);
            yellow += nested.YellowAlertCount;
            orange += nested.OrangeAlertCount;
            red += nested.RedAlertCount;
            infantry += nested.Infantry;
            cavalry += nested.Cavalry;
            artillery += nested.Artillery;
        }

        return new AggregateCache(
            attachedCurrentUnitCount, attachedExpectedUnitCount, attachedCurrentFieldStrength, attachedExpectedFieldStrength,
            subordinateFormationCount, subordinateCurrentUnitCount, subordinateExpectedUnitCount, subordinateCurrentFieldStrength, subordinateExpectedFieldStrength,
            attachedCurrentUnitCount + subordinateCurrentUnitCount,
            attachedExpectedUnitCount + subordinateExpectedUnitCount,
            attachedCurrentFieldStrength + subordinateCurrentFieldStrength,
            attachedExpectedFieldStrength + subordinateExpectedFieldStrength,
            commandCasualties, commandCurrentMaximum, commandExpectedMaximum,
            transferringUnitCount, highestTransferDays, yellow, orange, red, infantry, cavalry, artillery);
    }

    private string BuildAlertTooltip(ReadinessLevel level, string title)
    {
        var units = EnumerateUnits(this).Where(u => u.IsLandAsset && u.ReadinessStatus == level).OrderBy(u => u.StrengthPercentExact).ThenBy(u => u.Name).ToList();
        if (units.Count == 0) return string.Empty;
        var lines = new List<string> { $"{title} — {units.Count:N0} UNIT{(units.Count == 1 ? "" : "S")}" };
        foreach (var unit in units.Take(16))
        {
            lines.Add(string.Empty);
            lines.Add($"{unit.Name} — {unit.StrengthPercent}%");
            lines.Add(unit.CommandPath);
            lines.Add($"Field Strength: {unit.FieldStrength:N0} / {unit.ConfiguredMaxStrength:N0}   Casualties: {unit.Casualties:N0}");
        }
        if (units.Count > 16) lines.Add($"\n…and {units.Count - 16:N0} more.");
        return string.Join("\n", lines);
    }

    private static IEnumerable<CombatUnitNode> EnumerateUnits(OobNode node)
    {
        foreach (var child in node.Children)
        {
            if (child is CombatUnitNode unit) yield return unit;
            else foreach (var nested in EnumerateUnits(child)) yield return nested;
        }
    }

    private static readonly string[] AggregatePropertyNames =
    {
        nameof(AttachedCurrentUnitCount), nameof(AttachedExpectedUnitCount), nameof(AttachedCurrentFieldStrength), nameof(AttachedExpectedFieldStrength),
        nameof(SubordinateFormationCount), nameof(SubordinateCurrentUnitCount), nameof(SubordinateExpectedUnitCount), nameof(SubordinateCurrentFieldStrength), nameof(SubordinateExpectedFieldStrength),
        nameof(CommandCurrentUnitCount), nameof(CommandExpectedUnitCount), nameof(CommandCurrentFieldStrength), nameof(CommandExpectedFieldStrength), nameof(CommandCasualties),
        nameof(CommandCurrentMaximum), nameof(CommandExpectedMaximum), nameof(CurrentStrengthPercent), nameof(ExpectedStrengthPercent), nameof(NatoUnitType),
        nameof(TransferringUnitCount), nameof(HighestTransferDays), nameof(HasTransfers), nameof(TransferSummary),
        nameof(YellowAlertCount), nameof(OrangeAlertCount), nameof(RedAlertCount), nameof(HasYellowAlerts), nameof(HasOrangeAlerts), nameof(HasRedAlerts),
        nameof(YellowAlertDisplay), nameof(OrangeAlertDisplay), nameof(RedAlertDisplay), nameof(YellowAlertTooltip), nameof(OrangeAlertTooltip), nameof(RedAlertTooltip),
        nameof(AttachedExpectedDiffers), nameof(SubordinateExpectedDiffers), nameof(CommandExpectedDiffers), nameof(AttachedExpectedBrush), nameof(SubordinateExpectedBrush), nameof(CommandExpectedBrush)
    };

    private sealed record AggregateCache(
        int AttachedCurrentUnitCount,
        int AttachedExpectedUnitCount,
        int AttachedCurrentFieldStrength,
        int AttachedExpectedFieldStrength,
        int SubordinateFormationCount,
        int SubordinateCurrentUnitCount,
        int SubordinateExpectedUnitCount,
        int SubordinateCurrentFieldStrength,
        int SubordinateExpectedFieldStrength,
        int CommandCurrentUnitCount,
        int CommandExpectedUnitCount,
        int CommandCurrentFieldStrength,
        int CommandExpectedFieldStrength,
        int CommandCasualties,
        int CommandCurrentMaximum,
        int CommandExpectedMaximum,
        int TransferringUnitCount,
        int HighestTransferDays,
        int YellowAlertCount,
        int OrangeAlertCount,
        int RedAlertCount,
        int Infantry,
        int Cavalry,
        int Artillery);
}

public enum ReadinessLevel { Normal, Yellow, Orange, Red }
public enum ContractRiskLevel { None, Warning, Critical }
public enum PathLinkStatus { NotAvailable, Confirmed, Ambiguous, Missing }

public sealed class CombatUnitNode : OobNode
{
    public bool IsLandAsset { get; set; } = true;
    private int _editorOrder = int.MaxValue;
    private int _savedEditorOrder = int.MaxValue;
    public int EditorOrder { get => _editorOrder; set { if (_editorOrder != value) { _editorOrder = value; OnPropertyChanged(); } } }

    public override bool IsGroup => false;
    public int UnitId { get; set; }
    public int ParentId { get; set; }
    public int Nation { get; set; }
    public int UnitType { get; set; }
    public int UnitTier { get; set; }
    public int CommanderId { get; set; }
    public int StateId { get; set; }
    public int WeaponId { get; set; }
    private int? _gunCountOverride;
    public AideDeCamp.Services.ArtilleryRules? ArtilleryCalculation {get;set;}
    public int? GunCount { get=>_gunCountOverride ?? (IsLandAsset && UnitType==2?ArtilleryCalculation?.Count(TotalMenRaw,CasualtyRatioRaw):null); set=>_gunCountOverride=value; }
    public int ContractMonths { get; set; }
    public double ExperienceRaw { get; set; }
    public string EnlistDateRaw { get; set; } = string.Empty;
    public DateTime? EnlistDate { get; set; }
    public DateTime? CampaignDate { get; set; }
    private bool _isBatchSelected;
    private readonly Dictionary<string, string> _originalEditValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _savedEditValues = new(StringComparer.Ordinal);
    public int TotalMenRaw { get; set; }
    public double CasualtyRatioRaw { get; set; }
    public double TransferTimeRaw { get; set; }
    public int RegimentLineStart { get; set; }
    public int? PathNameLineIndex { get; set; }
    public PathLinkStatus PathLinkStatus { get; set; } = PathLinkStatus.NotAvailable;
    public string PathLinkMessage { get; set; } = "paths.dat is not available for this save.";
    public int ConfiguredMaxStrength { get; set; } = 2000;
    public string RaisedText { get; set; } = string.Empty;
    public string CommanderName { get; set; } = string.Empty;
    public string CommanderDisplayName { get; set; } = string.Empty;
    public string HomeStateName { get; set; } = string.Empty;
    public string WeaponName { get; set; } = string.Empty;
    public string CommandPath { get; set; } = string.Empty;
    public string SearchIndex { get; private set; } = string.Empty;
    public string TypeName => UnitType switch { 0 => "Infantry", 1 => "Cavalry", 2 => "Artillery", _ => $"Type {UnitType}" };
    public int PresentationTier => OobPresentation.DisplayTier(UnitTier);
    public string TierName => OobPresentation.TierName(UnitTier, headquarters: false);
    public string TransferMarker => IsInTransfer ? "⇢ IN TRANSFER" : string.Empty;
    public string CardBackground => IsInTransfer ? "#333840" : "#262B33";
    public bool IsBatchSelected => _isBatchSelected;
    public string CardBorder => IsBatchSelected ? "#8EC5FF" : IsInTransfer ? "#737B86" : "#48515D";
    public int? ContractRemainingMonths
    {
        get
        {
            if (CampaignDate is not DateTime game || EnlistDate is not DateTime enlist) return null;
            var elapsed = Math.Max(0, MonthsBetween(enlist, game));
            return Math.Max(0, ContractMonths - elapsed);
        }
    }
    public string ContractRemainingText => ContractRemainingMonths is int remaining ? $"{remaining} mo" : "—";

    public bool TryCalculateContractMonthsForRemaining(int remainingMonths, out int contractMonths)
    {
        contractMonths = ContractMonths;
        if (CampaignDate is not DateTime game || EnlistDate is not DateTime enlist || remainingMonths < 0) return false;
        var elapsed = Math.Max(0, MonthsBetween(enlist, game));
        var total = (long)elapsed + remainingMonths;
        if (total < 0 || total > int.MaxValue) return false;
        contractMonths = (int)total;
        return true;
    }

    public bool TrySetContractRemaining(int remainingMonths)
    {
        if (!TryCalculateContractMonthsForRemaining(remainingMonths, out var contractMonths)) return false;
        ContractMonths = contractMonths;
        OnPropertyChanged(nameof(ContractMonths));
        OnPropertyChanged(nameof(ContractRemainingMonths));
        OnPropertyChanged(nameof(ContractRemainingText));
        return true;
    }

    public void CaptureOriginalState()
    {
        _originalEditValues.Clear();
        _savedEditValues.Clear();
        foreach (var field in TrackedEditFields)
        {
            var value = GetTrackedValue(field);
            _originalEditValues[field] = value;
            _savedEditValues[field] = value;
        }
        _savedEditorOrder = EditorOrder;
    }

    public void MarkSavedState()
    {
        foreach (var field in TrackedEditFields) _savedEditValues[field] = GetTrackedValue(field);
        _savedEditorOrder = EditorOrder;
    }

    public EditState GetEditState(string field)
    {
        if (_originalEditValues.Count == 0) return EditState.Unchanged;
        var current = GetTrackedValue(field);
        var saved = _savedEditValues.GetValueOrDefault(field, current);
        var original = _originalEditValues.GetValueOrDefault(field, current);
        if (!string.Equals(current, saved, StringComparison.Ordinal)) return EditState.Unsaved;
        if (!string.Equals(current, original, StringComparison.Ordinal)) return EditState.SavedThisSession;
        return EditState.Unchanged;
    }

    public string GetEditTooltip(string field, string label)
    {
        if (_originalEditValues.Count == 0) return string.Empty;
        var originalRaw = _originalEditValues.GetValueOrDefault(field, string.Empty);
        var savedRaw = _savedEditValues.GetValueOrDefault(field, originalRaw);
        var currentRaw = GetTrackedValue(field);
        var original = DisplayTrackedValue(field, originalRaw);
        var saved = DisplayTrackedValue(field, savedRaw);
        var current = DisplayTrackedValue(field, currentRaw);
        var state = GetEditState(field);
        if (state == EditState.Unchanged) return string.Empty;
        return state == EditState.Unsaved
            ? $"{label} — UNSAVED\nOriginal: {original}\nLast saved: {saved}\nCurrent: {current}"
            : $"{label} — SAVED THIS SESSION\nOriginal: {original}\nSaved/current: {current}";
    }

    public string OriginalValue(string field) => DisplayTrackedValue(field, _originalEditValues.GetValueOrDefault(field, GetTrackedValue(field)));

    private static string DisplayTrackedValue(string field, string raw)
    {
        if ((field == "Weapon" || field == "Parent" || field == "HomeState") && raw.Contains('|')) return raw[(raw.IndexOf('|') + 1)..];
        return raw;
    }

    public static readonly string[] TrackedEditFields =
    {
        "Name", "HomeState", "FieldStrength", "Casualties", "Weapon", "EnlistDate", "Contract", "ContractRemaining", "Experience", "ETA", "Parent"
    };

    public bool HasUnsavedChange(string field) => GetEditState(field) == EditState.Unsaved;
    public bool HasUnsavedOrder => EditorOrder != _savedEditorOrder;
    public bool HasAnyUnsavedChanges => TrackedEditFields.Any(HasUnsavedChange) || HasUnsavedOrder;

    private string GetTrackedValue(string field) => field switch
    {
        "Name" => Name,
        "HomeState" => $"{StateId}|{HomeStateName}",
        "FieldStrength" => FieldStrength.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "Casualties" => Casualties.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "Weapon" => $"{WeaponId}|{WeaponName}",
        "EnlistDate" => EnlistDateRaw,
        "Contract" => ContractMonths.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "ContractRemaining" => ContractRemainingMonths?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—",
        "Experience" => ExperienceRaw.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        "ETA" => TransferTimeRaw.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        "Parent" => $"{ParentId}|{CommandPath}",
        _ => string.Empty
    };

    public void SetBatchSelected(bool selected)
    {
        if (_isBatchSelected == selected) return;
        _isBatchSelected = selected;
        OnPropertyChanged(nameof(IsBatchSelected));
        OnPropertyChanged(nameof(CardBorder));
    }

    private static int MonthsBetween(DateTime from, DateTime to)
    {
        if (to <= from) return 0;
        var months = (to.Year - from.Year) * 12 + to.Month - from.Month;
        if (to.Day < from.Day) months--;
        return Math.Max(0, months);
    }

    public int Casualties
    {
        get
        {
            var total = Math.Max(0, TotalMenRaw);
            var ratio = double.IsFinite(CasualtyRatioRaw) ? Math.Clamp(CasualtyRatioRaw, 0.0, 100.0) : 0.0;
            var calculated = Math.Ceiling(total * ratio / 100.0 - 0.000001);
            return calculated >= int.MaxValue ? int.MaxValue : Math.Max(0, (int)calculated);
        }
        set
        {
            value = Math.Max(0, value);
            var field = FieldStrength;
            TotalMenRaw = field + value;
            CasualtyRatioRaw = TotalMenRaw == 0 ? 0 : value * 100.0 / TotalMenRaw;
            OnAllStrengthChanged();
        }
    }
    public int FieldStrength
    {
        get => Math.Max(0, TotalMenRaw - Casualties);
        set
        {
            value = Math.Max(0, value);
            var casualties = Casualties;
            TotalMenRaw = value + casualties;
            CasualtyRatioRaw = TotalMenRaw == 0 ? 0 : casualties * 100.0 / TotalMenRaw;
            OnAllStrengthChanged();
        }
    }

    public bool TrySetStrengthComponents(int fieldStrength, int casualties)
    {
        if (fieldStrength < 0 || casualties < 0) return false;
        var total = (long)fieldStrength + casualties;
        if (total > int.MaxValue) return false;
        TotalMenRaw = (int)total;
        CasualtyRatioRaw = TotalMenRaw == 0 ? 0 : casualties * 100.0 / TotalMenRaw;
        OnAllStrengthChanged();
        return true;
    }
    public int TotalStrength => TotalMenRaw;
    public double StrengthPercentExact => ConfiguredMaxStrength <= 0 ? 0 : FieldStrength * 100.0 / ConfiguredMaxStrength;
    public int StrengthPercent => (int)Math.Round(StrengthPercentExact);
    public ReadinessLevel ReadinessStatus => StrengthPercentExact <= 30.0 ? ReadinessLevel.Red : StrengthPercentExact < 40.0 ? ReadinessLevel.Orange : StrengthPercentExact < 50.0 ? ReadinessLevel.Yellow : ReadinessLevel.Normal;
    public string ReadinessIcon => ReadinessStatus switch { ReadinessLevel.Red => "●", ReadinessLevel.Orange => "●", ReadinessLevel.Yellow => "●", _ => string.Empty };
    public string ReadinessBrush => ReadinessStatus switch { ReadinessLevel.Red => "#F05D5E", ReadinessLevel.Orange => "#F29E4C", ReadinessLevel.Yellow => "#E9C46A", _ => "#66717E" };
    public string ReadinessLabel => ReadinessStatus == ReadinessLevel.Normal ? $"{StrengthPercent}%" : $"{ReadinessIcon} {StrengthPercent}%";

    public int TransferDays
    {
        get
        {
            if (!double.IsFinite(TransferTimeRaw) || TransferTimeRaw <= 0) return 0;
            var days = Math.Ceiling(TransferTimeRaw - 0.000001);
            return days >= int.MaxValue ? int.MaxValue : Math.Max(0, (int)days);
        }
        set
        {
            TransferTimeRaw = Math.Max(0, value);
            OnPropertyChanged(); OnPropertyChanged(nameof(IsInTransfer)); OnPropertyChanged(nameof(TransferMarker)); OnPropertyChanged(nameof(CardBackground)); OnPropertyChanged(nameof(CardBorder));
        }
    }
    public bool IsInTransfer => TransferDays > 0;

    public ContractRiskLevel ContractRisk => ContractRemainingMonths is not int remaining || ContractMonths <= 0
        ? ContractRiskLevel.None
        : remaining <= ContractMonths / 4.0 ? ContractRiskLevel.Critical
        : remaining <= ContractMonths / 2.0 ? ContractRiskLevel.Warning
        : ContractRiskLevel.None;
    public string ContractRiskBrush => ContractRisk switch { ContractRiskLevel.Critical => "#F05D5E", ContractRiskLevel.Warning => "#F29E4C", _ => "#9AA6B1" };
    public string ContractRiskLabel => ContractRemainingMonths is not int remaining ? "Contract: —" : ContractRisk switch
    {
        ContractRiskLevel.Critical => $"Contract: {remaining} mo — expiring",
        ContractRiskLevel.Warning => $"Contract: {remaining} mo — halfway",
        _ => $"Contract: {remaining} mo"
    };

    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(Metrics)); OnPropertyChanged(nameof(CardMetrics));
        OnPropertyChanged(nameof(IdentityCommander)); OnPropertyChanged(nameof(IdentitySecondary));
        SearchIndex = string.Join("\u001F", Name, TypeName, HomeStateName, CommandPath, CommanderName, WeaponName, RaisedText, ContractRemainingText);
        foreach (var name in new[] { nameof(Name), nameof(FieldStrength), nameof(Casualties), nameof(TotalStrength), nameof(StrengthPercentExact), nameof(StrengthPercent), nameof(ReadinessStatus), nameof(ReadinessIcon), nameof(ReadinessBrush), nameof(ReadinessLabel), nameof(TransferDays), nameof(IsInTransfer), nameof(TransferMarker), nameof(CardBackground), nameof(CardBorder), nameof(HomeStateName), nameof(WeaponName), nameof(TypeName), nameof(TierName), nameof(CommandPath), nameof(ContractMonths), nameof(ContractRemainingMonths), nameof(ContractRemainingText), nameof(ContractRisk), nameof(ContractRiskBrush), nameof(ContractRiskLabel), nameof(ExperienceRaw), nameof(IsBatchSelected), nameof(SearchIndex), nameof(PathLinkStatus), nameof(PathLinkMessage), nameof(CommanderDisplayName) }) OnPropertyChanged(name);
    }
    private void OnAllStrengthChanged()
    {
        foreach (var name in new[] { nameof(FieldStrength), nameof(Casualties), nameof(TotalStrength), nameof(StrengthPercentExact), nameof(StrengthPercent), nameof(ReadinessStatus), nameof(ReadinessIcon), nameof(ReadinessBrush), nameof(ReadinessLabel) }) OnPropertyChanged(name);
    }
}

public enum EditState { Unchanged, Unsaved, SavedThisSession }

public sealed record WeaponOption(int Id, string Name, int? UnitType = null, int? WeaponClass = null)
{
    public bool IsCompatibleWith(int unitType) => UnitType is null || UnitType == unitType;
    public string CompatibilityName => UnitType switch { 0 => "Infantry", 1 => "Cavalry", 2 => "Artillery", _ => "Unknown" };
    public override string ToString() => Name;
}
public sealed record StateOption(int Id, string Name, string Abbreviation) { public override string ToString() => Name; }

public sealed record ReadinessAlertItem(CombatUnitNode Unit)
{
    public string Name => Unit.Name;
    public int Percent => Unit.StrengthPercent;
    public string Path => Unit.CommandPath;
    public string Icon => Unit.ReadinessIcon;
    public string Brush => Unit.ReadinessBrush;
    public string Summary => $"{Unit.Name}   {Unit.StrengthPercent}%";
}

internal static class EnumerableExtensions
{
    public static int Max<T>(this IEnumerable<T> source, Func<T, int> selector, int defaultValue)
    {
        var any = false; var max = defaultValue;
        foreach (var item in source) { var value = selector(item); if (!any || value > max) max = value; any = true; }
        return any ? max : defaultValue;
    }
}
