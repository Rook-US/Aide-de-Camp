namespace AideDeCamp.Services;

// Column identity is shared by the display template, editor, and context menu.
public static class RosterFields
{
    public static string? FromColumn(string? path) => path switch {
        "Name" => "Name", "HomeStateName" => "HomeState", "WeaponName" => "Weapon",
        "FieldStrength" => "FieldStrength", "Casualties" => "Casualties",
        "ContractMonths" => "Contract", "ContractRemaining" => "ContractRemaining",
        "Experience" => "Experience", "ETA" => "ETA", _ => null
    };
    public static bool IsSupported(string? key) => key is "Name" or "HomeState" or "Weapon"
        or "FieldStrength" or "Casualties" or "Contract" or "ContractRemaining" or "Experience" or "ETA";
}
