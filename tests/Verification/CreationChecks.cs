using AideDeCamp.Models;
using AideDeCamp.Services;

internal static class CreationChecks
{
    public static async Task Run(string source, string config, Action<bool, string> check)
    {
        var copy = Path.Combine(Path.GetTempPath(), "adc-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(copy);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
        try {
            using var data = new GrandTacticianDataService(); await data.LoadAsync(copy, config);
            var original = Directory.EnumerateFiles(copy).ToDictionary(f => Path.GetFileName(f)!, File.ReadAllBytes);
            var officers = data.GetCreationOfficers();
            var max = data.GetCreationMaximums();
            check(max.Count == 3 && max.Values.All(v => v > 0), "Creation sizes come from actual configuration");
            check(data.GetCreationPerks(false).Count == 57 && data.GetCreationPerks(true).Count == 54, "Separate combat and HQ perk catalogs parsed");
            var assigned = data.Units.Select(u => u.CommanderId).Concat(data.Groups.Values.Select(g => g.CommanderId)).ToHashSet();
            var officer = officers.First(o => o.Faction == 0 && !assigned.Contains(o.Id));
            var parent = data.Groups.Values.First(g => g.Nation == 0 && g.UnitTier == 14 && g.ParentId >= 0);
            var b = new UnitBlueprint { Name = "ADC automated create check", Faction = 0, NativeTier = 13, UnitType = 0, CommanderId = officer.Id, Strength = Math.Min(1000, max[0]), WeaponId = data.WeaponOptions.First(w => w.UnitType == 0).Id, HomeStateId = 17, Experience = 50, Training = 40, Perks = [new(2, 0, .25)], SupplyPercent = [50, 75, 25, 100] };
            var request = new CreationRequest(b, new(parent.GroupId));
            var fixtureLines = File.ReadAllLines(Path.Combine(source, "paths.dat"));
            var fixture = PathRecordParser.Parse(fixtureLines, strictFields: true).Single(r => r.Name == "ADC Batch New Experience" && r.CommanderId == 102 && r.UnitType == 0);
            var generated = CreationRecords.Path(b, "test history");
            var gameWritten = fixtureLines.Skip(fixture.Start).Take(fixture.End - fixture.Start).ToArray();
            var differences = Enumerable.Range(0, generated.Count).Where(i => generated[i] != gameWritten[i]).ToArray();
            Console.WriteLine("Path differences from accepted Test 14 record: " + string.Join(", ", differences.Select(i => $"{i}: {gameWritten[i]} -> {generated[i]}")));
            check(gameWritten.Length == generated.Count && differences.All(i => new[] {0, 1, 3, 30, 31, 215, 216, 217, 218, 237}.Contains(i)), "New path defaults independently match game-written Test 14 except explicit identity, stock, history and recalculation fields");
            var assessment = data.AssessCreation(request);
            check(assessment.CanCreate, "Valid creation assessment: " + string.Join("; ", assessment.Errors));
            check(assessment.Confirmations.Count > 0, "Untested mapped choices require confirmation");
            bool refused = false; try { data.CreateUnit(request, false, new()); } catch (InvalidOperationException) { refused = true; }
            check(refused && !data.HasCreationChanges, "No record mutation before confirmation");
            check(!data.AssessCreation(request with { Placement = new(null) }).CanCreate, "Unattached combat unit rejected");
            check(!data.AssessCreation(request with { Blueprint = b with { WeaponId = data.WeaponOptions.First(w => w.UnitType == 2).Id } }).CanCreate, "Artillery weapon rejected for infantry");
            check(!data.AssessCreation(request with { Blueprint = b with { Experience = double.NaN } }).CanCreate, "Nonfinite experience rejected");
            check(!data.AssessCreation(request with { Blueprint = b with { Faction = 1 } }).CanCreate, "Cross-faction parent and commander rejected");
            check(!data.AssessCreation(request with { Blueprint = b with { CommanderId = parent.CommanderId } }).CanCreate, "Already assigned commander rejected");
            check(!data.AssessCreation(request with { Blueprint = b with { HomeStateId = 41 } }).CanCreate, "Foreign state is excluded from domestic home-state choices");
            check(!data.AssessCreation(request with { Blueprint = b with { Strength = max[0] + 1 } }).CanCreate, "Creation enforces the configured size limit");
            check(data.AssessCreation(request with { Blueprint = b with { Perks = [new(9, 0, 0)] } }).Confirmations.Any(w => w.Contains("outside the game's offered list")), "Mapped off-branch perks require a specific confirmation");
            var malformedRegiment = new[] { "1" }.Concat(CreationRecords.Regiment(0, b, parent.GroupId, data.GameDate!.Value, (0, 0, 0))).ToArray();
            malformedRegiment[12] = "not training";
            bool badField = false; try { CreationRecords.ValidateFieldTypes(malformedRegiment, false); } catch (InvalidDataException) { badField = true; }
            check(badField, "Malformed typed regiment fields are rejected");
            var malformedPath = new[] { "1" }.Concat(generated).ToArray(); malformedPath[224] = "unknown";
            bool malformedRefused = false; try { PathRecordParser.Parse(malformedPath, strictFields: true); } catch (InvalidDataException) { malformedRefused = true; }
            check(malformedRefused, "Strict parser rejects malformed reserved Boolean rather than skipping it");
            var session = new EditSession(); int before = data.Units.Count;
            var unit = (CombatUnitNode)data.CreateUnit(request, true, session);
            check(data.Units.Count == before + 1 && data.HasCreationChanges && unit.HasSupplyStock && unit.SupplyStockAt(0) == b.Strength / 2, "New unit and full starting stock staged together");
            check(session.Undo(out _) && data.Units.Count == before && !data.HasCreationChanges, "One undo removes all creation records");
            check(session.Redo(out _) && data.Units.Count == before + 1, "One redo restores creation");
            var result = await data.SaveAsync(); data.MarkSavedStates();
            check(result.WroteFiles && File.Exists(Path.Combine(result.BackupDirectory!, "armygrouprefs.dat")), "Creation saved with complete backup");
            using (var reload = new GrandTacticianDataService()) {
                await reload.LoadAsync(copy, config); var loaded = reload.Units.Single(u => u.Name == b.Name);
                check(loaded.ExperienceRaw == 50 && loaded.ParentId == parent.GroupId && loaded.SupplyStockAt(2) == b.Strength / 4, "ADC reload preserves experience, numeric parent, and stocks");
                var raw = File.ReadAllLines(Path.Combine(copy, "regiments.dat")); int s = loaded.RegimentLineStart;
                check(raw[s + 11] == "40" && raw[s + 17] == "2" && raw[s + 19] == "0.25", "Independent field assertions preserve training and perk progress");
            }
            check(session.Undo(out _) && data.HasCreationChanges, "Creation can be undone after saving");
            await data.SaveAsync(); data.MarkSavedStates();
            foreach (var file in new[] { "groups.dat", "regiments.dat", "paths.dat", "armygrouprefs.dat" })
                check(original[file].SequenceEqual(File.ReadAllBytes(Path.Combine(copy, file))), "Undo and save restores byte-exact " + file);
            session.Redo(out _);
            session.Execute("Rename creation", new[] { unit }, () => unit.Name = "ADC edited creation");
            await data.SaveAsync(); data.MarkSavedStates();
            session.Undo(out _); session.Undo(out _); session.Redo(out _);
            await data.SaveAsync(); data.MarkSavedStates();
            using (var reload = new GrandTacticianDataService()) { await reload.LoadAsync(copy, config); check(reload.Units.Any(u => u.Name == b.Name) && reload.Units.All(u => u.Name != "ADC edited creation"), "Undo saved rename then undo/redo creation preserves semantic name and path state"); }
            session.Undo(out _); await data.SaveAsync(); data.MarkSavedStates();
            var town = data.GetPlayableTownsByState().First(t => t.Location.Owner == 0);
            var hq = b with { Headquarters = true, Name = "ADC automated HQ check", NativeTier = 16, WeaponId = -1, HomeStateId = -1, Strength = 0, Training = 0, Experience = 0, Perks = [new(), new(), new(), new()] };
            var hqRequest = new CreationRequest(hq, new(null, town));
            check(!data.AssessCreation(hqRequest with { Placement = new(parent.GroupId) }).CanCreate, "Army under division rejected");
            check(!data.AssessCreation(hqRequest with { Placement = new(null) }).CanCreate, "Unresolved town rejected");
            check(!data.AssessCreation(hqRequest with { Blueprint = hq with { WeaponId = 25 } }).CanCreate, "HQ with a combat weapon rejected at service boundary");
            var hqCheck = data.AssessCreation(hqRequest); check(hqCheck.CanCreate, "Town HQ assessment: " + string.Join("; ", hqCheck.Errors));
            var group = (GroupNode)data.CreateUnit(hqRequest, true, session);
            await data.SaveAsync(); data.MarkSavedStates();
            using (var reload = new GrandTacticianDataService()) { await reload.LoadAsync(copy, config); check(reload.Groups.Values.Any(g => g.Name == hq.Name && g.ParentId == -1), "Independent HQ reloads"); }
            var battle = File.ReadAllLines(Path.Combine(copy, "battledata.dat")); var deployment = Enumerable.Range(0, int.Parse(battle[42])).Select(i => 43 + 15 * i).Single(s => battle[s] == group.GroupId.ToString() && battle[s + 13] == "True");
            check(float.Parse(battle[deployment + 2], System.Globalization.CultureInfo.InvariantCulture) == town.Location.X && float.Parse(battle[deployment + 3], System.Globalization.CultureInfo.InvariantCulture) == -town.Location.Z, "Town deployment uses verified coordinates with map Z conversion");
            session.Undo(out _); await data.SaveAsync();
            check(original["battledata.dat"].SequenceEqual(File.ReadAllBytes(Path.Combine(copy, "battledata.dat"))), "HQ undo restores deployment and economy tail byte-exact");
            File.WriteAllText(Path.Combine(copy, "version.dat"), "9.999");
            check(!data.AssessCreation(request).CanCreate, "Unknown version layout cannot be bypassed by confirmation");
        } finally {
            if (Path.GetFullPath(copy).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(copy, true);
        }
    }
}
