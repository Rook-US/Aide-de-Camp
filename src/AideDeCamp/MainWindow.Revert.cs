using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;

public partial class MainWindow
{
    private Dictionary<CombatUnitNode, UnitEditSnapshot> _loadedSnapshots = new();
    private void RosterContextOpening(object sender, ContextMenuEventArgs e)
    {
        DependencyObject? createHit = e.OriginalSource as DependencyObject;
        while (createHit is not null && createHit is not DataGridRow)
            createHit = createHit is Visual ? VisualTreeHelper.GetParent(createHit) : LogicalTreeHelper.GetParent(createHit);
        _creationRosterContext = (createHit as DataGridRow)?.DataContext is RosterRow creationRow ? (OobNode?)creationRow.Unit ?? creationRow.Group : null;
        if (RosterGrid.ContextMenu is not ContextMenu menu) return;
        foreach (var old in menu.Items.OfType<MenuItem>().Where(m => Equals(m.Tag, "FieldAction")).ToList()) menu.Items.Remove(old);
        DependencyObject? hit = e.OriginalSource as DependencyObject;
        while (hit is not null && hit is not DataGridCell)
            hit = hit is Visual ? VisualTreeHelper.GetParent(hit) : LogicalTreeHelper.GetParent(hit);
        var cell = hit as DataGridCell;
        if (cell?.DataContext is not RosterRow { Unit: CombatUnitNode unit }) return;
        var field = RosterFields.FromColumn(cell.Column.SortMemberPath);
        if (field is null) return;
        var show = new MenuItem { Header = $"Show Original Value — {field}", Tag = "FieldAction" };
        show.Click += (_, _) => MessageBox.Show(this, unit.OriginalValue(field), $"Original {field} — {unit.Name}");
        var revert = new MenuItem { Header = $"Revert This Field — {field}", Tag = "FieldAction", IsEnabled = CanEdit(unit) && unit.GetEditState(field) != EditState.Unchanged };
        revert.Click += (_, _) => RevertField(unit, field);
        menu.Items.Insert(0, revert); menu.Items.Insert(0, show);
        if (!CanEdit(unit)) return;
        if (field == "Weapon")
        {
            var choose = new MenuItem { Header = "Assign Compatible Weapon", Tag = "FieldAction" };
            foreach (var weapon in _data.CompatibleWeaponsFor(unit))
            {
                var item = new MenuItem { Header = weapon.Name, IsCheckable = true, IsChecked = weapon.Id == unit.WeaponId, Tag = "FieldAction" };
                item.Click += (_, _) => QuickAssignWeapon(unit, weapon);
                choose.Items.Add(item);
            }
            menu.Items.Insert(0, choose);
        }
        else if (field == "HomeState")
        {
            var choose = new MenuItem { Header = "Assign Home State", Tag = "FieldAction" };
            foreach (var state in _data.StateOptions)
            {
                var item = new MenuItem { Header = state.Name, IsCheckable = true, IsChecked = state.Id == unit.StateId, Tag = "FieldAction" };
                item.Click += (_, _) => QuickAssignState(unit, state);
                choose.Items.Add(item);
            }
            menu.Items.Insert(0, choose);
        }
        else if (field is "Contract" or "ContractRemaining")
        {
            var choose = new MenuItem { Header = "Set Contract Length", Tag = "FieldAction" };
            foreach (var months in new[] { 3, 6, 9, 12, 18, 24, 36 })
            {
                var item = new MenuItem { Header = $"{months} months", IsCheckable = true, IsChecked = months == unit.ContractMonths, Tag = "FieldAction" };
                item.Click += (_, _) => QuickAssignContract(unit, months);
                choose.Items.Add(item);
            }
            menu.Items.Insert(0, choose);
        }
    }

