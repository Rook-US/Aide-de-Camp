using System.Globalization;
using System.IO;

namespace AideDeCamp.Services;
public sealed record ArtilleryRules(int Maximum,float MenToSprites,float GunsPerSprite)
{
    public int? Count(int savedMen,double sickPercent) {
        if(savedMen<0 || !double.IsFinite(sickPercent) || sickPercent<0 || Maximum<=0)return null;
        int capped=Math.Min(savedMen,Maximum);
        int sick=(int)MathF.Max(0,capped*MathF.Min((float)sickPercent,100)/100);
        int strength=Math.Clamp(capped-sick,0,Maximum);
        float guns=MathF.Ceiling(MathF.Ceiling(strength*MenToSprites)*GunsPerSprite);
        return float.IsFinite(guns) && guns>=0 && guns<int.MaxValue?(int)guns:null;
    }
}
public static class CampaignRules
{
    public sealed record ShipClass(int Id,string Name,int Guns,float BuildYears) {
        public int? WorkDays(double remaining) {
            var days=MathF.Ceiling(BuildYears*365.25f*(float)remaining);
            return remaining>=0 && float.IsFinite(days) && days<int.MaxValue?(int)days:null;
        }
    }
    public static IReadOnlyDictionary<int,ShipClass> Ships(string save,string? config) {
        var result=new Dictionary<int,ShipClass>();
        var file=Resolve(save,config,"shiptypes.dat");
        var prefs=Resolve(save,config,"campaignprefs.txt");
        if(file is null || prefs is null)return result;
        try {
            var settings=File.ReadAllLines(prefs);
            int setting=Array.FindIndex(settings,s=>s.StartsWith("Building duration in game years per resource",StringComparison.OrdinalIgnoreCase));
            if(setting<0 || setting+2>=settings.Length)return result;
            float factor=float.Parse(settings[setting+2],CultureInfo.InvariantCulture);
            if(!float.IsFinite(factor) || factor<=0)return result;
            var lines=File.ReadAllLines(file);int p=0;
            string Take()=>p<lines.Length?lines[p++]:throw new InvalidDataException("Truncated ship class.");
            int I()=>int.Parse(Take(),CultureInfo.InvariantCulture);
            float F()=>float.Parse(Take(),CultureInfo.InvariantCulture);
            void Skip(int n){for(int j=0;j<n;j++)Take();}
            int count=I();if(count<0 || count>10000)throw new InvalidDataException("Invalid ship class count.");
            for(int i=0;i<count;i++) {
                int id=I();string name=Take();Skip(10);int guns=I();Skip(15);
                int resources=I();if(resources<0 || resources>10000)throw new InvalidDataException("Invalid resource count.");
                float duration=0;for(int j=0;j<resources;j++){I();duration+=F()*factor;}
                Skip(7+14); // Three techs, port level, three upgrades; thirteen stats and research flags.
                if(guns<0 || !float.IsFinite(duration) || duration<0 || !result.TryAdd(id,new(id,name,guns,duration)))throw new InvalidDataException("Invalid ship class.");
            }
            if(p!=lines.Length)throw new InvalidDataException("Unrecognized ship class tail.");
            return result;
        }catch(Exception ex) when(ex is IOException or FormatException or OverflowException or InvalidDataException) {
            ErrorLog.Write("Read ship class configuration",ex);return new Dictionary<int,ShipClass>();
        }
    }
    public static string? Resolve(string save,string? config,string filename) {
        var scenario=Directory.GetParent(save)?.FullName;
        if(scenario is not null && File.Exists(Path.Combine(scenario,filename)))return Path.Combine(scenario,filename);
        var local=Path.Combine(save,filename);if(File.Exists(local))return local;
        return config is not null && File.Exists(Path.Combine(config,filename))?Path.Combine(config,filename):null;
    }
    public static double? Setting(string? file,string label) {
        if(file is null)return null;var lines=File.ReadAllLines(file);
        for(int i=0;i+1<lines.Length;i++)if(lines[i].Trim().StartsWith(label,StringComparison.OrdinalIgnoreCase) && double.TryParse(lines[i+1],NumberStyles.Float,CultureInfo.InvariantCulture,out double v) && double.IsFinite(v))return v;
        return null;
    }
    public static ArtilleryRules? Artillery(string save,string? config) {
        var version=Path.Combine(save,"version.dat");if(!File.Exists(version) || File.ReadLines(version).FirstOrDefault()?.Trim()!="1.142")return null;
        var file=Resolve(save,config,"unitprefs.txt");if(file is null)return null;var l=File.ReadAllLines(file);
        int start=Array.FindIndex(l,s=>s.StartsWith("Unit Type 2#",StringComparison.OrdinalIgnoreCase));
        if(start<0 || start+11>=l.Length || l[start+1].Trim()!="Artillery")return null;
        if(!int.TryParse(l[start+3],out int maximum) || !float.TryParse(l[start+11],NumberStyles.Float,CultureInfo.InvariantCulture,out var factor))return null;
        var guns=Setting(file,"Guns per sprite to show up");
        return maximum>0 && factor>0 && float.IsFinite(factor) && guns is >0 and <=100?new(maximum,factor,(float)guns):null;
    }
}
