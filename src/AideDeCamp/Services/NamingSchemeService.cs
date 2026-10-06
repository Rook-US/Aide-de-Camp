using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AideDeCamp.Models;

namespace AideDeCamp.Services;

public sealed class NamingSchemeService
{
    private static readonly Regex LeadingNumberRegex = new(@"^\s*(\d+)(?:st|nd|rd|th|\.)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RomanRegex = new(@"^\s*([IVXLCDM]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int CurrentNation { get; set; }
    public Func<CombatUnitNode, CommandCategory>? Classify { get; set; }
    public List<string> Diagnostics { get; } = new();
    public string SettingsPath { get; }
    public NamingSettings Settings { get; private set; } = new();

    public NamingSchemeService()
    {
        var dir = AppPaths.Root;
        Directory.CreateDirectory(dir);
        SettingsPath = Path.Combine(dir, "naming-rules.json");
        Load();
    }

    public void Load()
    {
        try
        {
            var loadPath=AppPaths.ReadPath("naming-rules.json");
            if (File.Exists(loadPath))
                Settings = JsonSerializer.Deserialize<NamingSettings>(File.ReadAllText(loadPath), JsonOptions()) ?? new NamingSettings();
        }
        catch
        {
            Settings = new NamingSettings();
        }
    }

    public void Save()
    {
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, JsonOptions()));
    }

    public string RenderName(NamingRule rule, CombatUnitNode unit, int number, string stateAbbreviation)
    {
        var special = rule.SpecialNames.FirstOrDefault(s => TryParseNumberDesignator(s.NumberText, out var n) && n == number && !string.IsNullOrWhiteSpace(s.Name));
        if (special is not null)
            return ApplyCase(special.Name, rule.CaseStyle);

        var parts = new List<string>();
        foreach (var token in rule.Tokens)
        {
            var value = token.Kind switch
            {
                NamingTokenKind.Number => FormatNumber(number, rule.NumberStyle),
                NamingTokenKind.HomeState => rule.StateStyle == StateStyle.Abbreviation ? stateAbbreviation : unit.HomeStateName,
                NamingTokenKind.UnitType => string.IsNullOrEmpty(rule.UnitTypeOverride) ? unit.TypeName : rule.UnitTypeOverride,
                NamingTokenKind.UnitTier => string.IsNullOrEmpty(rule.UnitTierOverride) ? unit.TierName : rule.UnitTierOverride,
                NamingTokenKind.CustomText => token.Text,
                _ => string.Empty
            };
            parts.Add(value);
        }
        return ApplyCase(string.Concat(parts), rule.CaseStyle);
    }

