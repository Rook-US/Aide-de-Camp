using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AideDeCamp.Models;
using Microsoft.Win32;

namespace AideDeCamp.Services;

public sealed class GameInstallationService
{
    private const string GameFolderName = "Grand Tactician The Civil War (1861-1865)";
    private readonly string _settingsDirectory = AppPaths.Root;
    private string SettingsFile => Path.Combine(_settingsDirectory, "settings.json");

    public string? InstallationRoot { get; private set; }
    public bool AutoDetectionConfirmed => ReadSettings().ConfirmedAutoDetection;
    public string? ConfigDirectory => InstallationRoot is null ? null : FindChildDirectory(InstallationRoot, "Config");
    public string? CampaignsDirectory => InstallationRoot is null ? null : FindChildDirectory(InstallationRoot, "Campaigns");

    public string? AutoDetect()
    {
        var candidates = new List<string>();
        var remembered = ReadRememberedPath();
        if (!string.IsNullOrWhiteSpace(remembered)) candidates.Add(remembered!);

        foreach (var steamRoot in GetSteamRoots())
        {
            candidates.Add(Path.Combine(steamRoot, "steamapps", "common", GameFolderName));
            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            foreach (var library in ParseSteamLibraries(vdf))
                candidates.Add(Path.Combine(library, "steamapps", "common", GameFolderName));
        }

        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(pf86))
            candidates.Add(Path.Combine(pf86, "Steam", "steamapps", "common", GameFolderName));
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(pf))
            candidates.Add(Path.Combine(pf, "Steam", "steamapps", "common", GameFolderName));

        foreach (var candidate in candidates.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TrySetInstallation(candidate, remember: false))
            {
                RememberPath(InstallationRoot!);
                return InstallationRoot;
            }
        }
        return null;
    }


    public void ConfirmAutoDetection()
    {
        var settings = ReadSettings();
        settings.ConfirmedAutoDetection = true;
        if (InstallationRoot is not null) settings.InstallationRoot = InstallationRoot;
        WriteSettings(settings);
    }
    public bool TrySetInstallation(string path, bool remember = true)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        path = Path.GetFullPath(path.Trim());

        // Be forgiving if the user points at Config/Campaigns instead of the game root.
        var name = new DirectoryInfo(path).Name;
        if (name.Equals("Config", StringComparison.OrdinalIgnoreCase) || name.Equals("Campaigns", StringComparison.OrdinalIgnoreCase))
            path = Directory.GetParent(path)?.FullName ?? path;

        if (!Directory.Exists(path)) return false;
        if (FindChildDirectory(path, "Config") is null || FindChildDirectory(path, "Campaigns") is null) return false;

        InstallationRoot = path;
        if (remember) RememberPath(path);
        return true;
    }

    public IReadOnlyList<CampaignSaveGroup> ScanActiveCampaigns()
    {
        var campaignsRoot = CampaignsDirectory;
        if (campaignsRoot is null || !Directory.Exists(campaignsRoot)) return Array.Empty<CampaignSaveGroup>();

        var groups = new Dictionary<string, CampaignSaveGroup>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> regimentFiles;
        try { regimentFiles = Directory.EnumerateFiles(campaignsRoot, "regiments.dat", SearchOption.AllDirectories).ToList(); }
        catch { return Array.Empty<CampaignSaveGroup>(); }

        foreach (var regimentFile in regimentFiles)
        {
            var saveDir = Path.GetDirectoryName(regimentFile);
            if (saveDir is null) continue;
            var saveName = new DirectoryInfo(saveDir).Name;
            if (!saveName.StartsWith("Save", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsUsableSave(saveDir)) continue;

            var campaignDir = FindCampaignOwner(saveDir, campaignsRoot);
            if (!groups.TryGetValue(campaignDir, out var group))
            {
                group = new CampaignSaveGroup
                {
                    CampaignDirectory = campaignDir,
                    RelativePath = Path.GetRelativePath(campaignsRoot, campaignDir),
                    DisplayName = ReadCampaignName(campaignDir, campaignsRoot)
                };
                groups[campaignDir] = group;
            }
            group.Saves.Add(BuildSaveEntry(saveDir));
        }

        foreach (var group in groups.Values)
        {
            var ordered = group.Saves.OrderByDescending(s => s.LastModified).ToList();
            group.Saves.Clear();
            foreach (var save in ordered) group.Saves.Add(save);
        }

        return groups.Values
            .OrderBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsUsableSave(string dir) =>
        File.Exists(Path.Combine(dir, "regiments.dat")) &&
        File.Exists(Path.Combine(dir, "groups.dat")) &&
        File.Exists(Path.Combine(dir, "commanders.txt"));

    private static SaveEntry BuildSaveEntry(string saveDir)
    {
        var dirName = new DirectoryInfo(saveDir).Name;
        var scenario = Path.Combine(saveDir, "scenario.dat");
        var metadata=SaveMetadata.Read(saveDir);
        var gameDate = metadata.Date;
        var modified = metadata.Modified;
        var isAuto = dirName.Equals("Save", StringComparison.OrdinalIgnoreCase);
        return new SaveEntry
        {
            SaveDirectory = saveDir,
            IsAutosave = isAuto,
            LastModified = modified,
            GameDate = gameDate,
            DisplayName = metadata.SaveName+(isAuto?" (Autosave)":"")
        };
    }

    private static string FormatSaveFolderName(string dirName)
    {
        var m = Regex.Match(dirName, @"^Save_?(\d{1,2})_(\d{1,2})_(\d{4})_(\d{1,2})_(\d{1,2})_(\d{1,2})$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var d) && int.TryParse(m.Groups[2].Value, out var mo) &&
            int.TryParse(m.Groups[3].Value, out var y) && int.TryParse(m.Groups[4].Value, out var h) &&
            int.TryParse(m.Groups[5].Value, out var mi) && int.TryParse(m.Groups[6].Value, out var s))
        {
            try { return new DateTime(y, mo, d, h, mi, s).ToString("MMM d, yyyy  h:mm:ss tt"); } catch { }
        }
        return dirName;
    }

    private static string ReadGameDate(string scenarioFile)
    {
        if (!File.Exists(scenarioFile)) return string.Empty;
        try
        {
            var lines = File.ReadAllLines(scenarioFile);
            if (lines.Length > 4 && int.TryParse(lines[2], out var d) && int.TryParse(lines[3], out var m) && int.TryParse(lines[4], out var y))
                return new DateTime(y, m, d).ToString("MMM d, yyyy");
        }
        catch { }
        return string.Empty;
    }

    private static string FindCampaignOwner(string saveDir, string campaignsRoot)
    {
        var current = Directory.GetParent(saveDir);
        while (current is not null && current.FullName.StartsWith(campaignsRoot, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(current.FullName, "ScenarioDescr.txt"))) return current.FullName;
            if (Path.GetFullPath(current.FullName).TrimEnd(Path.DirectorySeparatorChar)
                .Equals(Path.GetFullPath(campaignsRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) break;
            current = current.Parent;
        }
        return Directory.GetParent(saveDir)?.FullName ?? campaignsRoot;
    }

    private static string ReadCampaignName(string campaignDir, string campaignsRoot)
    {
        var descriptor = Path.Combine(campaignDir, "ScenarioDescr.txt");
        if (File.Exists(descriptor))
        {
            try
            {
                var useful = File.ReadLines(descriptor)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("//"))
                    .Take(2)
                    .ToArray();
                if (useful.Length > 0)
                {
                    var candidate = useful[0];
                    if (candidate.Length > 90) candidate = candidate[..90] + "…";
                    return $"{new DirectoryInfo(campaignDir).Name} — {candidate}";
                }
            }
            catch { }
        }
        return Path.GetRelativePath(campaignsRoot, campaignDir);
    }

    private static string? FindChildDirectory(string root, string expectedName)
    {
        var direct = Path.Combine(root, expectedName);
        if (Directory.Exists(direct)) return direct;
        try
        {
            return Directory.EnumerateDirectories(root)
                .FirstOrDefault(d => new DirectoryInfo(d).Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    private IEnumerable<string> GetSteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, key) in new[]
        {
            (RegistryHive.CurrentUser, @"Software\Valve\Steam"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam")
        })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var sub = baseKey.OpenSubKey(key);
                var value = sub?.GetValue("InstallPath")?.ToString() ?? sub?.GetValue("SteamPath")?.ToString();
                if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value)) roots.Add(value);
            }
            catch { }
        }
        return roots;
    }

    private static IEnumerable<string> ParseSteamLibraries(string vdf)
    {
        if (!File.Exists(vdf)) yield break;
        string text;
        try { text = File.ReadAllText(vdf); } catch { yield break; }
        foreach (Match m in Regex.Matches(text, "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase))
        {
            var path = m.Groups["path"].Value.Replace("\\\\", "\\");
            if (Directory.Exists(path)) yield return path;
        }
    }

    private string? ReadRememberedPath() => ReadSettings().InstallationRoot;

    private void RememberPath(string path)
    {
        var settings = ReadSettings();
        settings.InstallationRoot = path;
        WriteSettings(settings);
    }

    private AppSettings ReadSettings()
    {
        try
        {
            var loadPath=AppPaths.ReadPath("settings.json");
            if (!File.Exists(loadPath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(loadPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    private void WriteSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private sealed class AppSettings
    {
        public string? InstallationRoot { get; set; }
        public bool ConfirmedAutoDetection { get; set; }
    }
}
