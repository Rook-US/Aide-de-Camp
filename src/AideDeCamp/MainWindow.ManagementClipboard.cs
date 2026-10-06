using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using AideDeCamp.Services;

namespace AideDeCamp;
public partial class MainWindow
{
    private static string ManagementColumn(DataGridColumn column)=>column is DataGridBoundColumn {Binding:Binding binding}?binding.Path.Path:column.SortMemberPath;
    private static string ManagementField(string property)=>property switch {
        "ConditionPercent"=>"Condition","ConstructionCompletedPercent"=>"ConstructionCompletion","RepairWorkRemainingPercent"=>"RepairRemaining",
        "OrderTotal"=>"OrderQuantity","StandardizationStart"=>"StandardizationYear",_=>property
    };
    private void ManagementClipboardKeyDown(object sender,KeyEventArgs e) {
        if(!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || e.Key is not (Key.C or Key.V))return;
        e.Handled=true;
        ManagementClipboard(e.Key==Key.V);
    }
    private void ManagementClipboard(bool paste) {
        try {
            var cells=ManagementGrid.SelectedCells.OrderBy(c=>ManagementGrid.Items.IndexOf(c.Item)).ThenBy(c=>c.Column.DisplayIndex).ToArray();if(cells.Length==0)return;
            if(!paste) {
                ClipboardAccess.SetText(string.Join("\n",cells.GroupBy(c=>c.Item).Select(row=>string.Join("\t",row.Select(c=>Convert.ToString(c.Item.GetType().GetProperty(ManagementColumn(c.Column))?.GetValue(c.Item),CultureInfo.InvariantCulture)??"")))));return;
            }
            if(_data.IsReadOnlySave || _data.Management is not {} doc)return;
            var source=ClipboardAccess.GetText().TrimEnd('\r','\n').Split('\n').Select(s=>s.TrimEnd('\r').Split('\t')).ToArray();bool scalar=source.Length==1&&source[0].Length==1;
            var rows=cells.GroupBy(c=>c.Item).ToArray();
            if(!scalar && (source.Length!=rows.Length || rows.Select((r,i)=>r.Count()!=source[i].Length).Any(v=>v)))throw new InvalidOperationException("Select a matching rectangle, or copy one value to fill the selected cells.");
            var changes=new Dictionary<ManagementDocument.Field,string>();
            for(int i=0;i<rows.Length;i++) {
                int id=ManagementId(rows[i].Key);
                var record=doc.Records.Single(r=>r.Domain==_workspace && r.Id==id && (_workspace!="Weapons" || r.Side==_nation));
                var values=new Dictionary<string,string>();int column=0;
                foreach(var cell in rows[i]) {
                    string key=ManagementField(ManagementColumn(cell.Column)),value=scalar?source[0][0]:source[i][column];column++;
                    if(key=="Branch" && !int.TryParse(value,out _))value=(Array.FindIndex(new[]{"None","Infantry","Cavalry","Artillery","Engineer","Navy"},s=>s.Equals(value,StringComparison.OrdinalIgnoreCase))-1).ToString(CultureInfo.InvariantCulture);
                    values[key]=value;
                }
                var available=ManagementBatchPlanner.CommonFields(doc,new[]{record});
                foreach(var pair in values) {
                    var field=available.SingleOrDefault(f=>f.Key==pair.Key)??throw new InvalidOperationException(record.Name+": "+pair.Key+" is not editable for this record.");
                    changes[field]=doc.Validate(field,pair.Value);
                }
            }
            if(changes.Keys.Any(f=>f.Key=="Name") && rows.Length>1 && MessageBox.Show(this,"This paste changes multiple names. Some records may receive the same name. Apply?","Paste names",MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
            _editSession.ExecuteManagement("Paste roster cells",doc,()=>doc.Apply(changes));_management=null;RefreshManagement();UpdateDirtyState();
            StatusText.Text=$"Pasted into {rows.Length:N0} selected records. Ctrl+Z undoes the paste; Save changes writes it.";
        }catch(Exception ex){StatusText.Text="Paste/copy: "+ex.Message;}
    }
}
