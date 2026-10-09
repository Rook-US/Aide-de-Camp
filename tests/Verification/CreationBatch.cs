using System.Text.Json;
using AideDeCamp.Models;
using AideDeCamp.Services;

internal static class CreationBatch
{
    public static async Task AuditResave(string input, string resave, string config, Action<bool, string> check)
    {
        if (Path.GetFullPath(input).Equals(Path.GetFullPath(resave), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("A separate game resave is required.");
        string[][] Records(string folder, string file, int width) {
            var lines = File.ReadAllLines(Path.Combine(folder, file));
            if (File.ReadAllText(Path.Combine(folder, "version.dat")).Trim() != "1.142" || lines.Length != 1 + int.Parse(lines[0]) * width) throw new InvalidDataException("Unrecognized records.");
            return Enumerable.Range(0, int.Parse(lines[0])).Select(i => lines.Skip(1 + i * width).Take(width).ToArray()).ToArray();
        }
        double Number(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        bool Equal(string a, string b) => a == b || double.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) && double.TryParse(b, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) && Math.Abs(x - y) < .0001;
        var beforeGroups = Records(input, "groups.dat", 32); var groups = Records(resave, "groups.dat", 32);
        var beforeUnits = Records(input, "regiments.dat", 39); var units = Records(resave, "regiments.dat", 39);
        var pathLines = File.ReadAllLines(Path.Combine(resave, "paths.dat"));
        var paths = PathRecordParser.Parse(pathLines, strictFields: true);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(input, "ADC-creation-test.json")));
        using var loaded = new GrandTacticianDataService(); await loaded.LoadAsync(resave, config);
        Console.WriteLine("READ-ONLY game resave audit: " + resave);
        Console.WriteLine($"Counts: {groups.Length} HQs, {units.Length} combat units, {paths.Count} complete paths");
        foreach (var entry in manifest.RootElement.EnumerateArray()) {
            var b = entry.GetProperty("blueprint").Deserialize<UnitBlueprint>()!;
            int commanderField = b.Headquarters ? 4 : 5;
            string[] Match(string[][] records, string[][] parentRecords) => records.Single(r => r[1] == b.Name && (b.Headquarters ? int.Parse(r[3]) == b.Faction && int.Parse(r[17]) == b.NativeTier : int.Parse(r[4]) == b.UnitType && int.Parse(r[24]) == b.NativeTier && int.Parse(parentRecords.Single(g => g[0] == r[3])[3]) == b.Faction));
            var old = Match(b.Headquarters ? beforeGroups : beforeUnits, beforeGroups); var current = Match(b.Headquarters ? groups : units, groups);
            Console.WriteLine($"ITEM {b.Name}: ID {old[0]} -> {current[0]}");
            int[] fields = b.Headquarters ? [3, 15, 16, 17, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31] : [1, 2, 4, 6, 10, 11, 13, 14, 15, 16, 23, 24, 26, 27, 29, 30];
            if (!b.Headquarters && !(b.Faction == 1 && b.Perks[0].Id == -1 && b.Perks[0].Progress == 1)) fields = fields.Concat(new[] {17, 18, 19}).ToArray();
            check(fields.All(f => Equal(old[f], current[f])), b.Name + " retained requested identity, equipment, experience, recruitment and perk fields");
            if (b.Faction == 0) check(old[commanderField] == current[commanderField], b.Name + " retained selected Union commander");
            else {
                var officers = Records(resave, "commanders.txt", 66);
                var assigned = officers.Single(o => o[0] == current[commanderField]);
                check(int.Parse(assigned[4]) == b.Faction, b.Name + " has a resolved commander in its faction");
                Console.WriteLine($" COMMANDER: requested {entry.GetProperty("commander").GetString()} #{old[commanderField]}; game saved {assigned[3]} #{current[commanderField]}");
                if (!b.Headquarters) Console.WriteLine($" PERK: requested {old[17]}/{old[18]}/{old[19]}; game saved {current[17]}/{current[18]}/{current[19]}");
            }
            int parentField = b.Headquarters ? 2 : 3;
            if (int.Parse(old[parentField]) >= 0) {
                var parent = beforeGroups.Single(g => g[0] == old[parentField]);
                var nowParent = groups.Single(g => g[0] == current[parentField]);
                check(parent[1] == nowParent[1] && parent[3] == nowParent[3] && parent[17] == nowParent[17] && (b.Faction == 1 || parent[4] == nowParent[4]), b.Name + " retained explicit parent identity after game renumbering");
            } else check(current[parentField] == "-1", b.Name + " remains independent");
            var path = paths.Single(p => p.Name == b.Name && p.CommanderId == int.Parse(current[commanderField]));
            check(b.Headquarters || path.UnitType == b.UnitType && path.Abbreviation == b.Name, b.Name + " has a complete game-written path");
            if (!b.Headquarters) {
                check(path.SupplyStock is not null && path.SupplyStock.Select((n, i) => Math.Abs(n - b.Strength * b.SupplyPercent[i] / 100) < .0001).All(x => x), b.Name + " retained all four requested stock amounts");
                Console.WriteLine(" stock=" + string.Join(",", path.SupplyStock!) + $" experience={current[10]} training={current[11]} perk={current[17]}/{current[18]}/{current[19]}");
            }
            var town = entry.GetProperty("placement").GetProperty("Town");
            if (town.ValueKind != JsonValueKind.Null) {
                var battle = File.ReadAllLines(Path.Combine(resave, "battledata.dat")); CreationRecords.DeploymentTail(battle);
                var deployment = Enumerable.Range(0, int.Parse(battle[42])).Select(i => battle.Skip(43 + 15 * i).Take(15).ToArray()).Single(r => r[0] == current[0] && r[13] == "True");
                var location = town.GetProperty("Location");
                check(Math.Abs(Number(deployment[2]) - location.GetProperty("X").GetDouble()) < .01 && Math.Abs(Number(deployment[3]) + location.GetProperty("Z").GetDouble()) < .01, b.Name + " retained " + location.GetProperty("Name").GetString() + " deployment");
            }
            Console.WriteLine(" normalized fields: " + string.Join("; ", Enumerable.Range(0, old.Length).Where(i => !Equal(old[i], current[i])).Select(i => $"{i}: {old[i]} -> {current[i]}")));
        }
        check(loaded.Groups.Values.Count(g => g.Name.StartsWith("ADC Tool ")) == 3 && loaded.Units.Count(u => u.Name.StartsWith("ADC Tool ")) == 5, "All eight creations reload through ADC");
        foreach (string file in new[] {"groups.dat", "regiments.dat", "paths.dat", "battledata.dat", "armygrouprefs.dat"}) Console.WriteLine(file + " SHA256 " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(resave, file)))));
    }
    // Makes one disposable game test through the production backend, never by
    // patching an original save or bypassing its structural assessment.
    public static async Task Build(string source, string config, string target)
    {
        if (Directory.Exists(target)) throw new IOException("Refusing to replace an existing test folder.");
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(source).Where(d => !Path.GetFileName(d).EndsWith("Backups", StringComparison.OrdinalIgnoreCase)))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        using var data = new GrandTacticianDataService(); await data.LoadAsync(target, config);
        var session = new EditSession(); var items = new List<object>();
        CreationOfficer Officer(int side, bool hq) {
            var assigned = data.Groups.Values.Select(g => g.CommanderId).Concat(data.Units.Select(u => u.CommanderId)).ToHashSet();
            return data.GetCreationOfficers().First(o => o.Faction == side && o.Active && o.Status == 0 && !assigned.Contains(o.Id) && (hq ? o.Rank >= 6 : o.Rank >= 4));
        }
        UnitBlueprint Design(string name, int side, bool hq, int type = 0) {
            var uniform = data.GetCreationUniforms().First(u => u.Faction == side);
            return new() { Name = name, Faction = side, Headquarters = hq, NativeTier = hq ? 16 : type == 2 ? 11 : 13, UnitType = type,
                CommanderId = Officer(side, hq).Id, Strength = hq ? 0 : type == 2 ? 60 : type == 1 ? 600 : 1000,
                WeaponId = hq ? -1 : type == 2 ? 4 : type == 1 ? 25 : 14, HomeStateId = hq ? -1 : side == 0 ? 31 : 38,
                Coat = uniform.Coat, Trousers = uniform.Trousers, ColorVariation = uniform.Variation,
                Perks = hq ? [new(), new(), new(), new()] : [new()] };
        }
        OobNode Add(UnitBlueprint b, CreationPlacement placement) {
            var request = new CreationRequest(b, placement); var review = data.AssessCreation(request);
            if (!review.CanCreate) throw new InvalidDataException(string.Join("; ", review.Errors));
            var node = data.CreateUnit(request, true, session); // Authorized experimental copy; record every confirmation in manifest.
            items.Add(new { blueprint = b, placement, commander = data.GetCreationOfficers().Single(o => o.Id == b.CommanderId).Name, confirmations = review.Confirmations });
            return node;
        }
        var philadelphia = data.GetCreationTowns().Single(t => t.Location.Name == "Philadelphia");
        var union = (GroupNode)Add(Design("ADC Tool Union HQ", 0, true) with { Perks = [new(0, 0, 0), new(-1, 0, 1), new(), new()] }, new(null, philadelphia));
        var division = (GroupNode)Add(Design("ADC Tool Division", 0, true) with { NativeTier = 14 }, new(union.GroupId));
        Add(Design("ADC Tool Draft Infantry", 0, false) with { Training = 60, Experience = 60, ContractMonths = 24, RecruitingType = 1, SupplyPercent = [50, 50, 50, 50], Perks = [new(2, 1, .25)] }, new(division.GroupId));
        Add(Design("ADC Tool Veteran Cavalry", 0, false, 1) with { Training = 80, Experience = 80, SupplyPercent = [50, 50, 50, 50], Perks = [new(9, 1, .35)] }, new(division.GroupId));
        Add(Design("ADC Tool Horse Artillery", 0, false, 2) with { Training = 40, Experience = 40, HorseArtillery = true, SupplyPercent = [50, 50, 50, 50], Perks = [new(14, 0, .5)] }, new(division.GroupId));
        var fort = data.GetExistingGarrisons().Single(f => f.FortName == "Fort Monroe");
        Add(Design("ADC Tool Fort Howitzers", 0, false, 2) with { WeaponId = 41, HomeStateId = 17 }, new(fort.GroupId));
        var richmond = data.GetCreationTowns().Single(t => t.Location.Name == "Richmond");
        var confederate = (GroupNode)Add(Design("ADC Tool Confederate HQ", 1, true) with { NativeTier = 15, Perks = [new(1, 0, 0), new(), new(), new()] }, new(null, richmond));
        Add(Design("ADC Tool Confederate Infantry", 1, false) with { Training = 20, Experience = 20, ContractMonths = 36, Perks = [new(-1, 0, 1)] }, new(confederate.GroupId));
        var result = await data.SaveAsync(); data.MarkSavedStates();
        if (!result.WroteFiles) throw new InvalidOperationException("Batch did not write any files.");
        using (var reload = new GrandTacticianDataService()) {
            await reload.LoadAsync(target, config);
            if (reload.Units.Count(u => u.Name.StartsWith("ADC Tool ")) != 5 || reload.Groups.Values.Count(g => g.Name.StartsWith("ADC Tool ")) != 3)
                throw new InvalidDataException("Batch reload count mismatch.");
        }
        var scenario = TextFileBuffer.Read(Path.Combine(target, "scenario.dat")); var lines = scenario.CloneLines();
        if (lines.Count < 25) throw new InvalidDataException("Missing test-label field.");
        lines[24] = "ADC Test 15 - Create Tool Batch"; scenario.WriteTo(Path.Combine(target, "scenario.dat"), lines);
        await File.WriteAllTextAsync(Path.Combine(target, "ADC-creation-test.json"), JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Combined production-backend test: " + target);
        Console.WriteLine("Three HQs, five combat units. Game confirmation pending; source save unchanged.");
    }
    private static void CopyDirectory(string source, string target) {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(source)) CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}
