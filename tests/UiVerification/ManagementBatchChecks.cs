using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;
using AideDeCamp;
using AideDeCamp.Services;

internal static partial class Program
{
    private static void CheckManagementBatches(MainWindow window,GrandTacticianDataService data,Application app) {
        bool initialDirty=data.HasUnsavedChanges;
        void Open(string workspace){typeof(MainWindow).GetMethod("OpenWorkspace",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{1,workspace});Flush(window);}
        void Shot(Window target,string file){Flush(target);var bmp=new RenderTargetBitmap((int)target.ActualWidth,(int)target.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(target);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using var stream=File.Create(file);png.Save(stream);}
        foreach(var workspace in new[]{"Officers","Weapons","Navy"}) {
            Open(workspace);
            if(workspace=="Navy"){typeof(MainWindow).GetMethod("ShipsInPort_Click",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{window,new RoutedEventArgs()});Flush(window);}
            var grid=(DataGrid)window.FindName("ManagementGrid");grid.SelectedItems.Clear();grid.SelectedItems.Add(grid.Items[0]);grid.SelectedItems.Add(grid.Items[1]);Flush(window);
            var ids=grid.SelectedItems.Cast<object>().Select(r=>(int)r.GetType().GetProperty("Id")!.GetValue(r)!).ToHashSet();
            Check(((Button)window.FindName("BatchEditButton")).IsEnabled,workspace+" selection enables the fixed Batch Edit button");
            string field=workspace=="Officers"?"Leadership":workspace=="Weapons"?"Stock on hand":"Condition";
            window.Dispatcher.BeginInvoke(new Action(()=>{
                var dialog=app.Windows.OfType<ManagementEditWindow>().Single(w=>w.Owner==window);Flush(dialog);
                Check(dialog.Title.StartsWith("Batch edit")&&Descendants(dialog).OfType<TextBlock>().Any(t=>t.Text.Contains("2 selected records")),workspace+" batch dialog clearly identifies all selected records");
                var check=Descendants(dialog).OfType<CheckBox>().Single(c=>c.Content is TextBlock text&&text.Text.StartsWith(field));((Grid)check.Parent).Children.OfType<TextBox>().Single().Text=workspace=="Weapons"?"4321":"88";
                Check(check.IsChecked==true && ((Grid)check.Parent).Background is SolidColorBrush brush && brush.Color==Color.FromRgb(47,70,69),workspace+" changed batch field auto-selects and highlights");
                var editedBox=((Grid)check.Parent).Children.OfType<TextBox>().Single();
                editedBox.Text="";
                Check(check.IsChecked==false && ((Grid)check.Parent).Background==Brushes.Transparent,workspace+" clearing a mixed batch field clears its selection and highlight");
                editedBox.Text=workspace=="Weapons"?"4321":"88";
                if(workspace=="Officers") {
                    Check(new[]{"Fame","Leadership","Initiative","Administration","Cunning"}.All(key=>Descendants(dialog).OfType<CheckBox>().Any(c=>c.Content is TextBlock t&&t.Text.StartsWith(key))),"Fame and all four officer attributes are editable in the batch dialog");
                    var fame=Descendants(dialog).OfType<CheckBox>().Single(c=>c.Content is TextBlock t&&t.Text.StartsWith("Fame"));((Grid)fame.Parent).Children.OfType<TextBox>().Single().Text="99.5";
                    var administration=Descendants(dialog).OfType<CheckBox>().Single(c=>c.Content is TextBlock t&&t.Text.StartsWith("Administration"));((Grid)administration.Parent).Children.OfType<TextBox>().Single().Text="95";
                }
                if(workspace=="Navy")Check(Descendants(dialog).OfType<TextBlock>().Any(t=>t.Text.Contains("99% condition, not construction completion")&&t.Text.Contains("100 finishes construction")),"Ship editor explains hull condition, construction completion and repair work separately");
                Shot(dialog,"UI-Batch-"+workspace+".png");
                Descendants(dialog).OfType<Button>().Single(b=>b.Content?.ToString()=="Stage changes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }),DispatcherPriority.ApplicationIdle);
            ((Button)window.FindName("BatchEditButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush(window);
            var records=data.Management!.Records.Where(r=>r.Domain==workspace&&ids.Contains(r.Id)&&(workspace!="Weapons"||r.Side==1)).ToList();
            string key=workspace=="Officers"?"Leadership":workspace=="Weapons"?"Stock":"Condition";
            Check(records.Count==2&&records.All(r=>data.Management.Value(r.Fields.Single(f=>f.Key==key))==(workspace=="Weapons"?"4321":"88")),workspace+" batch stages the checked value on both selected records");
            if(workspace=="Officers")Check(records.All(r=>data.Management.Value(r.Fields.Single(f=>f.Key=="Fame"))=="99.5")&&grid.Columns.Any(c=>c.Header?.ToString()=="Fame"),"Fame batch changes appear on the officer roster");
            Check(grid.SelectedItems.Count==2,workspace+" keeps the selection after staging");
            typeof(MainWindow).GetMethod("UndoWorkingEdit",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);Flush(window);
            Check(data.HasUnsavedChanges==initialDirty&&grid.SelectedItems.Count==2,workspace+" batch undoes in one action while retaining selection");
            grid.SelectedItems.Clear();typeof(MainWindow).GetMethod("RecallManagementBatch_Click",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{window,new RoutedEventArgs()});Flush(window);
            Check(grid.SelectedItems.Count==2,workspace+" can recall its previous batch after clearing selection");
        }
        Open("Economy");
        var cash=(TextBlock)window.FindName("TreasuryBalanceText");var edit=(Button)window.FindName("EditTreasuryButton");
        var cashRect=cash.TransformToAncestor(window).TransformBounds(new Rect(cash.RenderSize));var editRect=edit.TransformToAncestor(window).TransformBounds(new Rect(edit.RenderSize));
        Check(editRect.Left>=cashRect.Right&&editRect.Left-cashRect.Right<20,"Edit treasury sits beside its balance instead of the far-right edge");
        Shot(window,"UI-Treasury-Adjacent.png");
    }
}
