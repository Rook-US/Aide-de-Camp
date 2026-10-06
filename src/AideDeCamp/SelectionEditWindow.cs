using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;

public sealed class SelectionEditWindow : Window
{
    public SelectionEditPlan? Plan {get;private set;}
    public SelectionEditWindow(IReadOnlyList<CombatUnitNode> units,IReadOnlyList<WeaponOption> weapons,IReadOnlyList<StateOption> states,EditValidationService validation) {
        Title=$"Edit Selected — {units.Count:N0} units";Width=820;Height=780;MinWidth=650;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"SurfaceBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var root=new DockPanel{Margin=new Thickness(16)};Content=root;
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var summary=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};footer.Children.Add(summary);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(actions);
        actions.Children.Add(new Button{Content="Cancel",IsCancel=true});
        var commit=new Button{Content="Commit changes",IsEnabled=false};actions.Children.Add(commit);
        var panel=new StackPanel();root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        panel.Children.Add(new TextBlock{Text="Changed fields are selected automatically. Blank mixed fields leave each unit unchanged. Hover the summary for every proposed change. Commit changes stages one undoable edit; Save changes writes it to disk.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        var entries=new Dictionary<int,List<(string Key,CheckBox Check,Func<string> Value)>>();
        var strengthPreviews=new Dictionary<int,TextBlock>();
        static string Range(IEnumerable<int> values){var numbers=values.Distinct().Order().ToArray();return numbers.Length==0?"unavailable":numbers.Length==1?numbers[0].ToString("N0"):$"{numbers[0]:N0}–{numbers[^1]:N0}";}
        void Preview() {
            Plan=SelectionEditPlan.Build(units,u=>entries.GetValueOrDefault(u.UnitType,new()).Where(e=>e.Check.IsChecked==true).ToDictionary(e=>e.Key,e=>e.Value()),weapons,states,validation);
            summary.Text=$"{Plan.Changes.Count:N0} units will change • {units.Count:N0} selected"+(Plan.Errors.Count>0?$" • {Plan.Errors.Count} errors — hover for details":"");
            summary.ToolTip=Plan.Tooltip;ToolTipService.SetShowDuration(summary,60000);
            commit.IsEnabled=Plan.Changes.Count>0 && Plan.Errors.Count==0;
            foreach(var pair in strengthPreviews) {
                var group=units.Where(u=>u.UnitType==pair.Key).ToArray();
                var proposed=group.Select(u=>Plan.Changes.FirstOrDefault(p=>p.Unit==u).Candidate??u).ToArray();
                pair.Value.Text="Proposed field strength per unit: "+Range(proposed.Select(u=>u.FieldStrength))
                    +(pair.Key==2?" • Estimated guns per battery: "+Range(proposed.Where(u=>u.GunCount.HasValue).Select(u=>u.GunCount!.Value)):"");
            }
        }
        foreach(var group in units.GroupBy(u=>u.UnitType)) {
            var selected=group.ToArray();var list=new List<(string,CheckBox,Func<string>)>();entries[group.Key]=list;
            var body=new StackPanel{Margin=new Thickness(12)};
            panel.Children.Add(new GroupBox{Header=$"{selected[0].TypeName} — {selected.Length:N0} selected",Content=body,Margin=new Thickness(0,0,0,12)});
            body.Children.Add(new TextBlock{Text=string.Join(", ",selected.Take(8).Select(u=>u.Name))+(selected.Length>8?"…":""),ToolTip=string.Join("\n",selected.Select(u=>u.Name)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
            body.Children.Add(new TextBlock{Text="Current unit maximum: "+string.Join(" / ",selected.Select(u=>u.ConfiguredMaxStrength).Distinct().Order().Select(n=>n.ToString("N0"))),Margin=new Thickness(0,0,0,8)});
            var strengthPreview=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};body.Children.Add(strengthPreview);strengthPreviews[group.Key]=strengthPreview;
            foreach(var definition in TypedUnitEdit.Definitions) {
                string key=definition.Key;var originals=selected.Select(u=>TypedUnitEdit.Read(u,key)).Distinct().ToArray();string baseline=originals.Length==1?originals[0]:"";
                var row=new Grid{Margin=new Thickness(0,3,0,3)};row.ColumnDefinitions.Add(new(){Width=new GridLength(260)});row.ColumnDefinitions.Add(new());
                var check=new CheckBox{Content=definition.Label,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(check);
                Control editor;Func<string> value=()=>baseline;
                if(key is "Weapon" or "HomeState") {
                    var combo=new ComboBox{IsEditable=true,IsTextSearchEnabled=false,Text=baseline};
                    combo.ItemsSource=key=="Weapon"?weapons.Where(w=>selected.All(u=>validation.GetWeaponCompatibility(u,w)==WeaponCompatibility.Compatible)).Select(w=>w.Name).ToArray():states.Select(s=>s.Name).ToArray();
                    editor=combo;value=()=>combo.Text;
                    combo.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,_)=>Sync()));
                } else {var box=new TextBox{Text=baseline};editor=box;value=()=>box.Text;box.TextChanged+=(_,_)=>Sync();}
                editor.ToolTip=originals.Length>1?"Mixed values — enter a value to apply to this group":definition.Label;
                if(key=="Name" && selected.Length>1)editor.ToolTip="Warning: this assigns the same name to every unit in this group. Unit names usually should be unique.";
                Grid.SetColumn(editor,1);row.Children.Add(editor);body.Children.Add(row);list.Add((key,check,value));
                void Sync(){var current=value();bool same=current==baseline;
                    if(key is "FieldStrength" or "Casualties" or "Experience" or "Contract" or "ContractRemaining" or "ETA")
                        same=same || double.TryParse(current,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var a)&&double.TryParse(baseline,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var b)&&a==b;
                    check.IsChecked=!same;Preview();}
                check.Checked+=(_,_)=>{row.Background=new SolidColorBrush(Color.FromRgb(47,70,69));Preview();};check.Unchecked+=(_,_)=>{row.Background=Brushes.Transparent;Preview();};
                if(key=="Name" && selected.Length>1)body.Children.Add(new TextBlock{Text="Shared name: every selected "+selected[0].TypeName.ToLowerInvariant()+" unit will receive this name.",Foreground=Brushes.Goldenrod,TextWrapping=TextWrapping.Wrap});
            }
        }
        commit.Click+=(_,_)=>{Preview();if(commit.IsEnabled)DialogResult=true;};Preview();
    }
}
