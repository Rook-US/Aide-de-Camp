using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AideDeCamp.Services;

namespace AideDeCamp;

public sealed class ManagementEditWindow:Window
{
    public Dictionary<ManagementDocument.Field,string>? Changes {get;private set;}
    public ManagementEditWindow(ManagementDocument document,ManagementDocument.Record record,string name):this(document,new[]{record},name){}
    public ManagementEditWindow(ManagementDocument document,IReadOnlyList<ManagementDocument.Record> records,string name)
    {
        var record=records[0];var common=ManagementBatchPlanner.CommonFields(document,records);
        Title=(records.Count>1?"Batch edit ":"Edit ")+name;Width=820;Height=690;MinWidth=650;MinHeight=350;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"SurfaceBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var root=new DockPanel{Margin=new Thickness(16)};Content=root;
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var cancel=new Button{Content="Cancel",IsCancel=true};actions.Children.Add(cancel);
        var apply=new Button{Content="Stage checked changes"};actions.Children.Add(apply);
        var panel=new StackPanel();root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        panel.Children.Add(new TextBlock{Text="Check the fields to change. Changes stay in the editor until Save changes. Ctrl+Z undoes a staged edit.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
        if(records.Count>1)panel.Children.Add(new TextBlock{Text=$"{records.Count} selected records. Checked values apply to all of them; mixed values start blank. Only fields editable for every selected record are shown.\n"+string.Join(", ",records.Take(8).Select(r=>r.Name))+(records.Count>8?"…":""),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        if(record.Domain=="Officers")panel.Children.Add(new TextBlock{Text="Promotion dates apply to their named rank. They do not promote or reassign the officer. Branch is historical background; Navy can affect assignment eligibility. The game may adjust rank/dates when assigning commands.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        if(record.Domain=="Weapons")panel.Children.Add(new TextBlock{Text="Stock is a direct grant in pieces, not a paid purchase. Standardization is calculated by the game from its start year, research, stock/equipment and weapon complexity. Editing an existing order changes its delivery rate; delivered quantity, payment and timestamps stay as saved.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        if(record.Domain=="Navy")panel.Children.Add(new TextBlock{Text="Condition is hull condition: 99 means 99% condition, not construction completion. Construction completed: 99 means 99% built; 100 finishes construction. Repair work remaining: 1 means 1% work remains; 0 finishes repairs. Progress changes also adjust condition by the work completed unless you check Condition to set it explicitly. Construction/repair fields appear only when every selected ship has that work active. Travel, fleet assignment and supplies still affect readiness for duty.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        if(record.Domain=="Treasury")panel.Children.Add(new TextBlock{Text="Set this faction's national cash balance in dollars. Negative balances are allowed down to −100,000,000. This directly changes cash; debt, income, expenses and the other faction's balance stay unchanged. The game stores amounts with limited floating-point precision.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        var entries=new List<(ManagementDocument.Field Field,CheckBox Check,Func<string> Value)>();
        foreach(var field in common) {
            var allValues=records.Select(r=>document.Value(r.Fields.Single(f=>f.Key==field.Key))).Distinct().ToArray();bool mixed=allValues.Length>1;
            var row=new Grid{Margin=new Thickness(0,4,0,4)};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new GridLength(220)});
            var check=new CheckBox{Content=new TextBlock{Text=field.Label,TextWrapping=TextWrapping.Wrap},VerticalAlignment=VerticalAlignment.Center};row.Children.Add(check);
            FrameworkElement input;Func<string> value;
            if(field.Kind=="bool") {var toggle=new CheckBox{IsThreeState=mixed,IsChecked=mixed?null:bool.Parse(allValues[0]),Content=mixed?"Mixed — choose a value":"Enabled"};input=toggle;value=()=>toggle.IsChecked is bool selected?selected.ToString():throw new InvalidOperationException(field.Label+": choose enabled or disabled.");}
            else if(field.Key=="Branch") {var combo=new ComboBox{ItemsSource=new[]{"None","Infantry","Cavalry","Artillery","Engineer","Navy"},SelectedIndex=mixed?-1:int.Parse(allValues[0],CultureInfo.InvariantCulture)+1};input=combo;value=()=>(combo.SelectedIndex-1).ToString(CultureInfo.InvariantCulture);}
            else {var box=new TextBox{Text=mixed?"":allValues[0],ToolTip=mixed?"Mixed values — enter one value for all selected records":field.Label};input=box;value=()=>box.Text;}
            Grid.SetColumn(input,1);row.Children.Add(input);panel.Children.Add(row);entries.Add((field,check,value));
        }
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=System.Windows.Media.Brushes.OrangeRed,Margin=new Thickness(0,10,0,10)};panel.Children.Add(error);
        apply.Click+=(_,_)=>{
            try {
                var changes=ManagementBatchPlanner.Plan(document,records,entries.Where(e=>e.Check.IsChecked==true).ToDictionary(e=>e.Field.Key,e=>e.Value()));
                Changes=changes;DialogResult=true;
            } catch(Exception ex) {error.Text=ex.Message;}
        };
    }
}
