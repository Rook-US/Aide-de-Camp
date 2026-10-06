using System.IO;

namespace AideDeCamp.Services;
public sealed record SaveMetadata(string Campaign,string SaveName,string Date,string Faction,DateTime Modified)
{
    public string Summary=>$"{Campaign}  •  {SaveName}  •  {Date}  •  Player: {Faction}";
    public static SaveMetadata Read(string directory,string? config=null) {
        string[] lines=Array.Empty<string>();
        try {var file=Path.Combine(directory,"scenario.dat");if(File.Exists(file))lines=TextFileBuffer.Read(file).Lines.ToArray();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
        string At(int index)=>lines.Length>index?lines[index].Trim():"";
        string date="Date unavailable";
        if(int.TryParse(At(2),out var day)&&int.TryParse(At(3),out var month)&&int.TryParse(At(4),out var year))try{date=new DateTime(year,month,day).ToString("MMM d, yyyy");}catch(ArgumentOutOfRangeException){}
        string code=At(12);string letter=code.Split('/','\\').LastOrDefault()??"";
        string campaign=At(25);
        if(campaign.Length==0 && config is not null && code.Length>0 && !Path.IsPathRooted(code) && !code.Contains("..")) {
            var descriptor=Path.Combine(Path.GetDirectoryName(config)!,"Campaigns",code.Replace('/',Path.DirectorySeparatorChar),"ScenarioDescr.txt");
            campaign=CampaignName(descriptor);
        }
        if(campaign.Length==0)campaign="Campaign";
        if(letter.Length>0)campaign=letter+" — "+campaign;
        string name=At(24);if(name.Length==0)name=new DirectoryInfo(directory).Name.Equals("Save",StringComparison.OrdinalIgnoreCase)?"Autosave":new DirectoryInfo(directory).Name;
        string faction=At(1);if(faction.Length==0)faction=At(0) switch {"0"=>"Union","1"=>"Confederacy",_=>"Unknown"};
        DateTime modified=DateTime.MinValue;
        try {modified=Directory.EnumerateFiles(directory).Where(f=>Path.GetExtension(f) is ".dat" or ".txt").Select(File.GetLastWriteTime).DefaultIfEmpty(Directory.GetLastWriteTime(directory)).Max();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
        return new(campaign,name,date,faction,modified);
    }
    public static string CampaignName(string descriptor) {
        try{return File.Exists(descriptor)?File.ReadLines(descriptor).Select(s=>s.Trim()).FirstOrDefault(s=>s.Length>0&&!s.StartsWith("//"))??"":"";}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return "";}
    }
}
