using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;

public sealed class SupplyStockEditWindow : Window
{
    private static readonly string[] Categories =
        ["Small-arms ammunition", "Artillery ammunition", "Provisions", "Forage"];

    public SupplyStockEditPlan? Plan { get; private set; }
    public bool RawValues { get; private set; }

    public SupplyStockEditWindow(IReadOnlyList<CombatUnitNode> units, bool rawValues)
    {
        RawValues = rawValues;
        Title = $"Edit supply stock — {units.Count:N0} unit(s)";
        Width = 650; Height = 490; MinWidth = 550; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(16) };
        Content = root;
        var footer = new StackPanel();
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        footer.Children.Add(summary);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(actions);
        actions.Children.Add(new Button { Content = "Cancel", IsCancel = true });
        var commit = new Button { Content = "Stage supply changes", IsEnabled = false };
        actions.Children.Add(commit);

        var body = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(new TextBlock {
            Text = "Amounts are stored separately for each unit. Enter a value for any category you want to change; leave the others blank. Infantry does not use artillery ammunition or forage. Artillery does not use small-arms ammunition. Changes stage as one undoable edit; Save changes writes them with a backup.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });
        var rawToggle = new CheckBox { Content = "Enter exact saved values instead of percentages", IsChecked = rawValues,
            Margin = new Thickness(0, 0, 0, 12) };
        body.Children.Add(rawToggle);
        var fields = new Dictionary<int, TextBox>();
        var currentLabels = new Dictionary<int, TextBlock>();
        for (var slot = 0; slot < 4; slot++)
        {
            var relevant = units.Where(u => SupplyStockEditPlan.IsRelevant(u.UnitType, slot)).ToArray();
            var group = new GroupBox { Header = Categories[slot], Margin = new Thickness(0, 0, 0, 8),
                IsEnabled = relevant.Length > 0 };
            var stack = new StackPanel { Margin = new Thickness(8) };
            group.Content = stack; body.Children.Add(group);
            var current = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            stack.Children.Add(current); currentLabels[slot] = current;
            var box = new TextBox { MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Stretch };
            stack.Children.Add(box); fields[slot] = box;
            box.TextChanged += (_, _) => Preview();
        }
        void UpdateCurrent()
        {
            foreach (var slot in fields.Keys)
            {
                var relevant = units.Where(u => SupplyStockEditPlan.IsRelevant(u.UnitType, slot)).ToArray();
                if (relevant.Length == 0) { currentLabels[slot].Text = "Not used by the selected unit types."; continue; }
                var values = relevant.Select(u => RawValues ? u.SupplyStockAt(slot)!.Value :
                    u.FieldStrength > 0 ? 100 * u.SupplyStockAt(slot)!.Value / u.FieldStrength : double.NaN)
                    .Where(double.IsFinite).ToArray();
                string Display(double value) => value.ToString(RawValues ? "R" : "0.####", CultureInfo.InvariantCulture);
                var range = values.Length == 0 ? "unavailable" : values.Length == 1 || values.Min() == values.Max()
                    ? Display(values[0])
                    : $"{Display(values.Min())}–{Display(values.Max())}";
                currentLabels[slot].Text = $"{relevant.Length:N0} applicable unit(s) • current {range}{(RawValues ? " saved units" : "% of active strength")}";
            }
        }
        void Preview()
        {
            Plan = SupplyStockEditPlan.Build(units, fields.ToDictionary(p => p.Key, p => p.Value.Text), RawValues);
            summary.Text = Plan.Errors.Count > 0 ? string.Join("\n", Plan.Errors.Take(4)) :
                $"{Plan.Changes.Count:N0} stock amount(s) will change across {Plan.Changes.Select(c => c.Unit).Distinct().Count():N0} unit(s).";
            commit.IsEnabled = Plan.CanApply;
        }
        rawToggle.Checked += (_, _) => ChangeMode(true);
        rawToggle.Unchecked += (_, _) => ChangeMode(false);
        void ChangeMode(bool raw)
        {
            RawValues = raw;
            foreach (var box in fields.Values) box.Clear();
            UpdateCurrent(); Preview();
        }
        commit.Click += (_, _) => { Preview(); if (Plan?.CanApply == true) DialogResult = true; };
        UpdateCurrent(); Preview();
    }
}
