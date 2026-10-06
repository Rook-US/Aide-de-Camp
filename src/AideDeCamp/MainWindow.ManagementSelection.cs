using System.Windows;
using System.Windows.Controls;

namespace AideDeCamp;
public partial class MainWindow
{
    private bool _refreshingManagement;
    private readonly Dictionary<string,HashSet<int>> _managementSelections=new();
    private readonly Dictionary<string,HashSet<int>> _managementBatches=new();
    private string ManagementSelectionKey=>$"{_data.SaveDirectory}:{WorkspaceKey}:{(_workspace=="Navy"?_portView.ToString():_workspace=="Economy"?_nationView:"")}";
    private static int ManagementId(object row)=>(int)row.GetType().GetProperty("Id")!.GetValue(row)!;
    private bool CanBatchManagement=>_workspace is "Officers" or "Weapons" or "Navy" && !_data.IsReadOnlySave && _data.Management?.Records.Any(r=>r.Domain==_workspace)==true && ManagementGrid.SelectedItems.Count>0;
    private void ManagementSelection_Changed(object sender,SelectionChangedEventArgs e) {
        if(_refreshingManagement||ManagementWorkspace is null||ManagementWorkspace.Visibility!=Visibility.Visible)return;
        _managementSelections[ManagementSelectionKey]=ManagementGrid.SelectedItems.Cast<object>().Select(ManagementId).ToHashSet();
        UpdateManagementSelectionUi();
    }
    private void RestoreManagementSelection() {
        var ids=_managementSelections.GetValueOrDefault(ManagementSelectionKey,new());
        foreach(var row in ManagementGrid.Items.Cast<object>())if(ids.Contains(ManagementId(row)))ManagementGrid.SelectedItems.Add(row);
    }
    private void UpdateManagementSelectionUi() {
        if(ManagementWorkspace.Visibility!=Visibility.Visible)return;
        bool board=_workspace=="Economy" && (_nationView is "Projects" or "Policies");
        SelectionCountText.Text=board?(_selectedBoardTag is null?"0 selected":"1 selected"):$"{ManagementGrid.SelectedItems.Count:N0} selected";
        EditSelectedButton.IsEnabled=!_data.IsReadOnlySave && (board?_selectedBoardTag is not null:ManagementGrid.SelectedItems.Count>0);
        BatchEditButton.IsEnabled=CanBatchManagement;
        ReselectBatchButton.IsEnabled=_workspace is "Officers" or "Weapons" or "Navy";
        ClearSelectionButton.IsEnabled=board?_selectedBoardTag is not null:ManagementGrid.SelectedItems.Count>0;
    }
    private void RecallManagementBatch_Click(object sender,RoutedEventArgs e) {
        if(!_managementBatches.TryGetValue(ManagementSelectionKey,out var ids)){StatusText.Text="No previous batch for this faction and view.";return;}
        _managementSelections[ManagementSelectionKey]=new(ids);
        _refreshingManagement=true;ManagementGrid.SelectedItems.Clear();RestoreManagementSelection();_refreshingManagement=false;UpdateManagementSelectionUi();
        StatusText.Text=$"Restored {ManagementGrid.SelectedItems.Count} visible records from the previous batch.";
    }
}
