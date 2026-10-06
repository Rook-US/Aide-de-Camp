using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AideDeCamp;
using AideDeCamp.Models;
using AideDeCamp.Services;
using System.IO;

internal static partial class Program
{
    private static int checks;
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); checks++; }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent) {
        yield return parent;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static void Flush(Window window) {
        window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout();
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Aide-de-Camp;component/Themes/Dark.xaml", UriKind.Relative) });
            if (args.Length == 2 && args[0] == "--tree-evidence") { TreeEvidence(args[1]); return 0; }
            if (args.Length == 2 && args[0] == "--tree-checks") { TreeEvidence(args[1], true); Console.WriteLine($"ALL {checks} TREE CHECKS PASSED"); return 0; }
            if (args.Length == 2 && args[0] == "--tree-performance") { TreePerformance(args[1]); return 0; }
            // No installation detection and no access to user save files.
            var window = new MainWindow(true);
            var data = (GrandTacticianDataService)typeof(MainWindow).GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var hq = new GroupNode { GroupId = 1, Name = "HQ display fixture", Nation = 0, ParentId = -1, UnitTier = 16, CommanderDisplayName = "MG Test Commander", IsExpanded = true };
            ((Dictionary<int, GroupNode>)data.Groups).Add(1, hq);
            var units = (List<CombatUnitNode>)data.Units;
            for (int i = 0; i < 150; i++) units.Add(new CombatUnitNode { UnitId = i + 1, EditorOrder = i, Name = $"{i + 1}th Ohio Infantry", Nation = 0, ParentId = 1, UnitType = 0, UnitTier = 13,
                TotalMenRaw = 1000 + i, HomeStateName = "Ohio", StateId = 29, CommanderDisplayName = "BG Test Officer", WeaponName = "Test musket", ContractMonths = 12 });
            typeof(MainWindow).GetMethod("RebuildSide", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { true });
            var rosterPanel = (UIElement)window.FindName("RosterViewPanel"); rosterPanel.Visibility = Visibility.Visible;
            ((UIElement)window.FindName("OobViewPanel")).Visibility = Visibility.Collapsed;
            var grid = (DataGrid)window.FindName("RosterGrid");
            window.Show(); Flush(window);
            var row = grid.Items.OfType<RosterRow>().First(r => r.Unit == units[0]);
            grid.ScrollIntoView(row); grid.SelectedItem = row; Flush(window);
            foreach (var key in new[] { "Name", "FieldStrength", "Casualties", "HomeStateName", "Experience" }) {
                var column = grid.Columns.Single(c => c.SortMemberPath == key);
                grid.ScrollIntoView(row, column); Flush(window);
                var content = column.GetCellContent(row)!;
                var text = Descendants(content).OfType<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text));
                Check(text.ActualHeight >= text.FontSize && text.ActualWidth > 0, key + " text has measurable, unclipped row height");
                Check(text.Foreground is SolidColorBrush brush && brush.Color.R > 150, key + " has readable light foreground");
                grid.CurrentCell = new DataGridCellInfo(row, column); grid.BeginEdit(); Flush(window);
                Check(Descendants(column.GetCellContent(row)!).Any(d => d is TextBox or ComboBox), key + " enters typed edit mode");
                grid.CommitEdit(DataGridEditingUnit.Cell, true); Flush(window);
            }
            var weaponColumn = grid.Columns.Single(c => c.SortMemberPath == "WeaponName");
            grid.ScrollIntoView(row, weaponColumn); Flush(window);
            var weaponHost = Descendants(weaponColumn.GetCellContent(row)!).OfType<RosterCellHost>().First();
            weaponHost.Field = ""; Flush(window);
            Check(Descendants(weaponHost).OfType<TextBlock>().Any(t => t.Text == units[0].WeaponName), "Empty template field resolves through owning weapon column without crash or blank text");
            weaponHost.Field = "Name"; Flush(window);
            Check(Descendants(weaponHost).OfType<TextBlock>().Any(t => t.Text == units[0].WeaponName), "Owning column wins over stale recycled template identity");
            weaponHost.ClearValue(RosterCellHost.FieldProperty); Flush(window);
            var createCell = typeof(MainWindow).GetMethod("CreateRosterCell", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Check(createCell.Invoke(window, new object[] { row, "", true }) is Border, "Direct empty-key editor request is safely rejected");
            Check(createCell.Invoke(window, new object[] { row, "UnknownField", false }) is Border, "Direct unknown-key display request is safely rejected");
            var quickWeapon = typeof(MainWindow).GetMethod("QuickAssignWeapon", BindingFlags.Instance | BindingFlags.NonPublic)!;
            quickWeapon.Invoke(window, new object[] { units[0], new WeaponOption(900, "Replacement musket", 0) }); Flush(window);
            Check(units[0].WeaponId == 900 && units[0].WeaponName == "Replacement musket", "Roster menu weapon action survives roster rebuild");
            row = grid.Items.OfType<RosterRow>().First(r => r.Unit == units[0]);
            grid.ScrollIntoView(row, weaponColumn); Flush(window);
            Check(Descendants(weaponColumn.GetCellContent(row)!).OfType<TextBlock>().Any(t => t.Text == "Replacement musket"), "Weapon cell shows assigned weapon after menu action");
            var last = grid.Items.OfType<RosterRow>().Last(r => r.Unit is not null);
            grid.ScrollIntoView(last, grid.Columns[0]); Flush(window);
            var lastName = string.Join(" ", Descendants(grid.Columns[0].GetCellContent(last)!).OfType<TextBlock>().Select(t => t.Text));
            Check(lastName.Contains(last.Unit!.Name), "Virtualized cell displays its current row after scrolling: expected " + last.Unit.Name + "; rendered " + lastName);
            grid.ScrollIntoView(row, grid.Columns[0]); Flush(window);
            Check(string.Join(" ", Descendants(grid.Columns[0].GetCellContent(row)!).OfType<TextBlock>().Select(t => t.Text)).Contains("1th Ohio"), "Scroll-back restores original row identity");
            var hqRow = grid.Items.OfType<RosterRow>().Single(r => r.Group == hq);
            Check(hqRow.FieldStrengthText == hq.Metrics.Assigned.ToString("N0"), "HQ roster and card share assigned totals");
            grid.SelectedItem = hqRow; Flush(window);
            Check(Descendants((DependencyObject)window.FindName("SharedDetailPanel")).OfType<TextBox>().Any(t => t.Text == hq.Name), "HQ selection opens its name editor");
            foreach (var key in new[] { "GroupNodeTemplate", "CombatUnitTemplate" }) {
                var card = (FrameworkElement)((DataTemplate)window.FindResource(key)).LoadContent();
                card.DataContext = key == "GroupNodeTemplate" ? hq : units[0];
                card.Measure(new Size(470, double.PositiveInfinity)); card.Arrange(new Rect(card.DesiredSize)); card.UpdateLayout();
                var identity = Descendants(card).OfType<FrameworkElement>().Single(e => e.Name == "AlwaysIdentity");
                var detail = Descendants(card).OfType<FrameworkElement>().Single(e => e.Name == "DetailBody");
                detail.Opacity = 0;
                var text = Descendants(identity).OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)).ToList();
                Check(text.Count >= 3 && identity.Opacity == 1, key + " retains name, commander, state/type when detail is hidden");
            }
            var groups = (Dictionary<int, GroupNode>)data.Groups;
            var corps = new GroupNode { GroupId = 2, Name = "Corps fixture", Nation = 0, ParentId = 1, UnitTier = 15, IsExpanded = true };
            var division = new GroupNode { GroupId = 3, Name = "Division fixture", Nation = 0, ParentId = 2, UnitTier = 14, IsExpanded = true };
            groups.Add(2, corps); groups.Add(3, division);
            groups.Add(4, new GroupNode { GroupId = 4, Name = "Second Corps", Nation = 0, ParentId = 1, UnitTier = 15, IsExpanded = true });
            groups.Add(5, new GroupNode { GroupId = 5, Name = "Second Division", Nation = 0, ParentId = 4, UnitTier = 14, IsExpanded = true });
            for (int i = 0; i < 12; i++) units.Add(new CombatUnitNode { UnitId = 200 + i, Name = "Stack fixture " + i, Nation = 0,
                ParentId = i < 4 ? 2 : i < 8 ? 3 : 5, UnitType = 0, UnitTier = 13, TotalMenRaw = 1000 });
            typeof(MainWindow).GetMethod("RebuildSide", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { true });
            rosterPanel.Visibility = Visibility.Collapsed; ((UIElement)window.FindName("OobViewPanel")).Visibility = Visibility.Visible; Flush(window);
            double Geometry(string name, OobNode node) => (double)typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { node })!;
            Check(Geometry("GetNodeWidth", hq) > Geometry("GetNodeWidth", corps) && Geometry("GetNodeWidth", corps) > Geometry("GetNodeWidth", division) && Geometry("GetNodeWidth", division) > Geometry("GetNodeWidth", units[0]), "Measured native tiers have descending widths");
            Check(Math.Abs(corps.CanvasY + Geometry("GetSurfaceInset", corps) - units[0].CanvasY - Geometry("GetSurfaceInset", units[0])) < .1, "Immediate child HQ and combat card surfaces align despite different counter sizes");
            var canvas = (Canvas)window.FindName("NodeCanvas");
            foreach (var node in new OobNode[] { hq, corps, division, units[0] }) {
                var visual = canvas.Children.OfType<FrameworkElement>().Single(e => ReferenceEquals(e.Tag, node));
                var surface = Descendants(visual).OfType<FrameworkElement>().Single(e => e.Name == "CardSurface");
                var localInset = surface.TransformToAncestor(visual).Transform(new Point(0, 0)).Y * ((ScaleTransform)visual.LayoutTransform).ScaleY;
                Check(Math.Abs(localInset - Geometry("GetSurfaceInset", node)) < 1, node.Name + " rendered card inset matches layout measurement");
                Check(Math.Abs(visual.DesiredSize.Width - Geometry("GetNodeWidth", node)) < 1, node.Name + " rendered width matches hit-test footprint");
            }
            var laidOut = groups.Values.Cast<OobNode>().Concat(units).ToArray();
            bool overlapping = false;
            for (int i = 0; i < laidOut.Length; i++)
                for (int j = i + 1; j < laidOut.Length; j++) {
                    var a = laidOut[i]; var b = laidOut[j];
                    overlapping |= Math.Min(a.CanvasX + Geometry("GetNodeWidth", a), b.CanvasX + Geometry("GetNodeWidth", b)) - Math.Max(a.CanvasX, b.CanvasX) > .1
                        && Math.Min(a.CanvasY + Geometry("GetNodeHeight", a), b.CanvasY + Geometry("GetNodeHeight", b)) - Math.Max(a.CanvasY, b.CanvasY) > .1;
                }
            Check(!overlapping, "Mixed nested commands and long combat stacks have no intersecting card footprints");
            // Compare actual rendered bounds, not just the same cached rectangles
            // that the layout itself consumes. This catches underestimated templates.
            void CheckRenderedLayout(string label) {
                Flush(window); Flush(window);
                var visibleCards = canvas.Children.OfType<FrameworkElement>().Where(c => c.Tag is OobNode).ToArray();
                var bounds = visibleCards.Select(c => {
                    var node = (OobNode)c.Tag;
                    var scale = ((ScaleTransform)c.LayoutTransform).ScaleY;
                    Check(Math.Abs(c.ActualHeight * scale - Geometry("GetNodeHeight", node)) <= 1.1, label + " measured height matches rendered " + node.Name);
                    return new Rect(node.CanvasX, node.CanvasY, c.ActualWidth * scale, c.ActualHeight * scale);
                }).ToArray();
                bool intersects = false;
                for (int i = 0; i < bounds.Length; i++) for (int j = i + 1; j < bounds.Length; j++) {
                    var overlap = Rect.Intersect(bounds[i], bounds[j]);
                    intersects |= !overlap.IsEmpty && overlap.Width > .1 && overlap.Height > .1;
                }
                Check(!intersects, label + " actual rendered cards do not overlap");
                Check(units[1].CanvasY - units[0].CanvasY - Geometry("GetNodeHeight", units[0]) >= 31, label + " combat stack retains visible default gap");
            }
            var ui = (UiSettingsService)typeof(MainWindow).GetField("_ui", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            ui.Reset();
            var applySettings = typeof(MainWindow).GetMethod("ApplyUiSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;
            applySettings.Invoke(window, null); CheckRenderedLayout("Default settings");
            ui.Set("oob.cards.textScale", 4); ui.Set("oob.natoCounters.scale", 6); ui.Set("oob.cards.scale", 4);
            applySettings.Invoke(window, null); CheckRenderedLayout("Large text/counters/cards");
            ui.Reset(); applySettings.Invoke(window, null); CheckRenderedLayout("Defaults restored");
            object? Invoke(string method,params object[] arguments)=>typeof(MainWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,arguments);
            T Field<T>(string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            Invoke("OpenWorkspace",0,"Armies");Invoke("SetView",true);Flush(window);
            Invoke("RestoreSelection",new HashSet<int>{1,2,3});Invoke("RefreshRoster");Flush(window);
            Check(Field<HashSet<CombatUnitNode>>("_selectedUnits").Select(u=>u.UnitId).Order().SequenceEqual(new[]{1,2,3}),"Roster rebuild preserves all selected unit identities");
            Field<Dictionary<string,HashSet<int>>>("_previousBatches")["0:Armies"]=new(){1,2,3};
            Invoke("OpenWorkspace",1,"Armies");Flush(window);
            Check(Field<HashSet<CombatUnitNode>>("_selectedUnits").Count==0,"Faction change does not leak Union batch selection");
            Invoke("OpenWorkspace",0,"Armies");Flush(window);
            Check(Field<HashSet<CombatUnitNode>>("_selectedUnits").Count==3 && Field<bool>("_showRoster"),"Returning to faction restores selection and roster mode");
            Invoke("ClearSelection_Click",window,new RoutedEventArgs());Invoke("RecallBatch_Click",window,new RoutedEventArgs());
            Check(Field<HashSet<CombatUnitNode>>("_selectedUnits").Count==3,"Recall batch restores last batch after clearing");
            Invoke("OpenWorkspace",0,"Garrisons");Invoke("FieldRoster_Click",window,new RoutedEventArgs());
            Check(Field<CommandCategory>("_category")==CommandCategory.Garrison,"Garrisons roster toggle retains its workspace");
            foreach(var workspace in new[]{"Navy","Officers","Weapons","Economy"}) {
                Invoke("OpenWorkspace",1,workspace);Flush(window);
                Check(((UIElement)window.FindName("ManagementWorkspace")).Visibility==Visibility.Visible && ((UIElement)window.FindName("LocalViews")).Visibility==Visibility.Collapsed,workspace+" uses a single management roster");
            }
            Invoke("UnknownRoster_Click",window,new RoutedEventArgs());
            Check(Field<CommandCategory>("_category")==CommandCategory.Unknown && rosterPanel.Visibility==Visibility.Visible,"Unclassified records remain accessible through Review data");
            var batch=new BatchEditWindow(units.Take(3).ToArray(),Array.Empty<WeaponOption>(),new BatchEditPlanner(new EditValidationService()));
            T BatchField<T>(string name)=>(T)typeof(BatchEditWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(batch)!;
            BatchField<TextBox>("_experienceBox").Text="37.5";BatchField<TextBox>("_contractBox").Text="24";
            Check(BatchField<CheckBox>("_experienceEnabled").IsChecked==true && BatchField<CheckBox>("_contractEnabled").IsChecked==true && batch.Plan is not null && batch.Plan.Units.All(p=>p.Experience==37.5 && p.ContractMonths==24),"Batch dialog selects and plans changed fields automatically");
            BatchField<TextBox>("_contractBox").Text="12";
            Check(BatchField<CheckBox>("_contractEnabled").IsChecked==false,"Restoring original batch value clears the field selection");
            batch.Close();
            Invoke("OpenWorkspace",0,"Armies");Invoke("SetView",true);
            var fixedActions=(StackPanel)window.FindName("FixedActions");
            Check(fixedActions.Children.OfType<ContentControl>().Select(c=>c.Content?.ToString()).SequenceEqual(new[]{"Naming Scheme","Regimental scale","UI settings","Review data","Save changes"}),"Fixed actions follow the requested order");
            var search=(TextBox)window.FindName("SearchBox");var editSelected=(Button)window.FindName("EditSelectedButton");var batchButton=(Button)window.FindName("BatchEditButton");
            Check(search.TransformToAncestor(window).Transform(new Point()).X<editSelected.TransformToAncestor(window).Transform(new Point()).X && editSelected.TransformToAncestor(window).Transform(new Point()).X<batchButton.TransformToAncestor(window).Transform(new Point()).X,"Search and Edit Selected keep their shared left toolbar position");
            foreach(var fontScale in new[]{.65,1,2}) foreach(var width in new[]{1150d,1700d}) {
                ui.Set("theme.text.scale",fontScale);applySettings.Invoke(window,null);
                window.Width=width;Flush(window);
                var save=(Button)window.FindName("SaveButton");var game=(Button)window.FindName("GameFolderButton");
                var bounds=save.TransformToAncestor(window).TransformBounds(new Rect(save.RenderSize));
                var left=game.TransformToAncestor(window).TransformBounds(new Rect(game.RenderSize));
                Check(bounds.Right<=window.ActualWidth && window.ActualWidth-bounds.Right<35,"Save stays at upper right at width "+width);
                Check(fixedActions.TransformToAncestor(window).Transform(new Point()).X>left.Right+95,"Fixed actions do not overlap left controls at width "+width+" / text scale "+fontScale);
            }
            ui.Reset();applySettings.Invoke(window,null);Flush(window);
            var screenshot = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            screenshot.Render(window);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(screenshot));
            using (var file = File.Create("UI-verification.png")) png.Save(file);
            if(args.Length>0) {
                var managementWindow=new MainWindow(true){Width=1700};
                var realData=(GrandTacticianDataService)typeof(MainWindow).GetField("_data",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(managementWindow)!;
                realData.LoadAsync(args[0],args.Length>1?args[1]:null).GetAwaiter().GetResult();
                managementWindow.Show();
                foreach(var workspace in new[]{"Officers","Economy","Navy","Weapons"}) {
                    typeof(MainWindow).GetMethod("OpenWorkspace",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(managementWindow,new object[]{1,workspace});Flush(managementWindow);
                    var managementGrid=(DataGrid)managementWindow.FindName("ManagementGrid");
                    Check(managementGrid.Items.Count>0 && managementGrid.IsReadOnly,workspace+" shows real save records read-only");
                    Check(((Button)managementWindow.FindName("EditSelectedButton")).Visibility==Visibility.Visible,workspace+" shows the shared record editor action");
                    Check(managementGrid.Columns.Count>0 && managementGrid.ActualHeight>300,workspace+" has visible generated columns and a usable roster");
                    if(workspace=="Economy") {
                        var shownIds=managementGrid.Items.Cast<object>().Select(r=>(int)r.GetType().GetProperty("Id")!.GetValue(r)!).ToArray();
                        var states=ManagementSnapshot.Read(realData.SaveDirectory!,new Dictionary<int,string>(),realData.Groups.ToDictionary(g=>g.Key,g=>g.Value.Nation)).States;
                        Check(shownIds.Length>0 && shownIds.All(id=>states.Single(s=>s.Id==id && s.Side==1).IsVolunteerEditorState),"Economy roster contains only active U.S. recruiting states/territories");
                        Check(((TextBlock)managementWindow.FindName("TreasuryBalanceText")).Text.Contains("Confederacy national treasury balance") && ((Button)managementWindow.FindName("EditTreasuryButton")).IsEnabled,"Economy displays the selected faction's editable treasury balance");
                    }
                    if(workspace is "Officers" or "Weapons" or "Economy" or "Navy") {
                        var item=managementGrid.Items[0];managementGrid.SelectedItem=item;managementGrid.ScrollIntoView(item);Flush(managementWindow);
                        Check(((Button)managementWindow.FindName("EditSelectedButton")).IsEnabled,workspace+" enables Edit Selected for a selected record");
                        var rowElement=(DataGridRow)managementGrid.ItemContainerGenerator.ContainerFromItem(item);
                        bool dirtyBefore=realData.HasUnsavedChanges;
                        bool opened=false;
                        managementWindow.Dispatcher.BeginInvoke(new Action(()=>{
                            var dialog=app.Windows.Cast<Window>().FirstOrDefault(w=>w.Owner==managementWindow);
                            opened=workspace=="Economy"?dialog is StatePopulationWindow:dialog is ManagementEditWindow;
                            dialog?.Close();
                        }),DispatcherPriority.ApplicationIdle);
                        var click=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=rowElement};
                        managementGrid.RaiseEvent(click);Flush(managementWindow);
                        Check(opened && realData.HasUnsavedChanges==dirtyBefore,workspace+" row double-click opens its editor; cancel leaves the save unchanged");
                    }
                    var capture=new RenderTargetBitmap((int)managementWindow.ActualWidth,(int)managementWindow.ActualHeight,96,96,PixelFormats.Pbgra32);capture.Render(managementWindow);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(capture));using var file=File.Create("UI-"+workspace+".png");encoder.Save(file);
                }
                var officer=realData.Management!.Records.First(r=>r.Domain=="Officers" && r.Side==1);
                CheckNationBoards(managementWindow,realData,app);
                CheckManagementBatches(managementWindow,realData,app);
                typeof(MainWindow).GetMethod("NationView_Click",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(managementWindow,new object[]{new Button{Tag="States"},new RoutedEventArgs()});Flush(managementWindow);
                var edit=new ManagementEditWindow(realData.Management,officer,officer.Name){Owner=managementWindow};
                edit.Dispatcher.BeginInvoke(new Action(()=>{
                    Flush(edit);
                    var check=Descendants(edit).OfType<CheckBox>().Single(c=>c.Content is TextBlock t && t.Text.StartsWith("Experience"));
                    check.IsChecked=true;
                    ((Grid)check.Parent).Children.OfType<TextBox>().Single().Text="37.5";
                    Descendants(edit).OfType<Button>().Single(b=>b.Content?.ToString()=="Stage changes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }),DispatcherPriority.ApplicationIdle);
                Check(edit.ShowDialog()==true && edit.Changes!.Single().Value=="37.5" && !realData.Management.HasChanges,"Officer dialog stages checked fields without mutating the document before Apply");
                var stateId=realData.Management.Records.First(r=>r.Domain=="Economy").Id;
                var stateEdit=new StatePopulationWindow(realData,new[]{stateId},1){Owner=managementWindow};
                stateEdit.Dispatcher.BeginInvoke(new Action(()=>{
                    Flush(stateEdit);
                    Descendants(stateEdit).OfType<Button>().Single(b=>b.Content?.ToString()=="Preview").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var stage=Descendants(stateEdit).OfType<Button>().Single(b=>b.Content?.ToString()=="Stage previewed population changes");
                    Check(stage.IsEnabled && Descendants(stateEdit).OfType<DataGrid>().Single().Items.Count==1,"Population dialog creates a cross-faction preview before enabling staging");
                    stage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }),DispatcherPriority.ApplicationIdle);
                Check(stateEdit.ShowDialog()==true && stateEdit.Changes!.Single().Key.Key=="Population" && !realData.Management.HasChanges,"Population dialog stages only its previewed population field");
                typeof(MainWindow).GetField("_allowClose",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(managementWindow,true);
                managementWindow.Close();
            }
            Console.WriteLine($"ALL {checks} WINDOWS UI CHECKS PASSED");
            typeof(MainWindow).GetField("_allowClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close(); app.Shutdown(); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
