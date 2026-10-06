using System.Text.Json.Serialization;

namespace AideDeCamp.Models;

public enum NamingTokenKind
{
    Number,
    HomeState,
    UnitType,
    UnitTier,
    CustomText
}

public enum NumberStyle
{
    EnglishOrdinal,
    Cardinal,
    Roman,
    OrdinalDot
}

public enum StateStyle
{
    Abbreviation,
    FullName
}

public enum TextCaseStyle
{
    Preserve,
    Upper,
    Lower,
    Title
}

public sealed class NamingToken
{
    public NamingTokenKind Kind { get; set; }
    public string Text { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayLabel => Kind switch
    {
        NamingTokenKind.Number => "Number",
        NamingTokenKind.HomeState => "Home State",
        NamingTokenKind.UnitType => "Unit Type",
        NamingTokenKind.UnitTier => "Unit Tier",
        NamingTokenKind.CustomText => $"Text: {ShowWhitespace(Text)}",
        _ => Kind.ToString()
    };

    private static string ShowWhitespace(string text) => text.Replace(" ", "·").Replace("\t", "⇥");
}

public sealed class SpecialNameEntry
{
    public string NumberText { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public enum NamingFaction { Current, Union, Confederacy, Both }

public sealed class NamingRule
{
    public string Name { get; set; } = "New Naming Rule";
    public NamingFaction Faction { get; set; } = NamingFaction.Current;
    public string WeaponInclude { get; set; } = "";
    public string WeaponExclude { get; set; } = "";
    public string StateInclude { get; set; } = "";
    public string StateExclude { get; set; } = "";
    public string NameContains { get; set; } = "";
    public string NameExcludes { get; set; } = "";
    public string CommandScope { get; set; } = "Land";

    public int? UnitType { get; set; }
    public int? UnitTier { get; set; }
    public bool RenumberWithGaps { get; set; }
    public NumberStyle NumberStyle { get; set; } = NumberStyle.EnglishOrdinal;
    public StateStyle StateStyle { get; set; } = StateStyle.Abbreviation;
    public TextCaseStyle CaseStyle { get; set; } = TextCaseStyle.Preserve;
    public string UnitTypeOverride { get; set; } = string.Empty;
    public string UnitTierOverride { get; set; } = string.Empty;
    public string SkippedNumbersText { get; set; } = string.Empty;
    public List<NamingToken> Tokens { get; set; } = new()
    {
        new NamingToken { Kind = NamingTokenKind.Number },
        new NamingToken { Kind = NamingTokenKind.CustomText, Text = " " },
        new NamingToken { Kind = NamingTokenKind.HomeState }
    };
    public List<SpecialNameEntry> SpecialNames { get; set; } = new();

    public override string ToString() => Name;
}

public sealed class NamingSettings
{
    public List<NamingRule> Rules { get; set; } = new();
}

public sealed record NamingUnitTypeOption(int? Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record NamingTierOption(int? Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record RenamePreviewItem(CombatUnitNode Unit, string OldName, string NewName, int AssignedNumber)
{
    public string TypeName => Unit.TypeName;
    public string TierName => Unit.TierName;
    public string HomeState => Unit.HomeStateName;
    public string Raised => Unit.RaisedText;
}
