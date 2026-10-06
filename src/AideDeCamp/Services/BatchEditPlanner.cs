using AideDeCamp.Models;

namespace AideDeCamp.Services;

/// <summary>
/// Builds the exact per-unit execution plan used both by the batch preview and apply.
/// This prevents preview counts from disagreeing with what the editor actually changes.
/// </summary>
public sealed class BatchEditPlanner
{
    private readonly EditValidationService _validation;

    public BatchEditPlanner(EditValidationService validation) => _validation = validation;

    public BatchEditPlan Build(BatchEditRequest request, IReadOnlyList<CombatUnitNode> units)
    {
        var plan = new BatchEditPlan { Request = request };
        foreach (var unit in units)
        {
            var item = new BatchUnitPlan { Unit = unit };

            if (request.EtaDays is int eta && eta != unit.TransferDays)
            {
                var result = _validation.ValidateTransfer(unit, eta);
                if (result.Severity == ValidationSeverity.Error) item.Skips.Add($"ETA: {result.Message}");
                else item.EtaDays = eta;
            }

            if (request.ContractMonths is int contract && contract != unit.ContractMonths)
                item.ContractMonths = contract;

            if (request.ContractRemainingMonths is int remaining)
            {
                if (!unit.TryCalculateContractMonthsForRemaining(remaining, out var calculated))
                    item.Skips.Add($"Contract Remaining: enlistment date '{unit.EnlistDateRaw}' could not be converted safely.");
                else if (calculated != unit.ContractMonths)
                    item.ContractMonths = calculated;
            }

            if (request.Experience is double experience && experience != unit.ExperienceRaw)
            {
                var result = _validation.ValidateExperience(experience);
                if (result.Severity == ValidationSeverity.Error) item.Skips.Add($"Experience: {result.Message}");
                else
                {
                    item.Experience = experience;
                    if (result.Severity == ValidationSeverity.Warning) item.Warnings.Add(result.Message);
                }
            }

            if (request.Weapon is WeaponOption weapon && weapon.Id != unit.WeaponId)
            {
                var result = _validation.ValidateWeapon(unit, weapon, request.AllowUnknownWeaponCompatibility);
                if (result.Severity == ValidationSeverity.Error) item.Skips.Add($"Weapon: {result.Message}");
                else
                {
                    item.Weapon = weapon;
                    if (result.Severity == ValidationSeverity.Warning) item.Warnings.Add(result.Message);
                }
            }

            plan.Units.Add(item);
        }
        return plan;
    }

    public void Apply(BatchEditPlan plan)
    {
        foreach (var item in plan.Units.Where(x => x.HasChange))
        {
            if (item.EtaDays is int eta) item.Unit.TransferDays = eta;
            if (item.ContractMonths is int contract) item.Unit.ContractMonths = contract;
            if (item.Experience is double experience) item.Unit.ExperienceRaw = experience;
            if (item.Weapon is WeaponOption weapon)
            {
                item.Unit.WeaponId = weapon.Id;
                item.Unit.WeaponName = weapon.Name;
            }
            item.Unit.RefreshDisplay();
        }
    }
}
