using System.Windows;
using System.Windows.Controls;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;

public sealed class NamingFiltersWindow : Window
{
    public NamingFiltersWindow(NamingRule rule, GrandTacticianDataService data)
    {
        Title = "Naming scope and preservation filters"; Width = 720; Height = 780;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(16) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void Label(string text) => panel.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0,9,0,4), TextWrapping = TextWrapping.Wrap });
        Label("Faction (Current uses the faction active when the naming window opened)");
        var faction = new ComboBox { ItemsSource = Enum.GetValues<NamingFaction>(), SelectedItem = rule.Faction }; panel.Children.Add(faction);
        Label("Command scope"); var scope = new ComboBox { ItemsSource = new[] { "Land", "Field", "Garrison" }, SelectedItem = rule.CommandScope }; panel.Children.Add(scope);
        var boxes = new Dictionary<string, TextBox>();
        foreach (var (key, label) in new[] { ("WeaponInclude", "Weapon include IDs"), ("WeaponExclude", "Weapon exclude IDs"), ("StateInclude", "Home State include IDs"), ("StateExclude", "Home State exclude IDs"), ("NameContains", "Name contains any phrase"), ("NameExcludes", "Preserve names containing any phrase (e.g. Sharpshooter, Zouave, Ranger, Guard)") })
        {
            Label(label + " — comma separated; blank means no filter");
            var box = new TextBox { Text = (string)typeof(NamingRule).GetProperty(key)!.GetValue(rule)! }; boxes[key] = box; panel.Children.Add(box);
        }
        Label("Choose a weapon by name to add its stable ID");
        var weapons = new ComboBox { ItemsSource = data.WeaponOptions, DisplayMemberPath = "Name" }; panel.Children.Add(weapons);
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        void Add(string label, Action action) { var b = new Button { Content = label }; b.Click += (_, _) => action(); buttons.Children.Add(b); }
        void Pick(string key) { if (weapons.SelectedItem is WeaponOption w) boxes[key].Text = string.Join(", ", boxes[key].Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Append(w.Id.ToString()).Distinct()); }
        Add("Include weapon", () => Pick("WeaponInclude")); Add("Exclude weapon", () => Pick("WeaponExclude"));
        Label("Home State reference");
        panel.Children.Add(new TextBox { Text = string.Join("\n", data.StateOptions.Select(s => $"{s.Id}: {s.Name}")), IsReadOnly = true, Height = 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Label("Excluded units retain their names and reserve recognizable numbers. Naval and unclassified assets are always skipped. Preview and Apply use this same scope.");
        var save = new Button { Content = "Use filters", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,14,0,0) }; panel.Children.Add(save);
        save.Click += (_, _) =>
        {
            foreach (var key in new[] { "WeaponInclude", "WeaponExclude", "StateInclude", "StateExclude" })
                if (boxes[key].Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(s => !int.TryParse(s, out _)))
                { MessageBox.Show(this, "ID lists must contain whole numbers separated by commas.", "Invalid filter"); return; }
            foreach (var pair in boxes) typeof(NamingRule).GetProperty(pair.Key)!.SetValue(rule, pair.Value.Text);
            rule.Faction = (NamingFaction)faction.SelectedItem; rule.CommandScope = scope.SelectedItem?.ToString() ?? "Land";
            DialogResult = true;
        };
    }
}
