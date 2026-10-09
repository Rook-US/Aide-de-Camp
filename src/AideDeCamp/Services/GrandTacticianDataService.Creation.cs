using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using AideDeCamp.Models;

namespace AideDeCamp.Services;

public sealed partial class GrandTacticianDataService
{
    private readonly Dictionary<string, List<string>> _creationSaved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextFileBuffer> _creationCompanions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<OobNode> _sessionCreated = new();
    private IReadOnlyList<TownStateMap.MappedTown>? _creationTowns;
    private IReadOnlyList<ManagementSnapshot.State>? _creationStates;
    private TextFileBuffer CreationBuffer(string file) => file switch {
        "groups.dat" => _groupBuffer!, "regiments.dat" => _regimentBuffer!, "paths.dat" => _pathBuffer!,
        _ => _creationCompanions[file] };
    public bool HasCreationChanges => _creationSaved.Any(p => !p.Value.SequenceEqual(CreationBuffer(p.Key).Lines));
    private bool DiffersFromSaved(string file, TextFileBuffer buffer, List<string> lines) =>
        !(_creationSaved.TryGetValue(file, out var saved) ? saved : buffer.Lines).SequenceEqual(lines);
    private void TrackCreationBuffer(string file)
    {
        if (_creationSaved.ContainsKey(file)) return;
        if (file is not ("groups.dat" or "regiments.dat" or "paths.dat"))
            _creationCompanions.Add(file, TextFileBuffer.Read(Path.Combine(SaveDirectory!, file)));
        _creationSaved.Add(file, CreationBuffer(file).CloneLines());
    }
    private void ResetCreation() { _creationSaved.Clear(); _creationCompanions.Clear(); _sessionCreated.Clear(); _creationTowns = null; _creationStates = null; }
    public static bool IsCreationStateId(int id) => id is >= 0 and <= 40 or 46 or 47 or 50 or 51;
    public IReadOnlyList<ManagementSnapshot.State> GetCreationStateRules()
    {
        if (_creationStates is not null) return _creationStates;
        var snapshot = ManagementSnapshot.Read(SaveDirectory!, _stateNames, _groups.ToDictionary(g => g.Key, g => g.Value.Nation), file => Management?.OriginalLines(file));
        if (snapshot.States.Count == 0) throw new InvalidDataException("Saved home-state recruitment flags could not be resolved.");
        return _creationStates = snapshot.States;
    }
    public IReadOnlyList<TownStateMap.MappedTown> GetCreationTowns()
    {
        if (_creationTowns is not null) return _creationTowns;
        if (SaveDirectory is null || GameDate is null || File.ReadAllText(Path.Combine(SaveDirectory, "version.dat")).Trim() != "1.142") throw new InvalidDataException("Town mapping requires a dated version-1.142 save.");
        // Preview cache only. CreateUnit checks all loaded file hashes before
        // accepting this snapshot; SaveAsync checks them again before writing.
        return _creationTowns = TownStateMap.Map1861(TownLocationParser.Parse(File.ReadAllLines(Path.Combine(SaveDirectory, "IIPsTowns.dat"))), GameDate.Value)
            .Where(t => IsCreationStateId(t.StateId) && t.Location.Owner is 0 or 1).ToArray();
    }
    private IReadOnlyList<ExistingGarrisonReferenceService.Link> CreationGarrisons() => File.Exists(Path.Combine(SaveDirectory!, "garrisonrefs.dat"))
        ? ExistingGarrisonReferenceService.Resolve(TextFileBuffer.Read(Path.Combine(SaveDirectory!, "garrisonrefs.dat")).Lines, _groups) : [];
    private void AcceptCreationSave()
    {
        foreach (var file in _creationSaved.Keys.ToArray()) _creationSaved[file] = CreationBuffer(file).CloneLines();
    }

