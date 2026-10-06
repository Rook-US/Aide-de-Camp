using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AideDeCamp;
using AideDeCamp.Models;
using AideDeCamp.Services;

internal static partial class Program
{
    private static void TreePerformance(string output)
    {
        Directory.CreateDirectory(output);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var window = new MainWindow(true);
        object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var data = (GrandTacticianDataService)typeof(MainWindow).GetField("_data", flags)!.GetValue(window)!;
        var groups = (Dictionary<int, GroupNode>)data.Groups;
        var units = (List<CombatUnitNode>)data.Units;
        groups.Add(1, new() { GroupId=1, ParentId=-1, UnitTier=16, Name="Benchmark Army", IsExpanded=true });
        for (int d=0; d<30; d++) {
            groups.Add(10+d, new() { GroupId=10+d, ParentId=1, UnitTier=14, Name=$"Division {d+1}", CommanderDisplayName="BG Test Commander", IsExpanded=true });
            for (int u=0; u<10; u++) units.Add(new() { UnitId=100+d*10+u, ParentId=10+d, Name=$"{d*10+u+1} Volunteer Infantry", UnitTier=13, UnitType=0, TotalMenRaw=1000+u, CommanderDisplayName="COL Test Officer", WeaponName="Springfield rifle", ContractMonths=36, CommandPath=$"Benchmark Army › Division {d+1}" });
        }
        groups.Add(900, new() { GroupId=900, ParentId=-1, Nation=1, UnitTier=16, Name="Other faction army", IsExpanded=true });
        units.Add(new() { UnitId=901, ParentId=900, Nation=1, UnitTier=13, TotalMenRaw=1500, Name="Other faction unit" });
        var ui=(UiSettingsService)typeof(MainWindow).GetField("_ui",flags)!.GetValue(window)!;
        ui.Reset();
        typeof(MainWindow).GetField("_zoom",flags)!.SetValue(window,.8);
        Invoke("RebuildSide",true); Invoke("SetView",false); window.Show(); Flush(window); Flush(window);
        void Idle() {
            var frame = new DispatcherFrame();
            window.Dispatcher.BeginInvoke(new Action(() => frame.Continue=false), DispatcherPriority.ApplicationIdle);
            Dispatcher.PushFrame(frame);
            Flush(window);
        }
        var prepareMethod=typeof(MainWindow).GetMethod("PrepareSaveCardCache",flags);
        if (prepareMethod is not null) {
            var preparation=(Task)prepareMethod.Invoke(window,null)!;
            var loadingFrame = new DispatcherFrame();
            preparation.ContinueWith(_ => window.Dispatcher.BeginInvoke(new Action(() => loadingFrame.Continue=false)));
            Dispatcher.PushFrame(loadingFrame); preparation.GetAwaiter().GetResult();
        }
        Idle();
        var canvas=(Canvas)window.FindName("NodeCanvas");
        var original=canvas.Children.Cast<FrameworkElement>().ToArray();
        bool Positioned() => original.All(c => {
            var p=c.TransformToAncestor(canvas).Transform(new Point());
            return Math.Abs(p.X-Canvas.GetLeft(c))<1 && Math.Abs(p.Y-Canvas.GetTop(c))<1;
        });
        if (prepareMethod is not null) Check(Positioned(), "Load preparation preserves actual rendered card positions");
        var rows=new List<string> { "transition,milliseconds,allocated_bytes,reused_cards" };
        for (int i=0;i<8;i++) {
            var allocated=GC.GetAllocatedBytesForCurrentThread();
            var timer=Stopwatch.StartNew();
            typeof(MainWindow).GetField("_zoom",flags)!.SetValue(window,i%2==0?1.0:.8);
            Invoke("ApplyCanvasTransform"); Flush(window); Idle();
            timer.Stop();
            rows.Add($"{(i%2==0?"expand":"collapse")},{timer.Elapsed.TotalMilliseconds:F1},{GC.GetAllocatedBytesForCurrentThread()-allocated},{canvas.Children.Cast<FrameworkElement>().Count(original.Contains)}");
        }
        File.WriteAllLines(Path.Combine(output,"transitions.csv"),rows);
        Console.WriteLine(string.Join(Environment.NewLine,rows));
        if (prepareMethod is not null) {
            System.Collections.IDictionary Cache(string name) => (System.Collections.IDictionary)typeof(MainWindow).GetField(name,flags)!.GetValue(window)!;
            Check(Cache("_cardVisuals").Count==333, "Load preparation includes both factions, even off-screen cards");
            Check(Cache("_modeFootprints").Count>=666, "Both compact and detailed footprints prepared during loading");
            Check(canvas.Children.Cast<FrameworkElement>().SequenceEqual(original), "Every zoom transition retains all 331 card instances");
            Check(Positioned(), "Cached transitions preserve rendered card and connector coordinates");
            var unrelatedNotifications=0;
            groups[39].PropertyChanged += (_,e) => { if(e.PropertyName=="CardMetrics") unrelatedNotifications++; };
            var historyCount=Cache("_modeFootprints").Count;
            var session=(EditSession)typeof(MainWindow).GetField("_editSession",flags)!.GetValue(window)!;
            session.Execute("Single transfer edit",new[]{units[0]},()=>units[0].TransferDays=8);
            Invoke("RefreshSummaries",true,new[]{units[0]}); Flush(window); Idle();
            Check(canvas.Children.Cast<FrameworkElement>().SequenceEqual(original), "Individual edit retains changed and unchanged card controls");
            Check(unrelatedNotifications==0, "Individual edit does not refresh unrelated command summaries");
            Check(Cache("_modeFootprints").Count>historyCount, "Edited measurements retain earlier footprint versions for undo");
            Check(Descendants(original.Single(c=>ReferenceEquals(c.Tag,units[0]))).OfType<TextBlock>().Any(t=>t.Text.Contains("Transfer: 8d")), "Retained card displays the changed transfer alert");
            Invoke("UndoWorkingEdit"); Flush(window); Idle();
            Check(units[0].TransferDays==0 && canvas.Children.Cast<FrameworkElement>().SequenceEqual(original), "Undo restores data using retained cards");
            Invoke("RedoWorkingEdit"); Flush(window); Idle();
            Check(units[0].TransferDays==8 && canvas.Children.Cast<FrameworkElement>().SequenceEqual(original), "Redo updates retained cards");
            var batch=units.Take(10).ToArray();
            session.Execute("Batch strength edit",batch,()=> { foreach(var u in batch) u.FieldStrength=750; });
            Invoke("RefreshSummaries",true,batch); Flush(window); Idle();
            Invoke("UndoWorkingEdit"); Flush(window); Idle();
            Check(batch.All(u=>u.FieldStrength>=1000) && canvas.Children.Cast<FrameworkElement>().SequenceEqual(original), "Batch undo retains reusable cards and previous data");
            Invoke("InvalidateCardMeasurements");
            Check(Cache("_cardVisuals").Count==0 && Cache("_modeFootprints").Count==0 && Cache("_preparedDetailCards").Count==0 && canvas.Children.Count==0, "New-save invalidation drops visuals, footprints and prepared data");
            data.Dispose(); groups.Add(1,new() { GroupId=1, ParentId=-1, UnitTier=16, Name="New save army", IsExpanded=true });
            Invoke("RebuildSide",true); Flush(window); Idle();
            Check(canvas.Children.Count==1 && !original.Contains((FrameworkElement)canvas.Children[0]), "New save cannot reuse old cards with matching IDs");
            typeof(MainWindow).GetField("_allowClose",flags)!.SetValue(window,true); window.Close();
            Check(Cache("_cardVisuals").Count==0 && Cache("_modeFootprints").Count==0, "Closing releases the save card cache");
            return;
        }
        typeof(MainWindow).GetField("_allowClose",flags)!.SetValue(window,true); window.Close();
    }
}
