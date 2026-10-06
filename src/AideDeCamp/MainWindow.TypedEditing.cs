using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;

public partial class MainWindow
{
    private readonly Dictionary<CombatUnitNode, TypedUnitEdit> _typedDrafts = new();
    private bool TypedDirty => _typedDrafts.Values.Any(d => d.Dirty) || _groupNameDrafts.Values.Any(d => d.Dirty);
    private static bool FindInputAncestor(DependencyObject? element)
    {
        while (element is not null) {
            if (element is TextBox or ComboBox or Button) return true;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
    private TypedUnitEdit Draft(CombatUnitNode unit)
    {
        if (!_typedDrafts.TryGetValue(unit, out var draft)) {
            draft = new TypedUnitEdit(unit); _typedDrafts.Add(unit, draft);
            foreach (var field in draft.Fields.Values) field.Changed = () => { ValidateDraft(draft); UpdateDirtyState(); };
        }
        foreach (var field in draft.Fields.Values)
            field.NormalBackground = unit.GetEditState(field.Key) switch {
                EditState.Unsaved => "#4A3B22", EditState.SavedThisSession => "#213A2C", _ => "#162833" };
        draft.Sync(); ValidateDraft(draft); return draft;
    }
    private CombatUnitNode ValidateDraft(TypedUnitEdit d) => d.Validate(_data.WeaponOptions, _data.StateOptions, _validation);
    private FrameworkElement TypedEditor(TypedUnitEdit draft, string key)
    {
        var field = draft.Fields[key];
        Control editor;
        if (key is "Weapon" or "HomeState") {
            var combo = new ComboBox { IsEditable = true, IsTextSearchEnabled = false, MinWidth = 80 };
            combo.ItemsSource = key == "Weapon" ? _data.CompatibleWeaponsFor(draft.Unit).Select(w => w.Name).ToList() : _data.StateOptions.Select(s => s.Name).ToList();
            combo.SetBinding(ComboBox.TextProperty, new Binding("Text") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            editor = combo;
        } else {
            var box = new TextBox { MinWidth = 50 };
            box.SetBinding(TextBox.TextProperty, new Binding("Text") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            editor = box;
        }
        editor.DataContext = field;
        editor.SetBinding(Control.BackgroundProperty, new Binding("Background"));
        editor.SetBinding(Control.BorderBrushProperty, new Binding("Border"));
        editor.SetBinding(ToolTipProperty, new Binding("Help"));
        // Runtime roster editors do not inherit a XAML column element style. Give them
        // the same explicit dark-theme foreground, caret, and selection treatment.
        editor.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        if (editor is TextBox textBox) {
            textBox.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
            textBox.SetResourceReference(TextBox.SelectionBrushProperty, "SelectionBrush");
        }
        field.NormalForeground = key is "Contract" or "ContractRemaining" ? draft.Unit.ContractRiskBrush : "#E8EDF2";
        editor.SetBinding(Control.ForegroundProperty, new Binding("Foreground") { Source = field });
        editor.BorderThickness = new Thickness(2);
        return editor;
    }
    private FrameworkElement TypedPanel(CombatUnitNode unit)
    {
        var draft = Draft(unit);
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock { Text = "Typed edits are drafts until Apply or Save. Magenta = check this field; hover for the reason.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Orchid, Margin = new Thickness(0,0,0,8) });
        foreach (var def in TypedUnitEdit.Definitions) {
            panel.Children.Add(new TextBlock { Text = def.Label, Margin = new Thickness(0,5,0,2) });
            panel.Children.Add(TypedEditor(draft, def.Key));
        }
        var apply = new Button { Content = "Apply Working Changes", Margin = new Thickness(0,12,0,0) };
        apply.Click += (_, _) => CommitTypedDrafts(); panel.Children.Add(apply);
        var reset = new Button { Content = "Discard this unit's typed drafts" };
        reset.Click += (_, _) => { foreach (var f in draft.Fields.Values) f.Accept(TypedUnitEdit.Read(unit, f.Key)); ValidateDraft(draft); UpdateDirtyState(); };
        panel.Children.Add(reset);
        return panel;
    }
    private void ShowTypedDetails(CombatUnitNode unit)
    {
        SharedDetailPanel.Visibility = CanEdit(unit) ? Visibility.Visible : Visibility.Collapsed;
        SharedDetailPanel.Children.Clear();
        if (CanEdit(unit)) SharedDetailPanel.Children.Add(TypedPanel(unit));
    }
    private void OpenTypedCard(CombatUnitNode unit)
    {
        new Window { Title = unit.Name + " — Edit unit", Owner = this, Width = 460, Height = 730,
            Background = (Brush)FindResource("SurfaceBrush"), Foreground = (Brush)FindResource("TextBrush"),
            Content = new ScrollViewer { Content = TypedPanel(unit), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.Show();
    }
    private void InitializeTypedRoster()
    {

        DataTemplate Template(string key, bool editing) {
            var host = new FrameworkElementFactory(typeof(RosterCellHost));
            host.SetValue(RosterCellHost.FieldProperty, key);
            host.SetValue(RosterCellHost.EditingProperty, editing);
            return new DataTemplate { VisualTree = host };
        }
        for (int i = 0; i < RosterGrid.Columns.Count; i++) {
            var old = RosterGrid.Columns[i];
            var key = RosterFields.FromColumn(old.SortMemberPath);
            if (key is null) { old.IsReadOnly = true; continue; }
            RosterGrid.Columns[i] = new DataGridTemplateColumn {
                Header = old.Header, Width = old.Width, SortMemberPath = old.SortMemberPath,
                CellTemplate = Template(key, false), CellEditingTemplate = Template(key, true),
                CellStyle = old.CellStyle ?? (Style)FindResource("RosterDataGridCell") };
        }
        RosterGrid.IsReadOnly = false;
        RosterGrid.BeginningEdit += (_, e) => {
            if (e.Row.Item is not RosterRow row) { e.Cancel = true; return; }
            e.Cancel = row.Unit is CombatUnitNode u ? !CanEdit(u)
                : row.Group is not GroupNode g || !g.IsLandCommand || e.Column.SortMemberPath != "Name";
        };
    }
    internal FrameworkElement CreateRosterCell(RosterRow row, string key, bool editing)
    {
        // WPF may load a recycled template before its field DP has been assigned.
        // Never interpret an unknown key as Name, and never index a draft blindly.
        if (!RosterFields.IsSupported(key)) return new Border();
        TypedField? field = null;
        if (row.Unit is CombatUnitNode u && CanEdit(u)) Draft(u).Fields.TryGetValue(key, out field);
        else if (row.Group is GroupNode g && g.IsLandCommand && key == "Name") field = GroupNameDraft(g);
        if (editing && field is not null) {
            Control input = row.Unit is CombatUnitNode unit ? (Control)TypedEditor(Draft(unit), key) : GroupNameEditor(row.Group!);
            input.Padding = new Thickness(3, 1, 3, 1);
            input.Margin = new Thickness(0);
            input.MinHeight = 24;
            input.VerticalContentAlignment = VerticalAlignment.Center;
            return input;
        }
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 2, 4, 2), FontWeight = key == "Name" ? FontWeights.Bold : FontWeights.Normal };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var surface = new Border { Child = text };
        if (field is not null) {
            field.NormalForeground = key is "Contract" or "ContractRemaining" && row.Unit is CombatUnitNode contract ? contract.ContractRiskBrush : "#E8EDF2";
            text.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { Source = field });
            text.SetBinding(TextBlock.TextProperty, new Binding("Text") { Source = field });
            surface.SetBinding(Border.BackgroundProperty, new Binding("Background") { Source = field });
            surface.SetBinding(Border.BorderBrushProperty, new Binding("Border") { Source = field });
            surface.BorderThickness = new Thickness(0, 0, 0, 2);
            surface.SetBinding(ToolTipProperty, new Binding("Help") { Source = field });
        } else {
            var property = key switch { "Name" => "Name", "Weapon" => "WeaponName", "HomeState" => "HomeStateName",
                "Experience" => "ExperienceText", "FieldStrength" => "FieldStrengthText", "Casualties" => "CasualtiesText",
                "Contract" => "ContractText", "ContractRemaining" => "ContractRemainingText", "ETA" => "TransferText", _ => "Name" };
            text.SetBinding(TextBlock.TextProperty, new Binding(property) { Source = row });
        }
        if (key == "Name") {
            var line = new DockPanel { Margin = new Thickness(Math.Min(row.Depth * RosterRow.HierarchyIndent, 80), 0, 0, 0) };
            if (row.IsGroup) {
                var toggle = new Button { Content = row.TreeGlyph, Width = 22, Height = 22, Padding = new Thickness(0), Margin = new Thickness(0), DataContext = row };
                toggle.Click += RosterToggleGroup_Click; DockPanel.SetDock(toggle, Dock.Left); line.Children.Add(toggle);
            }
            line.Children.Add(surface); return line;
        }
        return surface;
    }
    private bool CommitTypedDrafts()
    {
        RosterGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RosterGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var drafts = _typedDrafts.Values.Where(d => d.Dirty).ToList();
        var candidates = drafts.ToDictionary(d => d, ValidateDraft);
        var errors = drafts.SelectMany(d => d.Fields.Values.Where(f => f.Result.Severity == ValidationSeverity.Error).Select(f => $"{d.Unit.Name} — {f.Label}: {f.Result.Message}")).ToList();
        if (errors.Count > 0) { ShowValidationReview(errors, false, "Invalid entries — save blocked"); return false; }
        var groupDrafts = _groupNameDrafts.Where(p => p.Value.Dirty).ToList();
        errors.AddRange(groupDrafts.Where(p => _validation.ValidateName(p.Value.Text).Severity == ValidationSeverity.Error).Select(p => p.Key.Name + ": " + _validation.ValidateName(p.Value.Text).Message));
        if (errors.Count > 0) { ShowValidationReview(errors, false, "Invalid entries — save blocked"); return false; }
        _editSession.Execute("Apply typed unit and HQ edits", _data.Units, _data.Groups.Values, () => {
            foreach (var pair in groupDrafts) pair.Key.Name = pair.Value.Text;
            foreach (var d in drafts) {
                var candidate = candidates[d];
                if (candidate.StateId != d.Unit.StateId && !d.Fields["Name"].Dirty) {
                    _naming.CurrentNation = candidate.Nation; _naming.Classify = u => _commandClassifier.CategoryForUnit(u, _data.Groups);
                    var rule = _naming.Settings.Rules.FirstOrDefault(r => r.Tokens.Any(t => t.Kind == NamingTokenKind.HomeState) && _naming.ExclusionReason(r,candidate) is null);
                    if (rule is not null) candidate.Name = _naming.NameForRehomedUnit(rule,candidate,_data.Units,_data.GetStateAbbreviation);
                }
                d.Apply(candidate);
            }
            if (groupDrafts.Count > 0) _data.RefreshFormationContext();
        });
        foreach (var pair in groupDrafts) pair.Value.Accept(pair.Key.Name);
        RefreshSummaries(true, drafts.Select(d=>d.Unit)); UpdateDirtyState(); return true;
    }
    private void ReviewData_Click(object sender, RoutedEventArgs e)
    {
        var messages = ScanWorkingData(ValidationSeverity.Error).Concat(ScanWorkingData(ValidationSeverity.Warning)).ToList();
        if(_data.Management is { } doc)messages.AddRange(doc.Review());
        if (messages.Count == 0) messages.Add("No issues detected by the currently mapped validation rules.");
        ShowValidationReview(messages, false, "Current data review — both factions", true);
    }
    private List<string> ScanWorkingData(ValidationSeverity severity)
    {
        var result = new List<string>();
        foreach (var unit in _data.Units.Where(CanEdit)) {
            var d = Draft(unit);
            result.AddRange(d.Fields.Values.Where(f=>f.Result.Severity==severity).Select(f=>$"{unit.Name} [ID {unit.UnitId}] — {f.Label}: {f.Result.Message}"));
        }
        foreach (var group in _data.Groups.Values.Where(g => g.IsLandCommand)) {
            var name = GroupNameDraft(group);
            if (name.Result.Severity == severity) result.Add($"{group.Name} — HQ name: {name.Result.Message}");
        }
        return result;
    }
    private bool ReviewBeforeSave()
    {
        var errors=ScanWorkingData(ValidationSeverity.Error);
        if(errors.Count>0) { ShowValidationReview(errors,false,"Known-invalid data — save blocked"); return false; }
        var warnings=ScanWorkingData(ValidationSeverity.Warning);
        return warnings.Count==0 || ShowValidationReview(warnings,true,"Review potentially risky data");
    }
    private bool ShowValidationReview(List<string> messages, bool allowOverride, string title, bool browseUnclassified = false)
    {
        var dialog = new Window { Owner=this, Title=title, Width=760, Height=530, Background=(Brush)FindResource("SurfaceBrush"), Foreground=Brushes.White, WindowStartupLocation=WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin=new Thickness(16) };
        var bottom=new StackPanel(); DockPanel.SetDock(bottom,Dock.Bottom); panel.Children.Add(bottom);
        if(browseUnclassified) {
            var browse=new Button {Content="Browse unclassified records for current faction"};
            browse.Click+=(_,_)=>{dialog.DialogResult=false;OpenWorkspace(_nation,"Unclassified");};
            bottom.Children.Add(browse);
        }
        var ack=new CheckBox { Content="I understand these valid but risky values could damage this campaign.", Margin=new Thickness(0,10,0,8), Visibility=allowOverride?Visibility.Visible:Visibility.Collapsed };
        bottom.Children.Add(ack);
        var proceed=new Button { Content=allowOverride?"Acknowledge and save with backup":"Return to editing", IsEnabled=!allowOverride };
        ack.Checked+=(_,_)=>proceed.IsEnabled=true; ack.Unchecked+=(_,_)=>proceed.IsEnabled=false;
        proceed.Click+=(_,_)=>dialog.DialogResult=allowOverride; bottom.Children.Add(proceed);
        panel.Children.Add(new TextBox { Text=string.Join("\n\n",messages), IsReadOnly=true, TextWrapping=TextWrapping.Wrap, VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
        dialog.Content=panel; return dialog.ShowDialog()==true;
    }
}