    public IReadOnlyList<CreationOfficer> GetCreationOfficers()
    {
        var lines = TextFileBuffer.Read(Path.Combine(SaveDirectory!, "commanders.txt")).Lines;
        int count = CreationRecords.FixedCount(lines, 66, "commanders.txt");
        return Enumerable.Range(0, count).Select(i => {
            int s = 1 + 66 * i;
            return new CreationOfficer(RequiredInt(lines[s], "Commander ID"), lines[s + 3],
                RequiredInt(lines[s + 4], "Commander faction"), RequiredInt(lines[s + 13], "Commander branch"),
                RequiredInt(lines[s + 59], "Commander rank"), RequiredInt(lines[s + 60], "Commander status"),
                bool.Parse(lines[s + 65]));
        }).ToArray();
    }
    public IReadOnlyDictionary<int, int> GetCreationMaximums()
    {
        var file = CampaignRules.Resolve(SaveDirectory!, ConfigDirectory, "unitprefs.txt")
            ?? throw new InvalidDataException("Unit size configuration is missing.");
        var lines = File.ReadAllLines(file);
        var result = new Dictionary<int, int>();
        var names = new[] { "Infantry", "Cavalry", "Artillery" };
        for (int i = 0; i + 3 < lines.Length; i++)
            if (lines[i].StartsWith("Unit Type ", StringComparison.OrdinalIgnoreCase)) {
                int branch = Array.FindIndex(names, n => n.Equals(lines[i + 1].Trim(), StringComparison.OrdinalIgnoreCase));
                if (branch >= 0 && lines[i + 2].StartsWith("Maximum Unit Size", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(lines[i + 3], out int maximum) && maximum > 0) result[branch] = maximum;
            }
        if (result.Count != 3) throw new InvalidDataException("The configured maximum sizes could not all be resolved.");
        return result;
    }
    public IReadOnlyList<CreationPerkOption> GetCreationPerks(bool headquarters)
    {
        var file = CampaignRules.Resolve(SaveDirectory!, ConfigDirectory, headquarters ? "PerkTooltipsArmyCampaign.txt" : "PerkBattleTooltips.txt")
            ?? throw new InvalidDataException("The game's perk catalog is missing.");
        var lines = File.ReadAllLines(file);
        int count = int.Parse(lines[1], CultureInfo.InvariantCulture);
        if (count != (headquarters ? 18 : 19) || lines.Length < 3 + count * 12)
            throw new InvalidDataException("Unknown perk catalog layout.");
        var options = new List<CreationPerkOption>();
        for (int id = 0; id < count; id++) for (int level = 0; level < 3; level++) {
            int s = 3 + id * 12 + level * 4;
            if (!lines[s].StartsWith(id + "/", StringComparison.Ordinal)) throw new InvalidDataException("Perk catalog ID order changed.");
            options.Add(new(id, level, lines[s + 1], lines[s + 2]));
        }
        return options;
    }
    public bool IsCreationParent(GroupNode group, int faction)
    {
        var seen = new HashSet<int>(); var current = group;
        while (true) {
            if (!seen.Add(current.GroupId) || current.Nation != faction || current.UnitTier is < 14 or > 16) return false;
            if (current.ParentId < 0) return true;
            if (!_groups.TryGetValue(current.ParentId, out current!)) return false;
        }
    }

    public CreationAssessment AssessCreation(CreationRequest request)
    {
        var errors = new List<string>(); var warnings = new List<string>(); var b = request.Blueprint;
        try {
            if (SaveDirectory is null || IsReadOnlySave) throw new InvalidDataException("Open an extracted campaign save first.");
            if (File.ReadAllText(Path.Combine(SaveDirectory, "version.dat")).Trim() != "1.142")
                throw new InvalidDataException("Unable to proceed: no creation layout is mapped for this save version. Confirmation cannot resolve an unknown file layout.");
            if (GameDate is null) errors.Add("Campaign date is unresolved.");
            CreationRecords.FixedCount(_regimentBuffer!.Lines, 39, "regiments.dat");
            CreationRecords.FixedCount(_groupBuffer!.Lines, 32, "groups.dat");
            CreationRecords.ValidateFieldTypes(_regimentBuffer.Lines, false);
            CreationRecords.ValidateFieldTypes(_groupBuffer.Lines, true);
            if (_pathBuffer is null) errors.Add("A complete paths.dat file is required.");
            else PathRecordParser.Parse(_pathBuffer.Lines, strictFields: true);
            if (b.Faction is not (0 or 1)) errors.Add("Choose Union or Confederacy.");
            if (string.IsNullOrWhiteSpace(b.Name) || b.Name.Length > 200 || b.Name.Any(c => char.IsControl(c))) errors.Add("Enter a name without control characters (up to 200 characters).");
            if (_units.Any(u => u.Name == b.Name) || _groups.Values.Any(g => g.Name == b.Name)) errors.Add("Use an available name; this name already exists in the save.");
            if (b.Headquarters ? b.NativeTier is < 14 or > 16 : b.NativeTier is < 10 or > 13) errors.Add("The native tier does not belong to this kind of unit.");
            GroupNode? parent = null;
            if (request.Placement.ParentId is int id) {
                if (!_groups.TryGetValue(id, out parent) || !IsCreationParent(parent, b.Faction)) errors.Add("The parent must be an existing land HQ in the selected faction.");
                else if (b.Headquarters && b.NativeTier >= parent.UnitTier) errors.Add("A subordinate HQ must have a lower native tier than its parent.");
                if (request.Placement.Town is not null) errors.Add("Attached units inherit placement from the parent; remove the town selection.");
            } else if (!b.Headquarters) errors.Add("Combat units must be attached to an HQ.");
            else {
                var town = request.Placement.Town;
                if (town is null || !GetCreationTowns().Contains(town)) errors.Add("Select a town whose saved identity, state and position are verified for this map date.");
                else if (town.Location.Owner != b.Faction) warnings.Add("The selected town belongs to the other faction. The new HQ may enter hostile territory immediately.");
                CreationRecords.DeploymentTail(ReadCreationCompanion("battledata.dat"));
            }
            ValidateArmyReferences(ReadCreationCompanion("armygrouprefs.dat"), _groups.Count, _units.Count);
            var officer = GetCreationOfficers().SingleOrDefault(o => o.Id == b.CommanderId);
            if (officer is null || officer.Faction != b.Faction) errors.Add("Select an existing commander in this faction.");
            else {
                if (_groups.Values.Any(g => g.CommanderId == officer.Id) || _units.Any(u => u.CommanderId == officer.Id)) errors.Add("That commander is already assigned. Select an unused commander.");
                if (!officer.Active || officer.Status != 0) warnings.Add($"{officer.Name}: saved status {officer.Status}, active flag {officer.Active}; availability needs game confirmation.");
                warnings.Add($"Commander assignment: {officer.Name}, saved branch {officer.Branch}, rank {officer.Rank}. ADC checks faction and uniqueness; this rank/branch combination has not been game-tested.");
            }
            foreach (var color in new[] { b.Coat, b.Trousers, b.ColorVariation })
                if (!Regex.IsMatch(color, @"^\d{3}-\d{3}-\d{3}$") || color.Split('-').Any(v => int.Parse(v) > 255)) errors.Add("Uniform colors must use three RGB values from 000 to 255.");
            if (b.Perks.Length != (b.Headquarters ? 4 : 1)) errors.Add("HQs require four perk slots; combat units require one.");
            var catalog = GetCreationPerks(b.Headquarters);
            if (b.Perks.Where(p => p.Id >= 0).GroupBy(p => p.Id).Any(g => g.Count() > 1)) errors.Add("A perk cannot occupy multiple slots.");
            foreach (var p in b.Perks) {
                if (!b.Headquarters && !CreationRecords.IsStandardCombatPerk(b.UnitType, p.Id)) warnings.Add($"Perk ID {p.Id} is outside the game's offered list for this branch. Its saved ID is mapped, but its effect may not apply to this unit.");
                if (p.Id < -1 || p.Level is < 0 or > 2 || !double.IsFinite(p.Progress) || p.Progress is < 0 or > 1 ||
                    (p.Id == -1 && p.Level != 0) || (p.Id >= 0 && !catalog.Any(o => o.Id == p.Id && o.Level == p.Level))) errors.Add("A perk ID, level or progress is invalid for this unit's catalog.");
                else if (p.Id >= 0 && (b.Headquarters || p.Level != 0 || !(b.UnitType == 0 && p.Id == 2 || b.UnitType == 1 && p.Id == 9)))
                    warnings.Add($"{catalog.First(o => o.Id == p.Id && o.Level == p.Level).Name}: mapped saved values; this unit/level combination has not been game-tested. Its effects may depend on branch or HQ tier.");
            }
            if (!b.Headquarters) {
                var max = GetCreationMaximums();
                if (b.UnitType is < 0 or > 2) errors.Add("Choose infantry, cavalry or artillery.");
                else if (b.Strength < 1 || b.Strength > max[b.UnitType]) errors.Add($"Size must be between 1 and the configured maximum ({max[b.UnitType]:N0}).");
                var weapon = WeaponOptions.SingleOrDefault(w => w.Id == b.WeaponId);
                if (weapon is null || weapon.UnitType != b.UnitType) errors.Add("Select a weapon configured for this branch.");
                var home = GetCreationStateRules().SingleOrDefault(s => s.Id == b.HomeStateId && s.Side == b.Faction);
                if (!IsCreationStateId(b.HomeStateId) || home is null) errors.Add("Select a mapped home state or territory in this save.");
                else if (!home.Recruitable) warnings.Add($"{home.Name} is marked non-recruitable in this save. Its ID is mapped, but recruiting a new unit from it needs game confirmation.");
                if (b.ContractMonths is < 1 or > 120 || b.RecruitingType is < 0 or > 1) errors.Add("Contract must be 1–120 months, with volunteer or draft recruitment.");
                if (b.HorseArtillery && b.UnitType != 2) errors.Add("Horse artillery applies only to artillery units.");
                if (!double.IsFinite(b.Training) || b.Training is < 0 or > 100 || !double.IsFinite(b.Experience) || b.Experience is < 0 or > 100) errors.Add("Training and experience must be between 0 and 100.");
                if (b.SupplyPercent.Length != 4 || b.SupplyPercent.Any(v => !double.IsFinite(v) || v is < 0 or > 100)) errors.Add("Each starting stock must be between 0% and 100% of strength.");
                warnings.Add("This inserts a fully recruited unit. It does not deduct national money, weapon stocks or state recruits as in-game recruitment would.");
                if (b.Training != 0 || b.RecruitingType != 0 || b.ContractMonths != 12 || b.HorseArtillery) warnings.Add("Training, contract, recruitment type or horse artillery differs from the controlled creation tests. Their saved fields are mapped; this combination needs game confirmation.");
                warnings.Add("The no-order path initializes morale to 1 and the campaign-stat update timestamp to 0. The game recalculates these; the generalized initializer has not yet been game-resaved.");
            } else {
                if (b.WeaponId != -1 || b.Strength != 0 || b.HomeStateId != -1 || b.Training != 0 || b.Experience != 0 || b.HorseArtillery)
                    errors.Add("HQ records cannot contain a combat weapon, manpower, home state, training or combat experience. Clear those combat-only inputs.");
                warnings.Add("The game will generate this HQ's runtime path on load, as in the accepted HQ tests. HQ supply displays are recalculated from its formation; no combat stock or weapon is assigned to the HQ.");
            }
            if (b.Faction != 0 || b.Headquarters && b.NativeTier != 16 && b.NativeTier != 14)
                warnings.Add("This faction or native HQ tier was not covered by the controlled Union creation tests.");
            if (parent is not null) {
                var fort = CreationGarrisons().SingleOrDefault(g => g.GroupId == parent.GroupId);
                if (fort is not null && b.Headquarters) errors.Add("Only combat units can be added to an existing fort garrison in this creation flow.");
            }
            warnings.Add("Coverage is save version 1.142. The exact selected combination of parent, equipment, size, state, commander and perks has not been independently verified in game. A full-save backup will be made when saving.");
        } catch (Exception e) when (e is InvalidDataException or IOException or InvalidOperationException or FormatException or NotSupportedException or OverflowException) { errors.Add(e.Message); }
        return new(errors.Distinct().ToArray(), warnings.Distinct().ToArray());
    }

    private List<string> ReadCreationCompanion(string file) => _creationCompanions.TryGetValue(file, out var b) ? b.CloneLines() : TextFileBuffer.Read(Path.Combine(SaveDirectory!, file)).CloneLines();
    private static void ValidateArmyReferences(IReadOnlyList<string> l, int groups, int units)
    {
        if (l.Count != 3L + groups * 3L + units * 2L || l[0] != groups.ToString() || l[1 + groups] != groups.ToString() || l[2 + 3 * groups] != units.ToString())
            throw new InvalidDataException("armygrouprefs.dat: counts do not match the current OOB.");
        for (int i = 0; i < groups; i++) { _ = int.Parse(l[1 + i]); if (l[2 + groups + 2 * i] != i.ToString() || !bool.TryParse(l[3 + groups + 2 * i], out _)) throw new InvalidDataException("Invalid HQ reference."); }
        for (int i = 0; i < units; i++) if (l[3 + 3 * groups + 2 * i] != i.ToString() || !bool.TryParse(l[4 + 3 * groups + 2 * i], out _)) throw new InvalidDataException("Invalid combat reference.");
    }
    private void ResizeArmyReferences(int oldGroups, int oldUnits, bool hq, bool add)
    {
        var l = CreationBuffer("armygrouprefs.dat").CloneLines(); ValidateArmyReferences(l, oldGroups, oldUnits);
        var assignments = l.Skip(1).Take(oldGroups).ToList();
        var groups = l.Skip(2 + oldGroups).Take(2 * oldGroups).ToList();
        var units = l.Skip(3 + 3 * oldGroups).ToList();
        if (hq) { if (add) { assignments.Add("-1"); groups.AddRange([oldGroups.ToString(), "True"]); } else { assignments.RemoveAt(assignments.Count - 1); groups.RemoveRange(groups.Count - 2, 2); } }
        else if (add) units.AddRange([oldUnits.ToString(), "True"]); else units.RemoveRange(units.Count - 2, 2);
        var result = new List<string> { assignments.Count.ToString() }; result.AddRange(assignments); result.Add((groups.Count / 2).ToString()); result.AddRange(groups); result.Add((units.Count / 2).ToString()); result.AddRange(units);
        CreationBuffer("armygrouprefs.dat").ReplaceLines(result);
    }

    public OobNode CreateUnit(CreationRequest request, bool confirmed, EditSession session)
    {
        EnsureFilesUnchanged();
        var check = AssessCreation(request);
        if (!check.CanCreate) throw new InvalidDataException(string.Join("\n", check.Errors));
        if (check.Confirmations.Count > 0 && !confirmed) throw new InvalidOperationException("Review and confirm the creation warnings first.");
        var b = request.Blueprint; bool hq = b.Headquarters;
        string file = hq ? "groups.dat" : "regiments.dat";
        int id = hq ? _groups.Count : _units.Count;
        var fort = request.Placement.ParentId is int parent ? CreationGarrisons().SingleOrDefault(g => g.GroupId == parent) : null;
        var raw = hq ? CreationRecords.Group(id, b, request.Placement.ParentId) : CreationRecords.Regiment(id, b, request.Placement.ParentId!.Value, GameDate!.Value, fort is null ? (0, 0, 0) : (fort.X, fort.Y, fort.Z));
        List<string>? path = hq ? null : CreationRecords.Path(b, raw[31]);
        List<string>? deployment = hq && request.Placement.ParentId is null ? CreationRecords.Deployment(id, request.Placement.Town!.Location) : null;
        foreach (var f in new[] { file, "armygrouprefs.dat" }.Concat(path is null ? [] : new[] { "paths.dat" }).Concat(deployment is null ? [] : new[] { "battledata.dat" })) TrackCreationBuffer(f);
        OobNode node;
        if (hq) node = new GroupNode { GroupId = id, Name = b.Name, RawName = b.Name, ParentId = request.Placement.ParentId ?? -1, Nation = b.Faction, UnitTier = b.NativeTier, CommanderId = b.CommanderId, EditorOrder = id, IsLandCommand = true, SavedCategory = CommandCategory.FieldCommand, CommanderName = _commanders[b.CommanderId], CommanderDisplayName = CommanderDisplay(b.CommanderId, _commanders[b.CommanderId]) };
        else {
            ParseRegiments(new[] { "1" }.Concat(raw).ToArray());
            node = _units[^1]; _units.RemoveAt(_units.Count - 1);
            ((CombatUnitNode)node).EditorOrder = id;
        }
        void Apply() {
            int g = _groups.Count, u = _units.Count;
            if ((hq ? g : u) != id) throw new InvalidOperationException("Creation IDs changed; reopen Create to resolve the current save.");
            var l = CreationBuffer(file).CloneLines(); l.AddRange(raw); l[0] = (id + 1).ToString(); CreationBuffer(file).ReplaceLines(l);
            ResizeArmyReferences(g, u, hq, true);
            if (path is not null) { var p = _pathBuffer!.CloneLines(); p[0] = (int.Parse(p[0]) + 1).ToString(); p.AddRange(path); _pathBuffer.ReplaceLines(p); }
            if (deployment is not null) { var d = CreationBuffer("battledata.dat").CloneLines(); int tail = CreationRecords.DeploymentTail(d); d.InsertRange(tail, deployment); d[42] = (int.Parse(d[42]) + 1).ToString(); CreationBuffer("battledata.dat").ReplaceLines(d); }
            if (node is GroupNode group) { _groups.Add(id, group); group.CaptureOriginalState(); } else { var unit = (CombatUnitNode)node; _units.Add(unit); unit.IsLandAsset = true; }
            _sessionCreated.Add(node); ReindexCreation(); ResolveDisplayNames(); ResolveUnitContext();
            if (node is CombatUnitNode n) n.CaptureOriginalState();
        }
        void Undo() {
            if ((hq ? _groups.Keys.Max() : _units.Max(u => u.UnitId)) != id) throw new InvalidOperationException("Undo later creations first.");
            var l = CreationBuffer(file).CloneLines(); int width = hq ? 32 : 39; int start = Enumerable.Range(0, int.Parse(l[0])).Select(i => 1 + i * width).Single(s => l[s] == id.ToString());
            // Later edits may have been saved and then undone only in the model.
            // Capture that current semantic state, not stale saved record text.
            if (node is CombatUnitNode currentUnit) PatchRegiment(l, currentUnit); else PatchGroup(l, (GroupNode)node);
            raw = l.Skip(start).Take(width).ToList(); l.RemoveRange(start, width); l[0] = id.ToString(); CreationBuffer(file).ReplaceLines(l);
            ResizeArmyReferences(_groups.Count, _units.Count, hq, false);
            if (path is not null) { var p = _pathBuffer!.CloneLines(); PatchPath(p, (CombatUnitNode)node, new List<string>()); var match = PathRecordParser.Parse(p).Single(r => r.Name == raw[1] && r.Abbreviation == raw[2] && r.UnitType == b.UnitType && r.CommanderId == b.CommanderId); path = p.Skip(match.Start).Take(match.End - match.Start).ToList(); p.RemoveRange(match.Start, match.End - match.Start); p[0] = (int.Parse(p[0]) - 1).ToString(); _pathBuffer.ReplaceLines(p); }
            if (deployment is not null) { var d = CreationBuffer("battledata.dat").CloneLines(); CreationRecords.DeploymentTail(d); int s = Enumerable.Range(0, int.Parse(d[42])).Select(i => 43 + i * 15).Single(s => d[s] == id.ToString() && d[s + 13] == "True"); d.RemoveRange(s, 15); d[42] = (int.Parse(d[42]) - 1).ToString(); CreationBuffer("battledata.dat").ReplaceLines(d); }
            if (hq) _groups.Remove(id); else _units.Remove((CombatUnitNode)node);
            _sessionCreated.Remove(node); ReindexCreation(); ResolveUnitContext();
        }
        session.ExecuteCreation("Create " + b.Name, () => AtomicCreationMutation(Apply), () => AtomicCreationMutation(Undo));
        return node;
    }
    private void AtomicCreationMutation(Action action)
    {
        var files = _creationSaved.Keys.ToDictionary(f => f, f => CreationBuffer(f).CloneLines());
        var groups = _groups.ToArray(); var units = _units.ToArray(); var created = _sessionCreated.ToArray();
        try { action(); }
        catch {
            foreach (var (file, lines) in files) CreationBuffer(file).ReplaceLines(lines);
            _groups.Clear(); foreach (var (id, node) in groups) _groups.Add(id, node);
            _units.Clear(); _units.AddRange(units); _sessionCreated.Clear(); _sessionCreated.UnionWith(created);
            ReindexCreation(); throw;
        }
    }
    private void ReindexCreation()
    {
        for (int i = 0; i < _groups.Count; i++) _groups[int.Parse(_groupBuffer!.Lines[1 + 32 * i])].GroupLineStart = 1 + 32 * i;
        var byId = _units.ToDictionary(u => u.UnitId);
        for (int i = 0; i < _units.Count; i++) byId[int.Parse(_regimentBuffer!.Lines[1 + 39 * i])].RegimentLineStart = 1 + 39 * i;
        // Preserve pending stock edits while relocating path addresses.
        var stocks = _units.ToDictionary(u => u, u => Enumerable.Range(0, 4).Select(u.SupplyStockAt).ToArray());
        MatchPathRecords(_pathBuffer!.Lines);
        foreach (var (u, values) in stocks) if (values.All(v => v.HasValue)) u.RestoreSupplyStock(values[0], values[1], values[2], values[3]);
    }
    private void ValidateCreationWrite(IReadOnlyList<string> regiments, IReadOnlyList<string> groups, IReadOnlyList<string>? paths)
    {
        if (_creationSaved.Count == 0) return;
        int gc = CreationRecords.FixedCount(groups, 32, "groups.dat"), uc = CreationRecords.FixedCount(regiments, 39, "regiments.dat");
        CreationRecords.ValidateFieldTypes(regiments, false); CreationRecords.ValidateFieldTypes(groups, true);
        ValidateArmyReferences(CreationBuffer("armygrouprefs.dat").Lines, gc, uc);
        if (paths is null) throw new InvalidDataException("Missing paths.dat.");
        var records = PathRecordParser.Parse(paths, strictFields: true);
        if (records.GroupBy(r => (r.Name, r.Abbreviation, r.UnitType, r.CommanderId)).Any(g => g.Count() > 1)) throw new InvalidDataException("Ambiguous runtime path identity.");
        var groupIds = Enumerable.Range(0, gc).Select(i => int.Parse(groups[1 + i * 32])).ToHashSet();
        for (int i = 0; i < uc; i++) if (!groupIds.Contains(int.Parse(regiments[4 + i * 39]))) throw new InvalidDataException("A combat parent is unresolved.");
        for (int i = 0; i < gc; i++) {
            int s = 1 + i * 32, parent = int.Parse(groups[s + 2]);
            if (parent >= 0 && (!groupIds.Contains(parent) || int.Parse(groups[s + 3]) is 0 or 1 && _groups[parent].Nation != int.Parse(groups[s + 3]))) throw new InvalidDataException("An HQ parent or faction is unresolved.");
        }
        foreach (var unit in _sessionCreated.OfType<CombatUnitNode>()) {
            int s = unit.RegimentLineStart;
            if (records.Count(r => r.Name == unit.Name && r.Abbreviation == unit.Name && r.UnitType == unit.UnitType && r.CommanderId == unit.CommanderId) != 1) throw new InvalidDataException("A created unit lost its path identity.");
        }
        if (_creationCompanions.TryGetValue("battledata.dat", out var battle)) CreationRecords.DeploymentTail(battle.Lines);
    }

    private void ResolveSavedCommandCategories()
    {
        if (SaveDirectory is null || !File.Exists(Path.Combine(SaveDirectory, "version.dat")) || File.ReadAllText(Path.Combine(SaveDirectory, "version.dat")).Trim() != "1.142") return;
        var path = Path.Combine(SaveDirectory, "garrisonrefs.dat");
        try {
            var forts = File.Exists(path) ? ExistingGarrisonReferenceService.Resolve(TextFileBuffer.Read(path).Lines, _groups).Select(l => l.GroupId).ToHashSet() : new HashSet<int>();
            foreach (var group in _groups.Values) group.SavedCategory = group.UnitTier == 17 ? CommandCategory.Fleet
                : group.UnitTier is >= 14 and <= 16 ? forts.Contains(group.GroupId) ? CommandCategory.Garrison : CommandCategory.FieldCommand : null;
        } catch (InvalidDataException) { /* Keep legacy inspection available; creation reports the broken reference. */ }
    }

    public IReadOnlyList<CreationUniform> GetCreationUniforms()
    {
        return _units.Where(u => u.UnitType is >= 0 and <= 2).Select(u => {
            int s = u.RegimentLineStart;
            return new CreationUniform(u.Nation, _regimentBuffer!.Lines[s + 14], _regimentBuffer.Lines[s + 15], _regimentBuffer.Lines[s + 16]);
        }).Distinct().ToArray();
    }
}
