using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AideDeCamp.Services;

namespace AideDeCamp;

public partial class MainWindow
{
    private readonly Dictionary<string,double> _nationScroll=new();
    private static readonly Brush CompletedBrush=new SolidColorBrush(Color.FromRgb(39,101,68));
    private static readonly Brush PendingBrush=new SolidColorBrush(Color.FromRgb(152,111,38));
    private string? _selectedBoardTag;
    private Border? _selectedBoardBorder;
    private void SelectBoardCard(ContentControl? card) {
        if(_selectedBoardBorder is not null)_selectedBoardBorder.SetResourceReference(Border.BorderBrushProperty,"EdgeBrush");
        _selectedBoardTag=card?.Tag as string;
        _selectedBoardBorder=card?.Content as Border;
        if(_selectedBoardBorder is not null)_selectedBoardBorder.BorderBrush=new SolidColorBrush(Color.FromRgb(142,197,255));
        UpdateManagementSelectionUi();
    }
    private void EditSelectedBoardCard() {
        if(_selectedBoardTag is null || _data.Progression is null || _data.Management is not {} doc || _data.IsReadOnlySave)return;
        var parts=_selectedBoardTag.Split(':');
        if(parts.Length!=2 || !int.TryParse(parts[1],out var id))return;
        var dialog=new ProgressionWindow(_data.Progression,_nation,id,parts[0]=="Project"){Owner=this};
        if(dialog.ShowDialog()==true && dialog.Plan is not null)ApplyBoardPlan(dialog.Plan,"Edit "+parts[0].ToLowerInvariant());
    }
    private void NationBoard_SizeChanged(object sender,SizeChangedEventArgs e)=>SizeNationBoard();
    private void SizeNationBoard() {
        NationBoard.Width=Math.Max(1470,NationBoardViewport.ActualWidth-2);
        NationBoard.Height=Math.Max(120,NationBoardViewport.ActualHeight-20);
    }
    private void RememberNationScroll() {
        foreach(var column in NationBoard.Children.OfType<Grid>())
            foreach(var scroll in column.Children.OfType<ScrollViewer>())
                if(scroll.Tag is string key)_nationScroll[key]=scroll.VerticalOffset;
    }
    private static TextBlock BoardText(string text,bool bold=false)=>new() {
        Text=text,TextWrapping=TextWrapping.Wrap,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Margin=new Thickness(0,0,0,5)
    };
    private void BuildNationBoard(string search) {
        var doc=_data.Management;var progress=_data.Progression;
        _selectedBoardBorder=null;
        bool projects=_nationView=="Projects";
        bool Match(string text)=>text.Contains(search,StringComparison.OrdinalIgnoreCase);
        for(int category=0;category<6;category++) {
            NationBoard.ColumnDefinitions.Add(new ColumnDefinition());
            var column=new Grid{Margin=new Thickness(0,0,8,0)};Grid.SetColumn(column,category);NationBoard.Children.Add(column);
            column.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});column.RowDefinitions.Add(new RowDefinition());
            var header=new StackPanel{Margin=new Thickness(10)};
            header.Children.Add(BoardText(NationProgression.Funds[category],true));
            var headerBorder=new Border{Child=header,CornerRadius=new CornerRadius(5,5,0,0),BorderThickness=new Thickness(1)};
            headerBorder.SetResourceReference(Border.BackgroundProperty,"ControlBrush");headerBorder.SetResourceReference(Border.BorderBrushProperty,"EdgeBrush");column.Children.Add(headerBorder);
            var items=new StackPanel{Margin=new Thickness(0,8,3,0)};
            string scrollKey=$"{_nation}:{_nationView}:{category}:{search}";
            var scroll=new ScrollViewer{Content=items,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Tag=scrollKey};
            Grid.SetRow(scroll,1);column.Children.Add(scroll);
            scroll.Loaded+=(_,_)=>scroll.ScrollToVerticalOffset(_nationScroll.GetValueOrDefault(scrollKey));
            if(projects) {
                var funding=doc?.Records.SingleOrDefault(r=>r.Domain=="Funding"&&r.Side==_nation&&r.Id==category);
                header.Children.Add(BoardText("Available subsidy funds"));
                var amount=new TextBlock{Text=funding is null?"Unavailable":"$"+Math.Round(doc!.Numeric(funding.Fields[0].File,funding.Fields[0].Line),0,MidpointRounding.AwayFromZero).ToString("N0",CultureInfo.CurrentCulture),FontSize=19,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6),Tag=$"Balance:{category}"};header.Children.Add(amount);
                var edit=new Button{Content="Edit funding",Tag=$"Funding:{category}",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0),IsEnabled=funding is not null&&!_data.IsReadOnlySave};
                edit.Click+=(_,_)=>EditBoardFunding(funding!);header.Children.Add(edit);
                if(progress is not null)foreach(var project in progress.Projects.Where(p=>p.Fund==category&&p.Sides.Contains(_nation)&&progress.InScenario(p.Scenarios)&&Match(p.Name)).OrderBy(p=>p.Id))items.Children.Add(ProjectCard(project,progress));
            } else if(progress is not null) {
                var policies=progress.Policies.Where(p=>p.Side==_nation&&progress.InScenario(p.Scenarios)&&progress.PolicyCategory(p)==category&&Match(p.Name)).OrderBy(p=>p.Id).ToArray();
                header.Children.Add(BoardText($"{policies.Count(p=>progress.Progress(_nation,p.Id)>=1)} / {policies.Length} completed"));
                foreach(var group in policies.GroupBy(p=>p.Duration<=0?"Pre-war choices":p.IsAct?"Acts":"Policies").OrderBy(g=>g.Key=="Policies"?0:g.Key=="Acts"?1:2)) {
                    items.Children.Add(new TextBlock{Text=group.Key,Margin=new Thickness(6,5,0,8),FontWeight=FontWeights.SemiBold});
                    foreach(var policy in group)items.Children.Add(PolicyCard(policy,progress));
                }
            }
            if(items.Children.Count==0)items.Children.Add(new TextBlock{Text=progress is null?"Choose a save and game folder.":"No matching entries",Margin=new Thickness(10),TextWrapping=TextWrapping.Wrap});
        }
        SizeNationBoard();
        if(_selectedBoardBorder is null)_selectedBoardTag=null;
        UpdateManagementSelectionUi();
    }
    private ContentControl BoardCard(string tag,StackPanel contents,Action next,string tooltip) {
        var border=new Border{Child=contents,Padding=new Thickness(10),Margin=new Thickness(0,0,0,8),CornerRadius=new CornerRadius(5),BorderThickness=new Thickness(1)};
        border.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");border.SetResourceReference(Border.BorderBrushProperty,"EdgeBrush");
        var card=new ContentControl{Content=border,Tag=tag,Focusable=true,ToolTip=tooltip,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        if(tag==_selectedBoardTag){_selectedBoardBorder=border;border.BorderBrush=new SolidColorBrush(Color.FromRgb(142,197,255));}
        card.PreviewMouseLeftButtonDown+=(_,_)=>SelectBoardCard(card);
        card.GotKeyboardFocus+=(_,_)=>SelectBoardCard(card);
        System.Windows.Automation.AutomationProperties.SetName(card,tooltip);
        card.MouseDoubleClick+=(_,e)=>{
            if(e.Handled||e.ChangedButton!=MouseButton.Left)return;
            // A stage box handles its own target; its double-click must not also add a next stage.
            for(var source=e.OriginalSource as DependencyObject;source is not null && source!=card;source=VisualTreeHelper.GetParent(source))
                if(source is Button {Tag:string key} && key.StartsWith("Stage:"))return;
            e.Handled=true;next();
        };
        card.KeyDown+=(_,e)=>{if(e.Key==Key.Enter&&ReferenceEquals(e.OriginalSource,card)){e.Handled=true;next();}};
        ToolTipService.SetShowDuration(card,30000);return card;
    }
    private ContentControl ProjectCard(NationProgression.Project project,NationProgression progress) {
        int level=progress.Level(_nation,project.Id);string restriction=progress.ProjectRestriction(project,_nation);
        var content=new StackPanel();content.Children.Add(BoardText(project.Name,true));
        content.Children.Add(BoardDescription(project.Description));
        content.Children.Add(BoardText(project.Repeating?$"Repeatable · {level} stages completed":level>0?"Unlocked":"Single purchase"));
        var boxes=new WrapPanel();
        int shown=project.Repeating?Math.Min(100,Math.Max(5,level+5)):1;
        for(int stage=1;stage<=shown;stage++) {
            int target=stage;bool complete=level>=stage;
            var box=new Button{Content=project.Repeating?(complete?"✓ ":"")+stage:complete?"✓ Unlocked":"Unlock",Tag=$"Stage:{project.Id}:{stage}",MinWidth=project.Repeating?36:190,Padding=new Thickness(5),Margin=new Thickness(0,0,4,4),ToolTip=complete?$"Stage {stage} completed":restriction.Length>0?restriction:$"Double-click to complete stages 1–{stage}",IsEnabled=!_data.IsReadOnlySave};
            if(complete){box.Background=CompletedBrush;box.Foreground=Brushes.White;}
            box.MouseDoubleClick+=(_,e)=>{if(e.ChangedButton==MouseButton.Left){e.Handled=true;StageBoardProject(project.Id,target);}};
            box.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;StageBoardProject(project.Id,target);}};
            System.Windows.Automation.AutomationProperties.SetName(box,project.Name+", stage "+stage+(complete?", completed":", double-click to complete through this stage"));
            boxes.Children.Add(box);
        }
        content.Children.Add(boxes);
        if(project.Repeating)content.Children.Add(BoardText(shown<100?$"Stages 1–{shown} shown · more appear as you advance":"Editor limit: 100 stages"));
        if(restriction.Length>0)content.Children.Add(new TextBlock{Text=restriction.StartsWith("Requires one of:")?"Requires a policy · see details":"Complete in campaign · see details",Foreground=new SolidColorBrush(Color.FromRgb(233,196,106)),TextWrapping=TextWrapping.Wrap});
        return BoardCard($"Project:{project.Id}",content,()=>StageBoardProject(project.Id,progress.Level(_nation,project.Id)+1),project.Name+"\n"+project.Description.Replace("\\n","\n")+"\n"+(restriction.Length>0?restriction:"Double-click the row for the next stage. Direct completion does not spend funds."));
    }
    private ContentControl PolicyCard(NationProgression.Policy policy,NationProgression progress) {
        double value=progress.Progress(_nation,policy.Id);bool complete=value>=1,ready=!complete&&value>=0.999989;
        string restriction=progress.PolicyRestriction(policy,_nation);
        var content=new StackPanel();content.Children.Add(BoardText(policy.Name,true));
        content.Children.Add(BoardDescription(policy.Description));
        string percent=complete?"100%":ready?"99.999%":Math.Min(99.99,100*value).ToString("0.##",CultureInfo.CurrentCulture)+"%";
        content.Children.Add(BoardText(percent+(complete?" · Completed":ready?" · Ready to finish":" · Research progress")));
        var bar=new ProgressBar{Minimum=0,Maximum=100,Value=Math.Clamp(value*100,0,100),Height=9,Margin=new Thickness(0,2,0,5),Foreground=complete?CompletedBrush:ready?PendingBrush:new SolidColorBrush(Color.FromRgb(77,151,185)),Background=new SolidColorBrush(Color.FromRgb(32,44,55)),Tag=$"Progress:{policy.Id}"};
        content.Children.Add(bar);
        if(restriction.Length>0)content.Children.Add(BoardText("Pre-war choice · read only"));
        return BoardCard($"Policy:{policy.Id}",content,()=>StageBoardPolicy(policy.Id),policy.Name+"\n"+policy.Description.Replace("\\n","\n")+"\n"+(restriction.Length>0?restriction:"Double-click to set unfinished research and prerequisites to 99.999%. Save and advance campaign time to finish."));
    }
    private static TextBlock BoardDescription(string description)=>new() {
        Text=string.IsNullOrWhiteSpace(description)?"No in-game description provided.":description.Replace("\\n","\n").Trim(),TextWrapping=TextWrapping.Wrap,
        Foreground=new SolidColorBrush(Color.FromRgb(183,198,209)),Margin=new Thickness(0,2,0,8)
    };
    private void EditBoardFunding(ManagementDocument.Record record) {
        if(_data.Management is not {} doc||_data.IsReadOnlySave)return;
        var dialog=new ManagementEditWindow(doc,record,record.Name+" subsidy funds"){Owner=this};
        if(dialog.ShowDialog()==true&&dialog.Changes is not null)ApplyBoardPlan(new(dialog.Changes,record.Name+" funding updated."),"Edit subsidy funding");
    }
    private void StageBoardProject(int id,int level) {
        if(_data.Progression is not {} progress||_data.IsReadOnlySave)return;
        if(level<=progress.Level(_nation,id)){StatusText.Text="That stage is already complete. No changes staged.";return;}
        try{ApplyBoardPlan(progress.CompleteProject(_nation,id,level),"Complete project stages");}
        catch(Exception ex) when(ex is InvalidOperationException or System.IO.InvalidDataException or FormatException){StatusText.Text=ex.Message;}
    }
    private void StageBoardPolicy(int id) {
        if(_data.Progression is not {} progress||_data.IsReadOnlySave)return;
        try{ApplyBoardPlan(progress.CompletePolicy(_nation,id,true),"Ready policy for campaign completion");}
        catch(Exception ex) when(ex is InvalidOperationException or System.IO.InvalidDataException or FormatException){StatusText.Text=ex.Message;}
    }
    private void ApplyBoardPlan(NationProgression.Plan plan,string title) {
        if(_data.Management is not {} doc)return;
        _editSession.ExecuteManagement(title,doc,()=>doc.Apply(plan.Changes));
        _management=null;RefreshManagement();UpdateDirtyState();StatusText.Text=plan.Description+" Staged; Save changes writes it. Ctrl+Z undoes it.";
    }
}
