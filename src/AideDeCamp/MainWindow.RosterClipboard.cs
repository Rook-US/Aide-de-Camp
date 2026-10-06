using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;
public partial class MainWindow
{
    private void RosterCellsChanged(object sender,SelectedCellsChangedEventArgs e) {
        if(_syncingRosterSelection)return;
        _syncingRosterSelection=true;
        try {
            ClearBatchSelection(false);
            foreach(var unit in RosterGrid.SelectedCells.Select(c=>(c.Item as RosterRow)?.Unit).OfType<CombatUnitNode>().Distinct())AddBatchSelection(unit,false);
            if(RosterGrid.CurrentCell.Item is RosterRow {Unit:CombatUnitNode primary})ShowUnit(primary);
            UpdateSelectionUi();
        } finally {_syncingRosterSelection=false;}
    }
    private void RosterClipboardKeyDown(object sender,KeyEventArgs e) {
        if(!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || e.Key is not (Key.C or Key.V))return;
        // Text editors retain their normal text-selection clipboard behavior.
        if(Keyboard.FocusedElement is TextBox or ComboBox)return;
        e.Handled=true;
        RosterClipboard(e.Key==Key.V);
    }
    private void RosterClipboard(bool paste) {
        try {
            var cells=RosterGrid.SelectedCells.Where(c=>c.Item is RosterRow).OrderBy(c=>RosterGrid.Items.IndexOf(c.Item)).ThenBy(c=>c.Column.DisplayIndex).ToArray();
            if(cells.Length==0)return;
            if(!paste) {
                var text=string.Join("\n",cells.GroupBy(c=>c.Item).Select(row=>string.Join("\t",row.Select(c=>{
                    var item=(RosterRow)c.Item;var key=RosterFields.FromColumn(c.Column.SortMemberPath);
                    return item.Unit is {} unit && key is not null?TypedUnitEdit.Read(unit,key):item.GetType().GetProperty(c.Column.SortMemberPath)?.GetValue(item)?.ToString()??"";
                }))));
                ClipboardAccess.SetText(text);StatusText.Text=$"Copied {cells.Length:N0} cell(s). Paste applies to the selected destination cells.";return;
            }
            if(_data.IsReadOnlySave)return;
            string source=ClipboardAccess.GetText().TrimEnd('\r','\n');
            var rows=source.Split('\n').Select(s=>s.TrimEnd('\r').Split('\t')).ToArray();
            bool scalar=rows.Length==1 && rows[0].Length==1;
            var destinations=cells.GroupBy(c=>c.Item).ToArray();
            if(!scalar && (rows.Length!=destinations.Length || destinations.Select((row,i)=>row.Count()!=rows[i].Length).Any(v=>v)))throw new InvalidOperationException("Select the same number of rows and columns as the copied data, or copy one value to fill all selected cells.");
            var edits=new Dictionary<CombatUnitNode,Dictionary<string,string>>();
            for(int r=0;r<destinations.Length;r++) {
                int column=0;
                foreach(var cell in destinations[r]) {
                    if(cell.Item is not RosterRow {Unit:CombatUnitNode unit} || !CanEdit(unit))throw new InvalidOperationException("Paste currently supports editable combat-unit cells. Select unit rows only.");
                    string? key=RosterFields.FromColumn(cell.Column.SortMemberPath);
                    if(key is null)throw new InvalidOperationException("The selection includes a read-only column.");
                    if(_typedDrafts.TryGetValue(unit,out var draft) && draft.Dirty)throw new InvalidOperationException("Apply or discard existing cell drafts before pasting over this unit.");
                    if(!edits.TryGetValue(unit,out var values))edits[unit]=values=new();
                    values[key]=scalar?rows[0][0]:rows[r][column];column++;
                }
            }
            var plan=SelectionEditPlan.Build(edits.Keys,u=>edits[u],_data.WeaponOptions,_data.StateOptions,_validation);
            if(plan.Errors.Count>0)throw new InvalidOperationException(string.Join("\n",plan.Errors.Take(8)));
            if(plan.Changes.Count==0){StatusText.Text="Pasted values already match the selected cells.";return;}
            if(edits.Count>1 && edits.Values.Any(v=>v.ContainsKey("Name")) && MessageBox.Show(this,"This paste changes names on multiple units. Repeated names may be difficult to distinguish. Apply the paste?","Paste unit names",MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
            var changed=plan.Changes.Select(p=>p.Unit).ToArray();bool eta=plan.Changes.Any(p=>p.Unit.TransferTimeRaw!=p.Candidate.TransferTimeRaw);
            var selection=cells.Select(c=>(((RosterRow)c.Item).Unit!.UnitId,c.Column.SortMemberPath)).ToArray();
            _editSession.Execute("Paste roster cells",changed,plan.Apply);RefreshSummaries(eta,changed);UpdateDirtyState();
            _syncingRosterSelection=true;
            try {
                RosterGrid.UnselectAll();RosterGrid.UnselectAllCells();
                foreach(var (id,field) in selection) {
                    var row=RosterGrid.Items.OfType<RosterRow>().FirstOrDefault(r=>r.Unit?.UnitId==id);var column=RosterGrid.Columns.FirstOrDefault(c=>c.SortMemberPath==field);
                    if(row is not null && column is not null)RosterGrid.SelectedCells.Add(new DataGridCellInfo(row,column));
                }
            }finally{_syncingRosterSelection=false;}
            StatusText.Text=$"Pasted into {changed.Length:N0} units. Ctrl+Z undoes the whole paste; Save changes writes it.";StatusText.ToolTip=plan.Tooltip;
        } catch(Exception ex){StatusText.Text="Paste/copy: "+ex.Message;}
    }
}
