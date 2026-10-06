using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;

public partial class MainWindow
{
    private readonly Dictionary<GroupNode, TypedField> _groupNameDrafts = new();
    private TypedField GroupNameDraft(GroupNode group)
    {
        if (!_groupNameDrafts.TryGetValue(group, out var field)) {
            field = new TypedField("Name", "HQ name", group.Name);
            field.Changed = () => { field.SetResult(_validation.ValidateName(field.Text)); UpdateDirtyState(); };
            _groupNameDrafts.Add(group, field);
        }
        field.NormalBackground = group.Name != group.RawName ? "#4A3B22" : "#162833";
        field.Sync(group.Name);
        field.SetResult(_validation.ValidateName(field.Text));
        return field;
    }
    private TextBox GroupNameEditor(GroupNode group)
    {
        var box = new TextBox { DataContext = GroupNameDraft(group), BorderThickness = new Thickness(2) };
        box.SetBinding(TextBox.TextProperty, new Binding("Text") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        box.SetBinding(BackgroundProperty, new Binding("Background"));
        box.SetBinding(BorderBrushProperty, new Binding("Border"));
        box.SetBinding(ToolTipProperty, new Binding("Help"));
        box.SetResourceReference(ForegroundProperty, "TextBrush");
        return box;
    }
    private void ShowGroup(GroupNode group)
    {
        _selectedNode = group; _selectedUnit = null;
        NoSelectionPanel.Visibility = Visibility.Collapsed; InspectionText.Visibility = Visibility.Collapsed;
        SharedDetailPanel.Visibility = Visibility.Visible; SharedDetailPanel.Children.Clear();
        SharedDetailPanel.Children.Add(new TextBlock { Text = group.Name, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        SharedDetailPanel.Children.Add(new TextBlock { Text = group.IdentityCommander, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
        SharedDetailPanel.Children.Add(new TextBlock { Text = group.IdentitySecondary, TextWrapping = TextWrapping.Wrap });
        if (!group.IsLandCommand) {
            SharedDetailPanel.Children.Add(new TextBlock { Text = "Inspection only — naval/unknown headquarters mapping pending.", TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var metric in group.CardMetrics)
            SharedDetailPanel.Children.Add(new TextBlock { Text = metric.Label + ": " + metric.Value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        var transfer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4),
            ToolTip = "Adjust all subordinate combat-unit transfers: ±1 day; Ctrl ±5; Shift ±10; Ctrl+Shift ±20." };
        transfer.Children.Add(new TextBlock { Text = "Transfer ETA: ", VerticalAlignment = VerticalAlignment.Center });
        foreach (var delta in new[] { -1, 1 }) {
            var button = new Button { Content = delta < 0 ? "−" : "+", Tag = delta.ToString(), DataContext = group };
            button.Click += GroupTransferAdjust_Click; transfer.Children.Add(button);
        }
        SharedDetailPanel.Children.Add(transfer);
        SharedDetailPanel.Children.Add(new TextBlock { Text = "HQ name", Margin = new Thickness(0, 12, 0, 3) });
        SharedDetailPanel.Children.Add(GroupNameEditor(group));
        SharedDetailPanel.Children.Add(new TextBlock { Text = "HQ experience and Home State: save fields not yet verified. Editing is unavailable; subordinate-unit experience is not an HQ experience value.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        var apply = new Button { Content = "Apply HQ changes" };
        apply.Click += (_, _) => { if (CommitTypedDrafts()) { RebuildSide(); ShowGroup(group); } };
        SharedDetailPanel.Children.Add(apply);
        var discard = new Button { Content = "Discard HQ name draft" };
        discard.Click += (_, _) => { GroupNameDraft(group).Accept(group.Name); UpdateDirtyState(); };
        SharedDetailPanel.Children.Add(discard);
    }
}
