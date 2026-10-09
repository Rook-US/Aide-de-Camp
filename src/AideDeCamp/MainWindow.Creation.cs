using System.Windows;
using System.Windows.Controls;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;
public partial class MainWindow
{
    private bool _openingCreate;
    private async void CreateUnit_Click(object sender, RoutedEventArgs e)
    {
        if (_openingCreate || _saveInProgress) return;
        if (_data.SaveDirectory is null) { StatusText.Text = "Open a campaign save to create a unit."; return; }
        if (!CommitTypedDrafts()) return;
        OobNode? context = (sender as FrameworkElement)?.DataContext as OobNode;
        if (Equals((sender as FrameworkElement)?.Tag, "RosterCreate")) context = _creationRosterContext;
        else if (sender is Button) context = _selectedNode;
        try {
            _openingCreate = true;
            StatusText.Text = "Loading creation choices…";
            // Catalogs are read once, off the UI thread. Filtering thereafter is in memory.
            var catalog = await Task.Run(() => CreateUnitWindow.LoadCatalog(_data));
            var window = new CreateUnitWindow(_data, _naming, catalog, _nation, context) { Owner = this };
            StatusText.Text = "Create Unit ready.";
            if (window.ShowDialog() != true || window.Request is null) return;
            var created = _data.CreateUnit(window.Request, window.Confirmed, _editSession);
            SearchBox.Text = "";
            bool roster = _showRoster;
            var category = created is GroupNode group ? _commandClassifier.CategoryForGroup(group, _data.Groups) : _commandClassifier.CategoryForUnit((CombatUnitNode)created, _data.Groups);
            OpenWorkspace(window.Request.Blueprint.Faction, category == CommandCategory.Garrison ? "Garrisons" : "Armies");
            SearchBox.Text = ""; SetView(roster); RebuildSide();
            if (created is CombatUnitNode unit) NavigateToUnit(unit, _showRoster);
            else {
                var g = (GroupNode)created; _selectedNode = g;
                int parent = g.ParentId;
                while (_data.Groups.TryGetValue(parent, out var ancestor)) { ancestor.IsExpanded = true; parent = ancestor.ParentId; }
                g.IsExpanded = true;
                if (_showRoster) { RefreshRoster(); var row = _rosterRows.FirstOrDefault(r => r.Group == g); if (row is not null) { RosterGrid.SelectedItem = row; RosterGrid.ScrollIntoView(row); } }
                else { RefreshOobCanvas(); FocusNode(g); }
            }
            UpdateDirtyState();
            StatusText.Text = $"Created {created.Name} in ADC. Ctrl+Z undoes the whole creation. Save changes writes it with a full backup.";
        } catch (Exception error) {
            ErrorLog.Write("Create unit", error);
            MessageBox.Show(this, error.Message, "Unable to proceed with creation", MessageBoxButton.OK, MessageBoxImage.Warning);
        } finally { _openingCreate = false; }
    }
    private OobNode? _creationRosterContext;
}
