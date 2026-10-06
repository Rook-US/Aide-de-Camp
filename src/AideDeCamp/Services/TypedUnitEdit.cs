using System.ComponentModel;
using System.Globalization;
using AideDeCamp.Models;

namespace AideDeCamp.Services;

// Text drafts are separate from the numeric game model. Invalid text never reaches serialization.
public sealed class TypedField : INotifyPropertyChanged
{
    private string _text;
    public string Key { get; }
    public string Label { get; }
    public string Baseline { get; private set; }
    public string Text { get => _text; set { if (_text == value) return; _text = value; Changed?.Invoke(); Notify(); } }
    public bool Dirty => Text != Baseline;
    public ValidationResult Result { get; private set; } = ValidationResult.Valid();
    public string NormalBackground { get; set; } = "#162833";
    public string Background => Result.Severity == ValidationSeverity.Valid ? Dirty ? "#4A3B22" : NormalBackground : "#51204E";
    public string Border => Result.Severity == ValidationSeverity.Valid ? "#6F8596" : "#FF49DF";
    public string NormalForeground { get; set; } = "#E8EDF2";
    public string Foreground => Result.Severity == ValidationSeverity.Valid ? NormalForeground : "#E8EDF2";
    public string Help => Result.Severity == ValidationSeverity.Valid ? Label : $"{Result.Severity}: {Result.Message}";
    public Action? Changed { get; set; }
    public TypedField(string key, string label, string text) { Key = key; Label = label; _text = Baseline = text; }
    public void SetResult(ValidationResult value) { Result = value; Notify(); }
    public void Sync(string text) { if (!Dirty) { _text = Baseline = text; Notify(); } }
    public void Accept(string text) { _text = Baseline = text; Notify(); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify() { foreach (var p in new[] { "Text", "Dirty", "Background", "Border", "Foreground", "Help" }) PropertyChanged?.Invoke(this, new(p)); }
}

public sealed class TypedUnitEdit
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static readonly (string Key, string Label)[] Definitions = {
        ("Name", "Name"), ("FieldStrength", "Field Strength"), ("Casualties", "Casualties"),
        ("Weapon", "Weapon (name or ID)"), ("HomeState", "Home State (name or ID)"),
        ("Experience", "Experience (0–100)"), ("Contract", "Contract Length (months)"),
        ("EnlistDate", "Enlistment Start (yyyy-MM-dd)"), ("ContractRemaining", "Remaining Contract (months)"), ("ETA", "Transfer Days") };
    public CombatUnitNode Unit { get; }
    public Dictionary<string, TypedField> Fields { get; }
    public bool Dirty => Fields.Values.Any(f => f.Dirty);
    public TypedUnitEdit(CombatUnitNode unit)
    {
        Unit = unit;
        Fields = Definitions.ToDictionary(d => d.Key, d => new TypedField(d.Key, d.Label, Read(unit, d.Key)));
    }
    public void Sync() { foreach (var f in Fields.Values) f.Sync(Read(Unit, f.Key)); }
    public static string Read(CombatUnitNode u, string key) => key switch {
        "Name" => u.Name, "FieldStrength" => u.FieldStrength.ToString(Inv), "Casualties" => u.Casualties.ToString(Inv),
        "Weapon" => string.IsNullOrWhiteSpace(u.WeaponName) ? u.WeaponId.ToString(Inv) : u.WeaponName,
        "HomeState" => string.IsNullOrWhiteSpace(u.HomeStateName) ? u.StateId.ToString(Inv) : u.HomeStateName,
        "Experience" => u.ExperienceRaw.ToString("R", Inv), "Contract" => u.ContractMonths.ToString(Inv),
        "EnlistDate" => u.EnlistDateRaw, "ContractRemaining" => u.ContractRemainingMonths?.ToString(Inv) ?? "",
        "ETA" => u.TransferTimeRaw.ToString("R", Inv), _ => "" };
    public CombatUnitNode Validate(IReadOnlyList<WeaponOption> weapons, IReadOnlyList<StateOption> states, EditValidationService validation)
    {
        var candidate = new CombatUnitNode { UnitType = Unit.UnitType, UnitTier = Unit.UnitTier,
            ConfiguredMaxStrength = Unit.ConfiguredMaxStrength, CampaignDate = Unit.CampaignDate,
            PathLinkStatus = Unit.PathLinkStatus, PathLinkMessage = Unit.PathLinkMessage };
        UnitEditSnapshot.Capture(Unit).ApplyTo(candidate);
        foreach (var f in Fields.Values) f.SetResult(ValidationResult.Valid());
        void Error(string k, string m) => Fields[k].SetResult(ValidationResult.Error(m));
        void Warn(string k, string m) => Fields[k].SetResult(ValidationResult.Warning(m));
        bool Int(string k, out int n) {
            if (int.TryParse(Fields[k].Text, NumberStyles.Integer, Inv, out n) && n >= 0) return true;
            Error(k, "Enter a non-negative whole number within the save format's integer range."); return false;
        }
        candidate.Name = Fields["Name"].Text;
        Fields["Name"].SetResult(validation.ValidateName(candidate.Name));
        var fieldOk = Int("FieldStrength", out var field); var casOk = Int("Casualties", out var cas);
        if (fieldOk && casOk) {
            if (!candidate.TrySetStrengthComponents(field, cas)) { Error("FieldStrength", "Combined strength exceeds the save format's limit."); Error("Casualties", "Combined strength exceeds the save format's limit."); }
            else Fields["FieldStrength"].SetResult(validation.ValidateStrength(field, cas, candidate.ConfiguredMaxStrength));
        }
        if (!Fields["FieldStrength"].Dirty && !Fields["Casualties"].Dirty && (Unit.TotalMenRaw < 0 || Unit.CasualtyRatioRaw < 0 || Unit.CasualtyRatioRaw > 100)) {
            Error("FieldStrength", "Loaded raw manpower/casualty data is invalid; enter corrected field strength and casualties.");
            Error("Casualties", "Loaded raw manpower/casualty data is invalid.");
        }
        if (double.TryParse(Fields["Experience"].Text, NumberStyles.Float, Inv, out var xp) && double.IsFinite(xp) && xp >= 0) {
            candidate.ExperienceRaw = xp; Fields["Experience"].SetResult(validation.ValidateExperience(xp));
        } else Error("Experience", "Enter a finite, non-negative number. Letters, NaN and infinity are invalid.");
        if (Int("Contract", out var months)) { candidate.ContractMonths = months; if (months == 0) Warn("Contract", "Zero-length contract: confirm this is intentional."); }
        var dateText = Fields["EnlistDate"].Text;
        if (DateTime.TryParseExact(dateText.Trim(), new[] { "yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy", "M-d-yyyy", "MM-dd-yyyy", "d.M.yyyy", "dd.MM.yyyy" }, Inv, DateTimeStyles.None, out var date)) {
            candidate.EnlistDate = date;
            if (Fields["EnlistDate"].Dirty) candidate.EnlistDateRaw = date.ToString("M/d/yyyy", Inv);
            if (Unit.CampaignDate is DateTime now && date > now) Warn("EnlistDate", "Start date is later than the campaign date.");
        } else if (!Fields["EnlistDate"].Dirty && Unit.EnlistDate is not null) { /* preserve known legacy date formats */ }
        else if (!Fields["EnlistDate"].Dirty && string.IsNullOrWhiteSpace(dateText)) Warn("EnlistDate", "Start date is absent; remaining contract cannot be calculated.");
        else Error("EnlistDate", "Enter a real calendar date, preferably yyyy-MM-dd.");
        if (Fields["ContractRemaining"].Dirty) {
            if (Fields["Contract"].Dirty) Error("ContractRemaining", "Edit either total contract length or remaining months, not both in one draft.");
            else if (Int("ContractRemaining", out var remaining)) {
                if (Fields["EnlistDate"].Result.Severity == ValidationSeverity.Error || !candidate.TryCalculateContractMonthsForRemaining(remaining, out var total)) Error("ContractRemaining", "Valid start and campaign dates are required to calculate contract length.");
                else candidate.ContractMonths = total;
            }
        }
        if (double.TryParse(Fields["ETA"].Text, NumberStyles.Float, Inv, out var eta) && double.IsFinite(eta) && eta >= 0 && eta <= int.MaxValue) {
            candidate.TransferTimeRaw = eta;
            if (eta != Unit.TransferTimeRaw && Unit.PathLinkStatus != PathLinkStatus.Confirmed) Error("ETA", "No confirmed paths.dat link. Both ETA records must be updated safely.");
        } else Error("ETA", "Enter finite, non-negative days within the supported range.");
        var wtext = Fields["Weapon"].Text.Trim();
        var matches = weapons.Where(w => !Fields["Weapon"].Dirty ? w.Id == Unit.WeaponId : string.Equals(w.Name, wtext, StringComparison.OrdinalIgnoreCase) || w.Id.ToString(Inv) == wtext).ToList();
        if (matches.Count == 1) {
            candidate.WeaponId = matches[0].Id; candidate.WeaponName = matches[0].Name;
            var compatible = validation.GetWeaponCompatibility(candidate, matches[0]);
            if (compatible != WeaponCompatibility.Compatible) Warn("Weapon", compatible == WeaponCompatibility.Incompatible ? $"{matches[0].Name} is not intended for {Unit.TypeName}." : "Weapon compatibility is unknown.");
        } else if (!Fields["Weapon"].Dirty && matches.Count == 0) Warn("Weapon", "Current weapon is absent from loaded configuration; preserved unchanged.");
        else Error("Weapon", "Use a unique known weapon name or ID.");
        var stext = Fields["HomeState"].Text.Trim();
        var smatches = states.Where(s => !Fields["HomeState"].Dirty ? s.Id == Unit.StateId : string.Equals(s.Name, stext, StringComparison.OrdinalIgnoreCase) || s.Id.ToString(Inv) == stext).ToList();
        if (smatches.Count == 1) { candidate.StateId = smatches[0].Id; candidate.HomeStateName = smatches[0].Name; }
        else if (!Fields["HomeState"].Dirty && smatches.Count == 0) Warn("HomeState", "Current state ID is unresolved; preserved unchanged.");
        else Error("HomeState", "Use a unique known state name or ID.");
        return candidate;
    }
    public void Apply(CombatUnitNode candidate) {
        UnitEditSnapshot.Capture(candidate).ApplyTo(Unit);
        foreach (var f in Fields.Values) f.Accept(Read(Unit, f.Key));
    }
}
