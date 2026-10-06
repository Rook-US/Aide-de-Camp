using System.IO;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AideDeCamp.Models;

namespace AideDeCamp.Services;

public sealed record SaveResult(int ChangedUnitCount, int PatchedFieldCount, string? BackupDirectory, IReadOnlyList<string> Warnings)
{
    public bool WroteFiles => !string.IsNullOrWhiteSpace(BackupDirectory);
}

public sealed class GrandTacticianDataService : IDisposable
{
    private const int RegimentFields = 39;
    private const int GroupFields = 32;
    private readonly Dictionary<int, string> _commanders = new();
    private readonly Dictionary<int, string> _commanderRanks = new();
    private readonly Dictionary<int, string> _weaponNames = new();
    private readonly Dictionary<int, int?> _weaponUnitTypes = new();
    private readonly Dictionary<int, int?> _weaponClasses = new();
    private readonly Dictionary<int, string> _stateNames = new();
    private readonly Dictionary<int, string> _stateAbbreviations = new();
    private readonly Dictionary<int, GroupNode> _groups = new();
    private readonly List<CombatUnitNode> _units = new();
    private readonly Dictionary<string, string> _loadedFileHashes = new(StringComparer.OrdinalIgnoreCase);
    private TextFileBuffer? _regimentBuffer;
    private TextFileBuffer? _groupBuffer;
    private TextFileBuffer? _pathBuffer;
    private string? _tempExtractDir;

    private static readonly string[] MonitoredSaveFiles = { "regiments.dat", "paths.dat", "groups.dat", "scenario.dat", "version.dat", "commanders.txt", "nations.dat", "ships.dat", "weapons.dat", "shiptype.dat" };

    // GTCW State_ID is a fixed game enumeration, not a campaign-local sequence.
    // Keep this independent of regiment service-history prose: many campaign units
    // either have no "Raised in …" line or use wording which cannot be parsed safely.
    // The list includes territories and foreign locations because those are valid IDs
    // in the same field, even though only a subset are recruitable home states.
    private static readonly IReadOnlyDictionary<int, StateOption> CanonicalStates = new Dictionary<int, StateOption>
    {
        [0]=new(0,"Alabama","AL"), [1]=new(1,"Arizona Territory","AZ"), [2]=new(2,"Arkansas","AR"), [3]=new(3,"California","CA"),
        [4]=new(4,"Connecticut","CT"), [5]=new(5,"Dakota Territory","DT"), [6]=new(6,"Delaware","DE"), [7]=new(7,"Florida","FL"),
        [8]=new(8,"Georgia","GA"), [9]=new(9,"Illinois","IL"), [10]=new(10,"Indian Territory","IT"), [11]=new(11,"Indiana","IN"),
        [12]=new(12,"Iowa","IA"), [13]=new(13,"Kansas","KS"), [14]=new(14,"Kentucky","KY"), [15]=new(15,"Louisiana","LA"),
        [16]=new(16,"Maine","ME"), [17]=new(17,"Maryland","MD"), [18]=new(18,"Massachusetts","MA"), [19]=new(19,"Michigan","MI"),
        [20]=new(20,"Minnesota","MN"), [21]=new(21,"Mississippi","MS"), [22]=new(22,"Missouri","MO"), [23]=new(23,"Nebraska Territory","NT"),
        [24]=new(24,"Nevada","NV"), [25]=new(25,"New Hampshire","NH"), [26]=new(26,"New Jersey","NJ"), [27]=new(27,"New York","NY"),
        [28]=new(28,"North Carolina","NC"), [29]=new(29,"Ohio","OH"), [30]=new(30,"Oregon","OR"), [31]=new(31,"Pennsylvania","PA"),
        [32]=new(32,"Rhode Island","RI"), [33]=new(33,"South Carolina","SC"), [34]=new(34,"Tennessee","TN"), [35]=new(35,"Texas","TX"),
        [36]=new(36,"Unorganized Territory","UT"), [37]=new(37,"Vermont","VT"), [38]=new(38,"Virginia","VA"), [39]=new(39,"West Virginia","WV"),
        [40]=new(40,"Wisconsin","WI"), [41]=new(41,"Canada","CA"), [42]=new(42,"France","FR"), [43]=new(43,"Spain","ES"),
        [44]=new(44,"Mexico","MX"), [45]=new(45,"The Bahamas","BS"), [46]=new(46,"District of Columbia","DC"), [47]=new(47,"Colorado Territory","CO"),
        [48]=new(48,"Cuba","CU"), [49]=new(49,"Nicaragua","NI"), [50]=new(50,"New Mexico Territory","NM"), [51]=new(51,"Utah Territory","UT"),
        [52]=new(52,"Great Britain","GB")
    };

    public string? SaveDirectory { get; private set; }
    public string? ConfigDirectory { get; private set; }
    public bool IsReadOnlySave { get; private set; }
    public IReadOnlyDictionary<int, GroupNode> Groups => _groups;
    public IReadOnlyList<CombatUnitNode> Units => _units;
    public IReadOnlyList<WeaponOption> WeaponOptions => _weaponNames.OrderBy(x => x.Value).Select(x => new WeaponOption(x.Key, x.Value, _weaponUnitTypes.GetValueOrDefault(x.Key), _weaponClasses.GetValueOrDefault(x.Key))).ToList();
    public IReadOnlyList<StateOption> StateOptions => _stateNames.OrderBy(x => x.Value).Select(x => new StateOption(x.Key, x.Value, _stateAbbreviations.GetValueOrDefault(x.Key, MakeAbbreviation(x.Value)))).ToList();
    public string GameDateText { get; private set; } = string.Empty;
    public DateTime? GameDate { get; private set; }
    public string DisplayFingerprint => DisplayMetadataService.Fingerprint(_loadedFileHashes.GetValueOrDefault("regiments.dat", ""), _loadedFileHashes.GetValueOrDefault("groups.dat", ""));
    public ManagementDocument? Management { get; private set; }
    public NationProgression? Progression {get;private set;}
    public bool HasUnsavedChanges => _units.Any(u => u.HasAnyUnsavedChanges) || _groups.Values.Any(g => g.HasUnsavedParent) || Management?.HasChanges==true;
    public int OrphanUnitCount => _units.Count(u => !_groups.ContainsKey(u.ParentId));