    private void QuickAssignWeapon(CombatUnitNode unit, WeaponOption weapon)
    {
        if (_typedDrafts.TryGetValue(unit, out var draft)) draft.Fields["Weapon"].Accept(TypedUnitEdit.Read(unit, "Weapon"));
        if (unit.WeaponId == weapon.Id) return;
        if (_editSession.Execute($"Assign {weapon.Name} to {unit.Name}", new[] { unit }, () => { unit.WeaponId = weapon.Id; unit.WeaponName = weapon.Name; }))
            RefreshAfterQuickEdit(unit);
    }
    private void QuickAssignState(CombatUnitNode unit, StateOption state)
    {
        if (_typedDrafts.TryGetValue(unit, out var draft)) draft.Fields["HomeState"].Accept(TypedUnitEdit.Read(unit, "HomeState"));
        if (unit.StateId == state.Id) return;
        if (_editSession.Execute($"Set {unit.Name} home state", new[] { unit }, () => { unit.StateId = state.Id; unit.HomeStateName = state.Name; }))
            RefreshAfterQuickEdit(unit);
    }
    private void QuickAssignContract(CombatUnitNode unit, int months)
    {
        if (_typedDrafts.TryGetValue(unit, out var draft)) { draft.Fields["Contract"].Accept(TypedUnitEdit.Read(unit, "Contract")); draft.Fields["ContractRemaining"].Accept(TypedUnitEdit.Read(unit, "ContractRemaining")); }
        if (unit.ContractMonths == months) return;
        if (_editSession.Execute($"Set {unit.Name} contract", new[] { unit }, () => unit.ContractMonths = months))
            RefreshAfterQuickEdit(unit);
    }
    private void RefreshAfterQuickEdit(CombatUnitNode unit)
    {
        unit.RefreshDisplay(); RefreshSummaries(true, new[] { unit }); UpdateDirtyState();
        if (_selectedUnit == unit) ShowUnit(unit);
    }
    private void RevertField(CombatUnitNode unit, string field)
    {
        if (!CanEdit(unit) || !_loadedSnapshots.TryGetValue(unit, out var original)) return;
        var strength = new CombatUnitNode { TotalMenRaw = original.TotalMenRaw, CasualtyRatioRaw = original.CasualtyRatioRaw };
        if (field == "ETA" && unit.PathLinkStatus != PathLinkStatus.Confirmed)
        { MessageBox.Show(this, unit.PathLinkMessage, "Transfer revert blocked"); return; }
        if ((field == "FieldStrength" || field == "Casualties") &&
            (long)(field == "FieldStrength" ? strength.FieldStrength : unit.FieldStrength) + (field == "Casualties" ? strength.Casualties : unit.Casualties) > int.MaxValue)
        { MessageBox.Show(this, "The resulting total strength would overflow the save field.", "Revert blocked"); return; }
        var changed = _editSession.Execute($"Revert {field} for {unit.Name}", new[] { unit }, () =>
        {
            switch (field)
            {
                case "Name": unit.Name = original.Name; break;
                case "HomeState": unit.StateId = original.StateId; unit.HomeStateName = original.HomeStateName; break;
                case "FieldStrength": unit.TrySetStrengthComponents(strength.FieldStrength, unit.Casualties); break;
                case "Casualties": unit.TrySetStrengthComponents(unit.FieldStrength, strength.Casualties); break;
                case "Weapon": unit.WeaponId = original.WeaponId; unit.WeaponName = original.WeaponName; break;
                case "Contract": case "ContractRemaining": unit.ContractMonths = original.ContractMonths; break;
                case "Experience": unit.ExperienceRaw = original.ExperienceRaw; break;
                case "ETA": unit.TransferTimeRaw = original.TransferTimeRaw; break;
            }
        });
        if (_typedDrafts.TryGetValue(unit, out var draft) && draft.Fields.TryGetValue(field, out var input)) {
            input.Accept(TypedUnitEdit.Read(unit, field)); ValidateDraft(draft); UpdateDirtyState();
        }
        if (!changed) return;
        RefreshSummaries(true, new[] { unit }); UpdateDirtyState(); if (_selectedUnit == unit) ShowUnit(unit);
    }
}
