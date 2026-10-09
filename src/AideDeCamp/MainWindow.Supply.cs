using System.Windows;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;

public partial class MainWindow
{
    private void EditSupplies_Click(object sender, RoutedEventArgs e)
    {
        if (ManagementWorkspace.Visibility == Visibility.Visible) return;
        OpenSupplyEditor(_selectedUnits.OrderBy(u => u.CommandPath).ThenBy(u => u.Name).ToArray());
    }

    private void OpenSupplyEditor(IReadOnlyList<CombatUnitNode> units)
    {
        if (units.Count == 0 || units.Any(u => !CanEdit(u) || !u.HasSupplyStock ||
            u.PathLinkStatus != PathLinkStatus.Confirmed))
        {
            StatusText.Text = "Supply stock editing needs selected land combat units with unique, complete version-1.142 path records.";
            return;
        }
        if (units.Any(u => _typedDrafts.TryGetValue(u, out var draft) && draft.Dirty))
        {
            StatusText.Text = "Apply or discard the selected units' existing cell/detail drafts before editing supply stock.";
            return;
        }
        var dialog = new SupplyStockEditWindow(units, _ui.Get("supply.stock.rawValues") >= .5) { Owner = this };
        var accepted = dialog.ShowDialog() == true;
        if ((_ui.Get("supply.stock.rawValues") >= .5) != dialog.RawValues)
        {
            _ui.Set("supply.stock.rawValues", dialog.RawValues ? 1 : 0);
            try { _ui.Save(); }
            catch (Exception error) { ErrorLog.Write("Save supply display preference", error); }
        }
        if (!accepted || dialog.Plan is not { CanApply: true } plan) return;
        var changed = plan.Changes.Select(change => change.Unit).Distinct().ToArray();
        if (!_editSession.Execute($"Edit supply stock for {changed.Length:N0} unit(s)", changed, plan.Apply)) return;
        RefreshSummaries(false, changed);
        UpdateDirtyState();
        StatusText.Text = $"Supply stock staged for {changed.Length:N0} unit(s). Ctrl+Z undoes it; Save changes writes a full backup first.";
    }
}
