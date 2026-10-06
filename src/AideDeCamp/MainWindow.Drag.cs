using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;

public partial class MainWindow
{
    private sealed record UnitDrag(IReadOnlyList<CombatUnitNode> Units);
    private sealed record GroupDrag(GroupNode Group);
    private UnitDrag? _activeDrag;
    private DisplayOobModel? _beforeDrag;
    private GroupNode? _dropParent;
    private int _dropIndex = -1;
    private Border? _dragGhost;
    private readonly Dictionary<OobNode, Point> _nudges = new();
    private readonly Dictionary<OobNode, Point> _automaticPositions = new();
    private DateTime _lastEdgePan;
    private GroupNode? _hoverGroup;
    private DateTime _hoverSince;
    private readonly Dictionary<GroupNode, bool> _dragExpansions = new();
    private bool _dragCommitted;
    private GroupNode? _activeGroupDrag;

    private IEnumerable<OobNode> OrderedChildren(GroupNode group) => group.Children
        .OrderBy(n => n is GroupNode ? 0 : 1)
        .ThenBy(n => n is CombatUnitNode u ? u.EditorOrder : ((GroupNode)n).EditorOrder)
        .ThenByDescending(n => n is GroupNode g ? g.UnitTier : ((CombatUnitNode)n).UnitTier)
        .ThenBy(n => n is CombatUnitNode u ? u.UnitType : 0)
        .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase);
    private void ApplyDisplayOrder()
    {
        foreach (var c in EnumerateDisplayCommands())
        {
            var ordered = c.AttachedUnits.OrderBy(u => u.EditorOrder).ThenByDescending(u => u.UnitTier).ThenBy(u => u.UnitType).ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();
            c.AttachedUnits.Clear(); c.AttachedUnits.AddRange(ordered);
        }
    }
    private static DisplayOobModel CloneDisplay(DisplayOobModel source)
    {
        var result = new DisplayOobModel();
        DisplayCommandNode Clone(DisplayCommandNode c)
        {
            var copy = new DisplayCommandNode { Source = c.Source, Depth = c.Depth };
            copy.AttachedUnits.AddRange(c.AttachedUnits); result.CommandsById[c.Source.GroupId] = copy;
            copy.Subcommands.AddRange(c.Subcommands.Select(Clone)); return copy;
        }
        result.Roots.AddRange(source.Roots.Select(Clone)); return result;
    }
    private void BeginUnitDrag(DependencyObject source, CombatUnitNode unit)
    {
        var block = _selectedUnits.Contains(unit) ? _visibleCanvasNodes.OfType<CombatUnitNode>().Where(_selectedUnits.Contains).ToList() : new List<CombatUnitNode> { unit };
        if (block.Count == 0 || block.Any(u => !CanEdit(u))) return;
        _activeDrag = new UnitDrag(block); _beforeDrag = _displayModel;
        _dragCommitted = false; _dragExpansions.Clear(); _hoverGroup = null;
        _dropParent = null; _dropIndex = -1;
        _dragGhost = new Border { Background = new SolidColorBrush(Color.FromArgb(220, 35, 64, 77)), BorderBrush = Brushes.LightBlue, BorderThickness = new Thickness(2), Padding = new Thickness(9), Child = new TextBlock { Text = block.Count == 1 ? unit.Name : $"{block.Count} units", Foreground = Brushes.White } };
        DragOverlay.Children.Add(_dragGhost);
        try { DragDrop.DoDragDrop(source, _activeDrag, DragDropEffects.Move); }
        finally
        {
            if (!_dragCommitted) foreach (var pair in _dragExpansions) pair.Key.IsExpanded = pair.Value;
            // Canonical parentage/order changes only in OobDrop. Esc restores this projection.
            if (_beforeDrag is not null) { _displayModel = _beforeDrag; LayoutDisplayModel(); RebuildConnectorVisuals(); ApplyCanvasTransform(); }
            foreach (var card in NodeCanvas.Children.OfType<FrameworkElement>().ToList())
            { card.Opacity = 1; if (card.Tag is OobNode n && !_visibleCanvasNodes.Contains(n)) NodeCanvas.Children.Remove(card); }
            _activeDrag = null; _beforeDrag = null; _dropParent = null; _dropIndex = -1; _dragGhost = null; DragOverlay.Children.Clear();
        }
    }
    private void BeginGroupDrag(DependencyObject source, GroupNode group)
    {
        if (!group.IsLandCommand) return;
        _activeGroupDrag = group; _beforeDrag = _displayModel;
        _dropParent = null; _dropIndex = -1; _dragCommitted = false;
        _dragExpansions.Clear();
        _dragGhost = new Border { Background = (Brush)FindResource("SelectionBrush"), BorderBrush = Brushes.LightBlue,
            BorderThickness = new Thickness(2), Padding = new Thickness(8), IsHitTestVisible = false,
            Child = new TextBlock { Text = group.Name + " (entire command)", Foreground = Brushes.White } };
        DragOverlay.Children.Add(_dragGhost);
        try { DragDrop.DoDragDrop(source, new GroupDrag(group), DragDropEffects.Move); }
        finally {
            if (!_dragCommitted) foreach (var pair in _dragExpansions) pair.Key.IsExpanded = pair.Value;
            if (_beforeDrag is not null) { _displayModel = _beforeDrag; LayoutDisplayModel(); RebuildConnectorVisuals(); ApplyCanvasTransform(); }
            foreach (var card in NodeCanvas.Children.OfType<FrameworkElement>().ToList()) {
                card.Opacity = 1;
                if (card.Tag is OobNode node && !_visibleCanvasNodes.Contains(node)) NodeCanvas.Children.Remove(card);
            }
            _activeGroupDrag = null; _beforeDrag = null; _dropParent = null; _dropIndex = -1;
            _dragGhost = null; DragOverlay.Children.Clear();
        }
    }
    private bool ValidGroupDropParent(GroupNode moving, GroupNode target)
        => _data.CanMoveGroup(moving, target);

    private void PreviewGroupDrag(DragEventArgs e)
    {
        if (_activeGroupDrag is not GroupNode moving || _beforeDrag is null) return;
        e.Handled = true; e.Effects = DragDropEffects.None;
        var point = e.GetPosition(OobViewport);
        if ((DateTime.UtcNow - _lastEdgePan).TotalMilliseconds > 30) {
            var dx = point.X < 35 ? 16 : point.X > OobViewport.ActualWidth - 35 ? -16 : 0;
            var dy = point.Y < 35 ? 16 : point.Y > OobViewport.ActualHeight - 35 ? -16 : 0;
            if (dx != 0 || dy != 0) { _panX += dx; _panY += dy; ApplyCanvasTransform(); _lastEdgePan = DateTime.UtcNow; }
        }
        if (_dragGhost is not null) { Canvas.SetLeft(_dragGhost, point.X + 14); Canvas.SetTop(_dragGhost, point.Y + 14); }
        var x = (point.X - _panX) / _zoom; var y = (point.Y - _panY) / _zoom;
        var anchor = _visibleCanvasNodes.OfType<GroupNode>().Reverse().FirstOrDefault(n => n != moving &&
            x >= n.CanvasX - UnitStackGap && x <= n.CanvasX + GetNodeWidth(n) + UnitStackGap &&
            y >= n.CanvasY - UnitStackGap && y <= n.CanvasY + GetNodeHeight(n) + UnitStackGap);
        if (anchor is null) {
            if (_dropIndex >= 0 && x >= moving.CanvasX && x <= moving.CanvasX + GetNodeWidth(moving) &&
                y >= moving.CanvasY && y <= moving.CanvasY + GetNodeHeight(moving)) { e.Effects = DragDropEffects.Move; return; }
            ClearGroupPreview(); return;
        }
        GroupNode? parent;
        int index;
        if (anchor.UnitTier == moving.UnitTier) {
            parent = _data.Groups.GetValueOrDefault(anchor.ParentId);
            if (parent is null && (moving.ParentId >= 0 || anchor.Nation != moving.Nation)) { ClearGroupPreview(); return; }
            var siblings = (parent is null ? _beforeDrag.Roots : _beforeDrag.CommandsById[parent.GroupId].Subcommands)
                .Where(c => c.Source != moving).ToList();
            index = siblings.FindIndex(c => c.Source == anchor) + (x >= anchor.CanvasX + GetNodeWidth(anchor) / 2 ? 1 : 0);
        } else {
            parent = anchor;
            index = _beforeDrag.CommandsById[parent.GroupId].Subcommands.Count(c => c.Source != moving);
        }
        if (parent is not null && !ValidGroupDropParent(moving, parent)) { ClearGroupPreview(); return; }
        e.Effects = DragDropEffects.Move;
        if (_dropParent == parent && _dropIndex == index) return;
        if (parent is not null && !parent.IsExpanded) {
            _dragExpansions.TryAdd(parent, parent.IsExpanded); parent.IsExpanded = true;
        }
        _dropParent = parent; _dropIndex = index;
        var preview = CloneDisplay(_beforeDrag);
        var branch = preview.CommandsById[moving.GroupId];
        preview.Roots.Remove(branch);
        foreach (var command in preview.CommandsById.Values) command.Subcommands.Remove(branch);
        var destination = parent is null ? preview.Roots : preview.CommandsById[parent.GroupId].Subcommands;
        destination.Insert(Math.Clamp(index, 0, destination.Count), branch);
        _displayModel = preview; LayoutDisplayModel(); EnsureDragVisuals(); RebuildConnectorVisuals(); ApplyCanvasTransform();
        var branchNodes = FlattenRosterNodes(moving).ToHashSet();
        foreach (var card in NodeCanvas.Children.OfType<FrameworkElement>()) card.Opacity = card.Tag is OobNode n && branchNodes.Contains(n) ? .45 : 1;
        StatusText.Text = $"Insert {moving.Name} in {parent?.Name ?? "root commands"}, position {index + 1} • entire branch preview • Esc cancels";
    }
    private void ClearGroupPreview()
    {
        if (_beforeDrag is null || _dropIndex < 0) return;
        _dropIndex = -1; _dropParent = null; _displayModel = _beforeDrag;
        LayoutDisplayModel(); RebuildConnectorVisuals(); ApplyCanvasTransform();
        foreach (var card in NodeCanvas.Children.OfType<FrameworkElement>()) card.Opacity = 1;
    }
    private bool ValidDropParent(GroupNode group) => _activeDrag is not null &&
        _activeDrag.Units.All(u => u.Nation == group.Nation && CanEdit(u)) &&
        _commandClassifier.CategoryForGroup(group, _data.Groups) is CommandCategory.FieldCommand or CommandCategory.Garrison;

    private void OobDragOver(object sender, DragEventArgs e)
    {
        if (_activeGroupDrag is not null && e.Data.GetDataPresent(typeof(GroupDrag)))
        {
            PreviewGroupDrag(e);
            return;
        }
        if (_activeDrag is null || _beforeDrag is null || !e.Data.GetDataPresent(typeof(UnitDrag))) return;
        e.Handled = true; e.Effects = DragDropEffects.None;
        var point = e.GetPosition(OobViewport);
        if (_dragGhost is not null) { Canvas.SetLeft(_dragGhost, point.X + 14); Canvas.SetTop(_dragGhost, point.Y + 14); }
        if ((DateTime.UtcNow - _lastEdgePan).TotalMilliseconds > 30)
        {
            var dx = point.X < 35 ? 16 : point.X > OobViewport.ActualWidth - 35 ? -16 : 0;
            var dy = point.Y < 35 ? 16 : point.Y > OobViewport.ActualHeight - 35 ? -16 : 0;
            if (dx != 0 || dy != 0) { _panX += dx; _panY += dy; ApplyCanvasTransform(); _lastEdgePan = DateTime.UtcNow; }
        }
        var wx = (point.X - _panX) / _zoom; var wy = (point.Y - _panY) / _zoom;
        GroupNode? target = null; int index = -1;
        // Card targets are evaluated in the displayed preview. The insertion index uses
        // the original destination list excluding the moving block, preventing index drift.
        foreach (var node in _visibleCanvasNodes.Reverse())
        {
            if (node is CombatUnitNode moving && _activeDrag.Units.Contains(moving)) continue;
            if (wx < node.CanvasX || wx > node.CanvasX + GetNodeWidth(node) || wy < node.CanvasY - UnitStackGap / 2 || wy > node.CanvasY + GetNodeHeight(node) + UnitStackGap / 2) continue;
            if (node is GroupNode group) { target = group; index = int.MaxValue; }
            else if (node is CombatUnitNode anchor && _data.Groups.TryGetValue(anchor.ParentId, out var parent))
            {
                target = parent;
                var siblings = _beforeDrag.CommandsById[parent.GroupId].AttachedUnits.Where(u => !_activeDrag.Units.Contains(u)).ToList();
                index = siblings.IndexOf(anchor) + (wy >= node.CanvasY + GetNodeHeight(node) / 2 ? 1 : 0);
            }
            break;
        }
        // Keep the current insertion gap active while the pointer is inside it.
        if (target is null && _dropParent is not null)
        {
            var moving = _activeDrag.Units;
            if (moving.Any(u => wx >= u.CanvasX && wx <= u.CanvasX + GetNodeWidth(u) && wy >= u.CanvasY - UnitStackGap && wy <= u.CanvasY + GetNodeHeight(u) + UnitStackGap))
            { target = _dropParent; index = _dropIndex; }
        }
        if (target is null || !ValidDropParent(target)) { ClearDropPreview(); return; }
        if (_hoverGroup != target) { _hoverGroup = target; _hoverSince = DateTime.UtcNow; }
        var expanded = false;
        if (!target.IsExpanded && (DateTime.UtcNow - _hoverSince).TotalMilliseconds >= 650)
        { _dragExpansions.TryAdd(target, target.IsExpanded); target.IsExpanded = true; expanded = true; }
        var remaining = _beforeDrag.CommandsById[target.GroupId].AttachedUnits.Count(u => !_activeDrag.Units.Contains(u));
        index = Math.Clamp(index, 0, remaining);
        e.Effects = DragDropEffects.Move;
        if (_dropParent == target && _dropIndex == index && !expanded) return;
        _dropParent = target; _dropIndex = index;
        var preview = CloneDisplay(_beforeDrag);
        foreach (var command in preview.CommandsById.Values) command.AttachedUnits.RemoveAll(u => _activeDrag.Units.Contains(u));
        preview.CommandsById[target.GroupId].AttachedUnits.InsertRange(index, _activeDrag.Units);
        _displayModel = preview; LayoutDisplayModel(); EnsureDragVisuals(); RebuildConnectorVisuals(); ApplyCanvasTransform();
        // Ghost locations are the final layout positions; existing cards are reused.
        foreach (var card in NodeCanvas.Children.OfType<FrameworkElement>())
            if (card.Tag is CombatUnitNode cu && _activeDrag.Units.Contains(cu)) card.Opacity = .35;
        StatusText.Text = $"Insert {_activeDrag.Units.Count} unit(s) into {target.Name}, position {index + 1} • Esc cancels";
    }
    private void EnsureDragVisuals()
    {
        var existing = NodeCanvas.Children.OfType<FrameworkElement>().Select(e => e.Tag).OfType<OobNode>().ToHashSet();
        foreach (var node in _visibleCanvasNodes.Where(n => !existing.Contains(n)))
        {
            var template = (DataTemplate)FindResource(node is GroupNode ? "GroupNodeTemplate" : "CombatUnitTemplate");
            if (template.LoadContent() is not FrameworkElement element) continue;
            element.DataContext = node; element.Tag = node; ConfigureCard(element, node); Panel.SetZIndex(element, 10); NodeCanvas.Children.Add(element); MonitorRenderedCard(element);
        }
    }
    private void ClearDropPreview()
    {
        if (_beforeDrag is null || _dropParent is null) return;
        _dropParent = null; _dropIndex = -1; _displayModel = _beforeDrag;
        LayoutDisplayModel(); RebuildConnectorVisuals(); ApplyCanvasTransform();
        foreach (var card in NodeCanvas.Children.OfType<FrameworkElement>()) card.Opacity = 1;
    }
    private void OobDragLeave(object sender, DragEventArgs e)
    {
        var p = e.GetPosition(OobViewport);
        if (p.X < 0 || p.Y < 0 || p.X > OobViewport.ActualWidth || p.Y > OobViewport.ActualHeight) { if (_activeGroupDrag is not null) ClearGroupPreview(); else ClearDropPreview(); }
    }
    private void OobDrop(object sender, DragEventArgs e)
    {
        if (_activeGroupDrag is not null && e.Data.GetDataPresent(typeof(GroupDrag)))
        {
            if (_dropIndex < 0 || (_dropParent is not null && !ValidGroupDropParent(_activeGroupDrag, _dropParent))) return;
            var moving = _activeGroupDrag;
            var order = (_dropParent is null ? _displayModel.Roots : _displayModel.CommandsById[_dropParent.GroupId].Subcommands).Select(c => c.Source).ToList();
            var parent = _dropParent;
            _editSession.Execute("Move command intact", _data.Units, _data.Groups.Values, () => {
                if (parent is not null && moving.ParentId != parent.GroupId) _data.MoveGroup(moving, parent);
                for (int i = 0; i < order.Count; i++) order[i].EditorOrder = i;
            });
            _dragCommitted = true; _beforeDrag = null;
            RebuildSide(); ShowGroup(moving); UpdateDirtyState();
            StatusText.Text = $"Moved {moving.Name} intact. Ctrl+Z undoes hierarchy and order. Save writes intact group records.";
            e.Handled = true; return;
        }
        if (_activeDrag is null || _beforeDrag is null || _dropParent is null || !ValidDropParent(_dropParent)) return;
        e.Handled = true;
        var target = _dropParent; var block = _activeDrag.Units;
        var finalOrder = _displayModel.CommandsById[target.GroupId].AttachedUnits.ToList();
        var affected = _beforeDrag.CommandsById.Values.SelectMany(c => c.AttachedUnits).Where(u => block.Contains(u) || u.ParentId == target.GroupId).Distinct().ToList();
        var changed = _editSession.Execute($"Move {block.Count} unit(s) to {target.Name}", affected, () =>
        {
            foreach (var unit in block) if (unit.ParentId != target.GroupId) _data.MoveUnit(unit, target);
            for (int i = 0; i < finalOrder.Count; i++) finalOrder[i].EditorOrder = i;
        });
        _dragCommitted = true; _beforeDrag = null;
        if (changed) { target.IsExpanded = true; UpdateDirtyState(); RebuildSide(); StatusText.Text = $"Moved {block.Count} unit(s) to {target.Name}. Ctrl+Z undoes parentage and display order."; }
        else { RebuildSide(); }
    }
    private void ApplyNudges()
    {
        _automaticPositions.Clear();
        foreach (var node in EnumerateAllLaidOutNodes())
        {
            _automaticPositions[node] = new Point(node.CanvasX, node.CanvasY);
            if (_nudges.TryGetValue(node, out var delta)) { node.CanvasX += delta.X; node.CanvasY += delta.Y; }
        }
    }
    private void Nudge_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNode is null || !_automaticPositions.TryGetValue(_selectedNode, out var selected)) return;
        var values = ((FrameworkElement)sender).Tag.ToString()!.Split(','); var delta = new Point(int.Parse(values[0]), int.Parse(values[1]));
        IEnumerable<OobNode> nodes = NudgeScope.SelectedIndex == 1
            ? _automaticPositions.Where(p => Math.Abs(p.Value.Y + GetSurfaceInset(p.Key) - selected.Y - GetSurfaceInset(_selectedNode)) < 1).Select(p => p.Key)
            : FlattenRosterNodes(_selectedNode);
        foreach (var node in nodes.ToList()) { var old = _nudges.GetValueOrDefault(node); _nudges[node] = new Point(old.X + delta.X, old.Y + delta.Y); }
        LayoutDisplayModel(); RebuildConnectorVisuals(); ApplyCanvasTransform(); UpdateDirtyState();
        StatusText.Text = "Presentation nudge applied. Save Changes retains display metadata; game hierarchy is unchanged.";
    }
    private void ResetNudges_Click(object sender, RoutedEventArgs e)
    {
        _nudges.Clear(); LayoutDisplayModel(); RebuildConnectorVisuals(); ApplyCanvasTransform(); UpdateDirtyState();
    }
}
