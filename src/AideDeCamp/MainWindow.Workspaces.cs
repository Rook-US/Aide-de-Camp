using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;

public partial class MainWindow
{
    private string _workspace="Armies";
    private bool _portView;
    private string _nationView="States";
    private void NationView_Click(object sender,RoutedEventArgs e) {if(sender is Button {Tag:string view}){_selectedBoardTag=null;_selectedBoardBorder=null;_nationView=view;SearchBox.Text="";RefreshManagement();}}
    private ManagementSnapshot? _management;
    private readonly Dictionary<int,string> _lastWorkspaces=new();
    private readonly Dictionary<string,HashSet<int>> _workspaceSelections=new();
    private readonly Dictionary<string,HashSet<int>> _previousBatches=new();
    private readonly Dictionary<string,string> _workspaceSearches=new();
    private readonly Dictionary<string,bool> _workspaceRosterModes=new();
    private readonly Dictionary<int,bool> _navyPortModes=new();
    private string WorkspaceKey => $"{_nation}:{_workspace}";
    private void Workspace_Click(object sender, RoutedEventArgs e)
    {
        if(sender is Button {Tag:string key}) {var parts=key.Split(':');OpenWorkspace(int.Parse(parts[0]),parts[1]);}
    }
    private void OpenWorkspace(int side,string workspace)
    {
        _workspaceSelections[WorkspaceKey]=_selectedUnits.Select(u=>u.UnitId).ToHashSet();
        _workspaceSearches[WorkspaceKey]=SearchBox.Text;
        _workspaceRosterModes[WorkspaceKey]=_showRoster;
        if(_workspace=="Navy") _navyPortModes[_nation]=_portView;
        ClearBatchSelection(false);
        _selectedBoardTag=null;_selectedBoardBorder=null;
        _nation=side;_workspace=workspace;_lastWorkspaces[side]=workspace;
        SearchBox.Text=_workspaceSearches.GetValueOrDefault(WorkspaceKey,"");
        bool land=workspace is "Armies" or "Garrisons" or "Unclassified";
        LandWorkspace.Visibility=land?Visibility.Visible:Visibility.Collapsed;
        ManagementWorkspace.Visibility=land?Visibility.Collapsed:Visibility.Visible;
        LocalViews.Visibility=land?Visibility.Visible:Visibility.Collapsed;
        NavyViews.Visibility=workspace=="Navy"?Visibility.Visible:Visibility.Collapsed;
        NationViews.Visibility=workspace=="Economy"?Visibility.Visible:Visibility.Collapsed;
        _portView=_navyPortModes.GetValueOrDefault(side);
        if(land) {
            _category=workspace=="Garrisons"?CommandCategory.Garrison:workspace=="Unclassified"?CommandCategory.Unknown:CommandCategory.FieldCommand;
            ClearDetails();_selectedNode=null;RebuildSide(true);UpdateCommandCategoryButtons();
            SetView(_workspaceRosterModes.GetValueOrDefault(WorkspaceKey,workspace=="Unclassified"));
            RestoreSelection(_workspaceSelections.GetValueOrDefault(WorkspaceKey,new HashSet<int>()));
        } else {
            WorkspaceNotice.Visibility=Visibility.Collapsed;
            RefreshManagement();
        }
        UpdateNationButtons();UpdateSelectionUi();
        foreach(var button in WorkspaceButtons(this)) {
            if(button.Tag is string tag && tag.Contains(':')) {
                button.FontWeight=tag==WorkspaceKey?FontWeights.Bold:FontWeights.Normal;
                button.BorderBrush=new SolidColorBrush(tag==WorkspaceKey?Color.FromRgb(121,207,221):Color.FromRgb(82,96,110));
                button.BorderThickness=new Thickness(tag==WorkspaceKey?2:1);
            }
        }
    }
    private static IEnumerable<Button> WorkspaceButtons(DependencyObject parent) {
        if(parent is Button b) yield return b;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) foreach(var child in WorkspaceButtons(VisualTreeHelper.GetChild(parent,i)))yield return child;
    }
    private void RestoreSelection(HashSet<int> ids) {
        ClearBatchSelection(false);
        foreach(var unit in _data.Units.Where(u=>u.Nation==_nation && ids.Contains(u.UnitId) && IsUnitInCurrentCommandCategory(u))) AddBatchSelection(unit,false);
        UpdateSelectionUi();
    }
    private void RecallBatch_Click(object sender,RoutedEventArgs e) {
        if(_workspace is not ("Armies" or "Garrisons")) return;
        if(_previousBatches.TryGetValue(WorkspaceKey,out var ids)) {RestoreSelection(ids);StatusText.Text=$"Restored {_selectedUnits.Count} units from this workspace's previous batch.";}
        else StatusText.Text="No previous batch in this workspace for the loaded save.";
    }
    private void ReselectBatch_Click(object sender,RoutedEventArgs e) {
        if(ManagementWorkspace.Visibility==Visibility.Visible)RecallManagementBatch_Click(sender,e);
        else RecallBatch_Click(sender,e);
    }
    private void ClearSelection_Click(object sender,RoutedEventArgs e) {
        if(ManagementWorkspace.Visibility==Visibility.Visible) {
            ManagementGrid.SelectedItems.Clear();
            ManagementGrid.SelectedCells.Clear();
            _managementSelections[ManagementSelectionKey]=new();
            SelectBoardCard(null);
            UpdateManagementSelectionUi();
        } else ClearBatchSelection();
    }
    private void EditSelected_Click(object sender,RoutedEventArgs e) {
        if(ManagementWorkspace.Visibility==Visibility.Visible) {
            if(_workspace=="Economy" && (_nationView is "Projects" or "Policies"))EditSelectedBoardCard();
            else EditManagement_Click(sender,e);
        } else if(_selectedUnits.Count>0)BatchEdit_Click(sender,e);
        else if(_selectedNode is GroupNode {IsLandCommand:true}) {
            var editor=SharedDetailPanel.Children.OfType<TextBox>().FirstOrDefault();
            editor?.BringIntoView();editor?.Focus();editor?.SelectAll();
        }
    }
    private void ManagementGrid_AutoGeneratingColumn(object sender,DataGridAutoGeneratingColumnEventArgs e)
    {
        e.Column.Header=System.Text.RegularExpressions.Regex.Replace(e.PropertyName,"(?<=[a-z])(?=[A-Z])"," ");
        if(e.Column is DataGridTextColumn text && text.Binding is System.Windows.Data.Binding binding &&
            (e.PropertyType==typeof(double) || e.PropertyType==typeof(double?) || e.PropertyType==typeof(long) || e.PropertyType==typeof(int)))
            binding.StringFormat=e.PropertyName.EndsWith("Id") || e.PropertyName=="Id"?"0":e.PropertyType==typeof(int) || e.PropertyType==typeof(long)?"N0":"N2";
    }
    private void DeployedFleets_Click(object sender,RoutedEventArgs e) {_portView=false;RefreshManagement();}
    private void ShipsInPort_Click(object sender,RoutedEventArgs e) {_portView=true;RefreshManagement();}
    private void ManagementGrid_MouseDoubleClick(object sender,System.Windows.Input.MouseButtonEventArgs e) {
        if(e.ChangedButton!=System.Windows.Input.MouseButton.Left || _workspace is not ("Officers" or "Weapons" or "Economy" or "Navy") || _data.IsReadOnlySave)return;
        if(e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(ManagementGrid,source) is not DataGridRow row)return;
        ManagementGrid.SelectedItems.Clear();ManagementGrid.UnselectAllCells();ManagementGrid.SelectedItem=row.Item;
        e.Handled=true;EditManagement_Click(sender,e);
    }
    private void EditTreasury_Click(object sender,RoutedEventArgs e) {
        if(_data.IsReadOnlySave || _data.Management is not {} doc || _workspace!="Economy")return;
        var record=doc.Records.SingleOrDefault(r=>r.Domain=="Treasury" && r.Side==_nation);if(record is null)return;
        var dialog=new ManagementEditWindow(doc,record,record.Name){Owner=this};
        if(dialog.ShowDialog()!=true || dialog.Changes is null)return;
        _editSession.ExecuteManagement("Edit national treasury",doc,()=>doc.Apply(dialog.Changes));
        RefreshManagement();UpdateDirtyState();StatusText.Text="Treasury change staged. Save changes writes it; Ctrl+Z undoes it.";
    }
    private void RefreshManagement()
    {
        _refreshingManagement=true;
        ManagementBatchActions.Visibility=_workspace is "Officers" or "Weapons" or "Navy"?Visibility.Visible:Visibility.Collapsed;
        bool board=_workspace=="Economy" && (_nationView is "Projects" or "Policies");
        NationBoardViewport.Visibility=board?Visibility.Visible:Visibility.Collapsed;
        ManagementGrid.Visibility=board?Visibility.Collapsed:Visibility.Visible;
        RememberNationScroll();NationBoard.Children.Clear();NationBoard.ColumnDefinitions.Clear();
        TreasuryPanel.Visibility=_workspace=="Economy"?Visibility.Visible:Visibility.Collapsed;
        EditTreasuryButton.IsEnabled=false;TreasuryBalanceText.Text="National treasury balance: unavailable";
        if(_data.SaveDirectory is null) {ManagementGrid.ItemsSource=null;ManagementSummary.Text="Select a save to browse this workspace.";_refreshingManagement=false;return;}
        _management ??= ManagementSnapshot.Read(_data.SaveDirectory,_data.StateOptions.ToDictionary(s=>s.Id,s=>s.Name),_data.Groups.ToDictionary(g=>g.Key,g=>g.Value.Nation),_data.Management is {} working?working.WorkingLines:null);
        var doc=_data.Management;
        string search=SearchBox.Text?.Trim()??"";
        bool Match(string name) => name.Contains(search,StringComparison.OrdinalIgnoreCase);
        string faction=_nation==0?"Union":"Confederacy";
        if(_workspace=="Officers") {
            ManagementGrid.ItemsSource=_management.Officers.Where(o=>o.Side==_nation && Match(o.Name)).Select(o=>new {
                o.Id,o.Name,Rank=OfficerRank(o.Rank,_data.Groups.Values.Any(g=>g.CommanderId==o.Id&&g.UnitTier==17)?4:o.Branch),Branch=OfficerBranch(o.Branch),Command=string.Join("; ",_data.Groups.Values.Where(g=>g.CommanderId==o.Id).Select(g=>g.Name).Concat(_data.Units.Where(u=>u.CommanderId==o.Id).Select(u=>u.Name))),o.Experience,o.Fame,o.Leadership,o.Initiative,o.Administration,o.Cunning,
                o.Veteran,o.WestPoint,o.Political,o.DateOfRank,TimeInGrade=TimeInGrade(o.DateOfRank),StatusId=o.Status
            }).ToList();
            ManagementSummary.Text=$"{faction} officers • Double-click to edit. Experience, Fame, Leadership, Initiative, Administration and Cunning: 0–100. Select several rows for Batch Edit.";
        } else if(_workspace=="Economy") {
            var treasury=doc?.Records.SingleOrDefault(r=>r.Domain=="Treasury" && r.Side==_nation);
            if(treasury is not null) {
                TreasuryBalanceText.Text=$"{faction} national treasury balance: $ {doc!.Numeric(treasury.Fields[0].File,treasury.Fields[0].Line):N0}";
                EditTreasuryButton.IsEnabled=!_data.IsReadOnlySave;
            }
            ManagementGrid.ItemsSource=_management.States.Where(s=>s.Side==_nation && s.IsVolunteerEditorState && Match(s.Name)).Select(s=>new {
                s.Id,s.Name,s.Population,SupportPercent=s.Support,SavedAvailableVolunteers=s.Available,HiddenDeficit=s.Deficit,AlreadyRecruited=s.Recruited,SavedCapacity=s.Capacity,s.Recruitable
            }).ToList();
            ManagementSummary.Text=$"{faction} active U.S. recruitment states and territories • Double-click a row, or select several and choose Adjust selected states. Population is shared; saved volunteer balances update when the game recalculates.";
            foreach(var button in NationViews.Children.OfType<Button>())button.FontWeight=button.Tag?.ToString()==_nationView?FontWeights.Bold:FontWeights.Normal;
            if(_nationView is "Projects" or "Policies") {
                var progress=_data.Progression;
                ManagementGrid.ItemsSource=null;
                BuildNationBoard(search);
                ManagementSummary.Text=_nationView=="Projects"
                    ?$"{faction} projects • Double-click a stage to complete through it; double-click the project row for the next stage. Green = completed. Funding is preserved."
                    :$"{faction} policies & acts • Double-click to stage 99.999%, including unfinished prerequisites. Save and advance campaign time to finish. Green = completed; amber = ready to finish.";
                if(progress?.Notices.Count>0)ManagementSummary.Text+=" "+string.Join(" ",progress.Notices);
            }
        } else if(_workspace=="Weapons") {
            ManagementGrid.ItemsSource=(doc?.Stocks.Where(s=>s.Side==_nation)??Enumerable.Empty<ManagementDocument.Stock>()).Select(s=>new {
                Id=s.WeaponId,Name=_data.WeaponOptions.FirstOrDefault(w=>w.Id==s.WeaponId)?.Name??$"Weapon {s.WeaponId}",Stock=doc!.Numeric("nations.dat",s.StockLine),OrderTotal=doc.Numeric("nations.dat",s.OrderLine+1),Delivered=doc.Numeric("nations.dat",s.OrderLine+5),StandardizationStart=doc.Numeric("nations.dat",s.StandardizationLine)
            }).Where(w=>Match(w.Name)).ToList();
            ManagementSummary.Text=$"{faction} national weapons • Edit stock, existing order quantity and standardization start year. Standardization percentage is calculated by the game.";
        } else if(_workspace=="Navy") {
            var classes=CampaignRules.Ships(_data.SaveDirectory,_data.ConfigDirectory);
            ManagementGrid.ItemsSource=_management.Ships.Where(s=>s.Side==_nation && s.InPort==_portView && Match(s.Name+" "+_data.Groups.GetValueOrDefault(s.FleetId)?.Name)).Select(s=>new {
                s.Id,s.Name,Fleet=_data.Groups.GetValueOrDefault(s.FleetId)?.Name??"Unassigned",Class=classes.GetValueOrDefault(s.TypeId)?.Name??$"Class {s.TypeId}",Guns=classes.GetValueOrDefault(s.TypeId)?.Guns,ConditionPercent=s.Condition,s.Status,TravelETA=s.TravelDays,ConstructionCompletedPercent=s.ConstructionComplete,RepairWorkRemainingPercent=s.RepairRemaining>0?(double?)(100*s.RepairRemaining):null,WorkDaysRemaining=classes.GetValueOrDefault(s.TypeId)?.WorkDays(Math.Max(s.ConstructionRemaining,s.RepairRemaining))
            }).ToList();
            ManagementSummary.Text=$"{faction} — {(_portView?"Ships in port / repair / construction / harbor transit":"Deployed fleets — vessel roster")} • Double-click to edit, or select several for Batch Edit. Condition is hull health; construction completed and repair work remaining are separate readiness controls.";
            DeployedFleetsButton.FontWeight=_portView?FontWeights.Normal:FontWeights.Bold;
            ShipsInPortButton.FontWeight=_portView?FontWeights.Bold:FontWeights.Normal;
        }
        if(_management.Notices.Count>0) ManagementSummary.Text+=" "+string.Join(" ",_management.Notices);
        if(doc?.Notices.Count>0)ManagementSummary.Text+=" "+string.Join(" ",doc.Notices);
        RestoreManagementSelection();_refreshingManagement=false;UpdateManagementSelectionUi();
    }
    private static string OfficerBranch(int branch)=>branch switch {-1=>"None",0=>"Infantry",1=>"Cavalry",2=>"Artillery",3=>"Engineer",4=>"Navy",_=>$"Branch {branch}"};
    private static string OfficerRank(int rank,int branch) {
        string[] army={"Volunteer","Lieutenant","Captain","Major","Lieutenant Colonel","Colonel","Brigadier General","Major General","Lieutenant General","General"};
        string[] navy={"Master","Lieutenant","Commander","Captain","Flag Officer"};
        var ranks=branch==4?navy:army;return rank>=0&&rank<ranks.Length?ranks[rank]:$"Rank {rank}";
    }
    private string TimeInGrade(string date)=>DateTime.TryParseExact(date,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var promoted) && _data.GameDate is DateTime now ?promoted>now?"Future date":$"{(now-promoted).Days:N0} days":"Not recorded";
    private void EditManagement_Click(object sender,RoutedEventArgs e) {
        if(_data.Management is not { } doc || _data.IsReadOnlySave)return;
        try {
            var selected=SelectedManagementRows();if(selected.Count==0)throw new InvalidOperationException("Select a record first.");
            int Id(object row)=>(int)row.GetType().GetProperty("Id")!.GetValue(row)!;
            Dictionary<ManagementDocument.Field,string>? changes;
            if(_workspace=="Economy" && _nationView=="States") {var dialog=new StatePopulationWindow(_data,selected.Select(Id).ToArray(),_nation){Owner=this};if(dialog.ShowDialog()!=true)return;changes=dialog.Changes;}
            else if(_workspace=="Economy" && _nationView is "Projects" or "Policies") {
                if(selected.Count!=1 || _data.Progression is null)throw new InvalidOperationException("Select one entry.");
                var dialog=new ProgressionWindow(_data.Progression,_nation,Id(selected[0]),_nationView=="Projects"){Owner=this};if(dialog.ShowDialog()!=true)return;changes=dialog.Plan?.Changes;
            }
            else {
                string domain=_workspace=="Economy"?"Funding":_workspace;
                var records=selected.Select(row=>doc.Records.Single(r=>r.Domain==domain && r.Id==Id(row) && (domain is not ("Weapons" or "Funding") || r.Side==_nation)) with {Name=row.GetType().GetProperty("Name")?.GetValue(row)?.ToString()??"Selected record"}).ToList();
                if(domain is "Officers" or "Navy" or "Weapons" && (selected.Count>1 || ReferenceEquals(sender,BatchEditButton)))_managementBatches[ManagementSelectionKey]=selected.Select(Id).ToHashSet();
                var name=records.Count==1?records[0].Name:$"{records.Count} {domain.ToLowerInvariant()} records";
                var dialog=new ManagementEditWindow(doc,records,name){Owner=this};if(dialog.ShowDialog()!=true)return;changes=dialog.Changes;
            }
            if(changes is null)return;
            _editSession.ExecuteManagement("Edit "+_workspace,doc,()=>doc.Apply(changes));
            _management=null;RefreshManagement();UpdateDirtyState();StatusText.Text=$"Staged {changes.Count} {_workspace.ToLowerInvariant()} field(s). Save changes writes them; Ctrl+Z undoes them.";
        }catch(Exception ex){MessageBox.Show(this,ex.Message,"Edit "+_workspace,MessageBoxButton.OK,MessageBoxImage.Information);}
    }
}
