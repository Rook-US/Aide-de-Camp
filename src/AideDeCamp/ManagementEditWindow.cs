using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AideDeCamp.Services;

namespace AideDeCamp;

public sealed class ManagementEditWindow:Window
{
    public Dictionary<ManagementDocument.Field,string>? Changes {get;private set;}
    public ManagementEditWindow(ManagementDocument document,ManagementDocument.Record record,string name):this(document,new[]{record},name){}
    public ManagementEditWindow(ManagementDocument document,IReadOnlyList<ManagementDocument.Record> records,string name)
    {
        var record=records[0];var available=records.ToDictionary(r=>r,r=>ManagementBatchPlanner.CommonFields(document,new[]{r}));
        var common=available.Values.SelectMany(f=>f).DistinctBy(f=>f.Key).ToList();
        Title=(records.Count>1?"Batch edit ":"Edit ")+name;Width=820;Height=690;MinWidth=650;MinHeight=350;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"SurfaceBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var root=new DockPanel{Margin=new Thickness(16)};Content=root;
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var cancel=new Button{Content="Cancel",IsCancel=true};actions.Children.Add(cancel);
        var apply=new Button{Content="Commit changes"};actions.Children.Add(apply);
        var summary=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};DockPanel.SetDock(summary,Dock.Bottom);root.Children.Add(summary);
        var panel=new StackPanel();root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        panel.Children.Add(new TextBlock{Text="Changing a value selects and highlights its row. Restoring the original value clears it. Changes stay in the editor until Save changes; Ctrl+Z undoes a staged edit.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
        if(records.Count>1)panel.Children.Add(new TextBlock{Text=$"{records.Count} selected records. Mixed values start blank. Each field shows which records it applies to; hover its label for their names.\n"+string.Join(", ",records.Take(8).Select(r=>r.Name))+(records.Count>8?"…":""),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        if(record.Domain=="Officers")panel.Children.Add(new TextBlock{Text="Promotion dates apply to their named rank. They do not promote or reassign the officer. Branch is historical background; Navy can affect assignment eligibility. The game may adjust rank/dates when assigning commands.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        if(record.Domain=="Weapons")panel.Children.Add(new TextBlock{Text="Stock is a direct grant in pieces, not a paid purchase. Standardization is calculated by the game from its start year, research, stock/equipment and weapon complexity. Editing an existing order changes its delivery rate; delivered quantity, payment and timestamps stay as saved.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        if(record.Domain=="Navy")panel.Children.Add(new TextBlock{Text="Condition is hull condition: 99 means 99% condition, not construction completion. Construction completed: 99 means 99% built; 100 finishes construction. Repair work remaining: 1 means 1% work remains; 0 finishes repairs. Progress changes also adjust condition by the work completed unless you check Condition to set it explicitly. Construction/repair fields identify the selected ships with that work active. Travel, fleet assignment and supplies still affect readiness for duty.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        if(record.Domain=="Treasury")panel.Children.Add(new TextBlock{Text="Set this faction's national cash balance in dollars. Negative balances are allowed down to −100,000,000. This directly changes cash; debt, income, expenses and the other faction's balance stay unchanged. The game stores amounts with limited floating-point precision.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        var entries=new List<(ManagementDocument.Field Field,CheckBox Check,Func<string> Value,ManagementDocument.Record[] Targets)>();
        Dictionary<ManagementDocument.Field,string> BuildChanges() {
            var changes=new Dictionary<ManagementDocument.Field,string>();
            foreach(var target in records) {
                var values=entries.Where(e=>e.Check.IsChecked==true && e.Targets.Contains(target)).ToDictionary(e=>e.Field.Key,e=>e.Value());
                if(values.Count==0)continue;
                foreach(var pair in values) {
                    var field=available[target].Single(f=>f.Key==pair.Key);
                    try {changes[field]=document.Validate(field,pair.Value);}catch(Exception ex){throw new InvalidOperationException(target.Name+": "+ex.Message,ex);}
                }
            }
            return changes;
        }
        void Preview() {
            try {
                var changes=BuildChanges();
                var before=document.Capture();var originalValues=records.SelectMany(r=>r.Fields).Distinct().ToDictionary(f=>f,document.Value);
                var actual=new Dictionary<ManagementDocument.Field,string>();
                try {document.Apply(changes);foreach(var pair in originalValues)if(!Equivalent(pair.Value,document.Value(pair.Key)))actual[pair.Key]=document.Value(pair.Key);}
                finally {document.Restore(before);}
                int count=records.Count(r=>r.Fields.Any(actual.ContainsKey));
                summary.Text=$"{count} records will change • {records.Count} selected";
                summary.ToolTip=string.Join("\n",records.Where(r=>r.Fields.Any(actual.ContainsKey)).Select(r=>r.Name+"\n"+string.Join("\n",r.Fields.Where(actual.ContainsKey).Select(f=>"  "+f.Label+": "+originalValues[f]+" → "+actual[f]))));
                ToolTipService.SetShowDuration(summary,60000);apply.IsEnabled=actual.Count>0;
            }catch(Exception ex){summary.Text="Resolve the edited fields before committing.";summary.ToolTip=ex.Message;apply.IsEnabled=false;}
        }
        foreach(var field in common) {
            var targets=records.Where(r=>available[r].Any(f=>f.Key==field.Key && f.Kind==field.Kind)).ToArray();
            var allValues=targets.Select(r=>document.Value(r.Fields.Single(f=>f.Key==field.Key))).Distinct().ToArray();bool mixed=allValues.Length>1;
            var row=new Grid{Margin=new Thickness(0,4,0,4)};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new GridLength(220)});
            var check=new CheckBox{Content=new TextBlock{Text=field.Label+(targets.Length<records.Count?$" ({targets.Length} of {records.Count} selected)":""),TextWrapping=TextWrapping.Wrap},ToolTip=string.Join("\n",targets.Select(r=>r.Name)),VerticalAlignment=VerticalAlignment.Center};row.Children.Add(check);
            FrameworkElement input;Func<string> value;
            if(field.Kind=="bool") {var toggle=new CheckBox{IsThreeState=mixed,IsChecked=mixed?null:bool.Parse(allValues[0]),Content=mixed?"Mixed — choose a value":"Enabled"};input=toggle;value=()=>toggle.IsChecked is bool selected?selected.ToString():throw new InvalidOperationException(field.Label+": choose enabled or disabled.");}
            else if(field.Key=="Branch") {var combo=new ComboBox{ItemsSource=new[]{"None","Infantry","Cavalry","Artillery","Engineer","Navy"},SelectedIndex=mixed?-1:int.Parse(allValues[0],CultureInfo.InvariantCulture)+1};input=combo;value=()=>(combo.SelectedIndex-1).ToString(CultureInfo.InvariantCulture);}
            else {var box=new TextBox{Text=mixed?"":allValues[0],ToolTip=mixed?"Mixed values — enter one value for all selected records":field.Label};input=box;value=()=>box.Text;}
            Grid.SetColumn(input,1);row.Children.Add(input);panel.Children.Add(row);entries.Add((field,check,value,targets));
            if(field.Key=="Name" && targets.Length>1)panel.Children.Add(new TextBlock{Text="Warning: these records will share the same name.",Foreground=Brushes.Goldenrod});
            void SyncChanged() {
                bool changed;
                try {
                    string current=value();
                    changed=mixed ? input switch {TextBox => !string.IsNullOrWhiteSpace(current),ComboBox choice => choice.SelectedIndex>=0,CheckBox choice => choice.IsChecked is not null,_=>false} : !Equivalent(current,allValues[0]);
                } catch {changed=true;}
                check.IsChecked=changed;
                row.Background=changed?new SolidColorBrush(Color.FromRgb(47,70,69)):Brushes.Transparent;
                Preview();
            }
            check.Checked+=(_,_)=>Preview();check.Unchecked+=(_,_)=>Preview();
            if(input is TextBox text)text.TextChanged+=(_,_)=>SyncChanged();
            else if(input is ComboBox combo)combo.SelectionChanged+=(_,_)=>SyncChanged();
            else if(input is CheckBox toggle){toggle.Checked+=(_,_)=>SyncChanged();toggle.Unchecked+=(_,_)=>SyncChanged();toggle.Indeterminate+=(_,_)=>{check.IsChecked=false;row.Background=Brushes.Transparent;};}
        }
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=System.Windows.Media.Brushes.OrangeRed,Margin=new Thickness(0,10,0,10)};panel.Children.Add(error);
        apply.Click+=(_,_)=>{
            try {
                Preview();if(!apply.IsEnabled)return;
                var changes=BuildChanges();
                Changes=changes;DialogResult=true;
            } catch(Exception ex) {error.Text=ex.Message;}
        };
        Preview();
    }
    private static bool Equivalent(string left,string right)=>string.Equals(left,right,StringComparison.OrdinalIgnoreCase)
        || decimal.TryParse(left,NumberStyles.Float,CultureInfo.InvariantCulture,out var a)
        && decimal.TryParse(right,NumberStyles.Float,CultureInfo.InvariantCulture,out var b) && a==b;
}
