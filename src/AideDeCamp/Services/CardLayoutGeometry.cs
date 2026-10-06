namespace AideDeCamp.Services;

public readonly record struct CardRowFootprint(int Depth, double Height, double SurfaceInset, double Gap);

public static class CardLayoutGeometry
{
    // Native tiers, independent of regimental presentation labels or tree depth.
    public static double TierScale(bool headquarters, int nativeTier) => !headquarters ? .85
        : nativeTier >= 16 ? 1.20 : nativeTier == 15 ? 1.0 : .85;

    // Align the actual card surfaces, reserving the tallest counter above the row
    // and the deepest card body below it. Different content heights remain Auto.
    public static Dictionary<int, double> SurfaceBaselines(IEnumerable<CardRowFootprint> cards, double top = 70)
    {
        var rows = cards.GroupBy(c => c.Depth).OrderBy(g => g.Key);
        var result = new Dictionary<int, double>();
        foreach (var row in rows) {
            var baseline = top + row.Max(c => c.SurfaceInset);
            result[row.Key] = baseline;
            top = baseline + row.Max(c => c.Height - c.SurfaceInset) + row.Max(c => c.Gap);
        }
        return result;
    }
}
