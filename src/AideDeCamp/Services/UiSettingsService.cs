using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AideDeCamp.Services;

public sealed record UiParameter(string Key, string Label, double Default, double Min, double Max, bool Toggle = false)
{
    public string Section => Key[..Key.LastIndexOf('.')];
}

// Stable semantic keys; retain unknown JSON fields when round-tripping a config.
public sealed class UiSettingsService
{
    public static readonly UiParameter[] Parameters =
    {
        new("oob.presentation.regimentalScale", "Use regimental-scale labels", 0, 0, 1, true),
        new("oob.cards.scale", "Card scale", 1, .25, 4),
        new("oob.cards.textScale", "Text scale", 1.15, .4, 4),
        new("oob.cards.spacing", "Content spacing", 2, 0, 48),
        new("oob.cards.padding", "Internal padding", 9, 0, 100),
        new("oob.cards.shadedRows", "Shade alternate metric rows", 0, 0, 1, true),
        new("oob.cards.opacity", "Card background opacity", 1, 0, 1),
        new("oob.natoCounters.scale", "Counter scale", 1, .2, 6),
        new("oob.natoCounters.echelonScale", "Echelon symbol scale", 1.2, .25, 6),
        new("oob.natoCounters.hqStaff", "Show HQ staff", 1, 0, 1, true),
        new("oob.natoCounters.x", "Counter X shift", 0, -1000, 1000),
        new("oob.natoCounters.y", "Counter Y shift", 0, -1000, 1000),
        new("oob.spacing.army", "Army to children (plus base gutter)", 48, 0, 1600),
        new("oob.spacing.corps", "Corps to children (plus base gutter)", 40, 0, 1600),
        new("oob.spacing.division", "Division to children (plus base gutter)", 32, 0, 1600),
        new("oob.spacing.brigade", "Brigade to children (plus base gutter)", 24, 0, 1600),
        new("oob.spacing.regiment", "Regiment / lower to children (plus base gutter)", 24, 0, 1600),
        new("oob.spacing.roots", "Root edge gap", 80, 0, 8000),
        new("oob.spacing.commands", "HQ edge gap (plus base gutter)", 24, 0, 2000),
        new("oob.spacing.attached", "Combat column edge gap (plus base gutter)", 16, 0, 2000),
        new("oob.spacing.stack", "Combat stack edge gap", 32, 0, 1000),
        new("oob.spacing.minX", "Base horizontal gutter", 12, 0, 1000),
        new("oob.spacing.minY", "Base row gutter", 12, 0, 1000),
        new("oob.connectors.thickness", "Connector thickness", 1, .1, 20),
        new("oob.connectors.opacity", "Connector opacity", 1, 0, 1),
        new("oob.zoom.detail", "Show supplemental details at zoom", .95, .05, 3),
        new("roster.density.rowHeight", "Minimum roster row height", 28, 16, 160),
        new("roster.hierarchy.indent", "Hierarchy indent", 18, 0, 200),
        new("theme.text.scale", "Window text scale", 1, .65, 2)
    };
    public string SettingsPath { get; } = Path.Combine(AppPaths.Root, "Aide-de-Camp.UI.json");
    private JsonObject _root = new() { ["schema"] = "gtcw-oob-ui", ["version"] = 1 };
    public string? LoadWarning { get; private set; }
    public UiSettingsService(bool loadSaved = true)
    {
        var loadPath=AppPaths.ReadPath("Aide-de-Camp.UI.json","GTCW.OOBEditor.UI.json");
        if (!loadSaved || !File.Exists(loadPath)) return;
        try { Import(loadPath); }
        catch (Exception ex) { ErrorLog.Write("Load UI settings",ex); LoadWarning = $"UI config could not be loaded; defaults are active. {ex.Message}"; }
    }
    private static JsonNode? Lookup(JsonObject root, string key)
    {
        JsonNode? node = root;
        foreach (var part in key.Split('.')) { if (node is not JsonObject obj) return null; node = obj[part]; }
        return node;
    }
    public double Get(string key)
    {
        var spec = Parameters.First(p => p.Key == key);
        var node = Lookup(_root, key);
        if (node is null) return spec.Default;
        if (spec.Toggle && node is JsonValue v && v.TryGetValue<bool>(out var b)) return b ? 1 : 0;
        return node.GetValue<double>();
    }
    public void Set(string key, double value)
    {
        var spec = Parameters.First(p => p.Key == key);
        if (!double.IsFinite(value) || value < spec.Min || value > spec.Max) throw new ArgumentOutOfRangeException(key);
        var parts = key.Split('.'); var parent = _root;
        foreach (var part in parts[..^1])
        {
            if (parent[part] is not JsonObject) parent[part] = new JsonObject();
            parent = (JsonObject)parent[part]!;
        }
        parent[parts[^1]] = spec.Toggle ? JsonValue.Create(value >= .5) : JsonValue.Create(value);
    }
    public void Reset(string? section = null)
    {
        foreach (var p in Parameters.Where(p => section is null || p.Section == section)) Set(p.Key, p.Default);
    }
    public void Import(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("Expected a JSON object.");
        if (root["schema"]?.GetValue<string>() != "gtcw-oob-ui" || root["version"]?.GetValue<int>() is not int version || version < 1)
            throw new InvalidDataException("This is not a supported Aide-de-Camp UI config.");
        foreach (var p in Parameters)
        {
            // Reject malformed known sections instead of silently discarding them.
            JsonNode? node = root;
            foreach (var part in p.Key.Split('.'))
            {
                if (node is null) break;
                if (node is not JsonObject obj) throw new InvalidDataException($"Invalid section for {p.Label}.");
                node = obj[part];
            }
            if (node is null) continue;
            if (p.Toggle && node is JsonValue v && v.TryGetValue<bool>(out _)) continue;
            if (node is not JsonValue number || !number.TryGetValue<double>(out var value) || !double.IsFinite(value) || value < p.Min || value > p.Max)
                throw new InvalidDataException($"{p.Label} must be between {p.Min} and {p.Max}.");
        }
        _root = root; // Commit only after every known value is validated.
    }
    public void Save() => Export(SettingsPath);
    public void Export(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
