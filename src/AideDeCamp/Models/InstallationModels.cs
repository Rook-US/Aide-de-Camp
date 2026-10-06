using System.Collections.ObjectModel;

namespace AideDeCamp.Models;

public sealed class CampaignSaveGroup
{
    public string DisplayName { get; set; } = string.Empty;
    public string CampaignDirectory { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public ObservableCollection<SaveEntry> Saves { get; } = new();
    public override string ToString() => DisplayName;
}

public sealed class SaveEntry
{
    public string DisplayName { get; set; } = string.Empty;
    public string SaveDirectory { get; set; } = string.Empty;
    public string GameDate { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public bool IsAutosave { get; set; }
    public string DetailText => string.IsNullOrWhiteSpace(GameDate)
        ? $"Modified {LastModified:g}"
        : $"{GameDate}  •  Modified {LastModified:g}";
    public override string ToString() => DisplayName;
}
