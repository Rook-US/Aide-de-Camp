using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AideDeCamp.Services;

/// <summary>
/// Loss-conscious text file wrapper. It preserves the source encoding BOM state,
/// dominant line-ending convention, and whether the file ended with a newline.
/// </summary>
public sealed class TextFileBuffer
{
    private readonly byte[] _preamble;
    private string[] _lineEndings = Array.Empty<string>();

    private TextFileBuffer(string path, List<string> lines, Encoding encoding, byte[] preamble, string newLine, bool hasFinalNewLine)
    {
        FilePath = path;
        Lines = lines;
        Encoding = encoding;
        _preamble = preamble;
        NewLine = newLine;
        HasFinalNewLine = hasFinalNewLine;
    }

    public string FilePath { get; }
    public List<string> Lines { get; private set; }
    public Encoding Encoding { get; }
    public string NewLine { get; }
    public bool HasFinalNewLine { get; }
    public bool HasBom => _preamble.Length > 0;

    public static TextFileBuffer Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (encoding, preamble, offset) = DetectEncoding(bytes);
        var text = encoding.GetString(bytes, offset, bytes.Length - offset);
        var newLine = DetectDominantNewLine(text);
        var hasFinalNewLine = text.EndsWith("\r\n", StringComparison.Ordinal) ||
                              text.EndsWith("\n", StringComparison.Ordinal) ||
                              text.EndsWith("\r", StringComparison.Ordinal);
        var lines = Regex.Split(text, "\\r\\n|\\n|\\r").ToList();
        if (hasFinalNewLine && lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return new TextFileBuffer(path, lines, encoding, preamble, newLine, hasFinalNewLine) { _lineEndings = Regex.Matches(text, "\\r\\n|\\n|\\r").Select(m => m.Value).ToArray() };
    }

    public List<string> CloneLines() => new(Lines);

    public void ReplaceLines(IEnumerable<string> lines) => Lines = lines.ToList();
    public TextFileBuffer WithSplices(IEnumerable<(int Start,int Removed,int Added)> edits) {
        var endings=_lineEndings.ToList();
        foreach(var e in edits.OrderByDescending(e=>e.Start)) {
            int count=Math.Min(e.Removed,Math.Max(0,endings.Count-e.Start));
            var replacement=endings.Skip(e.Start).Take(Math.Min(count,e.Added)).Concat(Enumerable.Repeat(NewLine,Math.Max(0,e.Added-count))).ToArray();
            endings.RemoveRange(e.Start,count);endings.InsertRange(e.Start,replacement);
        }
        return new(FilePath,CloneLines(),Encoding,_preamble,NewLine,HasFinalNewLine){_lineEndings=endings.ToArray()};
    }

    public void WriteTo(string path, IReadOnlyList<string> lines)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            builder.Append(lines[i]);
            if (i < lines.Count - 1 || HasFinalNewLine) builder.Append(i < _lineEndings.Length ? _lineEndings[i] : NewLine);
        }
        var text = builder.ToString();
        var payload = Encoding.GetBytes(text);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        if (_preamble.Length > 0) stream.Write(_preamble, 0, _preamble.Length);
        stream.Write(payload, 0, payload.Length);
        stream.Flush(true);
    }

    private static (Encoding Encoding, byte[] Preamble, int Offset) DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
            return (new UTF32Encoding(false, false, true), new byte[] { 0xFF, 0xFE, 0, 0 }, 4);
        if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            return (new UTF32Encoding(true, false, true), new byte[] { 0, 0, 0xFE, 0xFF }, 4);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(false, true), new byte[] { 0xEF, 0xBB, 0xBF }, 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (new UnicodeEncoding(false, false, true), new byte[] { 0xFF, 0xFE }, 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (new UnicodeEncoding(true, false, true), new byte[] { 0xFE, 0xFF }, 2);

        try
        {
            var utf8 = new UTF8Encoding(false, true);
            _ = utf8.GetString(bytes);
            return (utf8, Array.Empty<byte>(), 0);
        }
        catch (DecoderFallbackException)
        {
            // Game data is normally ASCII/UTF-8. Latin-1 is a lossless single-byte
            // fallback for unusual modded text rather than replacing invalid bytes.
            return (System.Text.Encoding.Latin1, Array.Empty<byte>(), 0);
        }
    }

    private static string DetectDominantNewLine(string text)
    {
        var crlf = Regex.Matches(text, "\\r\\n").Count;
        var lf = Regex.Matches(text, "(?<!\\r)\\n").Count;
        var cr = Regex.Matches(text, "\\r(?!\\n)").Count;
        if (crlf >= lf && crlf >= cr && crlf > 0) return "\r\n";
        if (lf >= cr && lf > 0) return "\n";
        if (cr > 0) return "\r";
        return Environment.NewLine;
    }
}
