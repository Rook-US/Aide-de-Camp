using AideDeCamp.Models;

namespace AideDeCamp.Services;

public enum ValidationSeverity { Valid, Warning, Error }
public enum WeaponCompatibility { Compatible, Incompatible, Unknown }

public sealed record ValidationResult(ValidationSeverity Severity, string Message)
{
    public static ValidationResult Valid() => new(ValidationSeverity.Valid, string.Empty);
    public static ValidationResult Warning(string message) => new(ValidationSeverity.Warning, message);
    public static ValidationResult Error(string message) => new(ValidationSeverity.Error, message);
}

/// <summary>
/// One source of truth for edit-safety rules shared by detail editing and batch editing.
/// Warnings are advisory; structural errors are blocked.
/// </summary>
public sealed class EditValidationService
{
    private readonly WeaponCompatibilityService _weapons = new();
    private double? _observedExperienceMin;
    private double? _observedExperienceMax;

    public void SetObservedExperienceRange(IEnumerable<CombatUnitNode> units)
    {
        var values = units.Select(u => u.ExperienceRaw).Where(double.IsFinite).ToList();
        _observedExperienceMin = values.Count == 0 ? null : values.Min();
        _observedExperienceMax = values.Count == 0 ? null : values.Max();
    }

    public ValidationResult ValidateName(string value)
    {
        if (value.IndexOf('\0') >= 0 || value.Contains('\r') || value.Contains('\n'))
            return ValidationResult.Error("Unit names cannot contain NUL or line-break characters because the save format is line-oriented.");
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Warning("The unit name is blank. Grand Tactician may display this poorly, but the editor will allow it.");
        return ValidationResult.Valid();
    }

    public ValidationResult ValidateStrength(int fieldStrength, int casualties, int configuredMaximum)
    {
        if (fieldStrength < 0 || casualties < 0) return ValidationResult.Error("Field Strength and Casualties must be zero or greater.");
        if (configuredMaximum > 0 && fieldStrength > configuredMaximum)
            return ValidationResult.Warning($"Field Strength {fieldStrength:N0} is above the configured unit maximum of {configuredMaximum:N0}. This is unusual but can be applied intentionally.");
        return ValidationResult.Valid();
    }

    public ValidationResult ValidateExperience(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > 100)
            return ValidationResult.Error("Experience must be between 0 and 100.");
        return ValidationResult.Valid();
    }

    public ValidationResult ValidateTransfer(CombatUnitNode unit, int days)
    {
        if (days < 0) return ValidationResult.Error("Transfer ETA must be zero or greater.");
        return unit.PathLinkStatus == PathLinkStatus.Confirmed
            ? ValidationResult.Valid()
            : ValidationResult.Error($"Transfer ETA cannot be safely written for {unit.Name}: {unit.PathLinkMessage}");
    }

    public WeaponCompatibility GetWeaponCompatibility(CombatUnitNode unit, WeaponOption weapon)
    {
        return _weapons.Evaluate(unit, weapon);
    }

    public ValidationResult ValidateWeapon(CombatUnitNode unit, WeaponOption weapon, bool allowUnknown)
    {
        return GetWeaponCompatibility(unit, weapon) switch
        {
            WeaponCompatibility.Compatible => ValidationResult.Valid(),
            WeaponCompatibility.Incompatible => ValidationResult.Error($"{weapon.Name} is {weapon.CompatibilityName}-only and is incompatible with {unit.Name} ({unit.TypeName})."),
            _ when allowUnknown => ValidationResult.Warning($"Compatibility for {weapon.Name} is not identified by the loaded weapon data. Applying by explicit override."),
            _ => ValidationResult.Error($"Compatibility for {weapon.Name} is unknown. Use the explicit override only if you intend to force the assignment.")
        };
    }
}
