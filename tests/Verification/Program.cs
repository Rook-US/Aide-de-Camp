using System.Text;
using System.Text.Json.Nodes;
using AideDeCamp.Models;
using AideDeCamp.Services;

int checks = 0;
void Check(bool test, string label) { if (!test) throw new Exception("FAILED: " + label); checks++; Console.WriteLine("PASS " + label); }
var temp = Path.Combine(Path.GetTempPath(), "gtcw-verification-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
try
{
    var editUnit = new CombatUnitNode { Name="Test", UnitType=0, UnitTier=13, TotalMenRaw=500, ContractMonths=12,
        EnlistDateRaw="4/1/1861", EnlistDate=new DateTime(1861,4,1), CampaignDate=new DateTime(1861,7,1),
        WeaponId=1, WeaponName="Musket", StateId=1, HomeStateName="Ohio", PathLinkStatus=PathLinkStatus.Confirmed };
    var editWeapons = new[] { new WeaponOption(1,"Musket",0), new WeaponOption(2,"Cannon",2) };
    var editStates = new[] { new StateOption(1,"Ohio","OH") };
    var typed = new TypedUnitEdit(editUnit); var editValidator = new EditValidationService();
    var selectionBattery=new CombatUnitNode{Name="Battery",UnitType=2,UnitTier=13,TotalMenRaw=100,WeaponId=2,WeaponName="Cannon",ConfiguredMaxStrength=300,ArtilleryCalculation=new ArtilleryRules(300,30,1)};
    var infantryBefore=UnitEditSnapshot.Capture(editUnit);var selectionBatteryBefore=UnitEditSnapshot.Capture(selectionBattery);
    var selection=SelectionEditPlan.Build(new[]{editUnit,selectionBattery},u=>u.UnitType==2?new Dictionary<string,string>{{"FieldStrength","180"},{"Experience","25"}}:new Dictionary<string,string>{{"FieldStrength","800"},{"Experience","30"}},editWeapons,editStates,editValidator);
    Check(selection.Errors.Count==0&&selection.Changes.Count==2&&selection.Tooltip.Contains("estimated guns"),"Mixed selection previews separate strength and gun results");
    Check(UnitEditSnapshot.Capture(editUnit)==infantryBefore&&UnitEditSnapshot.Capture(selectionBattery)==selectionBatteryBefore,"Selection preview does not mutate units");
    var selectionSession=new EditSession();selectionSession.Execute("Mixed edit",new[]{editUnit,selectionBattery},selection.Apply);
    Check(editUnit.FieldStrength==800&&selectionBattery.FieldStrength==180&&selectionBattery.ExperienceRaw==25,"Mixed selection applies each group's values to its own units");
    selectionSession.Undo(out _);Check(UnitEditSnapshot.Capture(editUnit)==infantryBefore&&UnitEditSnapshot.Capture(selectionBattery)==selectionBatteryBefore,"Mixed selection restores both types in one undo");
    var incompatible=SelectionEditPlan.Build(new[]{editUnit},u=>new Dictionary<string,string>{{"Weapon","Cannon"}},editWeapons,editStates,editValidator);
    Check(incompatible.Errors.Count>0,"Selection editor blocks cross-type weapon assignment");
    var invalidSelection=SelectionEditPlan.Build(new[]{editUnit,selectionBattery},u=>new Dictionary<string,string>{{"Experience",u.UnitType==2?"bad":"25"}},editWeapons,editStates,editValidator);
    bool refused=false;try{invalidSelection.Apply();}catch(InvalidOperationException){refused=true;}
    Check(refused&&UnitEditSnapshot.Capture(editUnit)==infantryBefore,"One invalid destination blocks the entire selected edit");
    var metadataFolder=Path.Combine(temp,"metadata");Directory.CreateDirectory(metadataFolder);var metadataLines=Enumerable.Repeat("",26).ToArray();metadataLines[0]="0";metadataLines[1]="Union";metadataLines[2]="8";metadataLines[3]="7";metadataLines[4]="1861";metadataLines[12]="001/G";metadataLines[24]="Before the battle";metadataLines[25]="Summer 1861";File.WriteAllLines(Path.Combine(metadataFolder,"scenario.dat"),metadataLines);
    var saveMetadataCheck=SaveMetadata.Read(metadataFolder);Check(saveMetadataCheck.Campaign=="G — Summer 1861"&&saveMetadataCheck.SaveName=="Before the battle"&&saveMetadataCheck.Date=="Jul 8, 1861"&&saveMetadataCheck.Faction=="Union","Save metadata uses campaign title, save label, campaign date and player faction");
    var descriptor=Path.Combine(metadataFolder,"ScenarioDescr.txt");File.WriteAllLines(descriptor,new[]{"//Name of Scenario for buttons ->","Summer 1861 Alternative"});Check(SaveMetadata.CampaignName(descriptor)=="Summer 1861 Alternative","Campaign label skips descriptor comment placeholders");
    typed.Fields["Experience"].Text="15B";
    typed.Validate(editWeapons,editStates,editValidator);
    Check(typed.Fields["Experience"].Result.Severity==ValidationSeverity.Error && editUnit.ExperienceRaw==0, "Malformed draft does not mutate numeric model");
    typed.Sync(); Check(typed.Fields["Experience"].Text=="15B", "Invalid draft survives view synchronization");
    typed.Fields["Experience"].Text="15"; typed.Fields["Weapon"].Text="Cannon";
    var candidate=typed.Validate(editWeapons,editStates,editValidator);
    Check(typed.Fields["Weapon"].Result.Severity==ValidationSeverity.Warning && candidate.WeaponId==2, "Recognized incompatible weapon is overrideable warning");
    typed.Fields["Weapon"].Text="Imaginary gun"; typed.Validate(editWeapons,editStates,editValidator);
    Check(typed.Fields["Weapon"].Result.Severity==ValidationSeverity.Error, "Unknown weapon text blocks apply");
    typed.Fields["Weapon"].Text="1"; typed.Fields["EnlistDate"].Text="1861-02-30"; typed.Validate(editWeapons,editStates,editValidator);
    Check(typed.Fields["EnlistDate"].Result.Severity==ValidationSeverity.Error, "Impossible calendar date blocked");
    typed.Fields["EnlistDate"].Text="1861-04-01"; typed.Fields["FieldStrength"].Text=int.MaxValue.ToString(); typed.Fields["Casualties"].Text="1";
    typed.Validate(editWeapons,editStates,editValidator);
    Check(typed.Fields["FieldStrength"].Result.Severity==ValidationSeverity.Error, "Combined strength overflow blocked");
    typed.Fields["FieldStrength"].Text="500"; typed.Fields["Casualties"].Text="0"; typed.Fields["Experience"].Text="Infinity";
    typed.Validate(editWeapons,editStates,editValidator);
    Check(typed.Fields["Experience"].Result.Severity==ValidationSeverity.Error, "Nonfinite experience blocked");
    typed.Fields["Experience"].Text="15"; typed.Fields["Weapon"].Text="Cannon";
    typed.Apply(typed.Validate(editWeapons,editStates,editValidator));
    var reloadedDraft=new TypedUnitEdit(editUnit); reloadedDraft.Validate(editWeapons,editStates,editValidator);
    Check(reloadedDraft.Fields["Weapon"].Result.Severity==ValidationSeverity.Warning && !typed.Dirty, "Risk re-detected from model without session flags");
    var config = new UiSettingsService(loadSaved: false);
    Check(config.Get("oob.cards.scale") == 1, "Missing UI values use defaults");
    var path = Path.Combine(temp, "ui.json");
    File.WriteAllText(path, """{"schema":"gtcw-oob-ui","version":2,"future":{"a":42},"oob":{"cards":{"scale":1.4,"futureCard":"yes"}}}""");
    config.Import(path); config.Set("oob.cards.padding", 12); config.Export(path);
    var json = JsonNode.Parse(File.ReadAllText(path))!;
    Check(json["future"]!["a"]!.GetValue<int>() == 42 && json["oob"]!["cards"]!["futureCard"]!.GetValue<string>() == "yes" && json["version"]!.GetValue<int>() == 2, "Unknown fields and future version survive export");
    File.WriteAllText(path, """{"schema":"gtcw-oob-ui","version":1,"oob":{"cards":{"scale":900}}}""");
    bool rejected = false; try { config.Import(path); } catch { rejected = true; }
    Check(rejected && config.Get("oob.cards.scale") == 1.4, "Invalid import is atomic");
    config.Reset("oob.cards"); Check(config.Get("oob.cards.scale") == 1, "Section reset");
    var army = new GroupNode { GroupId = 1, ParentId = -1, UnitTier = 16, Nation = 0, Name = "Army" };
    var fleet = new GroupNode { GroupId = 2, ParentId = 1, UnitTier = 17, Nation = 0, Name = "Fleet" };
    var navalChild = new GroupNode { GroupId = 3, ParentId = 2, UnitTier = 14, Nation = 0, Name = "Division" };
    var unknown = new GroupNode { GroupId = 4, ParentId = -1, UnitTier = 12, Nation = 0, Name = "Unresolved" };
    var groups = new[] { army, fleet, navalChild, unknown }.ToDictionary(g => g.GroupId);
    var classify = new CommandClassificationService();
    Check(classify.CategoryForGroup(navalChild, groups) == CommandCategory.Fleet, "Nested Fleet descendants classify as Navy");
    Check(classify.CategoryForGroup(unknown, groups) == CommandCategory.Unknown, "Unknown root remains unknown");
    var u = new CombatUnitNode { UnitId = 1, Nation = 0, ParentId = 1, Name = "5th Ohio Infantry", HomeStateName = "Ohio", StateId = 10, UnitType = 0, UnitTier = 12, WeaponId = 1, FieldStrength = 1000, ContractMonths = 12, PathLinkStatus = PathLinkStatus.Confirmed };
    var enemy = new CombatUnitNode { UnitId = 2, Nation = 1, ParentId = 1, Name = "5th Ohio Infantry", HomeStateName = "Ohio", StateId = 10, UnitType = 0, UnitTier = 12 };
    var special = new CombatUnitNode { UnitId = 3, Nation = 0, ParentId = 1, Name = "9th Ohio Rangers", HomeStateName = "Ohio", StateId = 10, UnitType = 0, UnitTier = 12, WeaponId = 9 };
    var fresh = new CombatUnitNode { UnitId = 4, Nation = 0, ParentId = 1, Name = "Volunteer Infantry", HomeStateName = "Ohio", StateId = 10, UnitType = 0, UnitTier = 12 };
    var naval = new CombatUnitNode { UnitId = 5, Nation = 0, ParentId = 3, Name = "Boat", UnitType = 0, UnitTier = 12 };
    var unresolved = new CombatUnitNode { UnitId = 6, Nation = 0, ParentId = 1, Name = "Volunteers", HomeStateName = "Unknown State (ID 42)", StateId = 42, UnitType = 0, UnitTier = 12 };
    var naming = new NamingSchemeService { CurrentNation = 0, Classify = x => classify.CategoryForUnit(x, groups) };
    var rule = new NamingRule { UnitType = 0, NameExcludes = "Rangers", Faction = NamingFaction.Union };
    var pop = new[] { u, enemy, special, fresh, naval, unresolved };
    var preview = naming.BuildPreview(rule, pop, _ => "OH");
    Check(preview.Count == 2 && preview.All(p => p.Unit.Nation == 0), "Faction scope, Navy, unknown state and name exclusions");
    Check(preview.Single(p => p.Unit == fresh).AssignedNumber == 10, "Excluded numbered special unit reserves its number; allocation uses highest plus one");
    Check(preview.Single(p => p.Unit == u).AssignedNumber == 5, "Existing numeral preserved");
    rule.NameExcludes = ""; rule.WeaponExclude = "9";
    Check(naming.ExclusionReason(rule, special)!.Contains("weapon"), "Weapon ID exclusion");
    u.CaptureOriginalState();
    var edits = new EditSession();
    edits.Execute("Move block", new[] { u, fresh }, () => { u.ParentId = 4; fresh.ParentId = 4; u.EditorOrder = 1; fresh.EditorOrder = 2; });
    Check(edits.Undo(out _) && u.ParentId == 1 && fresh.ParentId == 1 && u.EditorOrder == int.MaxValue, "Single Undo restores block parentage and display order");
    Check(edits.Redo(out _) && u.ParentId == 4 && fresh.EditorOrder == 2, "Redo restores block move");
    edits.Undo(out _);
    try { edits.Execute("Failing batch", new[] { u }, () => { u.Name = "Bad"; throw new Exception(); }); } catch { }
    Check(u.Name == "5th Ohio Infantry", "Failed edit transaction rolls back in memory");
    u.Name = "New"; u.MarkSavedState(); u.Name = "Working";
    Check(u.OriginalValue("Name") == "5th Ohio Infantry" && u.GetEditTooltip("Name", "Name").Contains("Last saved: New"), "Original and saved values remain distinct");
    var planner = new BatchEditPlanner(new EditValidationService());
    var request = new BatchEditRequest(4, null, null, null, new WeaponOption(8, "Cavalry carbine", 1), false);
    var plan = planner.Build(request, new[] { u }); planner.Apply(plan);
    Check(u.TransferDays == 4 && u.WeaponId == 1 && plan.Units[0].Skips.Count == 1, "Batch applies confirmed ETA while skipping incompatible weapon");
    u.PathLinkStatus = PathLinkStatus.Ambiguous;
    var blocked = planner.Build(request with { EtaDays = 9 }, new[] { u }); planner.Apply(blocked);
    Check(u.TransferDays == 4, "Ambiguous ETA remains blocked");
    var model = new OobDisplayModelService().Build(groups, pop, 0, new[] { 1 }, g => classify.CategoryForGroup(g, groups) == CommandCategory.FieldCommand);
    Check(!model.CommandsById.ContainsKey(2) && !model.CommandsById.ContainsKey(3), "Land projection excludes nested Navy");
    fleet.IsLandCommand = false; army.Children.Add(fleet); army.InvalidateAggregateCache();
    Check(army.SubordinateFormationCount == 0, "Naval formations do not inflate land command summaries");
    var textPath = Path.Combine(temp, "source.dat"); var outPath = Path.Combine(temp, "out.dat");
    foreach (var encoding in new Encoding[] { new UTF8Encoding(false), new UTF8Encoding(true), new UnicodeEncoding(false,true), new UnicodeEncoding(true,true), new UTF32Encoding(false,true), Encoding.Latin1 })
    foreach (var source in new[] { "alpha\r\nbéta\ngamma\rdelta", "alpha\r\nbéta\ngamma\rdelta\r\n", "" })
    {
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(source)).ToArray(); File.WriteAllBytes(textPath, bytes);
        var buffer = TextFileBuffer.Read(textPath); buffer.WriteTo(outPath, buffer.Lines);
        Check(File.ReadAllBytes(outPath).SequenceEqual(bytes), $"Byte-exact roundtrip {encoding.WebName}, source length {source.Length}");
        if (source.Length > 0)
        {
            var lines = buffer.CloneLines(); lines[1] = "changed"; buffer.WriteTo(outPath, lines);
            var expected = encoding.GetPreamble().Concat(encoding.GetBytes(source.Replace("béta", "changed"))).ToArray();
            Check(File.ReadAllBytes(outPath).SequenceEqual(expected), $"Single field patch preserves mixed separators {encoding.WebName}, source length {source.Length}");
        }
    }
    File.WriteAllText(Path.Combine(temp, "regiments.dat"), "save A"); File.WriteAllText(Path.Combine(temp, "groups.dat"), "groups");
    var metadata = new DisplayMetadataService(Path.Combine(temp,"display")); metadata.Save(temp, new() { ["U:1"] = new(3, 20, 0) });
    Check(metadata.Load(temp)["U:1"].Order == 3, "Display metadata reloads for exact save image");
    File.WriteAllText(Path.Combine(temp, "regiments.dat"), "save B with reused ID 1");
    Check(metadata.Load(temp).Count == 0, "Display metadata cannot leak through reused IDs in another save");

    // End-to-end synthetic saves exercise the actual parser and writer, not a mock.
    var saveDir = Path.Combine(temp, "campaign"); Directory.CreateDirectory(saveDir);
    var reg = Enumerable.Repeat("000", 39).ToArray();
    reg[0] = "1"; reg[1] = "5th Test Infantry"; reg[3] = "1"; reg[4] = "0"; reg[5] = "7"; reg[6] = "1000";
    reg[9] = "12.345600"; reg[10] = "0.230000"; reg[13] = "1"; reg[24] = "12"; reg[26] = "4/1/1861"; reg[27] = "36";
    reg[2] = "5th Tree Label"; reg[29] = "11"; reg[31] = "Raised April 1, 1861 in Indiana\\nHistorical entry stays exact"; reg[33] = "1.5000";
    var groupLines = Enumerable.Repeat("0", 32).ToArray(); groupLines[0] = "1"; groupLines[1] = "1st Army"; groupLines[2] = "-1"; groupLines[17] = "16";
    string Mixed(IEnumerable<string> lines) => string.Concat(lines.Select((line, i) => line + (i % 3 == 0 ? "\r\n" : i % 3 == 1 ? "\n" : "\r")));
    var originalRegText = Mixed(new[] { "1" }.Concat(reg));
    var regimentPath = Path.Combine(saveDir, "regiments.dat"); var groupPath = Path.Combine(saveDir, "groups.dat");
    File.WriteAllText(regimentPath, originalRegText, new UTF8Encoding(true));
    File.WriteAllText(groupPath, string.Join("\r\n", new[] { "1" }.Concat(groupLines)));
    var commander = Enumerable.Repeat("0", 66).ToArray();
    commander[0] = "7"; commander[1] = "Doe"; commander[2] = "John"; commander[3] = "John Doe";
    commander[32] = "1"; commander[33] = "6"; commander[34] = "1861"; // Brigadier General promotion
    File.WriteAllText(Path.Combine(saveDir, "commanders.txt"), string.Join("\n", new[] { "1" }.Concat(commander)));
    File.WriteAllText(Path.Combine(saveDir, "scenario.dat"), "0\n0\n1\n7\n1861");
    var pathLines = Enumerable.Repeat("0", 15).ToArray(); pathLines[0] = reg[1]; pathLines[3] = "7"; pathLines[14] = "1.5000";
    var pathsPath = Path.Combine(saveDir, "paths.dat"); var originalPathsText = Mixed(pathLines); File.WriteAllText(pathsPath, originalPathsText);
    using (var data = new GrandTacticianDataService())
    {
        await data.LoadAsync(saveDir); var savedUnit = data.Units.Single();
        Check(data.StateOptions.Count == 53 && data.StateOptions.Single(s => s.Id == 29) == new StateOption(29, "Ohio", "OH") && savedUnit.HomeStateName == "Indiana", "Canonical GTCW State_ID table resolves Ohio 29 and all listed locations");
        Check(savedUnit.CommanderDisplayName == "BG John Doe", "Commander rank is derived from commanders.txt promotion date");
        Check(savedUnit.PathLinkStatus == PathLinkStatus.Confirmed, "Actual save parser confirms unique name / commander path link");
        Check(data.DisplayFingerprint == DisplayMetadataService.Fingerprint(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(regimentPath))), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(groupPath)))), "Loaded display fingerprint matches exact file bytes");
        var noOp = await data.SaveAsync(); Check(!noOp.WroteFiles, "No-op save writes no files");
        savedUnit.Name = "6th Test Infantry"; var result = await data.SaveAsync(); data.MarkSavedStates();
        Check(result.WroteFiles && result.PatchedFieldCount == 3 && File.Exists(Path.Combine(result.BackupDirectory!, "regiments.dat")), "Rename synchronizes Unit_Name and Override_Name and creates transaction backup");
        Check(File.ReadAllText(regimentPath) == originalRegText.Replace("5th Test Infantry", "6th Test Infantry").Replace("5th Tree Label", "6th Test Infantry"), "Actual rename preserves every other raw regiment field and separator");
        Check(data.DisplayFingerprint == DisplayMetadataService.Fingerprint(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(regimentPath))), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(groupPath)))), "Post-save display fingerprint matches written bytes");
        Check(File.ReadAllText(pathsPath) == originalPathsText.Replace("5th Test Infantry", "6th Test Infantry"), "Path name synchronizes without ETA normalization");
        Check(File.ReadAllBytes(Path.Combine(result.BackupDirectory!, "groups.dat")).SequenceEqual(File.ReadAllBytes(groupPath)) && File.Exists(Path.Combine(result.BackupDirectory!, "commanders.txt")), "Backup includes unchanged save files");
        var beforeInvalid = File.ReadAllBytes(regimentPath);
        savedUnit.ExperienceRaw = double.NaN;
        bool invalidBlocked=false; try { await data.SaveAsync(); } catch(InvalidDataException) { invalidBlocked=true; }
        Check(invalidBlocked && File.ReadAllBytes(regimentPath).SequenceEqual(beforeInvalid), "Save boundary blocks known-invalid numeric data without writes");
        savedUnit.ExperienceRaw=0;
        var backupRoot=Path.Combine(saveDir,"Aide-de-Camp_Backups"); var heldBackups=Path.Combine(saveDir,"HeldBackups");
        Directory.Move(backupRoot,heldBackups); File.WriteAllText(backupRoot,"block backup directory creation");
        savedUnit.ContractMonths++;
        bool backupBlocked=false; try { await data.SaveAsync(); } catch(IOException) { backupBlocked=true; }
        Check(backupBlocked && File.ReadAllBytes(regimentPath).SequenceEqual(beforeInvalid), "Failed backup prevents save replacement");
        File.Delete(backupRoot); Directory.Move(heldBackups,backupRoot); savedUnit.ContractMonths--;
        var beforeHomeState = File.ReadAllText(regimentPath); savedUnit.StateId = 22; savedUnit.HomeStateName = "Ohio";
        await data.SaveAsync(); data.MarkSavedStates();
        var expectedHomeState = beforeHomeState.Replace("\r11\r\n", "\r22\r\n");
        Check(File.ReadAllLines(regimentPath)[32] == reg[31] && File.ReadAllLines(regimentPath)[30] == "22", "Home State edit preserves original Service_History");
        savedUnit.TransferDays = 4; await data.SaveAsync(); data.MarkSavedStates();
        Check(File.ReadAllLines(regimentPath)[34] == "4" && File.ReadAllLines(pathsPath)[14] == "4", "ETA saves to both regiments and paths");
        var outside = File.ReadAllBytes(regimentPath).Concat(new byte[] { 10 }).ToArray(); File.WriteAllBytes(regimentPath, outside);
        bool metadataConflict = false; try { await data.SaveAsync(); } catch (IOException) { metadataConflict = true; }
        Check(metadataConflict, "Metadata-only save also blocks external file changes");
        savedUnit.Name = "Blocked overwrite";
        bool conflict = false; try { await data.SaveAsync(); } catch (IOException) { conflict = true; }
        Check(conflict && File.ReadAllBytes(regimentPath).SequenceEqual(outside), "External save change blocks overwrite and preserves external bytes");
    }
    var configDir=Path.Combine(temp,"weapon-config"); Directory.CreateDirectory(configDir);
    var cannon=Enumerable.Repeat("0",53).ToArray(); cannon[0]="2"; cannon[1]="Test Cannon"; cannon[3]="2";
    File.WriteAllLines(Path.Combine(configDir,"weapons.txt"),new[]{"1"}.Concat(cannon));
    using(var data=new GrandTacticianDataService()) {
        await data.LoadAsync(saveDir,configDir); data.Units[0].WeaponId=2; data.Units[0].WeaponName="Test Cannon";
        await data.SaveAsync();
    }
    using(var data=new GrandTacticianDataService()) {
        await data.LoadAsync(saveDir,configDir); var check=new TypedUnitEdit(data.Units[0]); check.Validate(data.WeaponOptions,data.StateOptions,editValidator);
        Check(check.Fields["Weapon"].Result.Severity==ValidationSeverity.Warning, "Incompatible weapon survives actual save and is flagged on reload");
    }
    groupLines[17] = "17"; groupLines[1] = "Fleet"; File.WriteAllText(groupPath, string.Join("\n", new[] { "1" }.Concat(groupLines)));
    using (var data = new GrandTacticianDataService())
    {
        await data.LoadAsync(saveDir); data.Units[0].ContractMonths = 99;
        bool navalBlocked = false; try { await data.SaveAsync(); } catch (InvalidOperationException) { navalBlocked = true; }
        Check(navalBlocked, "Save boundary blocks naval land-field mutations");
    }
    var zipPath = Path.Combine(temp, "save.zip"); System.IO.Compression.ZipFile.CreateFromDirectory(saveDir, zipPath);
    using (var data = new GrandTacticianDataService())
    {
        await data.LoadAsync(zipPath); data.Units[0].Name = "Zip edit";
        bool zipBlocked = false; try { await data.SaveAsync(); } catch (InvalidOperationException) { zipBlocked = true; }
        Check(data.IsReadOnlySave && zipBlocked, "ZIP remains inspection-only at save boundary");
    }
    // Parent_ID is authoritative over the stale parent suffix carried in a group name.
    var nameRepairDir = Path.Combine(temp, "formation-name-repair"); Directory.CreateDirectory(nameRepairDir);
    string GroupRecord(int id, string name, int parent, int tier) { var fields = Enumerable.Repeat("0", 32).ToArray(); fields[0] = id.ToString(); fields[1] = name; fields[2] = parent.ToString(); fields[3] = "0"; fields[17] = tier.ToString(); return string.Join("\n", fields); }
    File.WriteAllText(Path.Combine(nameRepairDir, "regiments.dat"), "0");
    File.WriteAllText(Path.Combine(nameRepairDir, "commanders.txt"), "0");
    File.WriteAllText(Path.Combine(nameRepairDir, "groups.dat"), "3\n" + GroupRecord(1, "4th Division", -1, 16) + "\n" + GroupRecord(2, "5th Division", -1, 14) + "\n" + GroupRecord(3, "1st Brigade, 5th Division", 1, 13));
    using (var data = new GrandTacticianDataService())
    {
        await data.LoadAsync(nameRepairDir);
        var brigade = data.Groups[3];
        Check(brigade.Name == "1st Brigade, 4th Division" && data.HasUnsavedChanges, "Formation suffix follows actual Parent_ID on load");
        await data.SaveAsync(); data.MarkSavedStates();
        Check(File.ReadAllLines(Path.Combine(nameRepairDir, "groups.dat"))[66] == "1st Brigade, 4th Division", "Corrected formation suffix writes to groups.dat");
        data.MoveGroup(brigade, data.Groups[2]);
        Check(brigade.Name == "1st Brigade, 5th Division", "Formation suffix follows intact command move");
        await data.SaveAsync();
        Check(File.ReadAllLines(Path.Combine(nameRepairDir, "groups.dat"))[66] == "1st Brigade, 5th Division" && File.ReadAllLines(Path.Combine(nameRepairDir, "groups.dat"))[67] == "2", "Formation name and parent save together after command move");
    }
    var rehome = new CombatUnitNode { UnitId = 77, Nation = 0, ParentId = 1, StateId = 10, HomeStateName = "Ohio", UnitType = 0, UnitTier = 12, Name = "1st Indiana Infantry" };
    rule.WeaponExclude = ""; rule.NameExcludes = "";
    Check(naming.NameForRehomedUnit(rule, rehome, new[] { special, rehome }, _ => "OH").StartsWith("10th"), "Rehomed unit takes destination highest number plus one");
    var suffixRule = new NamingRule { UnitType = 0, Faction = NamingFaction.Union, Tokens = new() { new() { Kind = NamingTokenKind.CustomText, Text = "Infantry-Brigade " }, new() { Kind = NamingTokenKind.Number } }, NumberStyle = NumberStyle.Roman };
    fresh.Name = "Infantry-Brigade V";
    Check(naming.BuildPreview(suffixRule, new[] { fresh }, _ => "OH").Single().AssignedNumber == 5, "Formatter preserves numeral after custom text");
    // Shared metrics must count leaves exactly once and distinguish transfer from manpower.
    var metricRoot = new GroupNode { Name = "Theater", UnitTier = 18 };
    var metricDivision = new GroupNode { Name = "Division", UnitTier = 14 };
    var infantry = new CombatUnitNode { Name = "Infantry", UnitType = 0, TotalMenRaw = 1000, CasualtyRatioRaw = 10, ConfiguredMaxStrength = 2000 };
    var cavalry = new CombatUnitNode { Name = "Cavalry", UnitType = 1, TotalMenRaw = 500, TransferDays = 3, ConfiguredMaxStrength = 1000 };
    var battery = new CombatUnitNode { Name = "Battery", UnitType = 2, TotalMenRaw = 120, ConfiguredMaxStrength = 240 };
    metricRoot.Children.Add(metricDivision); metricRoot.Children.Add(cavalry);
    metricDivision.Children.Add(infantry); metricDivision.Children.Add(battery);
    var metrics = metricRoot.Metrics;
    Check(metrics.Assigned == 1520 && metrics.Present == 1020 && metrics.Casualties == 100 && metrics.Maximum == 3240, "HQ totals sum assigned combat leaves; only present strength excludes transfers");
    Check(metrics.DirectHeadquarters == 1 && metrics.TotalHeadquarters == 1 && metrics.CombatUnits == 3 && metrics.Infantry == 900 && metrics.Cavalry == 500, "Theater/HQ counts distinguish headquarters, combat formations and branch manpower");
    Check(metrics.Guns is null && metrics.ArtilleryUnits == 1, "Artillerymen never become an invented gun count");
    battery.GunCount = 6; metricRoot.InvalidateAggregateCache();
    Check(metricRoot.Metrics.Guns == 6, "Verified gun values aggregate independently of artillery manpower");
    Check(!metricDivision.CardMetrics.Any(m => m.Label == "Cavalry") && metricDivision.CardMetrics.Any(m => m.Label.Contains("Casualties")), "Zero composition lines omitted while casualties remain visible");
    infantry.TotalMenRaw = 2000; metricRoot.RefreshAggregates();
    Check(metricRoot.Metrics.Assigned == 2420, "Metric cache invalidation reflects changed leaf manpower");
    var alertRoot = new GroupNode { GroupId=800, CommanderDisplayName="MG Fixture" };
    var alertChild = new GroupNode { GroupId=801, CommanderDisplayName="BG Fixture" };
    var alertUnit = new CombatUnitNode { UnitId=802, TotalMenRaw=100, ConfiguredMaxStrength=1000, TransferTimeRaw=3, CommanderDisplayName="COL Fixture" };
    alertChild.Children.Add(alertUnit); alertRoot.Children.Add(alertChild); alertRoot.Children.Add(alertUnit);
    Check(alertRoot.Metrics.Assigned==100 && alertRoot.Metrics.CombatUnits==1, "Compact totals count a shared combat leaf once");
    Check(alertRoot.CompactAlerts=="1 low strength (1 critical) • 1 in transfer", "Subordinate alerts count a shared leaf once with meaningful labels");
    Check(alertUnit.CompactStrength=="100 men • 0 casualties", "Compact strength labels manpower and casualties independently");
    Check(ReferenceEquals(alertUnit.CardMetrics, alertUnit.CardMetrics), "Unchanged metrics retain their cached rows");
    OobPresentation.RegimentalScale = true;
    Check(metricDivision.IdentitySecondary.Contains("Brigade") && metricDivision.UnitTier == 14, "Card identity uses presentation scale without mutating native tiers");
    OobPresentation.RegimentalScale = false;
    var groupSession = new EditSession();
    var oldGroupName = metricDivision.Name;
    groupSession.Execute("HQ edit", Array.Empty<CombatUnitNode>(), new[] { metricDivision }, () => { metricDivision.Name = "Named HQ"; metricDivision.EditorOrder = 7; metricDivision.ParentId = 44; });
    groupSession.Undo(out _);
    Check(metricDivision.Name == oldGroupName && metricDivision.EditorOrder == 0 && metricDivision.ParentId == 0, "HQ rename, parent and sibling order undo atomically");
    groupSession.Redo(out _);
    Check(metricDivision.Name == "Named HQ" && metricDivision.EditorOrder == 7 && metricDivision.ParentId == 44, "HQ transaction redo restores all changes");
    try { groupSession.Execute("Failed HQ edit", Array.Empty<CombatUnitNode>(), new[] { metricDivision }, () => { metricDivision.Name = "Broken"; throw new InvalidOperationException(); }); } catch (InvalidOperationException) { }
    Check(metricDivision.Name == "Named HQ", "Failed HQ transaction rolls back");
    var orderDir = Path.Combine(temp, "hq-order"); Directory.CreateDirectory(orderDir);
    File.WriteAllText(Path.Combine(orderDir, "regiments.dat"), "0");
    File.WriteAllText(Path.Combine(orderDir, "commanders.txt"), "0");
    var orderFile = Path.Combine(orderDir, "groups.dat");
    File.WriteAllText(orderFile, "4\n" + GroupRecord(10, "Army", -1, 16) + "\n" + GroupRecord(11, "Z Corps", 10, 15) + "\n" + GroupRecord(12, "A Corps", 10, 15) + "\n" + GroupRecord(13, "Division", 11, 14) + "\nTAIL");
    var originalBlocks = File.ReadAllLines(orderFile);
    using (var data = new GrandTacticianDataService()) {
        await data.LoadAsync(orderDir);
        Check(!data.CanMoveGroup(data.Groups[11], data.Groups[13]) && data.CanMoveGroup(data.Groups[13], data.Groups[12]), "Native hierarchy rejects corps beneath division and permits division beneath corps");
        data.Groups[12].EditorOrder = 0; data.Groups[11].EditorOrder = 1;
        await data.SaveAsync(); data.MarkSavedStates();
        var saved = File.ReadAllLines(orderFile);
        Check(saved[33] == "12" && saved[65] == "11" && saved[^1] == "TAIL", "HQ sibling insertion persists as intact groups.dat records");
        Check(saved.Skip(33).Take(32).SequenceEqual(originalBlocks.Skip(65).Take(32)) && saved.Skip(65).Take(32).SequenceEqual(originalBlocks.Skip(33).Take(32)), "HQ reorder preserves all unknown fields in both record blocks");
        data.Groups[11].Name = "Renamed Corps";
        await data.SaveAsync(); data.MarkSavedStates();
        Check(File.ReadAllLines(orderFile)[66] == "Renamed Corps", "Second save after reorder patches the correct relocated HQ record");
        data.Groups[11].Name = "Invalid\nHQ";
        var beforeInvalid = File.ReadAllBytes(orderFile);
        bool invalidHqBlocked = false; try { await data.SaveAsync(); } catch (InvalidDataException) { invalidHqBlocked = true; }
        Check(invalidHqBlocked && beforeInvalid.SequenceEqual(File.ReadAllBytes(orderFile)), "Invalid HQ name blocks save without writes");
    }
    using (var data = new GrandTacticianDataService()) {
        await data.LoadAsync(orderDir);
        var display = new OobDisplayModelService().Build(data.Groups, data.Units, 0, new[] {10});
        Check(display.Roots[0].Subcommands.Select(c => c.Source.GroupId).SequenceEqual(new[] {12,11}), "Display order after reload follows native record order instead of alphabetic names");
    }
    Check(!RosterFields.IsSupported("") && !RosterFields.IsSupported(null) && !RosterFields.IsSupported("Imaginary"), "Empty and unknown roster identifiers are rejected");
    Check(RosterFields.FromColumn("WeaponName") == "Weapon" && RosterFields.FromColumn("HomeStateName") == "HomeState", "Menu and cell use the same weapon/state field mapping");
    Check(RosterFields.FromColumn("CommandPath") is null, "Read-only columns never fall back to unit name editing");
    Check(new[] { "Name", "HomeStateName", "WeaponName", "FieldStrength", "Casualties", "ContractMonths", "ContractRemaining", "Experience", "ETA" }
        .All(path => RosterFields.IsSupported(RosterFields.FromColumn(path)) && typed.Fields.ContainsKey(RosterFields.FromColumn(path)!)), "Every editable roster column resolves to an actual typed draft field");
    var scales = new[] { 16, 15, 14 }.Select(t => CardLayoutGeometry.TierScale(true, t)).ToArray();
    Check(scales[0] > scales[1] && scales[1] > scales[2] && 470 * scales[2] > 360 * CardLayoutGeometry.TierScale(false, 13), "Native army/corps/division/combat widths are strictly descending");
    OobPresentation.RegimentalScale = true;
    Check(scales.SequenceEqual(new[] { 16, 15, 14 }.Select(t => CardLayoutGeometry.TierScale(true, t))), "Regimental labels do not alter physical tier scale");
    OobPresentation.RegimentalScale = false;
    var baseline = CardLayoutGeometry.SurfaceBaselines(new[] {
        new CardRowFootprint(0, 350, 120, 110), new CardRowFootprint(1, 300, 100, 85),
        new CardRowFootprint(1, 430, 85, 62), new CardRowFootprint(2, 270, 85, 62) });
    Check(baseline[0] == 190 && baseline[1] == 630, "Row baseline reserves the tallest floating counter above aligned card surfaces");
    Check(baseline[2] == 1145, "Next row clears the longest body plus gap and next counter, without averaging heights");
    Check(CardLayoutGeometry.SurfaceBaselines(Array.Empty<CardRowFootprint>()).Count == 0, "Empty tree geometry is safe");
    Check(new UiSettingsService(loadSaved: false).Get("oob.spacing.stack") >= 0, "Combat stack spacing remains a valid independent setting");
    Check(UiSettingsService.Parameters.Single(p => p.Key == "oob.spacing.stack").Default == 32,
        "Default combat stacks have a 32-unit gap independent of the HQ gutter");
    Check(UiSettingsService.Parameters.Single(p => p.Key == "oob.spacing.commands").Default == 24 &&
        UiSettingsService.Parameters.Single(p => p.Key == "oob.spacing.division").Default == 32,
        "Compact defaults reserve modest edge gutters");
    var numberingProbe = new NamingSchemeService { Classify = _ => CommandCategory.FieldCommand };
    var numbered = new CombatUnitNode { UnitId = 9001, Name = "49th Ohio", Nation = 0, StateId = 29, HomeStateName = "Ohio", UnitType = 0, UnitTier = 13 };
    var unnumbered = new CombatUnitNode { UnitId = 2, Name = "Ohio Volunteers", Nation = 0, StateId = 29, HomeStateName = "Ohio", UnitType = 0, UnitTier = 13 };
    var numberProbe = numberingProbe.BuildPreview(new NamingRule { Faction = NamingFaction.Union, UnitType = 0 }, new[] { numbered, unnumbered }, _ => "OH");
    Check(numberProbe.Single(p => p.Unit == numbered).AssignedNumber == 49 && numberProbe.Single(p => p.Unit == unnumbered).AssignedNumber == 50,
        "Existing 49th is preserved and forces an unnamed unit to 50 despite low available numbers and unrelated IDs");
    var gapRule = new NamingRule { Faction = NamingFaction.Union, UnitType = 0, RenumberWithGaps = true, NameExcludes = "Protected", SkippedNumbersText = "4, 8-10" };
    var gapUnits = Enumerable.Range(0, 20).Select(i => new CombatUnitNode { UnitId = 100 + i, Name = (100 + i) + "th Ohio", Nation = 0, StateId = 29, HomeStateName = "Ohio", UnitType = 0, UnitTier = 13 }).ToList();
    gapUnits.Add(new CombatUnitNode { UnitId = 999, Name = "2nd Protected Ohio", Nation = 0, StateId = 29, HomeStateName = "Ohio", UnitType = 0, UnitTier = 13 });
    var gaps = numberingProbe.BuildPreview(gapRule, gapUnits, _ => "OH");
    var allocated = gaps.Select(p => p.AssignedNumber).ToArray();
    Check(allocated[0] == 1 && allocated.Max() < 60 && allocated.Distinct().Count() == 20, "Gapped mode replaces high legacy numbers with unique low numbers");
    Check(!allocated.Intersect(new[] { 2, 4, 8, 9, 10 }).Any() && gaps.Count == 20, "Gapped mode respects excluded-unit occupancy and reserved ranges");
    Check(allocated.Zip(allocated.Skip(1)).Any(p => p.Second - p.First > 1), "Gapped numbering includes skipped numbers");
    Check(allocated.SequenceEqual(numberingProbe.BuildPreview(gapRule, gapUnits.AsEnumerable().Reverse(), _ => "OH").Select(p => p.AssignedNumber)), "Gapped preview is stable across refresh and source enumeration order");
    foreach (var item in gaps) item.Unit.Name = item.NewName;
    Check(allocated.SequenceEqual(numberingProbe.BuildPreview(gapRule, gapUnits, _ => "OH").Select(p => p.AssignedNumber)), "Applying names does not reroll gapped preview for unchanged population");
    var roundTripGap = System.Text.Json.JsonSerializer.Deserialize<NamingRule>(System.Text.Json.JsonSerializer.Serialize(gapRule))!;
    Check(roundTripGap.RenumberWithGaps && !System.Text.Json.JsonSerializer.Deserialize<NamingRule>("{}")!.RenumberWithGaps, "New numbering option persists and old rule files retain preserve-number mode");
    gapRule.SkippedNumbersText = ""; gapRule.NameExcludes = "";
    gapRule.SpecialNames.Add(new SpecialNameEntry { NumberText = "1", Name = "Buckeye Guard" });
    Check(numberingProbe.BuildPreview(gapRule, gapUnits, _ => "OH").First().NewName.Contains("Buckeye Guard"), "Gapped allocation uses special-name formatter for allocated numeral");
    foreach(var xp in new[] {0d,37.5,100}) Check(editValidator.ValidateExperience(xp).Severity==ValidationSeverity.Valid,$"Confirmed experience scale accepts {xp}");
    foreach(var xp in new[] {-1d,100.01,double.NaN,double.PositiveInfinity}) Check(editValidator.ValidateExperience(xp).Severity==ValidationSeverity.Error,"Out-of-range/nonfinite experience rejected");
    var managementDir=Path.Combine(temp,"management");Directory.CreateDirectory(managementDir);
    var emptyNames=new Dictionary<int,string>();var emptyFleets=new Dictionary<int,int>();
    foreach(var version in new[] {"", "garbage", "NaN", "1.143"}) {
        File.WriteAllText(Path.Combine(managementDir,"version.dat"),version);
        Check(ManagementSnapshot.Read(managementDir,emptyNames,emptyFleets).Notices.Count>0,"Unknown or malformed management version is reported safely");
    }
    File.WriteAllText(Path.Combine(managementDir,"version.dat"),"1.142");
    File.WriteAllText(Path.Combine(managementDir,"commanders.txt"),"");
    File.WriteAllText(Path.Combine(managementDir,"ships.dat"),"1\n0");
    var malformed=ManagementSnapshot.Read(managementDir,emptyNames,emptyFleets);
    Check(malformed.Officers.Count==0 && malformed.Ships.Count==0 && malformed.Notices.Count==3,"Malformed domains and missing nations are reported without partial records");
    string[] Ship(int id,int fleet,int pool,string build,string repair,bool moving,bool harbor,bool returning) {
        var fields=Enumerable.Repeat("0",23).ToArray();fields[0]=id.ToString();fields[1]="Ship "+id;fields[2]=fleet.ToString();fields[3]=pool.ToString();fields[5]="95";
        fields[13]=moving?"10":"0";fields[16]="5";fields[17]=build;fields[18]=repair;fields[19]=harbor?"10":"0";fields[22]=returning.ToString();return fields;
    }
    File.WriteAllLines(Path.Combine(managementDir,"ships.dat"),new[]{"3"}.Concat(Ship(0,44,-1,"0","0",false,false,false)).Concat(Ship(1,-1,1,"0.25","0",false,false,false)).Concat(Ship(2,44,-1,"0","0.5",true,true,false)));
    var ships=ManagementSnapshot.Read(managementDir,emptyNames,new Dictionary<int,int>{{44,0}}).Ships;
    Check(ships.Count==3 && ships[0].Side==0 && !ships[0].InPort && ships[1].Side==1 && ships[1].ConstructionComplete==75 && ships[1].TravelDays is null,"Ship factions and construction percentage use the serialized fields");
    Check(ships[2].InPort && ships[2].Status=="Returning to harbor" && ships[2].TravelDays==5,"Fleet-attached repair transit stays in harbor roster with travel ETA");
    // Optional read-only integration checks against each real save in a supplied campaign folder.
    if(args.Length>0) foreach(var realSave in Directory.EnumerateDirectories(args[0]).Where(d=>File.Exists(Path.Combine(d,"groups.dat")))) {
        using var campaign=new GrandTacticianDataService();await campaign.LoadAsync(realSave,null);
        var files=new[]{"nations.dat","commanders.txt","ships.dat"}.Select(f=>Path.Combine(realSave,f)).ToArray();
        string Hash(string file)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
        var hashes=files.Select(Hash).ToArray();
        var snapshot=ManagementSnapshot.Read(realSave,campaign.StateOptions.ToDictionary(s=>s.Id,s=>s.Name),campaign.Groups.ToDictionary(g=>g.Key,g=>g.Value.Nation));
        Check(campaign.Management is not null && campaign.Management.Notices.Count==0,"Editable domains validated: "+string.Join("; ",campaign.Management?.Notices??new()));
        Check(campaign.Management!.Stocks.Count>0 && campaign.Management.Records.Count(r=>r.Domain=="Economy")==53,"Full alliance weapon arrays and state population addresses resolved");
        Check(campaign.Management.Records.Where(r=>r.Domain=="Treasury").All(r=>r.Fields.Single().MirrorLine is int mirror && campaign.Management.Numeric("nations.dat",mirror)==campaign.Management.Numeric("nations.dat",r.Fields.Single().Line)),"Both serialized treasury balances agree in the real save");
        Check(snapshot.Notices.Count==0,Path.GetFileName(realSave)+": all management domains parse ("+string.Join("; ",snapshot.Notices)+")");
        Check(snapshot.States.Count==106 && snapshot.Officers.Count>0 && snapshot.Ships.Count>0,Path.GetFileName(realSave)+": complete state/officer/ship records");
        Check(snapshot.Ships.All(s=>s.Side>=0),Path.GetFileName(realSave)+": every vessel resolves to its faction (including foreign fleets)");
        Check(hashes.SequenceEqual(files.Select(Hash)),Path.GetFileName(realSave)+": inspection leaves source files unchanged");
    }
    var logDir=Path.Combine(temp,"logs");
    ManagementSnapshot.State RecruitmentState(int id,bool active)=>new(id,0,"test",10000,100,0,50,100,50,active);
    Check(new[]{41,42,43,44,45,48,49,52}.All(id=>!RecruitmentState(id,true).IsVolunteerEditorState),"Foreign locations cannot appear in volunteer editing even if marked recruitable");
    Check(!RecruitmentState(29,false).IsVolunteerEditorState && RecruitmentState(29,true).IsVolunteerEditorState && RecruitmentState(47,true).IsVolunteerEditorState,"Inactive states are hidden; exhausted active states and U.S. territories remain editable");
    var logPath=ErrorLog.Write("first operation",new InvalidOperationException("first error",new IOException("inner detail")),logDir)!;
    ErrorLog.Write("second operation",new Exception("second error"),logDir);
    Check(File.ReadAllText(logPath).Contains("inner detail") && File.ReadAllText(logPath).Contains("second error") && File.ReadAllText(logPath).Contains(".NET"),"Error log appends both failures with runtime and inner exception details");
    Check(ErrorLog.Write("unwritable destination",new Exception("test"),logPath) is null,"Logging failure cannot cause another application error");
    foreach(var bound in new[]{false,true}) {
        var settings=new UiSettingsService(false);
        foreach(var spec in UiSettingsService.Parameters)settings.Set(spec.Key,bound?spec.Max:spec.Min);
        var rangePath=Path.Combine(temp,"ranges.json");settings.Export(rangePath);var reload=new UiSettingsService(false);reload.Import(rangePath);
        Check(UiSettingsService.Parameters.All(p=>reload.Get(p.Key)==(bound?p.Max:p.Min)),"All UI settings round-trip their "+(bound?"maximum":"minimum")+" range");
        settings.Reset();Check(UiSettingsService.Parameters.All(p=>settings.Get(p.Key)==p.Default),"Expanded settings retain original defaults");
    }
    var gunRules=new ArtilleryRules(240,.25f,.5f);
    Check(gunRules.Count(60,0)==8 && gunRules.Count(75,0)==10 && gunRules.Count(240,0)==30,"Artillery uses the game's two ceiling operations");
    Check(gunRules.Count(300,0)==30 && gunRules.Count(60,50)==4 && gunRules.Count(60,100)==0,"Artillery caps manpower and subtracts truncated sick strength");
    Check(gunRules.Count(60,0)*2==16 && gunRules.Count(120,0)==15,"Subordinate battery guns must round per unit before summing");
    Check(new ArtilleryRules(300,.2f,.5f).Count(60,0)==6,"Artillery respects different campaign configuration ratios");
    var targetPop=RecruitmentProjection.Target(10000,100,110,10,.9,true);
    Check(targetPop>12000 && RecruitmentProjection.Available(10000,targetPop,100,110,.9)>=10,"Volunteer target clears the existing hidden deficit before adding available men");
    var deficitState=new ManagementSnapshot.State(1,0,"Fixture",10000,100,0,10,110,100,true);
    var partialPool=RecruitmentProjection.ForPopulation(deficitState,10500,.9);
    Check(partialPool is {Available:0,Deficit:6},"Population increase reduces estimated deficit before volunteers become available");
    Check(RecruitmentProjection.ForPopulation(deficitState,targetPop,.9) is {Available:>=10,Deficit:0},"Volunteer target produces an available pool with no projected deficit");
    Check(RecruitmentProjection.ForPopulation(deficitState,10000,null) is {Available:0,Deficit:10},"Restoring population restores saved pool even without recruitment settings");
    Check(RecruitmentProjection.ForPopulation(deficitState,10500,null) is null && RecruitmentProjection.ForPopulation(deficitState with {Capacity=0},10500,.9) is null,"Unavailable projection inputs do not disguise saved counters as estimates");
    Check(RecruitmentProjection.ForPopulation(deficitState,9000,.9) is {Available:0,Deficit:20},"Population decreases increase the projected deficit");
    foreach(var test in new[]{(Capacity:0L,Eligible:true),(Capacity:100L,Eligible:false)}) {
        bool targetRejected=false;try{RecruitmentProjection.Target(10000,test.Capacity,110,10,.9,test.Eligible);}catch(InvalidOperationException){targetRejected=true;}
        Check(targetRejected,"Missing capacity or recruitment eligibility blocks volunteer targeting");
    }
    if(args.Length>0) {
        string source=Directory.EnumerateDirectories(args[0]).First(d=>File.Exists(Path.Combine(d,"groups.dat")));
        string copy=Path.Combine(temp,"management-transaction");Directory.CreateDirectory(copy);
        foreach(var file in Directory.EnumerateFiles(source))File.Copy(file,Path.Combine(copy,Path.GetFileName(file)));
        // Create a partly delivered order only in the disposable fixture.
        var seed=ManagementDocument.Load(copy).Stocks.First(s=>s.Side==1);
        var seedBuffer=TextFileBuffer.Read(Path.Combine(copy,"nations.dat"));var seedLines=seedBuffer.CloneLines();
        seedLines[seed.OrderLine+1]="200";seedLines[seed.OrderLine+3]="0.25";seedLines[seed.OrderLine+5]="40";seedBuffer.WriteTo(seedBuffer.FilePath,seedLines);
        var original=Directory.EnumerateFiles(copy).ToDictionary(f=>Path.GetFileName(f)!,f=>File.ReadAllBytes(f));
        using var data=new GrandTacticianDataService();await data.LoadAsync(copy,args.Length>1?args[1]:null);
        var doc=data.Management!;var history=new EditSession();
        var xp=doc.Records.First(r=>r.Domain=="Officers").Fields.Single(f=>f.Key=="Experience");
        var date=doc.Records.First(r=>r.Domain=="Officers").Fields.Single(f=>f.Key=="Promotion1");
        var popField=doc.Records.First(r=>r.Domain=="Economy").Fields.Single();
        var stock=doc.Records.First(r=>r.Domain=="Weapons" && r.Side==1).Fields.Single(f=>f.Key=="Stock");
        var order=doc.Records.First(r=>r.Domain=="Weapons" && r.Side==1).Fields.Single(f=>f.Key=="OrderQuantity");
        var standard=doc.Records.First(r=>r.Domain=="Weapons" && r.Side==1).Fields.Single(f=>f.Key=="StandardizationYear");
        var condition=doc.Records.First(r=>r.Domain=="Navy").Fields.Single(f=>f.Key=="Condition");
        var unionCash=doc.Records.Single(r=>r.Domain=="Treasury" && r.Side==0).Fields.Single();
        var confCash=doc.Records.Single(r=>r.Domain=="Treasury" && r.Side==1).Fields.Single();
        var before=doc.Capture();
        history.ExecuteManagement("Mixed management edit",doc,()=>doc.Apply(new Dictionary<ManagementDocument.Field,string>{{xp,"37.5"},{date,"1861-04-12"},{popField,(doc.Numeric(popField.File,popField.Line)+10000).ToString(System.Globalization.CultureInfo.InvariantCulture)},{stock,"12345"},{order,"300"},{standard,"1860.25"},{condition,"73"}}));
        var expected=doc.Targets().ToDictionary(t=>t.Name,t=>t.Lines);
        history.ExecuteManagement("Both treasury balances",doc,()=>doc.Apply(new Dictionary<ManagementDocument.Field,string>{{unionCash,"5012345"},{confCash,"-345678"}}));
        expected=doc.Targets().ToDictionary(t=>t.Name,t=>t.Lines);
        Check(doc.Numeric("nations.dat",unionCash.MirrorLine!.Value)==5012345 && doc.Numeric("nations.dat",confCash.MirrorLine!.Value)==-345678 && doc.Review().Count()>0,"Treasury edits synchronize both saved copies, preserve faction separation and appear in review");
        Check(data.HasUnsavedChanges && doc.ChangedFields>=5,"All four management domains join the dirty/save transaction");
        bool invalid=false;try{doc.Apply(new Dictionary<ManagementDocument.Field,string>{{stock,"999"},{xp,"101"}});}catch(InvalidDataException){invalid=true;}
        Check(invalid && doc.Value(stock)=="12345","Invalid multi-field edit rolls back all tentative values");
        invalid=false;try{doc.Apply(new Dictionary<ManagementDocument.Field,string>{{order,"39"}});}catch(InvalidDataException){invalid=true;}
        Check(invalid && doc.Value(order)=="300","A partially delivered order cannot be resized below already-delivered pieces");
        Check(Enumerable.Range(0,6).Where(i=>i!=1).All(i=>doc.Line("nations.dat",seed.OrderLine+i)==seedLines[seed.OrderLine+i]),"Order resizing preserves all delivery dates, original payment and delivery history");
        var noOrder=doc.Records.First(r=>r.Domain=="Weapons" && doc.Numeric("nations.dat",r.Fields.Single(f=>f.Key=="OrderQuantity").Line)==0).Fields.Single(f=>f.Key=="OrderQuantity");
        invalid=false;try{doc.Apply(new Dictionary<ManagementDocument.Field,string>{{noOrder,"100"}});}catch(InvalidDataException){invalid=true;}
        Check(invalid,"Inactive weapon orders cannot be accidentally created with missing delivery terms");
        var result=await data.SaveAsync();data.MarkSavedStates();
        Check(result.WroteFiles && !data.HasUnsavedChanges,"Management-only Save changes commits successfully");
        Check(original.All(p=>p.Value.SequenceEqual(File.ReadAllBytes(Path.Combine(result.BackupDirectory!,p.Key!)))),"Management transaction backs up every original top-level file exactly");
        Check(original.All(p=>expected.TryGetValue(p.Key,out var lines)?lines.SequenceEqual(TextFileBuffer.Read(Path.Combine(copy,p.Key)).Lines):p.Value.SequenceEqual(File.ReadAllBytes(Path.Combine(copy,p.Key)))),"Save changes only staged lines; every other file remains byte-identical");
        Check(!(await data.SaveAsync()).WroteFiles,"A repeated save without changes performs no writes");
        using(var reload=new GrandTacticianDataService()) {await reload.LoadAsync(copy);Check(reload.Management!.Value(xp)=="37.5" && reload.Management.Value(date)=="1861-04-12" && reload.Management.Value(stock)=="12345" && reload.Management.Value(order)=="300" && reload.Management.Value(standard)=="1860.25","Officer, promotion, stock, order and standardization inputs survive a fresh parser load");}
        history.Undo(out _);Check(doc.Value(unionCash)==before[(unionCash.File,unionCash.Line)] && doc.Line(confCash.File,confCash.MirrorLine!.Value)==before[(confCash.File,confCash.MirrorLine.Value)],"Undo after Save restores both treasury copies for both factions");
        history.Undo(out _);Check(data.HasUnsavedChanges && before.All(p=>doc.Line(p.Key.File,p.Key.Line)==p.Value),"Undo after Save restores original values and marks them unsaved");
        await data.SaveAsync();data.MarkSavedStates();Check(original.All(p=>p.Value.SequenceEqual(File.ReadAllBytes(Path.Combine(copy,p.Key!)))),"Saving an undo restores all original file bytes");
        history.Redo(out _);Check(doc.Value(stock)=="12345" && data.HasUnsavedChanges,"Redo after save/undo restores the multi-domain plan");
        File.AppendAllText(Path.Combine(copy,"nations.dat"),"\nexternal change");
        invalid=false;try{await data.SaveAsync();}catch(IOException){invalid=true;}
        Check(invalid && File.ReadAllBytes(Path.Combine(copy,"commanders.txt")).SequenceEqual(original["commanders.txt"]),"External nations edits block the whole transaction before any other file changes");
        if(args.Length>1) {
            var classes=CampaignRules.Ships(source,args[1]);
            Check(classes.Count==41 && classes[0].Guns==2 && classes[0].Name.Contains("Tender"),"Ship classes parse their variable resource arrays and class gun counts");
            Check(classes[0].WorkDays(.5)==23 && classes[0].WorkDays(0)==0,"Remaining ship work days reproduce resource cost × fleet duration factor × saved fraction");
            Check(CampaignRules.Artillery(source,args[1])?.Count(60,0)==8,"Installed campaign artillery settings resolve to the verified gun calculation");
        }
    }
    if(args.Length>1) {
        var sourceProgress=Directory.EnumerateDirectories(args[0]).First(d=>File.Exists(Path.Combine(d,"commanders.txt")));
        var progressionCopy=Path.Combine(temp,"nation-progression");Directory.CreateDirectory(progressionCopy);
        foreach(var f in Directory.EnumerateFiles(sourceProgress))File.Copy(f,Path.Combine(progressionCopy,Path.GetFileName(f)));
        var nationFile=Path.Combine(progressionCopy,"nations.dat");
        // Mixed line endings deliberately exercise preservation when counted arrays grow.
        var rawLines=TextFileBuffer.Read(nationFile).Lines;
        File.WriteAllText(nationFile,string.Concat(rawLines.Select((s,i)=>s+(i%2==0?"\r\n":"\n"))),new UTF8Encoding(false));
        using var campaignProgress=new GrandTacticianDataService();await campaignProgress.LoadAsync(progressionCopy,args[1]);
        var document=campaignProgress.Management!;var progression=campaignProgress.Progression!;
        Check(progression.Notices.Count==0 && progression.Projects.Count==125 && progression.Policies.Count==117,"Installed project and policy catalogs load by named IDs and variable prerequisite/headline counts");
        Check(progression.Scenario=="001" && progression.Policies.Single(p=>p.Id==12).Name=="Military II" && progression.Policies.Single(p=>p.Id==3).Name=="Bread Basket I","Policy examples and active scenario are correctly mapped");
        Check(document.Records.Count(r=>r.Domain=="Funding" && r.Side==0)==6 && document.Records.Count(r=>r.Domain=="Funding" && r.Side==1)==6,"Exactly six spendable funding categories are exposed per faction");
        var seedPolicies=document.Records.Single(r=>r.Domain=="Progression"&&r.Side==0).Fields.Single(f=>f.Key=="Policies");
        var policySeed=document.ListValue(seedPolicies);for(int i=policySeed.Count-2;i>=0;i-=2)if(new[]{11,12}.Contains(int.Parse(policySeed[i])))policySeed.RemoveRange(i,2);
        document.Apply(new Dictionary<ManagementDocument.Field,string>{{seedPolicies,System.Text.Json.JsonSerializer.Serialize(policySeed)}});
        await campaignProgress.SaveAsync();campaignProgress.MarkSavedStates();
        // Fresh load makes the seeded fixture the baseline for save/undo byte comparisons.
        await campaignProgress.LoadAsync(progressionCopy,args[1]);document=campaignProgress.Management!;progression=campaignProgress.Progression!;
        var originalNationBytes=File.ReadAllBytes(nationFile);var originalOther=Directory.EnumerateFiles(progressionCopy).Where(f=>Path.GetFileName(f)!="nations.dat").ToDictionary(f=>f,File.ReadAllBytes);
        var funding=document.Records.Single(r=>r.Domain=="Funding"&&r.Side==1&&r.Id==5).Fields.Single();
        var cash=document.Records.Single(r=>r.Domain=="Treasury"&&r.Side==1).Fields.Single();
        var weaponStock=document.Records.First(r=>r.Domain=="Weapons"&&r.Side==1).Fields.Single(f=>f.Key=="Stock");
        int unionLevel=progression.Level(0,95),confLevel=progression.Level(1,95);var editHistory=new EditSession();
        editHistory.ExecuteManagement("Nation expansion",document,()=>{
            document.Apply(progression.CompleteProject(0,95,unionLevel+3).Changes);
            document.Apply(progression.CompleteProject(1,95,confLevel+2).Changes);
            document.Apply(progression.CompletePolicy(0,12).Changes);
            document.Apply(new Dictionary<ManagementDocument.Field,string>{{funding,"12345678"},{cash,"7654321"},{weaponStock,"2468"}});
        });
        Check(progression.Progress(0,11)==1 && progression.Progress(0,12)==1,"Military II completion includes unfinished Military I and sets progress to one");
        Check(progression.Level(0,95)==unionLevel+3 && progression.Level(1,95)==confLevel+2,"Repeatable projects add the exact requested levels independently per faction");
        bool noReplay=false;try{progression.CompletePolicy(0,12);}catch(InvalidOperationException){noReplay=true;}Check(noReplay,"Already-completed policies cannot be replayed");
        bool protectedProject=false;try{progression.CompleteProject(0,91,progression.Level(0,91)+1);}catch(InvalidOperationException){protectedProject=true;}Check(protectedProject,"Unmapped one-time support effects cannot be falsely marked complete");
        bool prewar=false;try{progression.CompletePolicy(1,75);}catch(InvalidOperationException){prewar=true;}Check(prewar,"Cross-faction/pre-war policy completion is blocked");
        var stagedLines=document.WorkingLines("nations.dat")!;await campaignProgress.SaveAsync();campaignProgress.MarkSavedStates();
        Check(stagedLines.SequenceEqual(TextFileBuffer.Read(nationFile).Lines) && !document.HasChanges && !(await campaignProgress.SaveAsync()).WroteFiles,"Counted-array growth commits and a second save is a no-op");
        using(var reread=new GrandTacticianDataService()) {
            await reread.LoadAsync(progressionCopy,args[1]);var loaded=reread.Management!;
            Check(loaded.Notices.Count==0 && reread.Progression!.Level(0,95)==unionLevel+3 && reread.Progression.Level(1,95)==confLevel+2 && reread.Progression.Progress(0,12)==1,"Reload follows shifted policy/project arrays through every alliance");
            Check(loaded.Value(loaded.Records.Single(r=>r.Domain=="Funding"&&r.Side==1&&r.Id==5).Fields.Single())=="12345678" && loaded.Value(loaded.Records.Single(r=>r.Domain=="Treasury"&&r.Side==1).Fields.Single())=="7654321","Later faction funding and mirrored treasury edits survive earlier array growth");
        }
        var next=progression.CompleteProject(0,95,unionLevel+4);editHistory.ExecuteManagement("Another level after save",document,()=>document.Apply(next.Changes));await campaignProgress.SaveAsync();campaignProgress.MarkSavedStates();
        Check(progression.Level(0,95)==unionLevel+4,"Additional progression edits work after a structural save");
        editHistory.Undo(out _);editHistory.Undo(out _);await campaignProgress.SaveAsync();campaignProgress.MarkSavedStates();
        Check(originalNationBytes.SequenceEqual(File.ReadAllBytes(nationFile)),"Undo after multiple structural saves restores original bytes including mixed line endings");
        Check(originalOther.All(k=>k.Value.SequenceEqual(File.ReadAllBytes(k.Key))),"Nation progression preserves every other save file");
        editHistory.Redo(out _);await campaignProgress.SaveAsync();campaignProgress.MarkSavedStates();Check(progression.Level(1,95)==confLevel+2,"Redo preserves project stages across save boundaries");
        // Seed absent one-time projects in this disposable copy, then verify their coupled grants.
        foreach(int side in new[]{0,1}) {
            var projectField=document.Records.Single(r=>r.Domain=="Progression"&&r.Side==side).Fields.Single(f=>f.Key=="Projects");
            var ids=document.ListValue(projectField);ids.RemoveAll(s=>new[]{5,10,11}.Contains(int.Parse(s)));
            document.Apply(new Dictionary<ManagementDocument.Field,string>{{projectField,System.Text.Json.JsonSerializer.Serialize(ids)}});
        }
        var grantBaseline=document.Capture();var grants=new EditSession();
        foreach(var grant in new[]{(Side:0,Id:5,Weapons:new[]{36,86},Amounts:new[]{3000d,2500d}),(Side:0,Id:10,Weapons:new[]{37,38},Amounts:new[]{32d,32d}),(Side:1,Id:11,Weapons:new[]{12},Amounts:new[]{48d})}) {
            var fields=grant.Weapons.Select(id=>document.Records.Single(r=>r.Domain=="Weapons"&&r.Side==grant.Side&&r.Id==id).Fields.Single(f=>f.Key=="Stock")).ToArray();
            var before=fields.Select(f=>document.Numeric(f.File,f.Line)).ToArray();
            var grantPlan=progression.CompleteProject(grant.Side,grant.Id,1);
            Check(progression.Level(grant.Side,grant.Id)==0 && fields.Select((f,i)=>document.Numeric(f.File,f.Line)==before[i]).All(x=>x),"Project "+grant.Id+" preview does not grant stock before staging");
            grants.ExecuteManagement("One-time grant",document,()=>document.Apply(grantPlan.Changes));
            Check(progression.Level(grant.Side,grant.Id)==1 && fields.Select((f,i)=>document.Numeric(f.File,f.Line)==before[i]+grant.Amounts[i]).All(x=>x),"Project "+grant.Id+" completion grants the exact traced weapon quantities");
            bool replay=false;try{progression.CompleteProject(grant.Side,grant.Id,2);}catch(InvalidOperationException){replay=true;}Check(replay,"Project "+grant.Id+" cannot replay its one-time grant");
        }
        await campaignProgress.SaveAsync();campaignProgress.MarkSavedStates();
        using(var grantReload=new GrandTacticianDataService()) {
            await grantReload.LoadAsync(progressionCopy,args[1]);
            Check(document.WorkingLines("nations.dat")!.SequenceEqual(grantReload.Management!.WorkingLines("nations.dat")!),"One-time project levels and weapon grants survive save/reload together");
        }
        grants.Undo(out _);grants.Undo(out _);grants.Undo(out _);
        var afterGrantUndo=document.Capture();
        Check(grantBaseline.All(p=>afterGrantUndo[p.Key]==p.Value),"Undo restores project levels and all coupled weapon stocks after saving");
        var nearPolicyField=document.Records.Single(r=>r.Domain=="Progression"&&r.Side==0).Fields.Single(f=>f.Key=="Policies");
        var nearSeed=document.ListValue(nearPolicyField);
        for(int i=nearSeed.Count-2;i>=0;i-=2)if(new[]{11,12,33,34}.Contains(int.Parse(nearSeed[i])))nearSeed.RemoveRange(i,2);
        document.Apply(new Dictionary<ManagementDocument.Field,string>{{nearPolicyField,System.Text.Json.JsonSerializer.Serialize(nearSeed)}});
        var nearBaseline=document.Capture();var nearHistory=new EditSession();
        var nearPlan=progression.CompletePolicy(0,33,true);
        Check(progression.Progress(0,33)==0,"Near-completion preview does not mutate research");
        nearHistory.ExecuteManagement("Finish through game research",document,()=>document.Apply(nearPlan.Changes));
        Check(new[]{11,12,33,34}.All(id=>progression.Progress(0,id)>=0.999989&&progression.Progress(0,id)<1),"Act and complete prerequisite chain are set below one at 99.999 percent");
        Check((float)NationProgression.NearCompletion<1,"Near-completion value remains below one at game Single precision");
        bool readyReplay=false;try{progression.CompletePolicy(0,33,true);}catch(InvalidOperationException){readyReplay=true;}
        Check(readyReplay,"Already-ready research cannot create another redundant transaction");
        var pastActs=document.Records.Single(r=>r.Domain=="Progression"&&r.Side==0).Fields.Single(f=>f.Key=="PastActs");
        Check(document.Value(pastActs)==nearBaseline[(pastActs.File,pastActs.Line)],"Near-completion preserves one-time activation history for the game's completion step");
        await campaignProgress.SaveAsync();campaignProgress.MarkSavedStates();
        using(var nearReload=new GrandTacticianDataService()) {
            await nearReload.LoadAsync(progressionCopy,args[1]);
            Check(new[]{11,12,33,34}.All(id=>nearReload.Progression!.Progress(0,id)>=0.999989&&nearReload.Progression.Progress(0,id)<1),"Near-complete policy and act research survives save/reload without rounding to complete");
        }
        nearHistory.Undo(out _);var nearRestored=document.Capture();Check(nearBaseline.All(p=>nearRestored[p.Key]==p.Value),"Near-completion and its prerequisites undo together after save");
        Check(progression.PolicyCategory(progression.Policies.Single(p=>p.Id==33))==4,"Shared prerequisite paths keep military acts in the military column");
        var batchOfficers=document.Records.Where(r=>r.Domain=="Officers"&&r.Side==1).Take(3).ToList();
        var batchWeapons=document.Records.Where(r=>r.Domain=="Weapons"&&r.Side==1).Take(3).ToList();
        var batchShips=document.Records.Where(r=>r.Domain=="Navy").Take(2).ToList();
        var batchBefore=document.Capture();var batchHistory=new EditSession();
        var officerPlan=ManagementBatchPlanner.Plan(document,batchOfficers,new Dictionary<string,string>{{"Experience","87.5"},{"Fame","99.5"},{"Leadership","100"},{"Administration","91"},{"Initiative","80"},{"Cunning","0"}});
        batchHistory.ExecuteManagement("Officer batch",document,()=>document.Apply(officerPlan));
        Check(batchOfficers.All(r=>document.Value(r.Fields.Single(f=>f.Key=="Fame"))=="99.5"),"Officer Fame is editable in batch in saved 0–100 units");
        Check(batchOfficers.All(r=>r.Fields.Where(f=>new[]{"Leadership","Administration","Initiative","Cunning"}.Contains(f.Key)).All(f=>document.Value(f)==(f.Key=="Leadership"?"100":f.Key=="Administration"?"91":f.Key=="Initiative"?"80":"0"))),"Officer batch edits all four attributes on every selected officer in 0–100 units");
        bool badAttribute=false;try{ManagementBatchPlanner.Plan(document,batchOfficers,new Dictionary<string,string>{{"Leadership","101"},{"Experience","60"}});}catch(InvalidDataException){badAttribute=true;}
        Check(badAttribute&&document.Value(batchOfficers[0].Fields.Single(f=>f.Key=="Experience"))=="87.5","Invalid batch attribute leaves all selected officers unchanged");
        var weaponsPlan=ManagementBatchPlanner.Plan(document,batchWeapons,new Dictionary<string,string>{{"Stock","6543"},{"StandardizationYear","1861.5"}});
        batchHistory.ExecuteManagement("Weapons batch",document,()=>document.Apply(weaponsPlan));
        Check(batchWeapons.All(r=>document.Value(r.Fields.Single(f=>f.Key=="Stock"))=="6543"),"Weapon stock and standardization support one atomic multi-record batch");
        var shipPlan=ManagementBatchPlanner.Plan(document,batchShips,new Dictionary<string,string>{{"Condition","99"},{"Name","Batch ship"}});
        batchHistory.ExecuteManagement("Ship batch",document,()=>document.Apply(shipPlan));
        Check(batchShips.All(r=>document.Value(r.Fields.Single(f=>f.Key=="Condition"))=="99"&&document.Value(r.Fields.Single(f=>f.Key=="Name"))=="Batch ship"),"Ship name and condition can be batch edited without changing work timers");
        Check(batchShips.SelectMany(r=>r.Fields.Where(f=>f.Kind is "completion" or "fraction")).All(f=>document.Line(f.File,f.Line)==batchBefore[(f.File,f.Line)]),"Condition is independent of construction and repair timers");
        await campaignProgress.SaveAsync();campaignProgress.MarkSavedStates();
        using(var batchReload=new GrandTacticianDataService()) {
            await batchReload.LoadAsync(progressionCopy,args[1]);var reloaded=batchReload.Management!;
            Check(new[]{"commanders.txt","ships.dat","nations.dat"}.All(file=>document.WorkingLines(file)!.SequenceEqual(reloaded.WorkingLines(file)!)),"All three management batch domains survive combined save/reload");
        }
        batchHistory.Undo(out _);batchHistory.Undo(out _);batchHistory.Undo(out _);var batchRestored=document.Capture();
        Check(batchBefore.All(p=>batchRestored[p.Key]==p.Value),"Batch undo restores all selected records across saves");
        // Seed a ship under construction in this disposable fixture, including its condition.
        var readinessFile=Path.Combine(progressionCopy,"ships.dat");var readinessLines=document.WorkingLines("ships.dat")!;
        var readinessShip=batchShips[0];var construction=readinessShip.Fields.Single(f=>f.Key=="ConstructionCompletion");var repair=readinessShip.Fields.Single(f=>f.Key=="RepairRemaining");var hull=readinessShip.Fields.Single(f=>f.Key=="Condition");
        readinessLines[construction.Line]="0.6";readinessLines[repair.Line]="0";readinessLines[hull.Line]="40";File.WriteAllLines(readinessFile,readinessLines);
        var readinessDoc=ManagementDocument.Load(progressionCopy);var readyShip=readinessDoc.Records.Single(r=>r.Domain=="Navy"&&r.Id==readinessShip.Id);var readyFields=readyShip.Fields.ToDictionary(f=>f.Key);
        var readyBefore=readinessDoc.Capture();var readyHistory=new EditSession();
        var readinessPlan=ManagementBatchPlanner.Plan(readinessDoc,new[]{readyShip},new Dictionary<string,string>{{"ConstructionCompletion","99"}});
        readyHistory.ExecuteManagement("Nearly built",readinessDoc,()=>readinessDoc.Apply(readinessPlan));
        Check(Math.Abs(readinessDoc.Numeric(construction.File,construction.Line)-.01)<.000001&&Math.Abs(readinessDoc.Numeric(hull.File,hull.Line)-99)<.001,"99 percent built stores one percent construction remaining and advances hull condition consistently");
        readinessDoc.Apply(ManagementBatchPlanner.Plan(readinessDoc,new[]{readyShip},new Dictionary<string,string>{{"ConstructionCompletion","100"}}));
        Check(readinessDoc.Numeric(construction.File,construction.Line)==0&&readinessDoc.Numeric(hull.File,hull.Line)==100,"100 percent construction clears its timer and finishes hull condition");
        Check(!ManagementBatchPlanner.CommonFields(readinessDoc,new[]{readyShip}).Any(f=>f.Key=="ConstructionCompletion"),"Finished ships do not offer a control that restarts construction");
        readyHistory.Undo(out _);var readyUndo=readinessDoc.Capture();Check(readyBefore.All(p=>readyUndo[p.Key]==p.Value),"Construction progress and coupled condition undo together");
        readinessLines[construction.Line]="0";readinessLines[repair.Line]="0.6";File.WriteAllLines(readinessFile,readinessLines);readinessDoc=ManagementDocument.Load(progressionCopy);readyShip=readinessDoc.Records.Single(r=>r.Domain=="Navy"&&r.Id==readinessShip.Id);
        readinessDoc.Apply(ManagementBatchPlanner.Plan(readinessDoc,new[]{readyShip},new Dictionary<string,string>{{"RepairRemaining","0"}}));
        Check(readinessDoc.Numeric(repair.File,repair.Line)==0&&readinessDoc.Numeric(hull.File,hull.Line)==100,"Zero remaining repair work clears the repair timer and restores condition");
    }
    Console.WriteLine($"ALL {checks} CHECKS PASSED");
}
catch(Exception ex) {Console.Error.WriteLine(ex);Environment.ExitCode=1;}
finally { Directory.Delete(temp, true); }
