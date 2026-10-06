using AideDeCamp.Models;

namespace AideDeCamp.Services;

public sealed class SelectionEditPlan
{
    public List<(CombatUnitNode Unit, CombatUnitNode Candidate)> Changes { get; } = new();
    public List<string> Errors { get; } = new();
    public List<string> Details { get; } = new();
    public string Tooltip => string.Join("\n", Errors.Concat(Details));

    public static SelectionEditPlan Build(IEnumerable<CombatUnitNode> units,
        Func<CombatUnitNode, IReadOnlyDictionary<string,string>> fields,
        IReadOnlyList<WeaponOption> weapons, IReadOnlyList<StateOption> states, EditValidationService validation)
    {
        var plan=new SelectionEditPlan();
        foreach(var unit in units) {
            var edits=fields(unit); if(edits.Count==0)continue;
            var draft=new TypedUnitEdit(unit);
            foreach(var entry in edits) {
                if(!draft.Fields.TryGetValue(entry.Key,out var field)){plan.Errors.Add(unit.Name+": unsupported field "+entry.Key);continue;}
                field.Text=entry.Value;
            }
            var candidate=draft.Validate(weapons,states,validation);
            candidate.ArtilleryCalculation=unit.ArtilleryCalculation;
            // An unrelated legacy field must not block or be normalized by a targeted edit.
            var changed=draft.Fields.Values.Where(f=>f.Dirty).ToArray();
            var keys=changed.Select(f=>f.Key).ToHashSet();
            if(!keys.Contains("Name"))candidate.Name=unit.Name;
            if(!keys.Overlaps(new[]{"FieldStrength","Casualties"})){candidate.TotalMenRaw=unit.TotalMenRaw;candidate.CasualtyRatioRaw=unit.CasualtyRatioRaw;}
            if(!keys.Contains("Weapon")){candidate.WeaponId=unit.WeaponId;candidate.WeaponName=unit.WeaponName;}
            if(!keys.Contains("HomeState")){candidate.StateId=unit.StateId;candidate.HomeStateName=unit.HomeStateName;}
            if(!keys.Contains("EnlistDate")){candidate.EnlistDate=unit.EnlistDate;candidate.EnlistDateRaw=unit.EnlistDateRaw;}
            if(!keys.Overlaps(new[]{"Contract","ContractRemaining"}))candidate.ContractMonths=unit.ContractMonths;
            if(!keys.Contains("Experience"))candidate.ExperienceRaw=unit.ExperienceRaw;
            if(!keys.Contains("ETA"))candidate.TransferTimeRaw=unit.TransferTimeRaw;
            foreach(var field in changed.Where(f=>f.Result.Severity==ValidationSeverity.Error))plan.Errors.Add(unit.Name+" — "+field.Label+": "+field.Result.Message);
            if(changed.Length==0 || UnitEditSnapshot.Capture(candidate)==UnitEditSnapshot.Capture(unit))continue;
            if(changed.Any(f=>f.Key=="Weapon")) {
                var weapon=weapons.FirstOrDefault(w=>w.Id==candidate.WeaponId);
                if(weapon is not null && validation.GetWeaponCompatibility(unit,weapon)!=WeaponCompatibility.Compatible)
                    plan.Errors.Add(unit.Name+": choose a weapon compatible with "+unit.TypeName+".");
            }
            plan.Changes.Add((unit,candidate));
            plan.Details.Add(unit.Name+" ("+unit.TypeName+")");
            foreach(var field in changed) {
                plan.Details.Add("  "+field.Label+": "+field.Baseline+" → "+TypedUnitEdit.Read(candidate,field.Key));
                if(field.Result.Severity==ValidationSeverity.Warning)plan.Details.Add("  Warning: "+field.Result.Message);
            }
            if(changed.Any(f=>f.Key is "FieldStrength" or "Casualties"))
                plan.Details.Add($"  Maximum: {unit.ConfiguredMaxStrength:N0}"+(unit.UnitType==2?$"; estimated guns: {candidate.GunCount?.ToString()??"unavailable"}":""));
        }
        return plan;
    }
    public void Apply() {
        if(Errors.Count>0)throw new InvalidOperationException("Resolve the preview errors before committing.");
        foreach(var (unit,candidate) in Changes){UnitEditSnapshot.Capture(candidate).ApplyTo(unit);unit.RefreshDisplay();}
    }
}
