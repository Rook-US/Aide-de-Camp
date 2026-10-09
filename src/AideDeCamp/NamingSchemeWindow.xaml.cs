using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;

public partial class NamingSchemeWindow : Window
{
    private readonly GrandTacticianDataService _data;
    private readonly NamingSchemeService _naming;
    private readonly ObservableCollection<NamingRule> _rules;
    private readonly ObservableCollection<NamingToken> _tokens = new();
    private readonly ObservableCollection<SpecialNameEntry> _specialNames = new();
    private readonly ObservableCollection<RenamePreviewItem> _preview = new();
    private NamingRule? _current;
    private bool _loading;
    private Point _tokenDragStart;
    private NamingToken? _dragToken;

    public bool WorkingNamesChanged { get; private set; }

    public NamingSchemeWindow(GrandTacticianDataService data, NamingSchemeService naming, int nation)
    {
        InitializeComponent();
        _data = data; _naming = naming;
        _naming.CurrentNation = nation;
        var classifier = new CommandClassificationService();
        _naming.Classify = u => classifier.CategoryForUnit(u, data.Groups);
        _rules = new ObservableCollection<NamingRule>(_naming.Settings.Rules);
        RuleList.ItemsSource = _rules;
        TokenList.ItemsSource = _tokens;
        SpecialNamesGrid.ItemsSource = _specialNames;
        RenamePreviewGrid.ItemsSource = _preview;
        UnitTypeCombo.ItemsSource = new[] { new NamingUnitTypeOption(null, "Any Type"), new NamingUnitTypeOption(0, "Infantry"), new NamingUnitTypeOption(1, "Cavalry"), new NamingUnitTypeOption(2, "Artillery") };
        var tiers = new List<NamingTierOption> { new(null, "Any Tier") };
        tiers.AddRange(Enumerable.Range(10, 9).Reverse().Select(t => new NamingTierOption(t, NamingSchemeService.TierName(t))));
        UnitTierCombo.ItemsSource = tiers;
        NumberStyleCombo.ItemsSource = Enum.GetValues<NumberStyle>();
        StateStyleCombo.ItemsSource = Enum.GetValues<StateStyle>();
        CaseStyleCombo.ItemsSource = Enum.GetValues<TextCaseStyle>();
        if (_rules.Count == 0) CreateDefaultRule();
        RuleList.SelectedIndex = 0;
    }

    private void CreateDefaultRule()
    {
        _rules.Add(new NamingRule { Name = "Infantry by Home State", UnitType = 0, Faction = _naming.CurrentNation == 0 ? NamingFaction.Union : NamingFaction.Confederacy });
    }

    private void NewRule_Click(object sender, RoutedEventArgs e)
    {
        SaveControlsToRule();
        var rule = new NamingRule { Name = $"Naming Rule {_rules.Count + 1}", Faction = _naming.CurrentNation == 0 ? NamingFaction.Union : NamingFaction.Confederacy };
        _rules.Add(rule); RuleList.SelectedItem = rule;
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var index = RuleList.SelectedIndex;
        _rules.Remove(_current);
        if (_rules.Count == 0) CreateDefaultRule();
        RuleList.SelectedIndex = Math.Clamp(index, 0, _rules.Count - 1);
    }

