using System.IO;
namespace AideDeCamp.Services;

public static class AppPaths
{
    // The override isolates automated verification from a user's preferences.
    public static string Root=>Environment.GetEnvironmentVariable("AIDE_DE_CAMP_DATA") is {Length:>0} path?Path.GetFullPath(path):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Aide-de-Camp");
    public static string LegacyRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GTCW.OOBEditor");
    public static string ReadPath(string name,string? legacyName=null) {
        var current=Path.Combine(Root,name);
        return File.Exists(current)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AIDE_DE_CAMP_DATA"))?current:Path.Combine(LegacyRoot,legacyName??name);
    }
}
