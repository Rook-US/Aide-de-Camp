using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AideDeCamp.Services;

namespace AideDeCamp;
public sealed class StatePopulationWindow:Window
{
    public Dictionary<ManagementDocument.Field,string>? Changes {get;private set;}
    public StatePopulationWindow(GrandTacticianDataService data,IReadOnlyCollection<int> ids,int side)
    {
        var doc=data.Management!;Title="State population and recruitment";Width=1040;Height=610;MinWidth=800;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"SurfaceBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var original=ManagementSnapshot.Read(data.SaveDirectory!,data.StateOptions.ToDictionary(s=>s.Id,s=>s.Name),data.Groups.ToDictionary(g=>g.Key,g=>g.Value.Nation),doc.OriginalLines);
        if(ids.Any(id=>!original.States.Any(s=>s.Id==id && s.Side==side && s.IsVolunteerEditorState)))throw new InvalidOperationException("Volunteer editing is limited to active U.S. recruitment states and territories.");
        var pref=CampaignRules.Resolve(data.SaveDirectory!,data.ConfigDirectory,"campaignprefs.txt");
        var exp=CampaignRules.Setting(pref,"Reduction of volunteers pools for higher recruiting numbers, exp");
        var threshold=CampaignRules.Setting(pref,"Minimum state support needed for recruitment");
        var root=new DockPanel{Margin=new Thickness(16)};Content=root;
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text=$"{ids.Count} selected states • { (side==0?"Union":"Confederacy") } recruitment targets",FontWeight=FontWeights.Bold});
        top.Children.Add(new TextBlock{Text="Population is shared by both factions. Volunteer projections are estimates calibrated to saved recruitment capacity; stale pools or changed policies can produce different results. The game recalculates after loading. Only population will be changed.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)});
        var options=new StackPanel{Orientation=Orientation.Horizontal};top.Children.Add(options);
        var mode=new ComboBox{Width=280,ItemsSource=new[]{"Set population","Increase population by %","Add available volunteers (estimate)","Set available volunteers (estimate)"},SelectedIndex=1};options.Children.Add(mode);
        var amount=new TextBox{Name="PopulationAmount",Text="10",Width=150,Margin=new Thickness(10,0,10,0)};options.Children.Add(amount);
        var preview=new Button{Content="Preview"};options.Children.Add(preview);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10)};top.Children.Add(status);
        var bottom=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        bottom.Children.Add(new Button{Content="Cancel",IsCancel=true});var stage=new Button{Content="Stage previewed population changes",IsEnabled=false};bottom.Children.Add(stage);
        var grid=new DataGrid{IsReadOnly=true,CanUserAddRows=false,AutoGenerateColumns=true};root.Children.Add(grid);
        Dictionary<ManagementDocument.Field,string>? plan=null;
        void Invalidate(){plan=null;stage.IsEnabled=false;status.Text="Preview the values before staging.";}
        amount.TextChanged+=(_,_)=>Invalidate();mode.SelectionChanged+=(_,_)=>Invalidate();
        preview.Click+=(_,_)=>{
            try{
                if(!double.TryParse(amount.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value) || !double.IsFinite(value) || value<0)throw new InvalidOperationException("Enter a finite, non-negative number.");
                if(mode.SelectedIndex>=2 && value!=Math.Truncate(value))throw new InvalidOperationException("Volunteer targets must be whole numbers.");
                plan=new();var rows=new List<object>();int skipped=0;
                foreach(int id in ids) {
                    var state=original.States.Single(s=>s.Id==id && s.Side==side);
                    var field=doc.Records.Single(r=>r.Domain=="Economy" && r.Id==id).Fields.Single(f=>f.Key=="Population");
                    double current=doc.Numeric(field.File,field.Line),next=current;string result="Ready";RecruitmentProjection.Pool? Project(int faction,double pop) {
                        var s=original.States.Single(s=>s.Id==id&&s.Side==faction);
                        return RecruitmentProjection.ForPopulation(s,pop,1-exp);
                    }
                    try {
                        if(mode.SelectedIndex==0)next=value;
                        else if(mode.SelectedIndex==1)next=current*(1+value/100);
                        else {
                            if(exp is null || threshold is null)throw new InvalidOperationException("Recruitment settings unavailable; use a population amount.");
                            long target=checked((long)value);
                            if(mode.SelectedIndex==2)target=checked(target+RecruitmentProjection.Available(state.Population,current,state.Capacity,state.Recruited,1-exp.Value));
                            next=RecruitmentProjection.Target(state.Population,state.Capacity,state.Recruited,target,1-exp.Value,state.Recruitable && state.Support/100>=threshold.Value);
                            next=Math.Max(current,next);
                        }
                        var text=doc.Validate(field,next.ToString("R",CultureInfo.InvariantCulture));next=float.Parse(text,CultureInfo.InvariantCulture);plan[field]=text;
                    }catch(Exception ex){skipped++;result=ex.Message;}
                    var unionBefore=Project(0,current);var unionAfter=Project(0,next);var confederacyBefore=Project(1,current);var confederacyAfter=Project(1,next);
                    string Count(long? number)=>number?.ToString("N0")??"Not estimable";
                    rows.Add(new{State=state.Name,CurrentPopulation=current.ToString("N0"),ProposedPopulation=next.ToString("N0"),UnionBefore=Count(unionBefore?.Available),UnionAfter=Count(unionAfter?.Available),UnionDeficitBefore=Count(unionBefore?.Deficit),UnionDeficitAfter=Count(unionAfter?.Deficit),ConfederacyBefore=Count(confederacyBefore?.Available),ConfederacyAfter=Count(confederacyAfter?.Available),ConfederacyDeficitBefore=Count(confederacyBefore?.Deficit),ConfederacyDeficitAfter=Count(confederacyAfter?.Deficit),Result=result});
                }
                grid.ItemsSource=rows;stage.IsEnabled=plan.Count>0;status.Text=$"{plan.Count} states ready; {skipped} skipped. Both sides' estimated available volunteers and remaining deficits are shown. The roster retains these estimates after committing. No recruited counters will change.";
            }catch(Exception ex){Invalidate();status.Text=ex.Message;}
        };
        stage.Click+=(_,_)=>{Changes=plan;DialogResult=true;};
    }
}