    public async Task LoadAsync(string savePath, string? configDirectory = null)
    {
        Reset();
        SaveDirectory = PrepareSaveDirectory(savePath);
        ConfigDirectory = configDirectory;
        RequireFile("regiments.dat");
        RequireFile("groups.dat");
        RequireFile("commanders.txt");

        CaptureFileSignatures();
        await Task.Run(() =>
        {
            LoadCanonicalStateNames();
            ParseScenario();
            ParseCommanders(Path.Combine(SaveDirectory!, "commanders.txt"));
            ParseGroups(Path.Combine(SaveDirectory!, "groups.dat"));
            ValidateGroupHierarchy();
            _regimentBuffer = TextFileBuffer.Read(Path.Combine(SaveDirectory!, "regiments.dat"));
            ParseRegiments(_regimentBuffer.Lines);
            InferStateNamesFromServiceHistory(_regimentBuffer.Lines);
            if (!string.IsNullOrWhiteSpace(configDirectory)) LoadConfig(configDirectory!);

            var paths = Path.Combine(SaveDirectory!, "paths.dat");
            if (File.Exists(paths))
            {
                _pathBuffer = TextFileBuffer.Read(paths);
                MatchPathRecords(_pathBuffer.Lines);
            }
            else
            {
                foreach (var unit in _units)
                {
                    unit.PathLinkStatus = PathLinkStatus.NotAvailable;
                    unit.PathLinkMessage = "paths.dat is not present; transfer ETA editing is disabled for safety.";
                }
            }

            ResolveDisplayNames();
            ResolveUnitContext();
            var classifier = new CommandClassificationService();
            foreach (var group in _groups.Values) group.IsLandCommand = classifier.CategoryForGroup(group, _groups) is CommandCategory.FieldCommand or CommandCategory.Garrison;
            foreach (var unit in _units) unit.IsLandAsset = classifier.IsLandEditable(unit, _groups);
            foreach (var group in _groups.Values) group.CaptureOriginalState();
            foreach (var unit in _units) unit.CaptureOriginalState();
            Management=ManagementDocument.Load(SaveDirectory!);
            Progression=Management.Records.Any(r=>r.Domain=="Progression")?new NationProgression(SaveDirectory!,ConfigDirectory,Management):null;
            EnsureFilesUnchanged();
        });
    }

    public List<OobNode> BuildRoots(int nation)
    {
        foreach (var g in _groups.Values) g.Children.Clear();
        var roots = new List<OobNode>();
        foreach (var group in _groups.Values)
        {
            if (group.ParentId >= 0 && _groups.TryGetValue(group.ParentId, out var parent) && parent.Nation == group.Nation) parent.Children.Add(group);
            else roots.Add(group);
        }
        foreach (var unit in _units)
        {
            if (_groups.TryGetValue(unit.ParentId, out var parent) && parent.Nation == unit.Nation) parent.Children.Add(unit);
        }
        SortTree(roots);
        RefreshGroupAggregates();
        return roots.Where(r => r is GroupNode g && g.Nation == nation).ToList();
    }

    public async Task<SaveResult> SaveAsync()
    {
        if (SaveDirectory is null || _regimentBuffer is null) throw new InvalidOperationException("No save loaded.");
        if (IsReadOnlySave) throw new InvalidOperationException("ZIP saves are opened read-only. Select the extracted save directory before writing changes.");

        EnsureFilesUnchanged(); // Also protect metadata-only saves.
        var validator = new EditValidationService();
        var typedWeapons = WeaponOptions; var typedStates = StateOptions;
        foreach (var unit in _units.Where(u => new CommandClassificationService().IsLandEditable(u, _groups))) {
            var check = new TypedUnitEdit(unit);
            check.Validate(typedWeapons, typedStates, validator);
            var errors = check.Fields.Values.Where(f => f.Result.Severity == ValidationSeverity.Error).ToList();
            if (unit.TotalMenRaw < 0 || unit.CasualtyRatioRaw < 0 || unit.CasualtyRatioRaw > 100)
                throw new InvalidDataException($"{unit.Name}: invalid raw manpower/casualty data. Correct it before saving.");
            if (errors.Count > 0) throw new InvalidDataException($"{unit.Name}: " + string.Join("; ", errors.Select(f => f.Label + ": " + f.Result.Message)));
        }
        var changedUnits = _units.Where(u => u.HasAnyUnsavedChanges).ToList();
        var changedGroups = _groups.Values.Where(g => g.HasUnsavedParent).ToList();
        foreach (var group in changedGroups) {
            if (!group.IsLandCommand) throw new InvalidOperationException("Naval and unclassified HQs are inspection-only.");
            var nameCheck = validator.ValidateName(group.Name);
            if (nameCheck.Severity == ValidationSeverity.Error) throw new InvalidDataException($"HQ {group.GroupId}: {nameCheck.Message}");
        }
        if (changedUnits.Count == 0 && changedGroups.Count == 0 && Management?.HasChanges!=true) return new SaveResult(0, 0, null, Array.Empty<string>());

        if (changedUnits.Any(u => !new CommandClassificationService().IsLandEditable(u, _groups)))
            throw new InvalidOperationException("Naval and unclassified units are inspection-only; unsafe land edits cannot be saved.");
        var unsafeTransfers = changedUnits.Where(u => u.HasUnsavedChange("ETA") && u.PathLinkStatus != PathLinkStatus.Confirmed).ToList();
        if (unsafeTransfers.Count > 0)
        {
            var details = string.Join("\n", unsafeTransfers.Take(12).Select(u => $"• {u.Name}: {u.PathLinkMessage}"));
            if (unsafeTransfers.Count > 12) details += $"\n• …and {unsafeTransfers.Count - 12} more";
            throw new InvalidOperationException("Transfer ETA changes cannot be saved because one or more paths.dat links are not uniquely confirmed:\n\n" + details);
        }

        ValidateGroupHierarchy();
        return await Task.Run(() => SaveCore(changedUnits, changedGroups));
    }

    public void MarkSavedStates()
    {
        foreach (var unit in _units) unit.MarkSavedState();
        foreach (var group in _groups.Values) group.MarkSavedState();
    }

