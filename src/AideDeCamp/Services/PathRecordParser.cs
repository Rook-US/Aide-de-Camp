using System.Globalization;
using System.IO;

namespace AideDeCamp.Services;

/// <summary>
/// Bounded reader for the installed game's 1.142 paths.dat structure. Offsets
/// locate a record only inside the current immutable save buffer; the four
/// saved identity fields are the cross-file key.
/// </summary>
public static class PathRecordParser
{
    public sealed record Record(string Name, string Abbreviation, int UnitType, int CommanderId,
        int Start, int End, int TransferLine, int? SupplyStockLine, IReadOnlyList<double>? SupplyStock);

    public static IReadOnlyList<Record> Parse(IReadOnlyList<string> lines, bool strictFields = false)
    {
        var cursor = new Cursor(lines, strictFields);
        var count = cursor.Count("record count", 4);
        var records = new List<Record>(count);
        for (var index = 0; index < count; index++)
        {
            var start = cursor.Position;
            var name = cursor.Text();
            var abbreviation = cursor.Text();
            var type = cursor.Int("unit type");
            var commander = cursor.Int("commander ID");
            cursor.Integers(cursor.Count("cover history count"), "cover history");
            cursor.Numbers(1, "rotation");
            var movements = cursor.Count("movement path count", 4);
            for (int j = 0; j < movements; j++) { cursor.Numbers(3, "path position"); cursor.Integers(1, "path status"); }
            cursor.Numbers(3, "movement time"); cursor.Booleans(4, "battle flags");
            var transferLine = cursor.Position;
            cursor.Finite("transfer time");
            cursor.Numbers(6, "transfer and patrol positions");
            cursor.Booleans(1, "basic garrison"); cursor.Numbers(1, "blockade");
            cursor.Integers(5, "campaign orders");
            cursor.Numbers(4, "campaign time and morale");
            cursor.Booleans(cursor.Count("active order type count"), "active order types");
            cursor.Reference();
            cursor.Integers(1, "order state");
            var queueCount = cursor.Count("order queue count");
            for (var order = 0; order < queueCount; order++)
            {
                cursor.Reference();
                cursor.Integers(2, "path IDs"); cursor.Numbers(2, "order time"); cursor.Integers(1, "order type"); cursor.Numbers(2, "move time and rotation");
                int received = cursor.Count("received unit count", 4); for (int j = 0; j < received; j++) cursor.Reference();
                cursor.Reference();
                int couriers = cursor.Count("courier line count", 16);
                for (int j = 0; j < couriers; j++) { cursor.Integers(1, "courier type"); cursor.Numbers(1, "courier time"); cursor.Reference(); cursor.Reference(); cursor.Reference(); cursor.Booleans(1, "courier active"); cursor.Numbers(1, "courier update"); }
            }
            cursor.Numbers(2, "upkeep and recruitment"); cursor.Booleans(2, "retreat state");
            cursor.Numbers(cursor.Count("ammunition count"), "ammunition");
            var supplyCount = cursor.Count("supply consumption count");
            cursor.Numbers(checked(supplyCount * 3), "supply consumption");
            int? supplyStockLine = null;
            IReadOnlyList<double>? supplyStock = null;
            if (supplyCount > 0)
            {
                for (var field = 0; field < 5; field++) cursor.Finite("supply header");
                supplyStockLine = cursor.Position;
                var values = new double[4];
                for (var slot = 0; slot < values.Length; slot++)
                    values[slot] = cursor.Finite($"supply stock {slot}");
                supplyStock = Array.AsReadOnly(values);
            }
            cursor.Booleans(1, "finished recruitment"); cursor.Integers(1, "battle flag"); cursor.Booleans(1, "reset stance"); cursor.Numbers(1, "missing average"); cursor.Booleans(1, "reserved/discarded flag"); cursor.Numbers(2, "battle-until and prior wounded");
            cursor.Booleans(5, "transport permissions");
            cursor.Numbers(3, "theater position");
            cursor.Integers(1, "reinforcement priority"); cursor.Numbers(1, "reform time"); cursor.Integers(1, "reinforcement type"); cursor.Text(); cursor.Numbers(1, "prestige casualties"); cursor.Integers(1, "combat zone count");
            cursor.Text(); cursor.Integers(2, "army group commanders");
            cursor.Numbers(2, "embarkation times"); cursor.Booleans(1, "enemy fleet"); cursor.Numbers(1, "embarkation update"); cursor.Booleans(2, "rout and commander-campaign flags"); cursor.Numbers(1, "coordination time");
            records.Add(new Record(name, abbreviation, type, commander, start, cursor.Position,
                transferLine, supplyStockLine, supplyStock));
        }
        if (cursor.Position != lines.Count)
            throw new InvalidDataException($"paths.dat contains {lines.Count - cursor.Position} trailing lines after {count} records.");
        return records;
    }

    private sealed class Cursor(IReadOnlyList<string> lines, bool strict)
    {
        public void Numbers(int count, string field) { if (!strict) { Skip(count, field); return; } for (int i = 0; i < count; i++) Finite(field); }
        public void Integers(int count, string field) { if (!strict) { Skip(count, field); return; } for (int i = 0; i < count; i++) Int(field); }
        public void Booleans(int count, string field) { if (!strict) { Skip(count, field); return; } for (int i = 0; i < count; i++) if (!bool.TryParse(Text(), out _)) throw new InvalidDataException($"paths.dat {field} is not a Boolean."); }
        public void Reference() { Text(); Text(); Integers(2, "unit reference"); }
        public int Position { get; private set; }

        public string Text()
        {
            if (Position >= lines.Count) throw new InvalidDataException("paths.dat ends inside a record.");
            return lines[Position++];
        }

        public int Int(string field)
        {
            var text = Text();
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                throw new InvalidDataException($"paths.dat {field} at line {Position - 1} is not an integer.");
            return value;
        }

        public int Count(string field, int minimumLinesPerItem = 1)
        {
            var value = Int(field);
            if (value < 0 || value > (lines.Count - Position) / minimumLinesPerItem)
                throw new InvalidDataException($"paths.dat {field} at line {Position - 1} is out of bounds.");
            return value;
        }

        public double Finite(string field)
        {
            var text = Text();
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                throw new InvalidDataException($"paths.dat {field} at line {Position - 1} is not finite.");
            return value;
        }

        public void Skip(int count, string field)
        {
            if (count < 0 || count > lines.Count - Position)
                throw new InvalidDataException($"paths.dat {field} exceeds the file at line {Position}.");
            Position += count;
        }

        public void SkipMultiple(int count, int width, string field)
        {
            if (count < 0 || count > (lines.Count - Position) / width)
                throw new InvalidDataException($"paths.dat {field} exceeds the file at line {Position}.");
            Position += count * width;
        }
    }
}
