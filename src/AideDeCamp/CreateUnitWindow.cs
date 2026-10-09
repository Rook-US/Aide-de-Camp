using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;

/// <summary>One movable quick-create surface; filtering never performs file I/O.</summary>
public sealed class CreateUnitWindow : Window
{
    public sealed record Catalog(IReadOnlyList<CreationOfficer> Officers, IReadOnlyList<WeaponOption> Weapons,
        IReadOnlyList<StateOption> States, IReadOnlyDictionary<int, int> Maximums,
        IReadOnlyList<TownStateMap.MappedTown> Towns, string TownProblem,
        IReadOnlyList<CreationPerkOption> CombatPerks, IReadOnlyList<CreationPerkOption> HqPerks,
        IReadOnlyList<GroupNode> Groups, IReadOnlySet<int> AssignedCommanders, IReadOnlyList<CreationUniform> Uniforms, IReadOnlyList<ManagementSnapshot.State> StateRules);
    private sealed record Choice(int Id, string Label) { public override string ToString() => Label; }
    private sealed record TownChoice(TownStateMap.MappedTown Town, string Label) { public override string ToString() => Label; }
    private readonly GrandTacticianDataService _data;
    private readonly NamingSchemeService _naming;
    private readonly Catalog _catalog;
    private readonly ComboBox _faction = Combo(), _kind = Combo(), _tier = Combo(), _parent = Combo(), _commander = Combo(), _weapon = Combo(), _home = Combo(), _recruit = Combo(), _state = Combo(), _town = Combo(), _names = Combo(), _uniform = Combo();
    private readonly TextBox _name = Text(), _size = Text(), _contract = Text("12"), _search = Text();
    private readonly CheckBox _advanced = new() { Content = "Advanced • show all sections and browse all weapons", Margin = new(0, 8, 0, 8) };
    private readonly CheckBox _horse = new() { Content = "Horse artillery" };
    private readonly Slider _experience = Slider(), _training = Slider();
    private readonly TextBlock _experienceText = Hint(""), _placementText = Hint(""), _sizeText = Hint(""), _review = Hint("");
    private readonly StackPanel _combat = new(), _location = new(), _perkPanel = new();
    private readonly List<(ComboBox Choice, Slider Progress)> _perks = new();
    private readonly TextBox[] _stock = [Text("100"), Text("100"), Text("100"), Text("100")];
    private readonly CheckBox _rawStock = new() { Content = "Enter raw stock amounts instead of percentages" };
    private readonly Expander _equipmentSection, _experienceSection;
    private bool _refreshing;
    public CreationRequest? Request { get; private set; }
    public bool Confirmed { get; private set; }