    public List<RenamePreviewItem> BuildPreview(NamingRule rule, IEnumerable<CombatUnitNode> allUnits, Func<int, string> stateAbbreviation)
    {
        Diagnostics.Clear();
        var population = allUnits.ToList();
        var matched = new List<CombatUnitNode>();
        foreach (var unit in population)
        {
            var reason = ExclusionReason(rule, unit);
            if (reason is null) matched.Add(unit);
            else Diagnostics.Add($"{unit.Name} [ID {unit.UnitId}]: {reason}");
        }
        var hasStateToken = rule.Tokens.Any(t => t.Kind == NamingTokenKind.HomeState);
        var skipped = ParseNumberSet(rule.SkippedNumbersText);
        foreach (var s in rule.SpecialNames)
            if (TryParseNumberDesignator(s.NumberText, out var specialNum)) skipped.Remove(specialNum); // special numbers are occupied but valid

        var result = new List<RenamePreviewItem>();
        var groups = matched.GroupBy(u => (u.Nation, State: hasStateToken ? u.StateId : int.MinValue));
        foreach (var group in groups)
        {
            // Excluded special units still occupy their existing number in the same faction/state/type/tier namespace.
            var usedNumbers = new HashSet<int>();
            foreach (var occupied in population.Where(u => !matched.Contains(u) && u.Nation == group.Key.Nation &&
                (!hasStateToken || u.StateId == group.Key.State) && Matches(rule, u)))
                if (TryReadExistingNumber(rule, occupied.Name, out var number)) usedNumbers.Add(number);
            var assignedNumbers = new Dictionary<CombatUnitNode, int>();
            var ordered = group.OrderBy(ParseRaisedDate).ThenBy(u => u.UnitId).ToList();

            // Preserve a uniquely recognizable existing numeral/special-name slot first.
            // Duplicate or reserved existing numbers are deliberately left unassigned and
            // are repaired by the normal sequence pass below.
            foreach (var unit in ordered.Where(_ => !rule.RenumberWithGaps))
            {
                if (!TryReadExistingNumber(rule, unit.Name, out var existing) || existing <= 0) continue;
                var isSpecial = rule.SpecialNames.Any(s => TryParseNumberDesignator(s.NumberText, out var n) && n == existing);
                if ((!isSpecial && skipped.Contains(existing)) || usedNumbers.Contains(existing)) continue;
                usedNumbers.Add(existing);
                assignedNumbers[unit] = existing;
            }

            long next = rule.RenumberWithGaps || usedNumbers.Count == 0 ? 1 : (long)usedNumbers.Max() + 1;
            var allocationIndex = 0;
            foreach (var unit in ordered)
            {
                if (!assignedNumbers.TryGetValue(unit, out var assigned))
                {
                    while (next <= int.MaxValue && (skipped.Contains((int)next) || usedNumbers.Contains((int)next))) next++;
                    if (next > int.MaxValue) { Diagnostics.Add($"{unit.Name}: no available number within integer range"); continue; }
                    assigned = (int)next;
                    usedNumbers.Add(assigned);
                    // Stable arithmetic seed: no random state and no dependence on
                    // names, so preview refresh and applying the same population agree.
                    next += rule.RenumberWithGaps ? NumberingStep(group.Key.Nation, group.Key.State, allocationIndex++) : 1;
                }
                var rendered = RenderName(rule, unit, assigned, stateAbbreviation(unit.StateId));
                if (new EditValidationService().ValidateName(rendered).Severity == ValidationSeverity.Error) { Diagnostics.Add($"{unit.Name}: rendered name contains unsafe line breaks"); continue; }
                result.Add(new RenamePreviewItem(unit, unit.Name, rendered, assigned));
            }
        }
        return result;
    }

    private static int NumberingStep(int nation, int state, int index)
    {
        unchecked {
            uint hash = (uint)state * 0x9E3779B9u ^ (uint)nation * 0x85EBCA6Bu ^ (uint)(index + 1) * 0xC2B2AE35u;
            hash ^= hash >> 16; hash *= 0x7FEB352Du; hash ^= hash >> 15;
            // Most increments are one; occasionally leave one or two free numbers.
            return (hash % 5) switch { 0 => 3, 1 => 2, _ => 1 };
        }
    }

    public string NameForRehomedUnit(NamingRule rule, CombatUnitNode unit, IEnumerable<CombatUnitNode> population, Func<int, string> stateAbbreviation)
    {
        var occupied = new HashSet<int>();
        foreach (var other in population.Where(u => u != unit && u.Nation == unit.Nation && u.StateId == unit.StateId && u.UnitType == unit.UnitType && u.UnitTier == unit.UnitTier))
            if (TryReadExistingNumber(rule, other.Name, out var number)) occupied.Add(number);
        long next = occupied.Count == 0 ? 1 : (long)occupied.Max() + 1;
        var skipped = ParseNumberSet(rule.SkippedNumbersText);
        while (next <= int.MaxValue && skipped.Contains((int)next)) next++;
        if (next > int.MaxValue) throw new InvalidOperationException("No available unit number in the destination Home State.");
        var name = RenderName(rule, unit, (int)next, stateAbbreviation(unit.StateId));
        if (new EditValidationService().ValidateName(name).Severity == ValidationSeverity.Error) throw new InvalidOperationException("The naming rule generates unsafe line breaks.");
        return name;
    }