    private SaveResult SaveCore(IReadOnlyList<CombatUnitNode> changedUnits, IReadOnlyList<GroupNode> changedGroups)
    {
        EnsureFilesUnchanged();
        var regimentLines = _regimentBuffer!.CloneLines();
        var groupLines = _groupBuffer!.CloneLines();
        var pathLines = _pathBuffer?.CloneLines();
        var warnings = new List<string>();
        var patchedFields = 0;

        foreach (var unit in changedUnits)
        {
            patchedFields += PatchRegiment(regimentLines, unit);
            if (pathLines is not null) patchedFields += PatchPath(pathLines, unit, warnings);
        }
        foreach (var group in changedGroups) patchedFields += PatchGroup(groupLines, group);

        // GTCW stores regiments as fixed record blocks.  The tree order is persisted by
        // rewriting only the order of those intact blocks, never their unknown fields.
        // This makes a drop between siblings survive reload instead of being UI metadata.
        if (_units.Any(u => u.HasUnsavedOrder)) ReorderRegimentRecords(regimentLines);
        if (_groups.Values.Any(g => g.HasUnsavedOrder)) ReorderGroupRecords(groupLines);

        ValidateRegimentLines(regimentLines);
        ValidateGroupLines(groupLines);
        if (pathLines is not null) ValidatePatchedPathLines(pathLines, changedUnits);

        var targets = new List<(string Name, TextFileBuffer Buffer, List<string> Lines)>();
        if(Management is not null) {targets.AddRange(Management.Targets());patchedFields+=Management.ChangedFields;}
        if (!_regimentBuffer.Lines.SequenceEqual(regimentLines, StringComparer.Ordinal)) targets.Add(("regiments.dat", _regimentBuffer, regimentLines));
        if (!_groupBuffer.Lines.SequenceEqual(groupLines, StringComparer.Ordinal)) targets.Add(("groups.dat", _groupBuffer, groupLines));
        if (_pathBuffer is not null && pathLines is not null && !_pathBuffer.Lines.SequenceEqual(pathLines, StringComparer.Ordinal)) targets.Add(("paths.dat", _pathBuffer, pathLines));
        if (targets.Count == 0) return new SaveResult(changedUnits.Count, 0, null, warnings);

        EnsureFilesUnchanged();
        // Preserve the entire pre-edit save, not just the particular files being patched.
        var backupDirectory = CreateBackup(Directory.EnumerateFiles(SaveDirectory!, "*", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).OfType<string>());
        var tempFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var replaced = new List<string>();
        var writtenHashes = new Dictionary<string, string>();

        try
        {
            foreach (var target in targets)
            {
                var temp = Path.Combine(SaveDirectory!, $".{target.Name}.oobeditor.{Guid.NewGuid():N}.tmp");
                target.Buffer.WriteTo(temp, target.Lines);
                var verify = TextFileBuffer.Read(temp);
                if (!verify.Lines.SequenceEqual(target.Lines, StringComparer.Ordinal))
                    throw new IOException($"Temporary {target.Name} failed round-trip verification before save.");
                tempFiles[target.Name] = temp;
                writtenHashes[target.Name] = ComputeHash(temp);
            }

            // Recheck immediately before replacement so a game/autosave update that occurred
            // while temporary files were being prepared cannot be silently overwritten.
            EnsureFilesUnchanged();

            foreach (var target in targets)
            {
                var destination = Path.Combine(SaveDirectory!, target.Name);
                File.Move(tempFiles[target.Name], destination, true);
                replaced.Add(target.Name);
            }
        }
        catch (Exception saveError)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var name in replaced)
            {
                var backup = Path.Combine(backupDirectory, name);
                var destination = Path.Combine(SaveDirectory!, name);
                try
                {
                    if (File.Exists(backup)) File.Copy(backup, destination, true);
                    else rollbackErrors.Add(new IOException($"Rollback backup is missing for {name}: {backup}"));
                }
                catch (Exception rollbackError)
                {
                    rollbackErrors.Add(new IOException($"Could not restore {name} from its transaction backup.", rollbackError));
                }
            }
            if (rollbackErrors.Count > 0)
                throw new IOException("The save transaction failed and one or more already-replaced files could not be rolled back automatically. Preserve the Aide-de-Camp_Backups folder and restore the affected files manually before continuing.",
                    new AggregateException(new[] { saveError }.Concat(rollbackErrors)));
            throw;
        }
        finally
        {
            foreach (var temp in tempFiles.Values)
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }

        _regimentBuffer.ReplaceLines(regimentLines);
        var byId = _units.ToDictionary(u => u.UnitId);
        for (int i = 0; i < _units.Count; i++) byId[RequiredInt(regimentLines[1+i*RegimentFields], "Saved Unit_ID")].RegimentLineStart = 1+i*RegimentFields;
        var groupsById = _groups;
        for (int i = 0; i < groupsById.Count; i++) groupsById[RequiredInt(groupLines[1+i*GroupFields], "Saved Group_ID")].GroupLineStart = 1+i*GroupFields;
        _groupBuffer.ReplaceLines(groupLines);
        if (_pathBuffer is not null && pathLines is not null) _pathBuffer.ReplaceLines(pathLines);
        foreach (var pair in writtenHashes) _loadedFileHashes[pair.Key] = pair.Value;
        Management?.AcceptSaved();
        return new SaveResult(changedUnits.Count + changedGroups.Count, patchedFields, backupDirectory, warnings);
    }

    public void MoveUnit(CombatUnitNode unit, GroupNode newParent)
    {
        unit.ParentId = newParent.GroupId;
        unit.Nation = newParent.Nation;
        unit.CommandPath = BuildCommandPath(newParent.GroupId);
        unit.RefreshDisplay();
    }

    public void MoveGroup(GroupNode group, GroupNode newParent)
    {
        if (!CanMoveGroup(group, newParent)) throw new InvalidOperationException("Invalid HQ destination: hierarchy, faction, or cycle constraint.");
        if (group.GroupId == newParent.GroupId || group.Nation != newParent.Nation || group.UnitTier >= newParent.UnitTier)
            throw new InvalidOperationException("That command cannot be attached to the selected headquarters.");
        var current = newParent;
        while (true)
        {
            if (current.GroupId == group.GroupId) throw new InvalidOperationException("A command cannot be moved beneath one of its own subcommands.");
            if (current.ParentId < 0 || !_groups.TryGetValue(current.ParentId, out var parent)) break;
            current = parent;
        }
        group.ParentId = newParent.GroupId;
        NormalizeFormationNames();
        ResolveUnitContext();
    }

    public void RefreshFormationContext() { NormalizeFormationNames(); ResolveUnitContext(); RefreshGroupAggregates(); }

    public bool CanMoveGroup(GroupNode moving, GroupNode target)
    {
        if (!moving.IsLandCommand || !target.IsLandCommand || moving == target || moving.Nation != target.Nation || moving.UnitTier >= target.UnitTier) return false;
        var seen = new HashSet<int>();
        var current = target;
        while (seen.Add(current.GroupId)) {
            if (current.GroupId == moving.GroupId) return false;
            if (current.ParentId < 0 || !_groups.TryGetValue(current.ParentId, out var parent)) return true;
            current = parent;
        }
        return false;
    }

    public IReadOnlyList<WeaponOption> CompatibleWeaponsFor(CombatUnitNode unit)
    {
        var all = WeaponOptions;
        var filtered = all.Where(w => w.UnitType == unit.UnitType).ToList();
        // Old/custom saves can hold a weapon that is absent or unclassified in config.
        // Keep that current selection visible so opening a detail card never silently changes it.
        var current = all.FirstOrDefault(w => w.Id == unit.WeaponId);
        if (current is not null && filtered.All(w => w.Id != current.Id)) filtered.Insert(0, current);
        return filtered.Count > 0 ? filtered : all;
    }

    public void RefreshGroupAggregates(IEnumerable<CombatUnitNode>? changedUnits = null)
    {
        var affected = new HashSet<GroupNode>();
        if (changedUnits is null) affected.UnionWith(_groups.Values);
        else foreach (var unit in changedUnits) {
            var parentId = unit.ParentId;
            while (_groups.TryGetValue(parentId, out var parent) && affected.Add(parent)) parentId = parent.ParentId;
        }
        // Two-phase invalidation prevents a parent from rebuilding its cache while a child
        // still holds an aggregate snapshot from the previous edit.
        foreach (var group in affected) group.InvalidateAggregateCache();
        foreach (var group in affected) group.NotifyAggregateChanged();
    }

    private static IEnumerable<CombatUnitNode> EnumerateUnits(OobNode node)
    {
        foreach (var child in node.Children)
        {
            if (child is CombatUnitNode unit) yield return unit;
            else foreach (var nested in EnumerateUnits(child)) yield return nested;
        }
    }

    private void ParseScenario()
    {
        var file = Path.Combine(SaveDirectory!, "scenario.dat");
        if (!File.Exists(file)) return;
        var lines = File.ReadAllLines(file);
        if (lines.Length > 4 && int.TryParse(lines[2], out var day) && int.TryParse(lines[3], out var month) && int.TryParse(lines[4], out var year))
        {
            try { GameDate = new DateTime(year, month, day); GameDateText = GameDate.Value.ToString("MMM d, yyyy"); } catch { }
        }
    }

    private void ResolveUnitContext()
    {
        var maximums = ReadUnitMaximums();
        var artillery=SaveDirectory is not null?CampaignRules.Artillery(SaveDirectory,ConfigDirectory):null;
        foreach (var group in _groups.Values) group.CommandPath = BuildCommandPath(group.ParentId);
        foreach (var unit in _units)
        {
            if (_groups.TryGetValue(unit.ParentId, out var parent)) unit.Nation = parent.Nation;
            unit.ConfiguredMaxStrength = unit.UnitType switch
            {
                0 => maximums.GetValueOrDefault("Infantry", 2000),
                1 => maximums.GetValueOrDefault("Cavalry", 2000),
                2 => maximums.GetValueOrDefault("Artillery", 240),
                _ => 2000
            };
            unit.CommandPath = BuildCommandPath(unit.ParentId);
            unit.ArtilleryCalculation=artillery;
            unit.RefreshDisplay();
        }
    }

    public string BuildCommandPath(int parentId)
    {
        var parts = new List<string>();
        var current = parentId;
        var guard = 0;
        while (current >= 0 && _groups.TryGetValue(current, out var group) && guard++ < 64)
        {
            parts.Add(group.Name);
            current = group.ParentId;
        }
        parts.Reverse();
        return string.Join(" › ", parts);
    }

    // Exact-save editor metadata retains state choices without rewriting service history.
    public void RestoreEditorStateNames(IEnumerable<KeyValuePair<int, string>> entries)
    {
        foreach (var group in entries.Where(e => !e.Value.StartsWith("Unknown State (") && !e.Value.StartsWith("State #")).GroupBy(e => e.Key))
        {
            var names = group.Select(e => e.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count != 1) continue;
            // Metadata may recover a previously seen modded ID, but cannot relabel a
            // known game ID.  That avoids a stale layout file turning Ohio (29) into
            // a different state in a later campaign.
            if (CanonicalStates.ContainsKey(group.Key)) continue;
            _stateNames[group.Key] = names[0]; _stateAbbreviations[group.Key] = MakeAbbreviation(names[0]);
        }
        ResolveDisplayNames();
        foreach (var unit in _units) unit.CaptureOriginalState();
    }

    public string GetStateAbbreviation(int stateId) => _stateAbbreviations.GetValueOrDefault(stateId, _stateNames.TryGetValue(stateId, out var name) ? MakeAbbreviation(name) : $"S{stateId}");
    public int GetNationForUnit(CombatUnitNode unit) => _groups.TryGetValue(unit.ParentId, out var group) ? group.Nation : unit.Nation;

    private void ParseGroups(string file)
    {
        _groupBuffer = TextFileBuffer.Read(file);
        var lines = _groupBuffer.Lines;
        if (lines.Count == 0 || !int.TryParse(lines[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 0)
            throw new InvalidDataException("groups.dat counter is invalid.");
        if (lines.Count < 1 + count * GroupFields) throw new InvalidDataException("groups.dat is shorter than expected.");

        for (var i = 0; i < count; i++)
        {
            var s = 1 + i * GroupFields;
            var group = new GroupNode
            {
                GroupId = RequiredInt(lines[s], $"groups.dat group {i} Group_ID"),
                GroupLineStart = s,
                EditorOrder = i,
                Name = lines[s + 1],
                ParentId = RequiredInt(lines[s + 2], $"groups.dat group {i} Parent_ID"),
                Nation = RequiredInt(lines[s + 3], $"groups.dat group {i} Nation"),
                CommanderId = RequiredInt(lines[s + 4], $"groups.dat group {i} Commander_ID"),
                UnitTier = RequiredInt(lines[s + 17], $"groups.dat group {i} Unit_Tier")
            };
            group.RawName = group.Name;
            group.CommanderName = _commanders.GetValueOrDefault(group.CommanderId, $"Commander #{group.CommanderId}");
            group.CommanderDisplayName = CommanderDisplay(group.CommanderId, group.CommanderName);
            if (!_groups.TryAdd(group.GroupId, group))
                throw new InvalidDataException($"groups.dat contains duplicate Group_ID {group.GroupId}; hierarchy is ambiguous and will not be edited.");
        }
        NormalizeFormationNames();
    }

    // GTCW stores a formation's own designation and its parent designation in one
    // display string (for example, "1st Brigade, 4th Division"). ParentId is
    // authoritative; repair only the suffix so a cross-linked name cannot claim
    // membership in the wrong division.
    private void NormalizeFormationNames()
    {
        // A renamed parent can itself be the suffix of another formation. Repeat
        // until stable so dictionary enumeration order cannot leave a descendant
        // carrying an obsolete grandparent name.
        for (var pass = 0; pass < _groups.Count; pass++)
        {
            var changed = false;
            foreach (var group in _groups.Values)
            {
                if (group.ParentId < 0 || !_groups.TryGetValue(group.ParentId, out var parent)) continue;
                var comma = group.Name.IndexOf(',');
                if (comma <= 0) continue;
                var ownDesignation = group.Name[..comma].Trim();
                var corrected = ownDesignation.Length == 0 ? group.Name : $"{ownDesignation}, {parent.Name}";
                if (group.Name == corrected) continue;
                group.Name = corrected;
                changed = true;
            }
            if (!changed) return;
        }
    }


    private void ValidateGroupHierarchy()
    {
        foreach (var group in _groups.Values)
        {
            var seen = new HashSet<int>();
            var current = group;
            var guard = 0;
            while (current.ParentId >= 0 && _groups.TryGetValue(current.ParentId, out var parent))
            {
                if (!seen.Add(current.GroupId) || parent.GroupId == group.GroupId)
                    throw new InvalidDataException($"groups.dat contains a parent cycle involving Group_ID {group.GroupId} ({group.Name}).");
                current = parent;
                if (++guard > _groups.Count)
                    throw new InvalidDataException($"groups.dat hierarchy could not be resolved safely near Group_ID {group.GroupId} ({group.Name}).");
            }
        }
    }

    private void ParseRegiments(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0 || !int.TryParse(lines[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 0)
            throw new InvalidDataException("regiments.dat counter is invalid.");
        if (lines.Count < 1 + count * RegimentFields) throw new InvalidDataException("regiments.dat is shorter than expected.");

        for (var i = 0; i < count; i++)
        {
            var s = 1 + i * RegimentFields;
            var unit = new CombatUnitNode
            {
                RegimentLineStart = s,
                EditorOrder = i,
                UnitId = RequiredInt(lines[s], $"regiments.dat unit {i} Unit_ID"),
                Name = lines[s + 1],
                ParentId = RequiredInt(lines[s + 3], $"regiments.dat unit {i} Parent_ID"),
                UnitType = RequiredInt(lines[s + 4], $"regiments.dat unit {i} Unit_type"),
                UnitTier = RequiredInt(lines[s + 24], $"regiments.dat unit {i} Unit_Tier"),
                CommanderId = RequiredInt(lines[s + 5], $"regiments.dat unit {i} Commander_ID"),
                TotalMenRaw = RequiredInt(lines[s + 6], $"regiments.dat unit {i} Total_Men"),
                CasualtyRatioRaw = RequiredFiniteDouble(lines[s + 9], $"regiments.dat unit {i} SickRatio"),
                ExperienceRaw = RequiredFiniteDouble(lines[s + 10], $"regiments.dat unit {i} Experience"),
                WeaponId = RequiredInt(lines[s + 13], $"regiments.dat unit {i} Weapon_ID"),
                EnlistDateRaw = lines[s + 26],
                EnlistDate = TryParseUnitDate(lines[s + 26]),
                ContractMonths = RequiredInt(lines[s + 27], $"regiments.dat unit {i} Contract"),
                CampaignDate = GameDate,
                StateId = RequiredInt(lines[s + 29], $"regiments.dat unit {i} State_ID"),
                TransferTimeRaw = RequiredFiniteDouble(lines[s + 33], $"regiments.dat unit {i} Transfer_Time"),
                RaisedText = ExtractRaisedText(lines[s + 31])
            };
            _units.Add(unit);
        }
    }

    public static DateTime? TryParseUnitDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();
        var formats = new[] { "M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "M-d-yyyy", "MM-dd-yyyy", "d.M.yyyy", "dd.MM.yyyy" };
        if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact)) return exact;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)) return parsed;
        var nums = Regex.Matches(raw, @"\d+").Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
        if (nums.Length >= 3)
        {
            try
            {
                if (nums[0] > 31) return new DateTime(nums[0], nums[1], nums[2]);
                if (nums[2] > 31) return new DateTime(nums[2], nums[0], nums[1]);
            }
            catch { }
        }
        return null;
    }

    private void ParseCommanders(string file)
    {
        var lines = File.ReadAllLines(file);
        if (lines.Length == 0 || !int.TryParse(lines[0], out var count)) return;
        const int fields = 66;
        for (var i = 0; i < count && 1 + (i + 1) * fields <= lines.Length; i++)
        {
            var s = 1 + i * fields;
            if (!int.TryParse(lines[s].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            var combined = lines[s + 3];
            if (string.IsNullOrWhiteSpace(combined)) combined = $"{lines[s + 2]} {lines[s + 1]}".Trim();
            _commanders[id] = combined;
            _commanderRanks[id] = CurrentCommanderRank(lines, s);
        }
    }

    // commanders.txt stores promotion dates as day/month/year triplets. The
    // highest promotion on or before the campaign date is the displayed rank.
    private string CurrentCommanderRank(IReadOnlyList<string> lines, int start)
    {
        if (GameDate is null) return string.Empty;
        var promotions = new (int Offset, string Rank)[] { (17,"Lt"), (20,"Cpt"), (23,"Maj"), (26,"Lt Col"), (29,"Col"), (32,"BG"), (35,"MG"), (38,"Lt Gen"), (41,"Gen") };
        var current = string.Empty;
        foreach (var (offset, rank) in promotions)
        {
            if (start + offset + 2 >= lines.Count) continue;
            if (!int.TryParse(lines[start + offset], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day) || !int.TryParse(lines[start + offset + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month) || !int.TryParse(lines[start + offset + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) || year <= 0) continue;
            try { if (new DateTime(year, month, day) <= GameDate.Value) current = rank; } catch { }
        }
        return current;
    }

    private string CommanderDisplay(int commanderId, string name)
        => _commanderRanks.TryGetValue(commanderId, out var rank) && !string.IsNullOrWhiteSpace(rank) ? $"{rank} {name}" : name;

    private void LoadConfig(string dir)
    {
        var weapons = FindConfigFile(dir, "weapons.txt");
        if (weapons is not null) ParseWeapons(weapons);
    }

    private void ParseWeapons(string file)
    {
        var lines = File.ReadAllLines(file);
        if (lines.Length == 0 || !int.TryParse(lines[0], out var count)) return;
        const int fields = 53;
        for (var i = 0; i < count && 1 + (i + 1) * fields <= lines.Length; i++)
        {
            var s = 1 + i * fields;
            if (!int.TryParse(lines[s].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            var name = lines[s + 1];
            var weaponClass = int.TryParse(lines[s + 2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var wc) ? wc : -1;
            var weaponGroup = int.TryParse(lines[s + 3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var wg) ? wg : -1;
            int? unitType = weaponGroup switch { 0 => 0, 1 => 1, 2 => 2, 3 => 2, _ => null };
            if (string.IsNullOrWhiteSpace(name)) continue;
            _weaponNames[id] = name;
            _weaponClasses[id] = weaponClass >= 0 ? weaponClass : null;
            _weaponUnitTypes[id] = unitType;
        }
    }

    public Dictionary<string, int> ReadUnitMaximums()
    {
        var result = new Dictionary<string, int> { ["Infantry"] = 2000, ["Cavalry"] = 2000, ["Artillery"] = 240 };
        if (ConfigDirectory is null) return result;
        var file = SaveDirectory is not null?CampaignRules.Resolve(SaveDirectory,ConfigDirectory,"unitprefs.txt"):FindConfigFile(ConfigDirectory, "unitprefs.txt");
        if (file is null) return result;
        var lines = File.ReadAllLines(file);
        for (var i = 0; i < lines.Length - 3; i++)
        {
            if (!lines[i].StartsWith("Unit Type ", StringComparison.OrdinalIgnoreCase) || !lines[i + 2].Contains("Maximum Unit Size", StringComparison.OrdinalIgnoreCase)) continue;
            var name = lines[i + 1].Trim();
            if (int.TryParse(lines[i + 3], out var max) && result.ContainsKey(name) && max > 0) result[name] = max;
        }
        return result;
    }

    private void InferStateNamesFromServiceHistory(IReadOnlyList<string> regimentLines)
    {
        var votes = new Dictionary<int, Dictionary<string, int>>();
        var rx = new Regex(@"(?:Raised|Formed).*?\bin\s+([A-Za-z .'-]+?)(?:\\n|$|,)", RegexOptions.IgnoreCase);
        foreach (var unit in _units)
        {
            var history = regimentLines[unit.RegimentLineStart + 31].Split("\\n", StringSplitOptions.None)[0];
            var match = rx.Match(history);
            if (!match.Success) continue;
            var name = match.Groups[1].Value.Trim().TrimEnd('.');
            if (name.Length < 2 || name.Length > 40) continue;
            if (!votes.TryGetValue(unit.StateId, out var dict)) votes[unit.StateId] = dict = new(StringComparer.OrdinalIgnoreCase);
            dict[name] = dict.GetValueOrDefault(name) + 1;
        }
        foreach (var (id, dict) in votes)
        {
            if (dict.Count != 1) continue; // Conflicting historical origins cannot establish a current State_ID mapping.
            var name = dict.Single().Key;
            if (CanonicalStates.ContainsKey(id)) continue;
            _stateNames[id] = name;
            _stateAbbreviations[id] = MakeAbbreviation(name);
        }
    }

    private void LoadCanonicalStateNames()
    {
        foreach (var state in CanonicalStates.Values)
        {
            _stateNames[state.Id] = state.Name;
            _stateAbbreviations[state.Id] = state.Abbreviation;
        }
    }

    private void MatchPathRecords(IReadOnlyList<string> pathLines)
    {
        var lookup = new Dictionary<(string Name, int Commander), List<int>>();
        for (var i = 0; i + 14 < pathLines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(pathLines[i])) continue;
            if (!int.TryParse(pathLines[i + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var commander)) continue;
            if (!double.TryParse(pathLines[i + 14], NumberStyles.Float, CultureInfo.InvariantCulture, out var transfer) || !double.IsFinite(transfer)) continue;
            var key = (pathLines[i], commander);
            if (!lookup.TryGetValue(key, out var list)) lookup[key] = list = new();
            list.Add(i);
        }

        foreach (var unit in _units)
        {
            unit.PathNameLineIndex = null;
            if (!lookup.TryGetValue((unit.Name, unit.CommanderId), out var candidates) || candidates.Count == 0)
            {
                unit.PathLinkStatus = PathLinkStatus.Missing;
                unit.PathLinkMessage = "No matching paths.dat record was found by unit name and commander.";
                continue;
            }
            if (candidates.Count == 1)
            {
                unit.PathNameLineIndex = candidates[0];
                unit.PathLinkStatus = PathLinkStatus.Confirmed;
                unit.PathLinkMessage = "Unique paths.dat record confirmed by unit name and commander.";
                continue;
            }

            var exactTransferMatches = candidates.Where(i => Math.Abs(RequiredFiniteDouble(pathLines[i + 14], "paths.dat Transfer_Time") - unit.TransferTimeRaw) <= 0.000001).ToList();
            if (exactTransferMatches.Count == 1)
            {
                unit.PathNameLineIndex = exactTransferMatches[0];
                unit.PathLinkStatus = PathLinkStatus.Confirmed;
                unit.PathLinkMessage = "Unique paths.dat record confirmed by name, commander, and matching transfer time.";
            }
            else
            {
                unit.PathLinkStatus = PathLinkStatus.Ambiguous;
                unit.PathLinkMessage = $"{candidates.Count} paths.dat candidates match this unit; the editor will not guess which one to write.";
            }
        }
    }

    private void ResolveDisplayNames()
    {
        foreach (var unit in _units)
        {
            unit.CommanderName = _commanders.GetValueOrDefault(unit.CommanderId, $"Commander #{unit.CommanderId}");
            unit.CommanderDisplayName = CommanderDisplay(unit.CommanderId, unit.CommanderName);
            unit.HomeStateName = _stateNames.GetValueOrDefault(unit.StateId, $"Unknown State (ID {unit.StateId})");
            unit.WeaponName = _weaponNames.GetValueOrDefault(unit.WeaponId, $"Weapon #{unit.WeaponId}");
        }
    }

    private static int PatchRegiment(List<string> lines, CombatUnitNode unit)
    {
        var s = unit.RegimentLineStart;
        var patched = 0;
        void Set(int offset, string value)
        {
            if (lines[s + offset] == value) return;
            lines[s + offset] = value;
            patched++;
        }

        if (unit.HasUnsavedChange("Name"))
        {
            EnsureLineSafe(unit.Name, $"Unit name for {unit.UnitId}");
            Set(1, unit.Name);
            // GTCW uses Override_Name in the OOB/tree presentation when present,
            // while its detail view can continue to show Unit_Name.  Keep them in
            // lockstep for an editor-directed rename.
            Set(2, unit.Name);
        }
        if (unit.HasUnsavedChange("Parent")) Set(3, unit.ParentId.ToString(CultureInfo.InvariantCulture));
        if (unit.HasUnsavedChange("FieldStrength") || unit.HasUnsavedChange("Casualties"))
        {
            Set(6, unit.TotalMenRaw.ToString(CultureInfo.InvariantCulture));
            Set(9, unit.CasualtyRatioRaw.ToString("0.###############", CultureInfo.InvariantCulture));
        }
        if (unit.HasUnsavedChange("Experience")) Set(10, unit.ExperienceRaw.ToString("0.###############", CultureInfo.InvariantCulture));
        if (unit.HasUnsavedChange("Weapon")) Set(13, unit.WeaponId.ToString(CultureInfo.InvariantCulture));
        if (unit.HasUnsavedChange("EnlistDate"))
        {
            EnsureLineSafe(unit.EnlistDateRaw, $"Enlistment start date for {unit.UnitId}");
            Set(26, unit.EnlistDateRaw);
        }
        if (unit.HasUnsavedChange("Contract") || unit.HasUnsavedChange("ContractRemaining")) Set(27, unit.ContractMonths.ToString(CultureInfo.InvariantCulture));
        if (unit.HasUnsavedChange("HomeState")) Set(29, unit.StateId.ToString(CultureInfo.InvariantCulture));
        if (unit.HasUnsavedChange("ETA")) Set(33, unit.TransferTimeRaw.ToString("0.###############", CultureInfo.InvariantCulture));
        return patched;
    }

    private static int PatchGroup(List<string> lines, GroupNode group)
    {
        var s = group.GroupLineStart;
        var patched = 0;
        if (lines[s + 1] != group.Name) { EnsureLineSafe(group.Name, $"Group name for {group.GroupId}"); lines[s + 1] = group.Name; patched++; }
        var value = group.ParentId.ToString(CultureInfo.InvariantCulture);
        if (lines[s + 2] != value) { lines[s + 2] = value; patched++; }
        return patched;
    }

    private void ReorderRegimentRecords(List<string> lines)
    {
        var header = lines[0];
        var trailing = lines.Skip(1 + _units.Count * RegimentFields).ToList();
        var blocks = _units.ToDictionary(u => u.UnitId, u => lines.Skip(u.RegimentLineStart).Take(RegimentFields).ToList());
        var ordered = _units
            .OrderBy(u => u.ParentId)
            .ThenBy(u => u.EditorOrder)
            .ThenBy(u => u.RegimentLineStart)
            .ToList();
        lines.Clear(); lines.Add(header);
        foreach (var unit in ordered)
        {
            lines.AddRange(blocks[unit.UnitId]);
        }
        lines.AddRange(trailing);
    }

    private void ReorderGroupRecords(List<string> lines)
    {
        var header = lines[0];
        var tail = lines.Skip(1 + _groups.Count * GroupFields).ToList();
        var blocks = _groups.Values.ToDictionary(g => g.GroupId, g => lines.Skip(g.GroupLineStart).Take(GroupFields).ToArray());
        // Reorder only within each parent's existing slots; unrelated record blocks stay put.
        var queues = _groups.Values.GroupBy(g => g.ParentId).ToDictionary(g => g.Key,
            g => new Queue<GroupNode>(g.OrderBy(n => n.EditorOrder).ThenBy(n => n.GroupLineStart)));
        var slots = _groups.Values.OrderBy(g => g.GroupLineStart).ToList();
        lines.Clear(); lines.Add(header);
        foreach (var slot in slots) lines.AddRange(blocks[queues[slot.ParentId].Dequeue().GroupId]);
        lines.AddRange(tail);
    }

    private static void ValidateGroupLines(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0 || !int.TryParse(lines[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 0)
            throw new InvalidDataException("Generated groups.dat counter is invalid.");
        if (lines.Count < 1 + count * GroupFields) throw new InvalidDataException("Generated groups.dat is shorter than its record counter requires.");
        for (var i = 0; i < count; i++)
        {
            var s = 1 + i * GroupFields;
            _ = RequiredInt(lines[s], $"Generated group {i} Group_ID");
            _ = RequiredInt(lines[s + 2], $"Generated group {i} Parent_ID");
        }
    }

    private static int PatchPath(List<string> lines, CombatUnitNode unit, List<string> warnings)
    {
        var needsName = unit.HasUnsavedChange("Name");
        var needsEta = unit.HasUnsavedChange("ETA");
        if (!needsName && !needsEta) return 0;
        if (unit.PathLinkStatus != PathLinkStatus.Confirmed || unit.PathNameLineIndex is not int n)
        {
            if (needsName) warnings.Add($"{unit.Name}: path name was not synchronized because its paths.dat link is {unit.PathLinkStatus}.");
            return 0;
        }
        if (n < 0 || n + 14 >= lines.Count) throw new InvalidDataException($"Confirmed paths.dat link for {unit.Name} is outside the file bounds.");
        var patched = 0;
        if (needsName && lines[n] != unit.Name) { EnsureLineSafe(unit.Name, $"Path unit name for {unit.UnitId}"); lines[n] = unit.Name; patched++; }
        if (needsEta)
        {
            var value = unit.TransferTimeRaw.ToString("0.###############", CultureInfo.InvariantCulture);
            if (lines[n + 14] != value) { lines[n + 14] = value; patched++; }
        }
        return patched;
    }

    private static void ValidateRegimentLines(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0 || !int.TryParse(lines[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 0)
            throw new InvalidDataException("Generated regiments.dat counter is invalid.");
        if (lines.Count < 1 + count * RegimentFields) throw new InvalidDataException("Generated regiments.dat is shorter than its record counter requires.");
        for (var i = 0; i < count; i++)
        {
            var s = 1 + i * RegimentFields;
            EnsureLineSafe(lines[s + 1], $"Generated unit {i} name");
            _ = RequiredInt(lines[s], $"Generated unit {i} Unit_ID");
            _ = RequiredInt(lines[s + 3], $"Generated unit {i} Parent_ID");
            _ = RequiredInt(lines[s + 6], $"Generated unit {i} Total_Men");
            _ = RequiredFiniteDouble(lines[s + 9], $"Generated unit {i} SickRatio");
            _ = RequiredFiniteDouble(lines[s + 10], $"Generated unit {i} Experience");
            _ = RequiredInt(lines[s + 13], $"Generated unit {i} Weapon_ID");
            _ = RequiredInt(lines[s + 27], $"Generated unit {i} Contract");
            _ = RequiredInt(lines[s + 29], $"Generated unit {i} State_ID");
            _ = RequiredFiniteDouble(lines[s + 33], $"Generated unit {i} Transfer_Time");
        }
    }

    private static void ValidatePatchedPathLines(IReadOnlyList<string> lines, IEnumerable<CombatUnitNode> changedUnits)
    {
        foreach (var unit in changedUnits.Where(u => (u.HasUnsavedChange("ETA") || u.HasUnsavedChange("Name")) && u.PathLinkStatus == PathLinkStatus.Confirmed))
        {
            if (unit.PathNameLineIndex is not int n || n < 0 || n + 14 >= lines.Count)
                throw new InvalidDataException($"Generated paths.dat link for {unit.Name} is invalid.");
            EnsureLineSafe(lines[n], $"Generated path name for {unit.Name}");
            _ = RequiredFiniteDouble(lines[n + 14], $"Generated paths.dat Transfer_Time for {unit.Name}");
        }
    }

    private string CreateBackup(IEnumerable<string> names)
    {
        var backupRoot = Path.Combine(SaveDirectory!, "Aide-de-Camp_Backups");
        Directory.CreateDirectory(backupRoot);
        var backupDirectory = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        var suffix = 1;
        while (Directory.Exists(backupDirectory)) backupDirectory = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + $"_{suffix++}");
        Directory.CreateDirectory(backupDirectory);
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var source = Path.Combine(SaveDirectory!, name);
            if (!File.Exists(source)) throw new IOException($"Backup source disappeared: {name}");
            var backup = Path.Combine(backupDirectory, name);
            File.Copy(source, backup, false);
            if (ComputeHash(source) != ComputeHash(backup)) throw new IOException($"Backup verification failed: {name}. Save aborted.");
        }
        foreach (var directory in Directory.EnumerateDirectories(SaveDirectory!)) {
            if (string.Equals(Path.GetFileName(directory), "Aide-de-Camp_Backups", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(directory),"OOBEditor_Backups",StringComparison.OrdinalIgnoreCase)) continue;
            CopyBackupDirectory(directory, Path.Combine(backupDirectory, Path.GetFileName(directory)));
        }
        return backupDirectory;
    }

    private static void CopyBackupDirectory(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Backup cannot safely traverse a linked save directory: " + source);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) {
            var copy = Path.Combine(destination, Path.GetFileName(file)); File.Copy(file, copy, false);
            if (ComputeHash(file) != ComputeHash(copy)) throw new IOException("Backup verification failed: " + file);
        }
        foreach (var dir in Directory.EnumerateDirectories(source)) CopyBackupDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private void CaptureFileSignatures()
    {
        _loadedFileHashes.Clear();
        foreach (var name in MonitoredSaveFiles)
        {
            var path = Path.Combine(SaveDirectory!, name);
            _loadedFileHashes[name] = File.Exists(path) ? ComputeHash(path) : "<missing>";
        }
    }

    private void EnsureFilesUnchanged()
    {
        var changed = new List<string>();
        foreach (var name in MonitoredSaveFiles)
        {
            var path = Path.Combine(SaveDirectory!, name);
            var current = File.Exists(path) ? ComputeHash(path) : "<missing>";
            if (!_loadedFileHashes.TryGetValue(name, out var original) || !string.Equals(current, original, StringComparison.Ordinal)) changed.Add(name);
        }
        if (changed.Count > 0)
            throw new IOException("The save changed on disk after it was loaded (" + string.Join(", ", changed) + "). Reload the save before applying editor changes so the game and editor do not overwrite each other.");
    }

    private static string ComputeHash(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void SortTree(List<OobNode> roots)
    {
        roots.Sort(CompareNodes);
        foreach (var root in roots) SortChildren(root);
    }

    private static void SortChildren(OobNode node)
    {
        var sorted = node.Children.OrderByDescending(c => c.IsGroup).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        node.Children.Clear();
        foreach (var child in sorted) { node.Children.Add(child); SortChildren(child); }
    }

    private static int CompareNodes(OobNode a, OobNode b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);

    private string PrepareSaveDirectory(string path)
    {
        IsReadOnlySave = false;
        if (Directory.Exists(path)) return NormalizeSaveFolder(path);
        if (File.Exists(path) && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            IsReadOnlySave = true;
            _tempExtractDir = Path.Combine(Path.GetTempPath(), "AideDeCamp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempExtractDir);
            ZipFile.ExtractToDirectory(path, _tempExtractDir);
            return NormalizeSaveFolder(_tempExtractDir);
        }
        throw new DirectoryNotFoundException(path);
    }

    private static string NormalizeSaveFolder(string dir)
    {
        if (File.Exists(Path.Combine(dir, "regiments.dat"))) return dir;
        var candidates = Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).Where(d => File.Exists(Path.Combine(d, "regiments.dat"))).ToList();
        return candidates.Count == 1 ? candidates[0] : throw new InvalidDataException("Could not uniquely locate regiments.dat in the selected save.");
    }

    private void RequireFile(string name)
    {
        if (!File.Exists(Path.Combine(SaveDirectory!, name))) throw new FileNotFoundException($"Required save file not found: {name}");
    }

    private static string? FindConfigFile(string dir, string name) => Directory.GetFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault();

    private static int RequiredInt(string value, string context)
    {
        if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            throw new InvalidDataException($"{context} is not a valid integer: '{value}'.");
        return parsed;
    }

    private static double RequiredFiniteDouble(string value, string context)
    {
        if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))
            throw new InvalidDataException($"{context} is not a valid finite number: '{value}'.");
        return parsed;
    }

    private static void EnsureLineSafe(string value, string context)
    {
        if (value.IndexOf('\0') >= 0 || value.Contains('\r') || value.Contains('\n'))
            throw new InvalidDataException($"{context} contains a line break or NUL character and cannot be serialized safely.");
    }

    private static string ExtractRaisedText(string history) => history.Split("\\n", StringSplitOptions.None).FirstOrDefault()?.Trim() ?? string.Empty;

    private static string MakeAbbreviation(string name)
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alabama"]="AL",["Arkansas"]="AR",["California"]="CA",["Connecticut"]="CT",["Delaware"]="DE",["Florida"]="FL",
            ["Georgia"]="GA",["Illinois"]="IL",["Indiana"]="IN",["Iowa"]="IA",["Kansas"]="KS",["Kentucky"]="KY",["Louisiana"]="LA",
            ["Maine"]="ME",["Maryland"]="MD",["Massachusetts"]="MA",["Michigan"]="MI",["Minnesota"]="MN",["Mississippi"]="MS",
            ["Missouri"]="MO",["New Hampshire"]="NH",["New Jersey"]="NJ",["New York"]="NY",["North Carolina"]="NC",["Ohio"]="OH",
            ["Oregon"]="OR",["Pennsylvania"]="PA",["Rhode Island"]="RI",["South Carolina"]="SC",["Tennessee"]="TN",["Texas"]="TX",
            ["Vermont"]="VT",["Virginia"]="VA",["West Virginia"]="WV",["Wisconsin"]="WI",["District of Columbia"]="DC"
        };
        if (known.TryGetValue(name, out var abbreviation)) return abbreviation;
        return string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0])));
    }

    private void Reset()
    {
        _commanders.Clear();
        _commanderRanks.Clear();
        _weaponNames.Clear();
        _weaponUnitTypes.Clear();
        _weaponClasses.Clear();
        _stateNames.Clear();
        _stateAbbreviations.Clear();
        _groups.Clear();
        _units.Clear();
        _loadedFileHashes.Clear();
        _regimentBuffer = null;
        _groupBuffer = null;
        _pathBuffer = null;
        Management = null;
        Progression=null;
        SaveDirectory = null;
        ConfigDirectory = null;
        GameDateText = string.Empty;
        GameDate = null;
        IsReadOnlySave = false;
        if (_tempExtractDir is not null && Directory.Exists(_tempExtractDir))
        {
            try { Directory.Delete(_tempExtractDir, true); } catch { }
        }
        _tempExtractDir = null;
    }

    public void Dispose() => Reset();
}