    public static Catalog LoadCatalog(GrandTacticianDataService data)
    {
        IReadOnlyList<TownStateMap.MappedTown> towns = []; string problem = "";
        try { towns = data.GetCreationTowns(); } catch (Exception e) { problem = e.Message; }
        return new(data.GetCreationOfficers(), data.WeaponOptions, data.StateOptions, data.GetCreationMaximums(), towns, problem,
            data.GetCreationPerks(false), data.GetCreationPerks(true), data.Groups.Values.ToArray(),
            data.Groups.Values.Select(g => g.CommanderId).Concat(data.Units.Select(u => u.CommanderId)).ToHashSet(), data.GetCreationUniforms(), data.GetCreationStateRules());
    }
    public CreateUnitWindow(GrandTacticianDataService data, NamingSchemeService naming, Catalog catalog, int faction, OobNode? context)
    {
        _data = data; _naming = naming; _catalog = catalog;
        Title = "Create Unit"; Width = 830; Height = 840; MinWidth = 650; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(20, 28, 36)); Foreground = Brushes.Gainsboro;
        var root = new DockPanel { Margin = new(20) }; Content = root;
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "Create a command or combat unit", FontSize = 23, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(Hint("Choose the formation first. Open any section in any order. Native tiers are shown alongside your display scale."));
        heading.Children.Add(_advanced);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(_review);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new(6), Padding = new(16, 7, 16, 7) };
        var create = new Button { Content = "Review & Create", Margin = new(6), Padding = new(16, 7, 16, 7) };
        create.Click += ReviewCreate; buttons.Children.Add(cancel); buttons.Children.Add(create); footer.Children.Add(buttons);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var identity = new StackPanel(); body.Children.Add(Section("1  Formation and placement", identity, true));
        Row(identity, "Faction", _faction); Row(identity, "Create", _kind); Row(identity, "Tier", _tier);
        Row(identity, "Name", _name);
        var nameBar = new DockPanel(); var suggest = new Button { Content = "Suggest names", Margin = new(0, 0, 8, 0) }; DockPanel.SetDock(suggest, Dock.Left); nameBar.Children.Add(suggest); nameBar.Children.Add(_names); Row(identity, "Available alternatives", nameBar);
        suggest.Click += (_, _) => SuggestNames(); _names.SelectionChanged += (_, _) => { if (_names.SelectedItem is string name) _name.Text = name; };
        Row(identity, "Parent HQ", _parent); identity.Children.Add(_placementText);
        Row(_location, "Filter by state", _state); Row(_location, "Find town", _search); Row(_location, "Spawn town", _town); identity.Children.Add(_location);
        Row(identity, "Commander", _commander);
        Row(identity, "Uniform colors", _uniform);
        _uniform.ToolTip = "Copies only the three mapped RGB color fields from an existing faction style, never a unit's record or hierarchy.";
        _equipmentSection = Section("2  Recruitment, equipment and starting stock", _combat, true); body.Children.Add(_equipmentSection);
        Row(_combat, "Home state", _home); Row(_combat, "Recruitment", _recruit); Row(_combat, "Contract (months)", _contract);
        Row(_combat, "Starting size (men)", _size); _combat.Children.Add(_sizeText);
        Row(_combat, "Weapon", _weapon); _combat.Children.Add(_horse);
        _combat.Children.Add(Hint("Supply values are per-unit stock, not HQ supply flow. 100% means one stock unit per man. The game may refill stocks after time advances."));
        _combat.Children.Add(_rawStock);
        var stockNames = new[] { "Small-arms ammunition", "Artillery ammunition", "Provisions", "Forage" };
        for (int i = 0; i < 4; i++) Row(_combat, stockNames[i], _stock[i]);
        _rawStock.Checked += (_, _) => ConvertStock(true); _rawStock.Unchecked += (_, _) => ConvertStock(false);
        var experiencePanel = new StackPanel(); _experienceSection = Section("3  Training, experience and perks", experiencePanel, false); body.Children.Add(_experienceSection);
        Row(experiencePanel, "Training (0–100)", _training); Row(experiencePanel, "Combat experience", _experience); experiencePanel.Children.Add(_experienceText);
        var presets = new WrapPanel();
        for (int i = 0; i < CreationRecords.ExperienceLabels.Length; i++) { int value = i * 20; var preset = new Button { Content = CreationRecords.ExperienceLabels[i] + $" ({value})", Margin = new(2), Padding = new(5, 3, 5, 3) }; preset.Click += (_, _) => { if (!Hq) _experience.Value = value; }; presets.Children.Add(preset); }
        experiencePanel.Children.Add(presets);
        experiencePanel.Children.Add(Hint("Combat experience and perk progress are separate. An empty perk at 100% progress offers an in-game choice. Assigned perks use levels I–III.")); experiencePanel.Children.Add(_perkPanel);
        body.Children.Add(Hint("Creation is staged in ADC. Undo removes the whole creation; Save changes makes a full backup. Future Army Builder templates will reuse these same fields and checks."));
        Set(_faction, [new(0, "Union"), new(1, "Confederacy")], faction);
        Set(_kind, [new(-1, "Headquarters / command"), new(0, "Infantry"), new(1, "Cavalry"), new(2, "Artillery")], context is null ? -1 : 0);
        Set(_recruit, [new(0, "Volunteers"), new(1, "Drafted")], 0);
        Set(_home, catalog.States.Where(s => PlayableState(s.Id)).Select(s => new Choice(s.Id, s.Name)), -1);
        Set(_state, new[] { new Choice(-1, "All verified states") }.Concat(catalog.Towns.Select(t => t.StateId).Distinct().Select(id => new Choice(id, StateName(id))).OrderBy(c => c.Label)), -1);
        _search.ToolTip = "Search saved town names; only towns with verified state and position links are listed.";
        _search.TextChanged += (_, _) => FilterTowns(); _state.SelectionChanged += (_, _) => FilterTowns();
        _kind.SelectionChanged += (_, _) => RefreshChoices(true); _faction.SelectionChanged += (_, _) => RefreshChoices(false); _tier.SelectionChanged += (_, _) => RefreshParents(null);
        _parent.SelectionChanged += (_, _) => UpdatePlacement();
        _advanced.Checked += (_, _) => { _equipmentSection.IsExpanded = true; _experienceSection.IsExpanded = true; RefreshWeapons(); RefreshHomeStates(); RefreshPerks(true); };
        _advanced.Unchecked += (_, _) => { RefreshWeapons(); RefreshHomeStates(); RefreshPerks(true); };
        _experience.ValueChanged += (_, _) => UpdateExperience(); _training.ValueChanged += (_, _) => UpdateExperience(); _size.TextChanged += (_, _) => UpdateSize();
        RefreshChoices(true);
        int? parentId = context is GroupNode g ? g.GroupId : context is CombatUnitNode u ? u.ParentId : null;
        RefreshParents(parentId); FilterTowns(); SuggestNames();
    }
    private static bool PlayableState(int id) => id is >= 0 and <= 40 or 46 or 47 or 50 or 51;
    private bool Hq => Selected(_kind) == -1;
    private int Faction => Selected(_faction);
    private static ComboBox Combo() => new() { MinHeight = 29, IsTextSearchEnabled = true, MaxDropDownHeight = 270 };
    private static TextBox Text(string value = "") => new() { Text = value, MinHeight = 29, VerticalContentAlignment = VerticalAlignment.Center };
    private static Slider Slider() => new() { Minimum = 0, Maximum = 100, TickFrequency = 20, TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight, IsSnapToTickEnabled = false, AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft, AutoToolTipPrecision = 0, MinHeight = 32 };
    private static TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 10), Opacity = .85 };
    private static Expander Section(string title, UIElement content, bool open) => new() { Header = title, Content = content, IsExpanded = open, Margin = new(0, 8, 10, 8), Padding = new(4) };
    private static void Row(Panel panel, string title, UIElement control)
    {
        var row = new Grid { Margin = new(0, 5, 0, 5) }; row.ColumnDefinitions.Add(new() { Width = new(190) }); row.ColumnDefinitions.Add(new());
        row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }); Grid.SetColumn(control, 1); row.Children.Add(control); panel.Children.Add(row);
    }
    private static int Selected(ComboBox combo) => combo.SelectedItem is Choice c ? c.Id : -1;
    private static void Set(ComboBox combo, IEnumerable<Choice> choices, int selected)
    {
        var list = choices.ToArray(); combo.ItemsSource = list; combo.SelectedItem = list.FirstOrDefault(c => c.Id == selected);
    }
    private string StateName(int id) => _catalog.States.FirstOrDefault(s => s.Id == id)?.Name ?? $"State {id}";
    private void RefreshChoices(bool resetKind)
    {
        if (_refreshing) return; _refreshing = true;
        var tiers = Hq ? new[] {16, 15, 14} : Selected(_kind) == 2 ? new[] {11, 10, 12, 13} : new[] {13, 12, 11, 10};
        int tier = resetKind ? tiers[0] : Selected(_tier);
        Set(_tier, tiers.Select(t => new Choice(t, $"{OobPresentation.TierName(t, Hq)}  (native {t})")), tier);
        Set(_commander, new[] {new Choice(-1, "Select an unused commander…")}.Concat(_catalog.Officers.Where(o => o.Faction == Faction && !_catalog.AssignedCommanders.Contains(o.Id)).OrderBy(o => o.Name).Select(o => new Choice(o.Id, $"{o.Name}  • rank {o.Rank}"))), -1);
        Set(_uniform, _catalog.Uniforms.Select((style, index) => (style, index)).Where(p => p.style.Faction == Faction).Select(p => new Choice(p.index, $"Coat {p.style.Coat} • trousers {p.style.Trousers}")), -1);
        if (_uniform.Items.Count > 0) _uniform.SelectedIndex = 0;
        _combat.IsEnabled = !Hq; _combat.ToolTip = Hq ? "HQs have no combat weapon, men, contract, home-state or stock record. These fields apply to their combat units." : null;
        _training.IsEnabled = _experience.IsEnabled = !Hq;
        _training.ToolTip = _experience.ToolTip = Hq ? "HQ records store perk progress, not combat training or experience." : null;
        _horse.IsEnabled = !Hq && Selected(_kind) == 2;
        if (!_horse.IsEnabled) _horse.IsChecked = false;
        if (!Hq && resetKind) _size.Text = _catalog.Maximums[Selected(_kind)].ToString();
        _refreshing = false;
        RefreshWeapons(); RefreshHomeStates(); RefreshParents(null); RefreshPerks(); UpdateExperience(); UpdateSize();
    }
    private void RefreshHomeStates()
    {
        int old = Selected(_home);
        Set(_home, _catalog.StateRules.Where(s => s.Side == Faction && PlayableState(s.Id) && (s.Recruitable || _advanced.IsChecked == true)).OrderBy(s => s.Name)
            .Select(s => new Choice(s.Id, s.Name + (s.Recruitable ? "" : " • not recruitable; confirmation required"))), old);
    }
    private void RefreshWeapons()
    {
        int old = Selected(_weapon); bool all = _advanced.IsChecked == true;
        Set(_weapon, _catalog.Weapons.Where(w => all || w.UnitType == Selected(_kind)).Select(w => new Choice(w.Id, w.Name + (all ? $" • {w.UnitType switch {0 => "Infantry", 1 => "Cavalry", 2 => "Artillery", _ => "Other"}}" : ""))), old);
        _weapon.ToolTip = all ? "All weapons are available to browse. Creation still requires equipment compatible with the selected branch." : "Weapons filtered by configured unit branch.";
    }
    private void RefreshParents(int? preferred)
    {
        if (_refreshing) return;
        int old = preferred ?? Selected(_parent);
        var choices = _catalog.Groups.Where(g => _data.IsCreationParent(g, Faction) && (!Hq || g.UnitTier > Selected(_tier)))
            .OrderBy(g => g.Name).Select(g => new Choice(g.GroupId, $"{g.Name} • {g.TierName} HQ • #{g.GroupId}"));
        Set(_parent, new[] { new Choice(-1, Hq ? "Independent command • choose a town" : "Select parent HQ…") }.Concat(choices), old);
        if (_parent.SelectedItem is null) _parent.SelectedIndex = 0;
        UpdatePlacement();
    }
    private void UpdatePlacement()
    {
        bool independent = Hq && Selected(_parent) < 0;
        _location.Visibility = independent ? Visibility.Visible : Visibility.Collapsed;
        _placementText.Text = independent ? (_catalog.TownProblem.Length > 0 ? "Town mapping unavailable: " + _catalog.TownProblem : "Independent command: select a verified town below.") : Selected(_parent) >= 0 ? "Placement follows this explicit parent HQ. No independent town deployment is written." : "Combat units need an existing parent HQ.";
    }
    private void FilterTowns()
    {
        var old = (_town.SelectedItem as TownChoice)?.Town;
        string search = _search.Text.Trim(); int state = Selected(_state);
        var choices = _catalog.Towns.Where(t => (state < 0 || t.StateId == state) && t.Location.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Location.Name).Select(t => new TownChoice(t, $"{t.Location.Name}, {StateName(t.StateId)} • {(t.Location.Owner == 0 ? "Union" : "Confederate")}" )).ToArray();
        _town.ItemsSource = choices; _town.SelectedItem = choices.FirstOrDefault(t => t.Town == old);
        if (_search.IsKeyboardFocusWithin && search.Length > 0) _town.IsDropDownOpen = true;
    }
    private void RefreshPerks(bool preserve = false)
    {
        var previous = preserve ? _perks.Select(p => (Id: Selected(p.Choice), Progress: p.Progress.Value)).ToArray() : [];
        _perkPanel.Children.Clear(); _perks.Clear(); var catalog = Hq ? _catalog.HqPerks : _catalog.CombatPerks;
        for (int slot = 0; slot < (Hq ? 4 : 1); slot++) {
            int oldId = slot < previous.Length ? previous[slot].Id : -1;
            var choice = Combo(); Set(choice, new[] { new Choice(-1, "No assigned perk") }.Concat(catalog.Where(p => Hq || _advanced.IsChecked == true || CreationRecords.IsStandardCombatPerk(Selected(_kind), p.Id) || p.Id * 3 + p.Level == oldId)
                .Select(p => new Choice(p.Id * 3 + p.Level, p.Name + (!Hq && !CreationRecords.IsStandardCombatPerk(Selected(_kind), p.Id) ? " • outside branch list" : "")))), oldId);
            var progress = Slider(); progress.ToolTip = "0–100% perk progress. This is independent of combat experience.";
            if (slot < previous.Length) progress.Value = previous[slot].Progress;
            Row(_perkPanel, (Hq ? "HQ" : "Combat") + $" perk {slot + 1}", choice); Row(_perkPanel, "Perk progress (%)", progress); _perks.Add((choice, progress));
            choice.SelectionChanged += (_, _) => { int value = Selected(choice); choice.ToolTip = value < 0 ? "At 100% progress an empty slot offers an in-game choice." : catalog.First(p => p.Id == value / 3 && p.Level == value % 3).Description; };
        }
    }
    private void UpdateExperience()
    {
        int stars = (int)Math.Floor(_experience.Value / 20);
        _experienceText.Text = Hq ? "HQ progression is represented by its four separate perk slots." : $"Training: {_training.Value:0}/100   •   Combat experience: {_experience.Value:0}/100   {new string('★', stars)}{new string('☆', 5 - stars)} • {CreationRecords.ExperienceLabels[stars]}";
    }
    private void UpdateSize()
    {
        if (Hq) { _sizeText.Text = "Combat-only fields are unavailable for HQ records."; return; }
        _sizeText.Text = $"Configured maximum: {_catalog.Maximums[Selected(_kind)]:N0} men.";
        if (Selected(_kind) == 2 && int.TryParse(_size.Text, out var men)) {
            var rule = _data.Units.FirstOrDefault(u => u.UnitType == 2)?.ArtilleryCalculation;
            if (rule?.Count(men, 0) is int guns) _sizeText.Text += $"  Estimated guns: {guns:N0}.";
        }
    }
    private void ConvertStock(bool toRaw)
    {
        if (!double.TryParse(_size.Text, out var size) || size <= 0) return;
        foreach (var box in _stock) if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) box.Text = (toRaw ? v * size / 100 : v * 100 / size).ToString("0.###", CultureInfo.InvariantCulture);
    }
    private void SuggestNames()
    {
        var used = _data.Units.Select(u => u.Name).Concat(_data.Groups.Values.Select(g => g.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        var sample = new CombatUnitNode { Name = _name.Text, ParentId = Selected(_parent), WeaponId = Selected(_weapon), UnitType = Math.Max(0, Selected(_kind)), UnitTier = Selected(_tier), Nation = Faction, StateId = Selected(_home), HomeStateName = StateName(Selected(_home)) };
        if (!Hq && Selected(_home) >= 0) names.AddRange(_naming.SuggestCreationNames(sample, _data.Units, used, _data.GetStateAbbreviation));
        for (int n = 1; n <= 10000 && names.Count < 6; n++) {
            string name = $"{n}{(n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch {1 => "st", 2 => "nd", 3 => "rd", _ => "th"})} {(Hq ? OobPresentation.TierName(Selected(_tier), true) : sample.TypeName + " " + sample.TierName)}";
            if (!used.Contains(name) && !names.Contains(name)) names.Add(name);
        }
        _names.ItemsSource = names;
        if (string.IsNullOrWhiteSpace(_name.Text) && names.Count > 0) _names.SelectedIndex = 0;
    }
    private void ReviewCreate(object sender, RoutedEventArgs e)
    {
        try {
            int size = Hq ? 0 : int.Parse(_size.Text, CultureInfo.InvariantCulture);
            var perks = _perks.Select(p => Selected(p.Choice) < 0 ? new CreationPerk(-1, 0, p.Progress.Value / 100) : new CreationPerk(Selected(p.Choice) / 3, Selected(p.Choice) % 3, p.Progress.Value / 100)).ToArray();
            if (Selected(_uniform) < 0) throw new InvalidOperationException("No saved uniform color style is available for this faction.");
            var uniform = _catalog.Uniforms[Selected(_uniform)];
            var blueprint = new UnitBlueprint { Headquarters = Hq, Faction = Faction, Name = _name.Text.Trim(), NativeTier = Selected(_tier), UnitType = Math.Max(0, Selected(_kind)),
                Coat = uniform.Coat, Trousers = uniform.Trousers, ColorVariation = uniform.Variation,
                CommanderId = Selected(_commander), Strength = size, WeaponId = Hq ? -1 : Selected(_weapon), HomeStateId = Hq ? -1 : Selected(_home), ContractMonths = Hq ? 12 : int.Parse(_contract.Text), RecruitingType = Hq ? 0 : Selected(_recruit),
                Experience = Hq ? 0 : _experience.Value, Training = Hq ? 0 : _training.Value, HorseArtillery = !Hq && _horse.IsChecked == true, Perks = perks,
                SupplyPercent = Hq ? [100, 100, 100, 100] : _stock.Select(s => double.Parse(s.Text, CultureInfo.InvariantCulture) * (_rawStock.IsChecked == true ? 100.0 / size : 1)).ToArray() };
            var request = new CreationRequest(blueprint, new(Selected(_parent) < 0 ? null : Selected(_parent), Hq && Selected(_parent) < 0 ? (_town.SelectedItem as TownChoice)?.Town : null));
            var check = _data.AssessCreation(request);
            if (!check.CanCreate) { _review.Text = string.Join("\n", check.Errors); return; }
            var placement = request.Placement.ParentId is int id ? _catalog.Groups.Single(g => g.GroupId == id).Name + " (parent HQ)" : request.Placement.Town!.Location.Name;
            string summary = $"{blueprint.Name}\n{(Faction == 0 ? "Union" : "Confederacy")} • {OobPresentation.TierName(blueprint.NativeTier, Hq)} (native {blueprint.NativeTier})\nPlacement: {placement}\nCommander: {_commander.Text}";
            if (!Hq) summary += $"\n{_kind.Text} • {size:N0} men • {_weapon.Text}\nHome state: {StateName(blueprint.HomeStateId)} • {_recruit.Text} • {blueprint.ContractMonths} months\nTraining: {blueprint.Training:0}/100 • Experience: {blueprint.Experience:0}/100\nStarting stock (small arms / artillery / provisions / forage):\n" + string.Join(" / ", blueprint.SupplyPercent.Select(p => $"{p:0.##}% ({size * p / 100:0.##})"));
            var perkCatalog = Hq ? _catalog.HqPerks : _catalog.CombatPerks;
            summary += "\nPerks: " + string.Join("; ", blueprint.Perks.Select(p => (p.Id < 0 ? "No perk" : perkCatalog.First(c => c.Id == p.Id && c.Level == p.Level).Name) + $" • {p.Progress * 100:0}% progress"));
            if (new CreationReviewWindow(summary, check.Confirmations) { Owner = this }.ShowDialog() != true) return;
            Request = request; Confirmed = true; DialogResult = true;
        } catch (Exception error) { _review.Text = "Check the entered values: " + error.Message; }
    }
}
