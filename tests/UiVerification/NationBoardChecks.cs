using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;
using AideDeCamp;
using AideDeCamp.Services;

internal static partial class Program
{
    private static void CheckNationBoards(MainWindow window,GrandTacticianDataService data,Application app) {
        bool initialDirty=data.HasUnsavedChanges;
        void View(int side,string view) {
            typeof(MainWindow).GetMethod("OpenWorkspace",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{side,"Economy"});
            typeof(MainWindow).GetMethod("NationView_Click",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{new Button{Tag=view},new RoutedEventArgs()});Flush(window);
        }
        void Undo(){typeof(MainWindow).GetMethod("UndoWorkingEdit",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);Flush(window);}
        FrameworkElement Find(string tag)=>Descendants((Grid)window.FindName("NationBoard")).OfType<FrameworkElement>().Single(e=>e.Tag?.ToString()==tag);
        void Double(Control control,object? source=null){control.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=source??control});Flush(window);}
        void Shot(string name) {
            var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(name);encoder.Save(output);
        }
        View(1,"Projects");
        var board=(Grid)window.FindName("NationBoard");
        var projectDescription=data.Progression!.Projects.Single(p=>p.Id==95).Description.Replace("\\n","\n").Trim();
        Check(Descendants(Find("Project:95")).OfType<TextBlock>().Any(t=>t.Text==projectDescription),"Project card shows its in-game description");
        var selectedProject=(ContentControl)Find("Project:95");
        selectedProject.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent,Source=selectedProject});Flush(window);
        var editSelected=(Button)window.FindName("EditSelectedButton");
        Check(editSelected.IsEnabled && ((TextBlock)window.FindName("SelectionCountText")).Text=="1 selected","Selecting a project enables the shared Edit Selected button");
        bool projectEditorOpened=false;
        window.Dispatcher.BeginInvoke(new Action(()=>{var dialog=app.Windows.OfType<ProgressionWindow>().Single(w=>w.Owner==window);projectEditorOpened=true;dialog.Close();}),DispatcherPriority.ApplicationIdle);
        editSelected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush(window);
        Check(projectEditorOpened,"Shared Edit Selected button opens the selected project's editor");
        Check(board.ColumnDefinitions.Count==6 && ((DataGrid)window.FindName("ManagementGrid")).Visibility==Visibility.Collapsed,"Combined projects board replaces the table with six category columns");
        Check(((StackPanel)window.FindName("NationViews")).Children.OfType<Button>().Select(b=>b.Content?.ToString()).SequenceEqual(new[]{"States","Projects & Funding","Policies"}),"Funding and projects share one navigation button");
        Check(Descendants(board).OfType<Button>().Count(b=>b.Tag?.ToString()?.StartsWith("Funding:")==true)==6,"Each category has its own funding editor");
        Check(Enumerable.Range(0,6).All(i=>((TextBlock)Find("Balance:"+i)).Text.StartsWith('$') && !((TextBlock)Find("Balance:"+i)).Text.Contains('.')),"All six subsidy amounts display rounded whole dollars");
        bool fundingOpened=false;
        window.Dispatcher.BeginInvoke(new Action(()=>{var dialog=app.Windows.OfType<ManagementEditWindow>().Single(w=>w.Owner==window);fundingOpened=true;dialog.Close();}),DispatcherPriority.ApplicationIdle);
        ((Button)Find("Funding:4")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush(window);
        Check(fundingOpened&&data.HasUnsavedChanges==initialDirty,"Column funding button opens its editor and cancel leaves save unchanged");
        int original=data.Progression!.Level(1,95),target=original+3;
        var stage=(Button)Find($"Stage:95:{target}");stage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush(window);
        Check(data.HasUnsavedChanges==initialDirty,"Single-clicking a stage box does not modify the save");
        Double(stage);
        Check(data.Progression.Level(1,95)==target&&data.HasUnsavedChanges,"Double-clicking stage N completes every missing stage through N");
        Check(((SolidColorBrush)((Button)Find($"Stage:95:{target}")).Background).Color==Color.FromRgb(39,101,68),"Completed stage boxes turn green after staging");
        Double((ContentControl)Find("Project:95"),Find($"Stage:95:{target}"));
        Check(data.Progression.Level(1,95)==target,"A stage double-click cannot also trigger the parent row's next-stage action");
        Double((Button)Find($"Stage:95:{target}"));Check(data.Progression.Level(1,95)==target,"Double-clicking a completed stage does not replay it");
        Double((ContentControl)Find("Project:95"));
        Check(data.Progression.Level(1,95)==target+1,"Double-clicking a project row completes exactly the next stage");
        Shot("UI-Nation-Projects.png");
        Undo();Undo();Check(data.Progression.Level(1,95)==original&&data.HasUnsavedChanges==initialDirty,"Both project gestures undo back to the original working save");
        View(0,"Projects");Check(((TextBlock)window.FindName("TreasuryBalanceText")).Text.StartsWith("Union"),"Switching factions refreshes board and treasury together");
        var readOnlyProject=data.Progression.Projects.First(p=>p.Id==91);
        int blockedBefore=data.Progression.Level(0,91);Double((ContentControl)Find("Project:91"));
        Check(data.Progression.Level(0,91)==blockedBefore&&data.HasUnsavedChanges==initialDirty,"Restricted project cards cannot bypass completion safeguards");
        window.Width=1150;Flush(window);
        Check(((ScrollViewer)window.FindName("NationBoardViewport")).ScrollableWidth>0 && board.ActualWidth>=1470,"Narrow windows scroll all six readable category columns horizontally");
        Shot("UI-Nation-Narrow.png");window.Width=1700;Flush(window);
        View(1,"Policies");
        var policyDescription=data.Progression!.Policies.Single(p=>p.Id==112).Description.Replace("\\n","\n").Trim();
        Check(Descendants(Find("Policy:112")).OfType<TextBlock>().Any(t=>t.Text==policyDescription),"Policy card shows its in-game description");
        Check(board.ColumnDefinitions.Count==6&&Descendants(board).OfType<ProgressBar>().Any(),"Policies and acts use category columns with progress bars");
        var originalPolicies=data.Management!.Capture();
        Double((ContentControl)Find("Policy:112"));
        Check(data.Progression.Progress(1,112)<1&&data.Progression.Progress(1,112)>=0.999989,"Policy double-click stages near completion rather than a premature 100 percent");
        Check(Math.Abs(((ProgressBar)Find("Progress:112")).Value-99.999)<0.001,"Policy bar follows the saved 0–1 research fraction");
        Check(Descendants(Find("Policy:112")).OfType<TextBlock>().Any(t=>t.Text.Contains("99.999%")&&t.Text.Contains("Ready")),"Near-complete policy is explicitly labeled ready to finish");
        Shot("UI-Nation-Policies.png");
        Undo();var restored=data.Management.Capture();
        Check(originalPolicies.All(p=>restored[p.Key]==p.Value)&&data.HasUnsavedChanges==initialDirty,"Policy gesture and prerequisites undo without writing the real save");
    }
}