    private void RuleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SaveControlsToRule();
        _current = RuleList.SelectedItem as NamingRule;
        LoadRuleToControls();
    }

    private void LoadRuleToControls()
    {
        if (_current is null) return;
        _loading = true;
        RuleNameBox.Text = _current.Name;
        UnitTypeCombo.SelectedItem = UnitTypeCombo.Items.Cast<NamingUnitTypeOption>().First(x => x.Id == _current.UnitType);
        UnitTierCombo.SelectedItem = UnitTierCombo.Items.Cast<NamingTierOption>().First(x => x.Id == _current.UnitTier);
        NumberStyleCombo.SelectedItem = _current.NumberStyle;
        GappedNumberingCheck.IsChecked = _current.RenumberWithGaps;
        StateStyleCombo.SelectedItem = _current.StateStyle;
        CaseStyleCombo.SelectedItem = _current.CaseStyle;
        UnitTypeOverrideBox.Text = _current.UnitTypeOverride;
        UnitTierOverrideBox.Text = _current.UnitTierOverride;
        SkippedNumbersBox.Text = _current.SkippedNumbersText;
        _tokens.Clear(); foreach (var t in _current.Tokens) _tokens.Add(t);
        _specialNames.Clear(); foreach (var s in _current.SpecialNames) _specialNames.Add(s);
        _preview.Clear();
        _loading = false;
        UpdatePreview();
    }

    private void SaveControlsToRule()
    {
        if (_current is null || _loading) return;
        _current.Name = RuleNameBox.Text.Trim().Length == 0 ? "Unnamed Rule" : RuleNameBox.Text.Trim();
        _current.UnitType = (UnitTypeCombo.SelectedItem as NamingUnitTypeOption)?.Id;
        _current.UnitTier = (UnitTierCombo.SelectedItem as NamingTierOption)?.Id;
        _current.RenumberWithGaps = GappedNumberingCheck.IsChecked == true;
        if (NumberStyleCombo.SelectedItem is NumberStyle ns) _current.NumberStyle = ns;
        if (StateStyleCombo.SelectedItem is StateStyle ss) _current.StateStyle = ss;
        if (CaseStyleCombo.SelectedItem is TextCaseStyle cs) _current.CaseStyle = cs;
        _current.UnitTypeOverride = UnitTypeOverrideBox.Text;
        _current.UnitTierOverride = UnitTierOverrideBox.Text;
        _current.SkippedNumbersText = SkippedNumbersBox.Text;
        _current.Tokens = _tokens.ToList();
        _current.SpecialNames = _specialNames.Where(s => !string.IsNullOrWhiteSpace(s.NumberText) || !string.IsNullOrWhiteSpace(s.Name)).ToList();
        RuleList.Items.Refresh();
    }

    private void RuleField_Changed(object sender, EventArgs e)
    {
        if (_loading) return;
        _preview.Clear(); SaveControlsToRule(); UpdatePreview();
    }

    private void AddToken_Click(object sender, RoutedEventArgs e)
    {
        if (!Enum.TryParse<NamingTokenKind>((sender as FrameworkElement)?.Tag?.ToString(), out var kind)) return;
        _tokens.Add(new NamingToken { Kind = kind }); SaveControlsToRule(); UpdatePreview();
    }

    private void AddCustomText_Click(object sender, RoutedEventArgs e)
    {
        _tokens.Add(new NamingToken { Kind = NamingTokenKind.CustomText, Text = CustomTextBox.Text });
        CustomTextBox.Clear(); SaveControlsToRule(); UpdatePreview();
    }

    private void TokenList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _loading = true;
        if (TokenList.SelectedItem is NamingToken { Kind: NamingTokenKind.CustomText } t)
        {
            SelectedTokenTextBox.IsEnabled = true; SelectedTokenTextBox.Text = t.Text;
        }
        else { SelectedTokenTextBox.IsEnabled = false; SelectedTokenTextBox.Text = string.Empty; }
        _loading = false;
    }

    private void SelectedTokenTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || TokenList.SelectedItem is not NamingToken { Kind: NamingTokenKind.CustomText } t) return;
        t.Text = SelectedTokenTextBox.Text; TokenList.Items.Refresh(); SaveControlsToRule(); UpdatePreview();
    }

    private void MoveTokenLeft_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveTokenRight_Click(object sender, RoutedEventArgs e) => MoveSelected(1);
    private void MoveSelected(int delta)
    {
        var index = TokenList.SelectedIndex; if (index < 0) return;
        var target = index + delta; if (target < 0 || target >= _tokens.Count) return;
        _tokens.Move(index, target); TokenList.SelectedIndex = target; SaveControlsToRule(); UpdatePreview();
    }
    private void RemoveToken_Click(object sender, RoutedEventArgs e)
    {
        if (TokenList.SelectedItem is NamingToken t) { _tokens.Remove(t); SaveControlsToRule(); UpdatePreview(); }
    }

    private void TokenList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _tokenDragStart = e.GetPosition(TokenList); _dragToken = FindTokenAt(e.GetPosition(TokenList));
    }
    private void TokenList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragToken is null) return;
        var p = e.GetPosition(TokenList);
        if (Math.Abs(p.X - _tokenDragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - _tokenDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(TokenList, _dragToken, DragDropEffects.Move); _dragToken = null;
    }
    private void TokenList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(NamingToken))) return;
        var source = (NamingToken)e.Data.GetData(typeof(NamingToken))!;
        var target = FindTokenAt(e.GetPosition(TokenList));
        if (target is null || ReferenceEquals(source, target)) return;
        var oldIndex = _tokens.IndexOf(source); var newIndex = _tokens.IndexOf(target);
        if (oldIndex < 0 || newIndex < 0) return;
        _tokens.Move(oldIndex, newIndex); TokenList.SelectedItem = source; SaveControlsToRule(); UpdatePreview();
    }
    private NamingToken? FindTokenAt(Point point)
    {
        var hit = TokenList.InputHitTest(point) as DependencyObject;
        while (hit is not null && hit is not ListBoxItem) hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
        return (hit as ListBoxItem)?.DataContext as NamingToken;
    }

    private void SpecialNamesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) => Dispatcher.BeginInvoke(new Action(() => { SaveControlsToRule(); UpdatePreview(); }));
    private void PreviewOption_Changed(object sender, RoutedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (_current is null || _loading) return;
        SaveControlsToRule();
        var sample = _data.Units.FirstOrDefault(u => PreviewFactionMatches(u) && _naming.ExclusionReason(_current, u) is null);
        if (sample is null)
        {
            PreviewText.Text = "No eligible units match this rule and preview faction."; PreviewSequenceText.Text = string.Empty; MatchSummaryText.Text = "Adjust faction or rule filters."; return;
        }
        var values = new[] { 1, 2, 3, 5, 11, 21 };
        string Render(int n) => _naming.RenderName(_current, sample, n, _data.GetStateAbbreviation(sample.StateId));
        var first = Render(5); var seq = string.Join("   |   ", values.Select(Render));
        if (_current.RenumberWithGaps) {
            try {
                var actual = _naming.BuildPreview(_current, _data.Units, _data.GetStateAbbreviation)
                    .Where(p => PreviewFactionMatches(p.Unit)).Take(6).ToList();
                first = actual.FirstOrDefault()?.NewName ?? "No matching units";
                seq = string.Join("   |   ", actual.Select(p => p.NewName));
            } catch (Exception ex) {
                PreviewText.Text = "Check numbering settings"; PreviewSequenceText.Text = ex.Message; return;
            }
        }
        if (ShowSpacesCheck.IsChecked == true) { first = ShowSpaces(first); seq = ShowSpaces(seq); }
        PreviewText.Text = first; PreviewSequenceText.Text = seq;
        MatchSummaryText.Text = $"Matches {_data.Units.Count(u => _naming.ExclusionReason(_current, u) is null):N0} combat units in the loaded save.";
    }

    private static string ShowSpaces(string text) => text.Replace(" ", "·").Replace("\t", "⇥");

    private bool PreviewFactionMatches(CombatUnitNode unit) => PreviewFactionCombo.SelectedIndex switch
    { 1 => unit.Nation == 0, 2 => unit.Nation == 1, 3 => true, _ => unit.Nation == _naming.CurrentNation };
    private void PreviewRename_Click(object sender, RoutedEventArgs e)
    {
        SaveControlsToRule(); if (_current is null) return;
        _preview.Clear();
        try { foreach (var item in _naming.BuildPreview(_current, _data.Units, _data.GetStateAbbreviation).Where(p => PreviewFactionMatches(p.Unit))) _preview.Add(item); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Naming preview blocked"); return; }
        var changes = _preview.Count(p => p.OldName != p.NewName);
        MatchSummaryText.Text = $"Rule scope: {_current.Faction} • {_preview.Count} matched • {changes} will rename • {_data.Units.Count - changes} preserved / skipped";
        MatchSummaryText.ToolTip = string.Join("\n", _naming.Diagnostics);
        DiagnosticsBox.Text = string.Join("\n", _naming.Diagnostics);

    }

    private void ApplyRename_Click(object sender, RoutedEventArgs e)
    {
        PreviewRename_Click(sender, e); // Always rebuild the exact plan from current rules and working data.
        if (_preview.Count == 0) return;
        if (MessageBox.Show($"Apply {_preview.Count:N0} proposed names to the editor's working state?\n\nNothing is written to the save until you use Save Changes in the main window.", "Apply Mass Rename", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var item in _preview) { item.Unit.Name = item.NewName; item.Unit.RefreshDisplay(); }
        WorkingNamesChanged = true;
        MessageBox.Show("Names applied to the working state. Review them in the OOB/Roster, then use Save Changes when satisfied.", "Mass Rename", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Filters_Click(object sender, RoutedEventArgs e)
    {
        SaveControlsToRule(); if (_current is null) return;
        new NamingFiltersWindow(_current, _data) { Owner = this }.ShowDialog();
        _preview.Clear(); UpdatePreview(); PreviewRename_Click(sender, e);
    }

    private void SaveRules_Click(object sender, RoutedEventArgs e)
    {
        SaveControlsToRule();
        _naming.Settings.Rules = _rules.ToList(); _naming.Save();
        MessageBox.Show($"Naming rules saved.\n{_naming.SettingsPath}", "Mass Rename / Naming Schemes", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveControlsToRule(); _naming.Settings.Rules = _rules.ToList(); _naming.Save();
        base.OnClosed(e);
    }
}
