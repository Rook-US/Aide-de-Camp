using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AideDeCamp.Services;

// IDs are valid only inside this exact save image. Never match IDs across campaign saves.
public sealed class DisplayMetadataService
{
    private readonly string _storageDirectory;
    private readonly bool _legacyFallback;
    public DisplayMetadataService(string? storageDirectory=null) {_storageDirectory=storageDirectory??Path.Combine(AppPaths.Root,"display");_legacyFallback=storageDirectory is null;}
    public sealed record Entry(int Order, double X, double Y, int? StateId = null, string? HomeStateName = null);
    public static string Fingerprint(string regiments, string groups) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("regiments.dat:" + regiments + "|groups.dat:" + groups)));
    private string MetadataPath(string directory, string? fingerprint = null)
    {
        fingerprint ??= Fingerprint(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "regiments.dat")))), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "groups.dat")))));
        var dir = _storageDirectory;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, fingerprint + ".json");
    }
    public Dictionary<string, Entry> Load(string directory, string? fingerprint = null)
    {
        var path = MetadataPath(directory, fingerprint);
        if(_legacyFallback&&!File.Exists(path))path=AppPaths.ReadPath(Path.Combine("display",Path.GetFileName(path)));
        return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path)) ?? new() : new();
    }
    public void Save(string directory, Dictionary<string, Entry> entries, string? fingerprint = null)
    {
        var path = MetadataPath(directory, fingerprint); var temp = path + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
