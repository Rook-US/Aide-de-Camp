using System.Windows;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;

public partial class MainWindow
{
    private string _savedDisplayState = "";
    private string DisplayState() => string.Join(";", AllMetadataNodes().OrderBy(NodeKey).Select(n =>
    {
        var d = _nudges.GetValueOrDefault(n);
        return FormattableString.Invariant($"{NodeKey(n)}:{(n is CombatUnitNode u ? u.EditorOrder : 0)}:{d.X:R}:{d.Y:R}");
    }));
    private bool DisplayDirty => _data.SaveDirectory is not null && _savedDisplayState != DisplayState();
    private readonly DisplayMetadataService _metadata = new();
    private static string NodeKey(OobNode node) => node is GroupNode g ? $"G:{g.GroupId}" : $"U:{((CombatUnitNode)node).UnitId}";
    private IEnumerable<OobNode> AllMetadataNodes() => _data.Groups.Values.Cast<OobNode>().Concat(_data.Units);
    private void LoadDisplayMetadata()
    {
        _nudges.Clear();
        if (_data.SaveDirectory is null) return;
        try
        {
            var entries = _metadata.Load(_data.SaveDirectory, _data.DisplayFingerprint);
            _data.RestoreEditorStateNames(entries.Values.Where(e => e.StateId.HasValue && !string.IsNullOrWhiteSpace(e.HomeStateName)).Select(e => new KeyValuePair<int, string>(e.StateId!.Value, e.HomeStateName!)));
            foreach (var node in AllMetadataNodes())
                if (entries.TryGetValue(NodeKey(node), out var entry))
                {
                    if (node is CombatUnitNode u) u.EditorOrder = entry.Order;
                    if (double.IsFinite(entry.X) && double.IsFinite(entry.Y) && Math.Abs(entry.X) < 100000 && Math.Abs(entry.Y) < 100000)
                        _nudges[node] = new Point(entry.X, entry.Y);
                }
        }
        catch (Exception ex) { MessageBox.Show(this, $"Display metadata could not be loaded. Automatic layout is active.\n{ex.Message}", "Display settings"); }
    }
    private void SaveDisplayMetadata()
    {
        if (_data.SaveDirectory is null || _data.IsReadOnlySave) return;
        var entries = AllMetadataNodes().ToDictionary(NodeKey, n =>
        {
            var delta = _nudges.GetValueOrDefault(n);
            return new DisplayMetadataService.Entry(n is CombatUnitNode u ? u.EditorOrder : int.MaxValue, delta.X, delta.Y, (n as CombatUnitNode)?.StateId, (n as CombatUnitNode)?.HomeStateName);
        });
        try { _metadata.Save(_data.SaveDirectory, entries, _data.DisplayFingerprint); _savedDisplayState = DisplayState(); }
        catch (Exception ex) { MessageBox.Show(this, $"Game save succeeded, but display order / nudges could not be retained.\n{ex.Message}", "Display metadata"); }
    }
}
