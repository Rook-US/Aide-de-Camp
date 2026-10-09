using System.Diagnostics;
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
    private static void CheckCreationUi(string save, string config)
    {
        using var data = new GrandTacticianDataService(); data.LoadAsync(save, config).GetAwaiter().GetResult();
        var timer = Stopwatch.StartNew(); var catalog = CreateUnitWindow.LoadCatalog(data); timer.Stop();
        Check(catalog.Towns.All(t => GrandTacticianDataService.IsCreationStateId(t.StateId) && t.Location.Owner is 0 or 1), "Town picker excludes Canada, the Bahamas and foreign-owned locations using mapped IDs");
        Console.WriteLine($"Creation catalog: {timer.ElapsedMilliseconds} ms");
        var context = data.Groups.Values.First(g => g.Nation == 0 && g.UnitTier == 14 && g.ParentId >= 0);
        var w = new CreateUnitWindow(data, new NamingSchemeService(), catalog, 0, context); w.Show(); Flush(w);
        T Field<T>(string name) => (T)typeof(CreateUnitWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
        int Id(ComboBox c) => c.SelectedItem is null ? -999 : (int)c.SelectedItem.GetType().GetProperty("Id")!.GetValue(c.SelectedItem)!;
        void Select(ComboBox c, int id) => c.SelectedItem = c.Items.Cast<object>().Single(x => (int)x.GetType().GetProperty("Id")!.GetValue(x)! == id);
        Check(Id(Field<ComboBox>("_faction")) == 0 && Id(Field<ComboBox>("_parent")) == context.GroupId, "Create defaults to active faction and clicked parent HQ");
        Check(Field<StackPanel>("_location").Visibility == Visibility.Collapsed, "Combat units cannot choose independent towns");
        Check(Field<TextBox>("_size").Text == catalog.Maximums[0].ToString(), "New combat unit defaults to configured maximum");
        Check(Field<ComboBox>("_weapon").Items.Count == catalog.Weapons.Count(w => w.UnitType == 0), "Infantry weapon list is filtered");
        ComboBox PerkChoice() => ((Grid)Field<StackPanel>("_perkPanel").Children[0]).Children.OfType<ComboBox>().Single();
        Check(PerkChoice().Items.Count == 25, "Guided infantry offers its eight mapped perks at three levels");
        Field<CheckBox>("_advanced").IsChecked = true;
        Check(Field<ComboBox>("_weapon").Items.Count == catalog.Weapons.Count, "Advanced mode can browse all weapons");
        Check(PerkChoice().Items.Count == 58, "Advanced mode exposes all nineteen combat perks at three levels");
        Select(PerkChoice(), 8 * 3 + 1);
        Field<CheckBox>("_advanced").IsChecked = false;
        Check(Id(PerkChoice()) == 25 && PerkChoice().SelectedItem!.ToString()!.Contains("outside branch list"), "Returning to guided mode preserves and labels an off-branch perk");
        timer.Restart();
        for (int i = 0; i < 25; i++) { Select(Field<ComboBox>("_kind"), i % 3); }
        timer.Stop(); Console.WriteLine($"25 branch/filter changes: {timer.ElapsedMilliseconds} ms");
        Check(timer.ElapsedMilliseconds < 2000, "In-memory choice filtering stays responsive");
        Select(Field<ComboBox>("_kind"), -1); Flush(w);
        Check(Field<StackPanel>("_location").Visibility == Visibility.Visible && !Field<StackPanel>("_combat").IsEnabled, "Independent HQ uses town placement and disables combat-only fields");
        Check(!Field<Slider>("_experience").IsEnabled && Field<StackPanel>("_perkPanel").Children.Count == 8, "HQ exposes four distinct perk slots and no combat experience field");
        Field<TextBox>("_search").Text = "phil";
        Check(Field<ComboBox>("_town").Items.Count == 1 && Field<ComboBox>("_town").Items[0].ToString()!.Contains("Philadelphia"), "Town search suggests the verified Philadelphia entry");
        Select(Field<ComboBox>("_state"), 27);
        Check(Field<ComboBox>("_town").Items.Count == 0, "State filter excludes a town in a different state");
        Field<TextBox>("_search").Text = ""; Select(Field<ComboBox>("_state"), -1);
        Select(Field<ComboBox>("_faction"), 1);
        Check(Id(Field<ComboBox>("_faction")) == 1 && Field<ComboBox>("_uniform").Items.Count > 0, "Faction switch resolves its own uniform styles");
        Select(Field<ComboBox>("_faction"), 0); Flush(w);
        var bitmap = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(w);
        var output = Path.GetFullPath("artifacts/create-unit-window.png"); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var file = File.Create(output)) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file); }
        Console.WriteLine("Create screenshot: " + output); w.Close();
        // Exercise both modal windows: selecting values must reach the service as a
        // canonical HQ request, without stale disabled combat fields.
        w = new CreateUnitWindow(data, new NamingSchemeService(), catalog, 0, null);
        bool reviewSeen = false;
        var reviewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        int ticks = 0;
        reviewTimer.Tick += (_, _) => {
            var review = Application.Current.Windows.OfType<CreationReviewWindow>().FirstOrDefault();
            if (review is not null) { reviewSeen = true; reviewTimer.Stop(); review.DialogResult = true; }
            else if (++ticks > 100) { reviewTimer.Stop(); w.Close(); }
        };
        w.Loaded += (_, _) => {
            Field<TextBox>("_name").Text = "ADC UI Creation Check";
            Field<ComboBox>("_commander").SelectedIndex = 1;
            Field<TextBox>("_search").Text = "Philadelphia";
            Field<ComboBox>("_town").SelectedIndex = 0;
            reviewTimer.Start();
            w.Dispatcher.BeginInvoke(new Action(() => typeof(CreateUnitWindow).GetMethod("ReviewCreate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, new object[] {w, new RoutedEventArgs()})));
        };
        bool? accepted = w.ShowDialog(); reviewTimer.Stop();
        Check(accepted == true && reviewSeen && w.Confirmed && w.Request is not null, "Create popup reaches explicit review and returns the confirmed request");
        Check(w.Request!.Blueprint.WeaponId == -1 && w.Request.Blueprint.HomeStateId == -1 && w.Request.Blueprint.Strength == 0, "HQ request excludes disabled combat values");
        var edits = new EditSession();
        var created = data.CreateUnit(w.Request, true, edits);
        Check(data.Groups.Values.Any(g => ReferenceEquals(g, created)), "Confirmed UI request creates the HQ through the production service without writing the source save");
    }
}
