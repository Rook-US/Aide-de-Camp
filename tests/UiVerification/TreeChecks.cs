using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AideDeCamp;
using AideDeCamp.Models;
using AideDeCamp.Services;

internal static partial class Program
{
    private static void TreeEvidence(string output, bool verify = false)
    {
        Directory.CreateDirectory(output);
        var window = new MainWindow(true) { Width = 1700, Height = 950 };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(window)!;
        object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var data = Field<GrandTacticianDataService>("_data");
        var groups = (Dictionary<int, GroupNode>)data.Groups;
        var units = (List<CombatUnitNode>)data.Units;
        groups.Add(1, new() { GroupId = 1, ParentId = -1, Name = "Army of the Cumberland", UnitTier = 16, IsExpanded = true, CommanderDisplayName = "MG William Rosecrans" });
        for (int c = 0; c < 6; c++) {
            groups.Add(10+c, new() { GroupId = 10+c, ParentId = 1, Name = $"{c+1} Corps", UnitTier = 15, IsExpanded = true, CommanderDisplayName = "MG Alexander McCook" });
            groups.Add(20+c, new() { GroupId = 20+c, ParentId = 10+c, Name = $"{c+1} Division", UnitTier = 14, IsExpanded = true, CommanderDisplayName = "BG Richard Johnson" });
            for (int u = 0; u < 3; u++) units.Add(new() { UnitId = 100+c*3+u, ParentId = 20+c, Name = $"{c*3+u+1} Ohio Volunteer Infantry", UnitType = 0, UnitTier = 13, TotalMenRaw = 1200, CommanderDisplayName = "COL William Gibson", WeaponName = "Springfield rifle", ContractMonths = 36 });
        }
        foreach (var group in groups.Values) group.CommandPath = data.BuildCommandPath(group.ParentId);
        foreach (var unit in units) unit.CommandPath = data.BuildCommandPath(unit.ParentId);
        units[0].CasualtyRatioRaw = 60;
        units[0].TransferTimeRaw = 9;
        Invoke("RebuildSide", true); Invoke("SetView", false);
        window.Show(); Flush(window);
        var ui = Field<UiSettingsService>("_ui"); ui.Reset(); Invoke("ApplyUiSettings"); Flush(window);
        typeof(MainWindow).GetField("_zoom", flags)!.SetValue(window, .8);
        Invoke("ApplyCanvasTransform"); Flush(window); Flush(window);
        var viewport = (FrameworkElement)window.FindName("OobViewport");
        typeof(MainWindow).GetField("_panX", flags)!.SetValue(window, 25 - groups[20].CanvasX * .8);
        typeof(MainWindow).GetField("_panY", flags)!.SetValue(window, 25 - groups[20].CanvasY * .8);
        Invoke("ApplyCanvasTransform"); Flush(window);
        void Capture(string name) {
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); png.Save(file);
        }
        Capture("tree-80");
        var pitch = groups[21].CanvasX - groups[20].CanvasX;
        File.WriteAllText(Path.Combine(output, "geometry.txt"), $"Division pitch: {pitch:0.0} world pixels; viewport: {viewport.ActualWidth:0}x{viewport.ActualHeight:0}; zoom: 0.8\n");
        typeof(MainWindow).GetField("_zoom", flags)!.SetValue(window, .55); Invoke("ApplyCanvasTransform"); Flush(window); Flush(window);
        typeof(MainWindow).GetField("_panX", flags)!.SetValue(window, 25 - groups[20].CanvasX * .55);
        typeof(MainWindow).GetField("_panY", flags)!.SetValue(window, 25 - groups[20].CanvasY * .55);
        Invoke("ApplyCanvasTransform"); Flush(window); Capture("tree-55");
        if (verify) {
            double Geometry(string name, OobNode n) => (double)Invoke(name, n)!;
            void Apply() { Invoke("ApplyUiSettings"); Flush(window); Flush(window); }
            void Zoom(double zoom) { typeof(MainWindow).GetField("_zoom", flags)!.SetValue(window, zoom); Invoke("ApplyCanvasTransform"); Flush(window); Flush(window); }
            var canvas = (Canvas)window.FindName("NodeCanvas");
            FrameworkElement Card(OobNode n) => canvas.Children.OfType<FrameworkElement>().Single(c => ReferenceEquals(c.Tag, n));
            void Layout(string label) {
                var bounds = canvas.Children.OfType<FrameworkElement>().Select(c => {
                    var n = (OobNode)c.Tag; var scale = ((ScaleTransform)c.LayoutTransform).ScaleY;
                    Check(Math.Abs(c.ActualHeight * scale - Geometry("GetNodeHeight", n)) < 1.1, label + " rendered height " + n.Name);
                    return new Rect(n.CanvasX, n.CanvasY, c.ActualWidth * scale, c.ActualHeight * scale);
                }).ToArray();
                Check(!bounds.Where((a, i) => bounds.Skip(i+1).Any(b => { var r = Rect.Intersect(a,b); return !r.IsEmpty && r.Width > .1 && r.Height > .1; })).Any(), label + " no rendered overlaps");
            }
            Check(pitch < 470, "Broad tree pitch improves on 0.8.10's 664 world pixels");
            Check(groups[20].CompactStrength.Contains("casualties") && groups[20].CompactAlerts.Contains("1 low strength") && groups[20].CompactAlerts.Contains("1 in transfer"), "Compact HQ includes subordinate strength and transfer alerts");
            Check(units[0].IdentitySecondary.Contains("1 Division"), "Compact membership identifies immediate parent");
            Zoom(.8); Layout("Compact broad tree");
            var essential = Descendants(Card(units[0])).OfType<TextBlock>().Single(t => t.Text == units[0].CompactStrength);
            Check(essential.IsVisible && essential.FontSize * .85 * .8 >= 12, "Compact strength has at least 12px text at practical 80% zoom");
            var compactHeight = Geometry("GetNodeHeight", units[0]);
            ui.Set("oob.zoom.detail", .7); Apply();
            Check(Geometry("GetNodeHeight", units[0]) > compactHeight + 50, "Threshold change expands real detail footprint at same zoom");
            Check(Descendants(Card(units[0])).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "Experience"), "Expanded detail includes experience");
            Layout("Detailed broad tree");
            typeof(MainWindow).GetField("_panX", flags)!.SetValue(window, 25-groups[20].CanvasX*.8);
            typeof(MainWindow).GetField("_panY", flags)!.SetValue(window, 25-groups[20].CanvasY*.8);
            Invoke("ApplyCanvasTransform"); Flush(window);
            Capture("tree-details-80");
            Zoom(.69); Check(Math.Abs(Geometry("GetNodeHeight", units[0])-compactHeight) < 1.1, "Zoom below configured threshold collapses extra footprint");
            Zoom(.7); Check(Geometry("GetNodeHeight", units[0]) > compactHeight + 50, "Exact configured threshold opens details");
            ui.Reset(); Apply();
            groups.Add(40, new() { GroupId=40, ParentId=-1, UnitTier=16, Name="Second independent army", IsExpanded=true });
            groups.Add(41, new() { GroupId=41, ParentId=40, UnitTier=14, Name="Western Division with a deliberately long formation designation", IsExpanded=true });
            groups.Add(42, new() { GroupId=42, ParentId=40, UnitTier=14, Name="Eastern Division", IsExpanded=true });
            groups.Add(50, new() { GroupId=50, ParentId=-1, UnitTier=16, Name="Third independent army", IsExpanded=true });
            for (int d=0; d<4; d++) groups.Add(60+d, new() { GroupId=60+d, ParentId=d==0?50:59+d, UnitTier=15-d, Name="Deep command " + d, IsExpanded=true });
            var attached = new CombatUnitNode { UnitId=600, ParentId=40, Name="Attached battery", UnitType=2, UnitTier=10, TotalMenRaw=120, GunCount=6 };
            var second = new CombatUnitNode { UnitId=601, ParentId=40, Name="Attached cavalry", UnitType=1, UnitTier=13, TotalMenRaw=500 };
            units.Add(attached); units.Add(second);
            Invoke("RebuildSide", true); Flush(window); Layout("Mixed roots and deep commands");
            double HqGap() => groups[42].CanvasX - groups[41].CanvasX - Geometry("GetNodeWidth", groups[41]);
            var hqGap = HqGap(); ui.Set("oob.spacing.commands", 4); Apply();
            Check(Math.Abs(hqGap-HqGap()-20) < .1, "HQ control reduces sibling edge gap by exactly 20px");
            var stackX = attached.CanvasX; ui.Set("oob.spacing.attached", 0); Apply();
            Check(Math.Abs(stackX-attached.CanvasX-8) < .1, "Combat column control reduces mixed boundary by its half-gap contribution");
            ui.Set("oob.spacing.stack", 0); Apply();
            var stack = new[] { attached, second }.OrderBy(n => n.CanvasY).ToArray();
            Check(Math.Abs(stack[1].CanvasY-stack[0].CanvasY-Geometry("GetNodeHeight", stack[0])) < .1, "Zero stack gap has no hidden 16px minimum");
            var rootDistance = groups[50].CanvasX-groups[40].CanvasX;
            ui.Set("oob.spacing.roots", 20); Apply();
            Check(Math.Abs(rootDistance-(groups[50].CanvasX-groups[40].CanvasX)-60) < .1, "Root edge gap responds exactly");
            var rankDistance = groups[41].CanvasY-groups[40].CanvasY;
            ui.Set("oob.spacing.army", 8); Apply();
            Check(Math.Abs(rankDistance-(groups[41].CanvasY-groups[40].CanvasY)-40) < .1, "Army row gap responds exactly");
            foreach (var spec in UiSettingsService.Parameters.Where(p => p.Key.StartsWith("oob.spacing."))) ui.Set(spec.Key, 0);
            Apply(); Layout("Zero gaps");
            Check(Math.Abs(HqGap()) < .1, "Zero horizontal gaps touch actual edges without overlapping");
            ui.Reset(); Apply();
            typeof(MainWindow).GetField("_selectedNode", flags)!.SetValue(window, groups[41]);
            Invoke("Nudge_Click", new Button { Tag="20,10" }, new RoutedEventArgs()); Flush(window);
            var nudges = Field<Dictionary<OobNode,Point>>("_nudges");
            Check(nudges[groups[41]] == new Point(20,10), "Nudge still applies manual offsets");
            var session = Field<EditSession>("_editSession");
            var originalName = groups[41].Name;
            session.Execute("Rename command", Array.Empty<CombatUnitNode>(), new[] { groups[41] }, () => groups[41].Name="Renamed command");
            Invoke("UndoWorkingEdit"); Flush(window);
            Check(groups[41].Name==originalName && nudges[groups[41]]==new Point(20,10), "Hierarchy edit undo preserves presentation nudges");
            Invoke("RedoWorkingEdit"); Flush(window);
            Check(groups[41].Name=="Renamed command" && nudges[groups[41]]==new Point(20,10), "Hierarchy edit redo preserves presentation nudges");
            foreach (var scale in new[] { .75, 1.5 }) {
                ui.Set("oob.cards.textScale", scale); ui.Set("oob.cards.scale", scale); Apply();
                var auto = Field<Dictionary<OobNode,Point>>("_automaticPositions")[groups[41]];
                Check(Math.Abs(groups[41].CanvasX-auto.X-20)<.1 && Math.Abs(groups[41].CanvasY-auto.Y-10)<.1, "Font/card reflow preserves nudge delta " + scale);
            }
            Zoom(1.1); Zoom(.8);
            Check(nudges[groups[41]] == new Point(20,10), "Detail transitions preserve manual offsets");
            var metadata = new DisplayMetadataService(); var metadataPath=Path.Combine(output,"metadata"); Directory.CreateDirectory(metadataPath);
            metadata.Save(metadataPath, new Dictionary<string,DisplayMetadataService.Entry> { ["G:41"] = new(0,20,10) }, "tree-fixture");
            var loaded = metadata.Load(metadataPath,"tree-fixture")["G:41"];
            Check(loaded.X==20 && loaded.Y==10, "Nudge metadata survives save/reload");
            nudges.Clear(); ui.Reset(); Apply();
            var anchor = Field<System.Collections.ObjectModel.ObservableCollection<OobNode>>("_visibleCanvasNodes").OrderBy(n =>
                Math.Pow(n.CanvasX*.8+Field<double>("_panX")-viewport.ActualWidth/2,2)+Math.Pow(n.CanvasY*.8+Field<double>("_panY")-viewport.ActualHeight/2,2)).First();
            var anchorScreen = new Point(anchor.CanvasX*.8+Field<double>("_panX"),anchor.CanvasY*.8+Field<double>("_panY"));
            ui.Set("oob.cards.textScale",1.4); Apply();
            Check(Math.Abs(anchorScreen.X-anchor.CanvasX*.8-Field<double>("_panX"))<1 && Math.Abs(anchorScreen.Y-anchor.CanvasY*.8-Field<double>("_panY"))<1, "Font reflow preserves the viewport anchor");
            ui.Reset(); Apply();
            foreach (var width in new[] { 1150d, 1700d }) {
                window.Width=width; Flush(window); Layout("Viewport " + width);
            }
            ui.Set("oob.spacing.commands", 17); ui.Set("oob.zoom.detail", 1.2); ui.Save();
            var reloaded = new UiSettingsService();
            Check(reloaded.Get("oob.spacing.commands")==17 && reloaded.Get("oob.zoom.detail")==1.2, "Gap and threshold settings survive save/reload");
            ui.Reset(); ui.Save(); Apply();
        }
        typeof(MainWindow).GetField("_allowClose", flags)!.SetValue(window, true); window.Close();
    }
}
