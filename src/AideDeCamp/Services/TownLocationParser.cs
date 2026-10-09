using System.Globalization;
using System.IO;

namespace AideDeCamp.Services;

/// <summary>
/// Reads game-written 1.142 IIPsTowns.dat sections. Town identity is its saved
/// name and exact world position; owner is deliberately not treated as a state.
/// </summary>
public static class TownLocationParser
{
    public sealed record Town(string Name, float X, float Y, float Z, int Owner);

    public static IReadOnlyList<Town> Parse(IReadOnlyList<string> lines)
    {
        var c = new Cursor(lines);
        var iipCount = c.Count("IIP count");
        for (var i = 0; i < iipCount; i++)
        {
            c.Text("IIP name");
            c.Integer("IIP type");
            c.Floats(4, "IIP position and blocked value");
            c.FloatArray("IIP stock");
            c.FloatArray("IIP relative prices");
            c.FloatArray("IIP prices");
            c.Floats(2, "IIP continuous profit");
            c.TrailingData();
            c.TrailingData();
            c.Skip(4, "IIP construction reference");
            c.Floats(2, "IIP construction times");
            var tradeCount = c.Count("IIP historical trades");
            c.Skip((long)tradeCount * 23, "IIP historical trade records");
            var focusCount = c.Count("IIP demand focus");
            c.Skip((long)focusCount * 2, "IIP demand focus pairs");
            c.Integer("IIP alliance owner");
            c.Floats(2, "IIP condition and trade losses");
            var productionCount = c.Count("IIP production pairs");
            c.Skip((long)productionCount * 2, "IIP production pairs");
            c.Integer("IIP harbor level");
            c.Float("IIP harbor timer");
        }

        var townCount = c.Count("town count");
        var towns = new List<Town>(townCount);
        var identities = new HashSet<(string, float, float, float)>();
        for (var i = 0; i < townCount; i++)
        {
            var name = c.Text("town name");
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException($"Town {i} has no saved identity.");
            var x = c.Float("town world x");
            var y = c.Float("town world y");
            var z = c.Float("town world z");
            c.Boolean("town capital flag");
            var owner = c.Integer("town owner");
            c.Floats(6, "town economy");
            c.Integer("town new ownership");
            c.Float("town occupation points");
            if (!identities.Add((name, x, y, z)))
                throw new InvalidDataException($"Town {i} duplicates a saved name-and-position identity.");
            towns.Add(new Town(name, x, y, z, owner));
        }

        c.Float("global top town income");
        var allianceCount = c.Count("alliance count");
        for (var alliance = 0; alliance < allianceCount; alliance++)
        {
            var resourceCount = c.Count("alliance resource count");
            for (var resource = 0; resource < resourceCount; resource++)
                for (var peer = 0; peer < allianceCount; peer++)
                {
                    c.TrailingData();
                    c.TrailingData();
                }
        }
        if (!c.End) throw new InvalidDataException("IIPsTowns.dat has material after its counted sections.");
        return towns;
    }

    private sealed class Cursor(IReadOnlyList<string> lines)
    {
        private int _position;
        public bool End => _position == lines.Count;

        public string Text(string field)
        {
            if (_position >= lines.Count) throw new InvalidDataException($"IIPsTowns.dat ends before {field}.");
            return lines[_position++];
        }

        public int Integer(string field)
        {
            var value = Text(field);
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                throw new InvalidDataException($"IIPsTowns.dat {field} is not an integer.");
            return result;
        }

        public int Count(string field)
        {
            var value = Integer(field);
            if (value < 0 || value > lines.Count - _position)
                throw new InvalidDataException($"IIPsTowns.dat {field} exceeds the remaining file.");
            return value;
        }

        public float Float(string field)
        {
            var value = Text(field);
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || !float.IsFinite(result))
                throw new InvalidDataException($"IIPsTowns.dat {field} is not finite.");
            return result;
        }

        public void Boolean(string field)
        {
            var value = Text(field);
            if (value is not ("True" or "False")) throw new InvalidDataException($"IIPsTowns.dat {field} is not a Boolean.");
        }

        public void Floats(int count, string field)
        {
            for (var i = 0; i < count; i++) Float(field);
        }

        public void FloatArray(string field)
        {
            var count = Count(field + " count");
            Floats(count, field);
        }

        public void TrailingData()
        {
            Text("trailing-data name");
            FloatArray("trailing-data values");
            FloatArray("trailing-data campaign dates");
            Float("trailing-data update rhythm");
        }

        public void Skip(long count, string field)
        {
            if (count < 0 || count > lines.Count - _position)
                throw new InvalidDataException($"IIPsTowns.dat {field} exceeds the remaining file.");
            _position += (int)count;
        }
    }
}
