using AideDeCamp.Models;

namespace AideDeCamp.Services;

public sealed class WeaponCompatibilityService
{
    public WeaponCompatibility Evaluate(CombatUnitNode unit, WeaponOption weapon)
    {
        if (unit.UnitType is < 0 or > 2 || weapon.UnitType is null or < 0 or > 2) return WeaponCompatibility.Unknown;
        return weapon.UnitType == unit.UnitType ? WeaponCompatibility.Compatible : WeaponCompatibility.Incompatible;
    }
}
