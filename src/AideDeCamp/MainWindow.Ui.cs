using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;

public partial class MainWindow
{
    private UiSettingsWindow? _settingsWindow;
    private bool _applyingUiSettings;
    private void UiSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new UiSettingsWindow(_ui, ApplyUiSettings) { Owner = this };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }
    private void ApplyUiSettings()
    {
        _applyingUiSettings = true;
        try {
            OobPresentation.RegimentalScale = Ui("oob.presentation.regimentalScale") >= .5;
            RegimentalScaleCheck.IsChecked = OobPresentation.RegimentalScale;
        } finally { _applyingUiSettings = false; }
        FontSize = 12 * Ui("theme.text.scale");
        RosterGrid.RowHeight = double.NaN;
        RosterGrid.MinRowHeight = Ui("roster.density.rowHeight");
        ManagementGrid.RowHeight = double.NaN;
        ManagementGrid.MinRowHeight = Ui("roster.density.rowHeight");
        RosterRow.HierarchyIndent = Ui("roster.hierarchy.indent");
        InvalidateCardMeasurements();
        ReflowCardsAtAnchor(); RefreshRoster();
        if (_ui.LoadWarning is not null) StatusText.Text = _ui.LoadWarning;
    }
    private static double NodeTierScale(OobNode node) => CardLayoutGeometry.TierScale(node is GroupNode, node is GroupNode group ? group.UnitTier : 13);
    private double NodeCardScale(OobNode node) => CardScale * NodeTierScale(node);
    private double CardFootprintWidth(bool group)
    {
        var width = (group ? 350 : 330) * Math.Max(1, Ui("oob.cards.textScale"));
        width = Math.Max(width, (group ? 102 : 92) * Ui("oob.natoCounters.scale") + Math.Abs(Ui("oob.natoCounters.x")) * 2 + Ui("oob.cards.padding") * 2);
        return width * CardScale;
    }
    private bool _cardReflowQueued;
    private int _cardWarmupGeneration;
    private System.Windows.Threading.DispatcherOperation? _cardWarmupOperation;
    private readonly Dictionary<FrameworkElement, string> _preparedDetailCards = new();
    private bool _preparingCardDetails;

    private void ScheduleCardDetailWarmup()
    {
        var generation = ++_cardWarmupGeneration;
        _cardWarmupOperation?.Abort(); _cardWarmupOperation = null;
        if (ShowCardDetails || _allowClose) return;
        // WPF defers creating metric rows while their parent is Collapsed. Prepare one
        // retained card per idle callback, allowing input/rendering between cards.
        // Restore compact visibility before returning; no extra content is displayed.
        var pending = new Queue<FrameworkElement>(NodeCanvas.Children.OfType<FrameworkElement>());
        void WarmNext()
        {
            if (generation != _cardWarmupGeneration || _allowClose || ShowCardDetails) return;
            if (!pending.TryDequeue(out var card)) return;
            if (NodeCanvas.Children.Contains(card)) PrepareCardDetails(card);
            if (pending.Count > 0) _cardWarmupOperation = Dispatcher.BeginInvoke(new Action(WarmNext), System.Windows.Threading.DispatcherPriority.Background);
        }
        _cardWarmupOperation = Dispatcher.BeginInvoke(new Action(WarmNext), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void MonitorRenderedCard(FrameworkElement card)
    {
        // Generated ItemsControl content can finish sizing after the initial measure.
        // A real rendered size always wins over an earlier estimate.
        card.SizeChanged += (_, _) => {
            if (_preparingCardDetails) return;
            if (card.Tag is not OobNode node || !NodeCanvas.Children.Contains(card)) return;
            var height = Math.Ceiling(card.ActualHeight * NodeCardScale(node));
            if (!double.IsFinite(height) || height <= 0) return;
            var surface = FindNamedDescendant<Border>(card, "CardSurface");
            var inset = surface is null ? 0 : surface.TransformToAncestor(card).Transform(new Point()).Y * NodeCardScale(node);
            if (Math.Abs(height - GetNodeHeight(node)) < 1 && Math.Abs(inset - GetSurfaceInset(node)) < 1) return;
            _nodeMeasuredHeights[node] = height;
            _nodeSurfaceInsets[node] = inset;
            if (_nodeMeasureSignatures.TryGetValue(node, out var signature))
                _modeFootprints[(node, ShowCardDetails, signature)] = (height, inset);
            if (_cardReflowQueued) return;
            _cardReflowQueued = true;
            Dispatcher.BeginInvoke(new Action(() => {
                _cardReflowQueued = false;
                if (!IsLoaded || _allowClose) return;
                WithViewportAnchor(() => {
                    LayoutDisplayModel();
                    RebuildConnectorVisuals();
                    ApplyCanvasTransform();
                });
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }
    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in VisualDescendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private void ConfigureCard(FrameworkElement element, OobNode node)
    {
        // Scale the measured template, including padding, as one layout footprint.
        var scale = NodeCardScale(node);
        element.Width = GetNodeWidth(node) / scale;
        element.LayoutTransform = new ScaleTransform(scale, scale);
        var cardSurface = FindNamedDescendant<Border>(element, "CardSurface");
        if (cardSurface is not null)
        {
            cardSurface.Padding = new Thickness(Ui("oob.cards.padding"));
            if (cardSurface.Background is Brush b) { var clone = b.CloneCurrentValue(); clone.Opacity = Ui("oob.cards.opacity"); cardSurface.Background = clone; }
        }
        var details = FindNamedDescendant<FrameworkElement>(element, "DetailBody");
        if (details is not null) details.Visibility = ShowCardDetails ? Visibility.Visible : Visibility.Collapsed;
        var textScale = Ui("oob.cards.textScale");
        element.Resources["MetricFontSize"] = 14.0 * textScale;
        element.Resources["MetricRowMargin"] = new Thickness(0, Ui("oob.cards.spacing"), 0, 0);
        foreach (var text in VisualDescendants(element).OfType<TextBlock>())
        {
            if (Equals(text.Tag, "Metric")) continue; // Generated rows use the shared metric resources.
            text.FontSize *= textScale;
            text.TextWrapping = TextWrapping.Wrap;
            var m = text.Margin;
            text.Margin = new Thickness(m.Left, m.Top, m.Right, m.Bottom + Ui("oob.cards.spacing"));
        }
        element.Resources["MetricStripeBrush"] = Ui("oob.cards.shadedRows") >= .5
            ? new SolidColorBrush(Color.FromArgb(24, 150, 175, 195)) : Brushes.Transparent;
        var detail = FindNamedDescendant<Grid>(element, "DetailBody");
        if (detail is not null && Ui("oob.cards.shadedRows") >= .5)
        {
            foreach (var grid in VisualDescendants(detail).OfType<Grid>().ToList())
                for (int i = 1; i < grid.RowDefinitions.Count; i += 2)
                {
                    var stripe = new Border { Background = new SolidColorBrush(Color.FromArgb(24, 150, 175, 195)), IsHitTestVisible = false };
                    Grid.SetRow(stripe, i); Grid.SetColumnSpan(stripe, Math.Max(1, grid.ColumnDefinitions.Count)); Panel.SetZIndex(stripe, -1); grid.Children.Add(stripe);
                }
        }
        var nato = FindNamedDescendant<NatoCounterControl>(element, "NatoCounterHost");
        if (nato is null) return;
        nato.EchelonScale = Ui("oob.natoCounters.echelonScale");
        nato.ShowHqStaff = Ui("oob.natoCounters.hqStaff") >= .5;
        var factor = Ui("oob.natoCounters.scale");
        nato.Width *= factor; nato.Height = (nato.Height + 10 * (nato.EchelonScale - 1)) * factor;
        // The counter keeps the same scale at every detail level. Its Auto grid row
        // reserves space in every measured footprint, so scale changes never crowd the
        // card's title/metrics or make adjacent nodes collide.
        nato.Margin = new Thickness(0, Math.Max(0, Ui("oob.natoCounters.y")), 0, Math.Max(0, -Ui("oob.natoCounters.y")) - 9);
        nato.RenderTransform = new TranslateTransform(Ui("oob.natoCounters.x"), 0);
    }
}
