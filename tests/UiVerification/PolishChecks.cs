using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using AideDeCamp;
using AideDeCamp.Models;
using AideDeCamp.Services;

internal static partial class Program
{
    private static void CheckPolish(MainWindow window,GrandTacticianDataService data) {
        var infantry=new CombatUnitNode{Name="Infantry fixture",UnitType=0,TotalMenRaw=1000,WeaponId=1,WeaponName="Musket",ConfiguredMaxStrength=2000};
        var battery=new CombatUnitNode{Name="Artillery fixture",UnitType=2,TotalMenRaw=100,WeaponId=2,WeaponName="Cannon",ConfiguredMaxStrength=300,ArtilleryCalculation=new ArtilleryRules(300,30,1)};
        var weapons=new[]{new WeaponOption(1,"Musket",0),new WeaponOption(2,"Cannon",2),new WeaponOption(3,"Rifle",0),new WeaponOption(4,"Rifled cannon",2)};
        var dialog=new SelectionEditWindow(new[]{infantry,battery},weapons,Array.Empty<StateOption>(),new EditValidationService()){Owner=window};dialog.Show();Flush(dialog);
        var groups=Descendants(dialog).OfType<GroupBox>().ToArray();
        Check(groups.Length==2&&groups.All(g=>Descendants(g).OfType<CheckBox>().Count()==TypedUnitEdit.Definitions.Length),"Mixed selection exposes all single-unit fields in clearly labeled type groups");
        ComboBox Weapon(GroupBox group)=>Descendants(group).OfType<ComboBox>().First();
        Check(Weapon(groups[0]).Items.Cast<string>().SequenceEqual(new[]{"Musket","Rifle"})&&Weapon(groups[1]).Items.Cast<string>().SequenceEqual(new[]{"Cannon","Rifled cannon"}),"Each weapon picker filters to its unit type");
        Weapon(groups[0]).SelectedItem="Rifle";Weapon(groups[1]).SelectedItem="Rifled cannon";Flush(dialog);
        Check(dialog.Plan?.Changes.Count==2&&dialog.Plan.Errors.Count==0&&dialog.Plan.Changes[0].Candidate.WeaponId==3&&dialog.Plan.Changes[1].Candidate.WeaponId==4,"Mixed weapon selections preview the correct assignment to each group");
        var strength=Descendants(groups[1]).OfType<CheckBox>().Single(c=>c.Content?.ToString()=="Field Strength");var strengthBox=((Grid)strength.Parent).Children.OfType<TextBox>().Single();strengthBox.Text="180";Flush(dialog);
        Check(strength.IsChecked==true&&Descendants(groups[1]).OfType<TextBlock>().Any(t=>t.Text.Contains("Estimated guns per battery")),"Strength edit auto-selects and shows its gun estimate");
        var capture=new RenderTargetBitmap((int)dialog.ActualWidth,(int)dialog.ActualHeight,96,96,PixelFormats.Pbgra32);capture.Render(dialog);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(capture));using(var output=File.Create("UI-Selection-Mixed.png"))encoder.Save(output);
        strengthBox.Text="100";Flush(dialog);Check(strength.IsChecked==false,"Restoring selected group's strength clears the field");
        Check(Descendants(dialog).OfType<Button>().Count(b=>b.Content?.ToString()=="Commit changes")==1,"Mixed selection has one commit button");dialog.Close();
        object? Invoke(string method,params object[] args)=>typeof(MainWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
        Invoke("OpenWorkspace",0,"Armies");Invoke("SetView",true);Flush(window);
        var grid=(DataGrid)window.FindName("RosterGrid");var column=grid.Columns.Single(c=>c.SortMemberPath=="Experience");
        var rows=grid.Items.OfType<RosterRow>().Where(r=>r.Unit is not null).Skip(30).Take(4).ToArray();var before=rows.Select(r=>r.Unit!.ExperienceRaw).ToArray();
        void Select(params RosterRow[] selected){grid.UnselectAll();grid.UnselectAllCells();foreach(var row in selected){var current=grid.Items.OfType<RosterRow>().Single(r=>r.Unit==row.Unit);var cell=new DataGridCellInfo(current,column);if(!grid.SelectedCells.Contains(cell))grid.SelectedCells.Add(cell);}Flush(window);}
        Select(rows[0],rows[1]);ClipboardAccess.SetText("42");Invoke("RosterClipboard",true);Flush(window);
        Check(rows.Take(2).All(r=>r.Unit!.ExperienceRaw==42),"One clipboard value fills several selected experience cells");
        Select(rows[0]);Invoke("RosterClipboard",false);Check(ClipboardAccess.GetText()=="42","Copy reads the selected cell's value");
        Select(rows[2],rows[3]);Invoke("RosterClipboard",true);Flush(window);Check(rows.Skip(2).All(r=>r.Unit!.ExperienceRaw==42),"Copied value fills additional selected rows");
        Invoke("UndoWorkingEdit");Invoke("UndoWorkingEdit");Check(rows.Select(r=>r.Unit!.ExperienceRaw).SequenceEqual(before),"Each roster paste undoes in a single step");
        Select(rows[0],rows[1]);ClipboardAccess.SetText("invalid");Invoke("RosterClipboard",true);Check(rows.Select(r=>r.Unit!.ExperienceRaw).SequenceEqual(before),"Invalid clipboard values leave every destination unchanged");
    }
}
