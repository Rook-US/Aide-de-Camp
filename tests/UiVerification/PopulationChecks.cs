using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AideDeCamp;
using AideDeCamp.Services;

internal static partial class Program
{
    private static void CheckPopulationRoster(MainWindow window,GrandTacticianDataService data,Application app)
    {
        object? Invoke(string method,params object[] args)=>typeof(MainWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
        object Value(object row,string key)=>row.GetType().GetProperty(key)!.GetValue(row)!;
        Invoke("OpenWorkspace",1,"Economy");
        Invoke("NationView_Click",new Button{Tag="States"},new RoutedEventArgs());Flush(window);
        var grid=(DataGrid)window.FindName("ManagementGrid");
        var originals=ManagementSnapshot.Read(data.SaveDirectory!,new Dictionary<int,string>(),data.Groups.ToDictionary(g=>g.Key,g=>g.Value.Nation),data.Management!.OriginalLines).States;
        var selected=originals.Where(s=>s.Side==1&&s.IsVolunteerEditorState&&s.Capacity>100).Take(2).ToArray();
        Check(selected.Length==2,"Population regression has two estimable recruitment states");
        grid.UnselectAll();grid.UnselectAllCells();
        foreach(var state in selected)grid.SelectedItems.Add(grid.Items.Cast<object>().Single(r=>(int)Value(r,"Id")==state.Id));
        var projected=new Dictionary<int,(string Available,string Deficit)>();
        Exception? callbackError=null;
        window.Dispatcher.BeginInvoke(new Action(()=>{
            try {
            var dialog=app.Windows.OfType<StatePopulationWindow>().Single(w=>w.Owner==window);Flush(dialog);
            Descendants(dialog).OfType<TextBox>().Single(t=>t.Name=="PopulationAmount").Text="50";
            Descendants(dialog).OfType<Button>().Single(b=>b.Content?.ToString()=="Preview").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var preview=Descendants(dialog).OfType<DataGrid>().Single();
            Check(preview.Items.Count==2,"Edit Selected previews both population changes");
            for(int i=0;i<selected.Length;i++)projected[selected[i].Id]=((string)Value(preview.Items[i],"ConfederacyAfter")!,(string)Value(preview.Items[i],"ConfederacyDeficitAfter")!);
            Descendants(dialog).OfType<Button>().Single(b=>b.Content?.ToString()=="Stage previewed population changes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        } catch(Exception ex) { callbackError=ex;Console.WriteLine(ex);foreach(var open in app.Windows.OfType<StatePopulationWindow>().ToArray())open.Close(); }
        }),DispatcherPriority.ApplicationIdle);
        Invoke("EditManagement_Click",window,new RoutedEventArgs());Flush(window);
        if(callbackError is not null)throw callbackError;
        foreach(var state in selected){
            var row=grid.Items.Cast<object>().Single(r=>(int)Value(r,"Id")==state.Id);
            Check((string)Value(row,"PoolBasis")! == "Estimated from population" && ((long)Value(row,"AvailableVolunteers")!).ToString("N0")==projected[state.Id].Available && ((long)Value(row,"VolunteerDeficit")!).ToString("N0")==projected[state.Id].Deficit,"Committed roster agrees with available-volunteer and deficit preview");
            Check((int)Value(row,"SavedDeficit")! == state.Deficit && (int)Value(row,"AlreadyRecruited")! == state.Recruited,"Population estimates preserve saved deficit and recruited counters");
        }
        Invoke("OpenWorkspace",0,"Economy");Flush(window);
        Check(grid.Items.Cast<object>().Where(r=>selected.Any(s=>s.Id==(int)Value(r,"Id"))).All(r=>(string)Value(r,"PoolBasis")! == (originals.Single(s=>s.Id==(int)Value(r,"Id")&&s.Side==0).Capacity>0?"Estimated from population":"Not estimable")),"Shared population updates the other faction's roster estimates or identifies missing capacity");
        Invoke("UndoWorkingEdit");Invoke("OpenWorkspace",1,"Economy");Flush(window);
        Check(grid.Items.Cast<object>().Where(r=>selected.Any(s=>s.Id==(int)Value(r,"Id"))).All(r=>(string)Value(r,"PoolBasis")! == "Saved"),"Undo restores saved recruitment balances");
        Invoke("RedoWorkingEdit");Flush(window);
        Check(grid.Items.Cast<object>().Where(r=>selected.Any(s=>s.Id==(int)Value(r,"Id"))).All(r=>(string)Value(r,"PoolBasis")! == "Estimated from population"),"Redo restores recruitment estimates");
        Invoke("UndoWorkingEdit");Flush(window);
    }
}
