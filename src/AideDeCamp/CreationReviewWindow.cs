using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AideDeCamp;

public sealed class CreationReviewWindow : Window
{
    public CreationReviewWindow(string summary, IReadOnlyList<string> confirmations)
    {
        Title = "Review creation"; Width = 700; Height = 740; MinWidth = 500; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(20, 28, 36)); Foreground = Brushes.Gainsboro;
        var root = new DockPanel { Margin = new(22) }; Content = root;
        var heading = new TextBlock { Text = "Review your new formation", FontSize = 22, Margin = new(0, 0, 0, 14) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var back = new Button { Content = "Back to editing", IsCancel = true, Margin = new(5), Padding = new(14, 7, 14, 7) };
        var create = new Button { Content = confirmations.Count > 0 ? "Confirm & Create" : "Create", Margin = new(5), Padding = new(14, 7, 14, 7) };
        create.Click += (_, _) => DialogResult = true; buttons.Children.Add(back); buttons.Children.Add(create);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 12, 15) });
        if (confirmations.Count > 0) body.Children.Add(new TextBlock { Text = "Confirm these mapped but untested choices", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Khaki, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) });
        foreach (var warning in confirmations) body.Children.Add(new TextBlock { Text = "• " + warning, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 12, 12) });
        body.Children.Add(new TextBlock { Text = "This stages one undoable creation. Save changes writes it to the campaign with a full backup.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 12, 10) });
    }
}
