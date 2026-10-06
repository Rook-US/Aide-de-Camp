using System.Windows;
using System.Windows.Controls;
using AideDeCamp.Models;

namespace AideDeCamp;

public partial class MainWindow
{
    // One retained visual per node/template for this loaded save. Old footprint
    // signatures remain available for undo; bindings always display current data.
    private readonly Dictionary<(OobNode Node, string Template), FrameworkElement> _cardVisuals = new();
    private int _cardCacheGeneration;
    private bool _cardCacheClosed;

    private FrameworkElement CachedCard(OobNode node)
    {
        var template = node is GroupNode g ? (g.IsLandCommand ? "GroupNodeTemplate" : "InspectionTemplate")
            : node is CombatUnitNode u && u.IsLandAsset ? "CombatUnitTemplate" : "InspectionTemplate";
        if (_cardVisuals.TryGetValue((node, template), out var cached)) return cached;
        var card = (FrameworkElement)((DataTemplate)FindResource(template)).LoadContent();
        card.DataContext = node; card.Tag = node;
        ConfigureCard(card, node);
        Panel.SetZIndex(card, 10);
        MonitorRenderedCard(card);
        _cardVisuals[(node, template)] = card;
        return card;
    }

    private static void SetCardDetailVisibility(FrameworkElement card, bool expanded)
    {
        var detail = FindNamedDescendant<FrameworkElement>(card, "DetailBody");
        if (detail is not null) detail.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PrepareCardDetails(FrameworkElement card)
    {
        if (card.Tag is not OobNode node) return;
        var signature = CardMeasureSignature(node, true);
        // Changed contents reuse their card but must prepare new detail rows.
        if (_preparedDetailCards.TryGetValue(card, out var prepared) && prepared == signature) return;
        var detached = card.Parent is null;
        if (detached) CardMeasureHost.Children.Add(card);
        var available = new Size(GetNodeWidth(node), double.PositiveInfinity);
        var detail = FindNamedDescendant<FrameworkElement>(card, "DetailBody");
        var wasVisible = detail?.Visibility ?? Visibility.Visible;
        _preparingCardDetails = true;
        try {
            foreach (var expanded in new[] { true, false }) {
                SetCardDetailVisibility(card, expanded);
                detail?.InvalidateMeasure(); card.InvalidateMeasure();
                card.Measure(available);
                card.Arrange(new Rect(new Size(GetNodeWidth(node), card.DesiredSize.Height)));
                card.UpdateLayout();
                var surface = FindNamedDescendant<Border>(card, "CardSurface");
                var inset = surface is null ? 0 : surface.TransformToAncestor(card).Transform(new Point()).Y * NodeCardScale(node);
                _modeFootprints[(node, expanded, CardMeasureSignature(node, expanded))] = (Math.Ceiling(card.DesiredSize.Height), inset);
            }
            _preparedDetailCards[card] = signature;
        } finally {
            if (detail is not null) { detail.Visibility = wasVisible; detail.InvalidateMeasure(); }
            card.InvalidateMeasure(); card.Measure(available); card.UpdateLayout();
            _preparingCardDetails = false;
            if (detached) CardMeasureHost.Children.Remove(card);
        }
    }

    private async Task PrepareSaveCardCache()
    {
        // Do this during the loading phase, including cards outside the current
        // faction, viewport, or collapsed branch. Yield between cards for responsiveness.
        var generation = _cardCacheGeneration;
        foreach (var node in _data.Groups.Values.Cast<OobNode>().Concat(_data.Units).ToArray()) {
            if (_allowClose || _cardCacheClosed || generation != _cardCacheGeneration) return;
            await Dispatcher.InvokeAsync(() => {
                if (!_cardCacheClosed && generation == _cardCacheGeneration) PrepareCardDetails(CachedCard(node));
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