    public string? ExclusionReason(NamingRule rule, CombatUnitNode unit)
    {
        var nation = rule.Faction switch { NamingFaction.Union => 0, NamingFaction.Confederacy => 1, _ => CurrentNation };
        if (rule.Faction != NamingFaction.Both && unit.Nation != nation) return "Outside faction scope";
        if (!Matches(rule, unit)) return "Unit type / tier does not match";
        var category = Classify?.Invoke(unit) ?? CommandCategory.Unknown;
        if (category is CommandCategory.Fleet or CommandCategory.Unknown || unit.UnitType is < 0 or > 2) return "Naval / unknown object: inspection only";
        if (rule.CommandScope == "Field" && category != CommandCategory.FieldCommand) return "Outside Field Commands scope";
        if (rule.CommandScope == "Garrison" && category != CommandCategory.Garrison) return "Outside Garrison scope";
        if (rule.Tokens.Any(t => t.Kind == NamingTokenKind.HomeState) &&
            (string.IsNullOrWhiteSpace(unit.HomeStateName) || unit.HomeStateName.StartsWith("Unknown State (") || unit.HomeStateName.StartsWith("State #")))
            return $"Unresolved Home State (ID {unit.StateId}); state-based naming skipped";
        static bool Has(string csv, int id) => csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(s => int.TryParse(s, out var n) && n == id);
        static bool Contains(string csv, string name) => csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(rule.WeaponInclude) && !Has(rule.WeaponInclude, unit.WeaponId)) return "Weapon not in include list";
        if (Has(rule.WeaponExclude, unit.WeaponId)) return $"Excluded weapon ID {unit.WeaponId}";
        if (!string.IsNullOrWhiteSpace(rule.StateInclude) && !Has(rule.StateInclude, unit.StateId)) return "Home State not in include list";
        if (Has(rule.StateExclude, unit.StateId)) return $"Excluded Home State ID {unit.StateId}";
        if (!string.IsNullOrWhiteSpace(rule.NameContains) && !Contains(rule.NameContains, unit.Name)) return "Name does not contain an included phrase";
        if (Contains(rule.NameExcludes, unit.Name)) return "Preserved by name exclusion: " + rule.NameExcludes;
        return null;
    }

    public static bool Matches(NamingRule rule, CombatUnitNode unit) =>
        (!rule.UnitType.HasValue || rule.UnitType.Value == unit.UnitType) &&
        (!rule.UnitTier.HasValue || rule.UnitTier.Value == unit.UnitTier);

    public static bool TryParseNumberDesignator(string? text, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        var m = Regex.Match(trimmed, @"^(\d+)(?:st|nd|rd|th|\.)?$", RegexOptions.IgnoreCase);
        if (m.Success) return int.TryParse(m.Groups[1].Value, out number) && number > 0;
        if (Regex.IsMatch(trimmed, @"^[IVXLCDM]+$", RegexOptions.IgnoreCase))
        {
            number = RomanToInt(trimmed);
            return number > 0;
        }
        return false;
    }

    public static string FormatNumber(int number, NumberStyle style) => style switch
    {
        NumberStyle.Cardinal => number.ToString(CultureInfo.InvariantCulture),
        NumberStyle.Roman => IntToRoman(number),
        NumberStyle.OrdinalDot => number.ToString(CultureInfo.InvariantCulture) + ".",
        _ => EnglishOrdinal(number)
    };

    public static string TierName(int tier) => tier switch
    {
        18 => "Army Group", 17 => "Fleet", 16 => "Army", 15 => "Corps", 14 => "Division", 13 => "Brigade",
        12 => "Regiment", 11 => "Battalion", 10 => "Battery", _ => $"Tier {tier}"
    };

    private static bool TryReadExistingNumber(NamingRule rule, string name, out int number)
    {
        number = 0;
        foreach (var s in rule.SpecialNames)
            if (string.Equals(name.Trim(), s.Name.Trim(), StringComparison.OrdinalIgnoreCase) && TryParseNumberDesignator(s.NumberText, out number)) return true;
        if (rule.Tokens.Count(t => t.Kind == NamingTokenKind.Number) == 1)
        {
            var pattern = "^" + string.Concat(rule.Tokens.Select(t => t.Kind == NamingTokenKind.Number ? @"(?<number>\d+(?:st|nd|rd|th|\.)?|[IVXLCDM]+)" : t.Kind == NamingTokenKind.CustomText ? Regex.Escape(t.Text) : @".+?")) + "$";
            var formatted = Regex.Match(name, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
            if (formatted.Success && TryParseNumberDesignator(formatted.Groups["number"].Value, out number)) return true;
        }
        var m = LeadingNumberRegex.Match(name);
        if (m.Success && int.TryParse(m.Groups[1].Value, out number)) return true;
        var roman = RomanRegex.Match(name);
        if (roman.Success)
        {
            number = RomanToInt(roman.Groups[1].Value);
            return number > 0;
        }
        return false;
    }

    private static HashSet<int> ParseNumberSet(string text)
    {
        var set = new HashSet<int>();
        foreach (var raw in (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Contains('-'))
            {
                var p = raw.Split('-', 2, StringSplitOptions.TrimEntries);
                if (p.Length == 2 && TryParseNumberDesignator(p[0], out var a) && TryParseNumberDesignator(p[1], out var b))
                {
                    if ((long)Math.Max(a, b) - Math.Min(a, b) > 100000) throw new ArgumentException("A reserved number range may contain at most 100,001 values.");
                    for (long n = Math.Min(a, b); n <= Math.Max(a, b); n++) set.Add((int)n);
                }
            }
            else if (TryParseNumberDesignator(raw, out var n)) set.Add(n);
        }
        return set;
    }

    private static DateTime ParseRaisedDate(CombatUnitNode unit)
    {
        var match = Regex.Match(unit.RaisedText ?? string.Empty, @"(?<month>January|February|March|April|May|June|July|August|September|October|November|December)\s+(?<day>\d{1,2}),\s+(?<year>\d{4})", RegexOptions.IgnoreCase);
        if (match.Success && DateTime.TryParse(match.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return dt;
        return DateTime.MaxValue;
    }

    private static string ApplyCase(string value, TextCaseStyle style) => style switch
    {
        TextCaseStyle.Upper => value.ToUpperInvariant(),
        TextCaseStyle.Lower => value.ToLowerInvariant(),
        TextCaseStyle.Title => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant()),
        _ => value
    };

    private static string EnglishOrdinal(int number)
    {
        var abs = Math.Abs(number);
        var mod100 = abs % 100;
        var suffix = mod100 is 11 or 12 or 13 ? "th" : (abs % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return number.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static string IntToRoman(int number)
    {
        if (number <= 0 || number > 3999) return number.ToString(CultureInfo.InvariantCulture);
        var map = new (int Value, string Numeral)[] { (1000,"M"),(900,"CM"),(500,"D"),(400,"CD"),(100,"C"),(90,"XC"),(50,"L"),(40,"XL"),(10,"X"),(9,"IX"),(5,"V"),(4,"IV"),(1,"I") };
        var result = string.Empty;
        foreach (var (value, numeral) in map)
            while (number >= value) { result += numeral; number -= value; }
        return result;
    }

    private static int RomanToInt(string roman)
    {
        var values = new Dictionary<char, int> { ['I']=1,['V']=5,['X']=10,['L']=50,['C']=100,['D']=500,['M']=1000 };
        var total = 0; var prev = 0;
        foreach (var c in roman.ToUpperInvariant().Reverse())
        {
            if (!values.TryGetValue(c, out var v)) return 0;
            if (v < prev) total -= v; else { total += v; prev = v; }
        }
        return total;
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
}
