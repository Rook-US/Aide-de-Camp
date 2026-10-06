using System.Globalization;
using System.IO;
using System.Text.Json;

namespace AideDeCamp.Services;

public sealed class NationProgression
{
    public sealed record Project(int Id,string Name,int[] Sides,int[] RequiredPolicies,int Fund,double Cost,bool Repeating,string Description,string[] Scenarios,int[] Dlc);
    public sealed record Policy(int Id,string Name,int Side,int[] RequiredPolicies,bool IsAct,double Duration,string Description,string[] Scenarios);
    public sealed record Plan(Dictionary<ManagementDocument.Field,string> Changes,string Description);
    public List<Project> Projects {get;}=new();
    public List<Policy> Policies {get;}=new();
    public List<string> Notices {get;}=new();
    public string Scenario {get;}
    private readonly ManagementDocument _doc;
    private readonly string? _prefs;
    public static readonly string[] Funds={"Politics","Economy","Agriculture","Industry","Military","Diplomacy"};
    private static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
    public NationProgression(string save,string? config,ManagementDocument doc) {
        _doc=doc;_prefs=CampaignRules.Resolve(save,config,"campaignprefs.txt");
        var scenario=Path.Combine(save,"scenario.dat");var scenarioLines=File.Exists(scenario)?TextFileBuffer.Read(scenario).Lines:new();
        Scenario=scenarioLines.Count>12?scenarioLines[12].Split('/','\\')[0]:"";
        if(config is null){Notices.Add("Choose the game folder to load project and policy names.");return;}
        void Read(string file,Action<List<string>> parse) {
            try {parse(TextFileBuffer.Read(Path.Combine(config,file)).Lines);}
            catch(Exception ex) when(ex is IOException or ArgumentException or FormatException or OverflowException or InvalidDataException){Notices.Add(file+": "+ex.Message);ErrorLog.Write("Load nation definitions",ex);}
        }
        Read("projects.dat",l=>{
            int count=int.Parse(l[0],Inv);if(count<0 || count>10000 || l.Count<1+count*18 || l.Skip(1+count*18).Any(s=>!string.IsNullOrWhiteSpace(s)))throw new InvalidDataException("Unsupported project catalog layout.");
            var rows=new List<Project>();
            for(int i=0;i<count;i++){int p=1+i*18;rows.Add(new(int.Parse(l[p],Inv),l[p+1],Ids(l[p+2]),Ids(l[p+3]),int.Parse(l[p+4],Inv),double.Parse(l[p+5],Inv),bool.Parse(l[p+6]),l[p+13],Parts(l[p+16]),Ids(l[p+17])));}
            if(rows.Select(r=>r.Id).Distinct().Count()!=rows.Count)throw new InvalidDataException("Duplicate project IDs.");Projects.AddRange(rows);
        });
        Read("policies.dat",l=>{
            int p=0;string Take()=>p<l.Count?l[p++]:throw new InvalidDataException("Truncated policy catalog.");
            int count=int.Parse(Take(),Inv);if(count<0 || count>10000)throw new InvalidDataException("Invalid policy count.");var rows=new List<Policy>();
            for(int i=0;i<count;i++) {
                // Installed mod has one empty separator between complete records.
                while(p<l.Count && string.IsNullOrWhiteSpace(l[p]))p++;
                int id=int.Parse(Take(),Inv);var scenarios=Parts(Take());int n=int.Parse(Take(),Inv);if(n<0 || n>100)throw new InvalidDataException("Invalid prerequisites.");
                var prereq=new List<int>();for(int j=0;j<Math.Max(5,n);j++){var s=Take();if(j<n)prereq.Add(int.Parse(s,Inv));}
                double duration=double.Parse(Take(),Inv);bool act=bool.Parse(Take());Take();string name=Take(),description=Take();bool.Parse(Take());
                n=int.Parse(Take(),Inv);if(n<0 || n>100)throw new InvalidDataException("Invalid headlines.");for(int j=0;j<Math.Max(5,n)+3;j++)Take();
                rows.Add(new(id,name,id>=100?1:0,prereq.ToArray(),act,duration,description,scenarios));
            }
            if(l.Skip(p).Any(s=>!string.IsNullOrWhiteSpace(s)) || rows.Select(r=>r.Id).Distinct().Count()!=rows.Count)throw new InvalidDataException("Unsupported policy catalog tail or duplicate IDs.");Policies.AddRange(rows);
        });
    }
    static string[] Parts(string s)=>s.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    static int[] Ids(string s)=>Parts(s).Select(s=>int.Parse(s,Inv)).ToArray();
    public bool InScenario(string[] ids)=>Scenario.Length>0 && ids.Contains(Scenario);
    private ManagementDocument.Field Field(int side,string key)=>_doc.Records.Single(r=>r.Domain=="Progression" && r.Side==side).Fields.Single(f=>f.Key==key);
    public int Level(int side,int id)=>_doc.ListValue(Field(side,"Projects")).Count(s=>int.Parse(s,Inv)==id);
    public double Progress(int side,int id) {
        var l=_doc.ListValue(Field(side,"Policies"));double value=0;
        for(int i=0;i<l.Count;i+=2)if(int.Parse(l[i],Inv)==id)value=Math.Max(value,double.Parse(l[i+1],Inv));return value;
    }
    public string ProjectRestriction(Project p,int side) {
        if(!p.Sides.Contains(side) || !InScenario(p.Scenarios))return "Unavailable to this faction/scenario";
        if(p.Dlc.Length>0)return "DLC-specific completion effects need mapping";
        if(p.Id is >=91 and <=94 or 103 or 110 or 112 or 116 or 117)return "Immediate support, diplomacy or formation effects need mapping; fund and complete in game";
        if(p.RequiredPolicies.Length>0 && !p.RequiredPolicies.Any(id=>Progress(side,id)>=1))return "Requires one of: "+string.Join(", ",p.RequiredPolicies.Select(id=>Policies.FirstOrDefault(x=>x.Id==id)?.Name??$"Policy {id}"));
        return "";
    }
    public string PolicyRestriction(Policy p,int side)=>p.Side!=side || !InScenario(p.Scenarios)?"Unavailable to this faction/scenario":p.Duration<=0?"Pre-war choice: effects on campaign initialization need mapping":"";
    public const double NearCompletion = 0.99999;
    // UI staging lets the normal campaign research tick cross the completion boundary.
    public Plan CompletePolicy(int side,int id,bool finishInGame=false) {
        var field=Field(side,"Policies");var values=_doc.ListValue(field);var visiting=new HashSet<int>();var finished=new HashSet<int>();var names=new List<string>();
        void Complete(int target) {
            if(finished.Contains(target) || Progress(side,target)>=1)return;
            if(!visiting.Add(target))throw new InvalidDataException("Cyclic policy prerequisites.");
            var p=Policies.Single(x=>x.Id==target);var restriction=PolicyRestriction(p,side);if(restriction!="")throw new InvalidOperationException(p.Name+": "+restriction);
            foreach(int before in p.RequiredPolicies)Complete(before);
            var indices=Enumerable.Range(0,values.Count/2).Select(i=>i*2).Where(i=>int.Parse(values[i],Inv)==target).ToArray();
            double goal=finishInGame?NearCompletion:1;
            if(Progress(side,target)>=goal){finished.Add(target);visiting.Remove(target);return;}
            string stored=goal.ToString("R",Inv);
            if(indices.Length==0){values.Add(target.ToString(Inv));values.Add(stored);}else foreach(int i in indices)values[i+1]=stored;
            names.Add(p.Name);finished.Add(target);visiting.Remove(target);
        }
        Complete(id);if(names.Count==0)throw new InvalidOperationException(finishInGame?"Already complete or ready to finish in game.":"This policy is already complete.");
        return new(new(){{field,JsonSerializer.Serialize(values)}},(finishInGame?"Set to 99.999%: ":"Complete: ")+string.Join(", ",names)+". Includes unfinished prerequisites. "+(finishInGame?"Save and advance campaign time to finish through normal research; timing depends on research speed. ":"Effects are applied by the game after loading/advancing; ")+"No treasury or subsidy charge.");
    }
    // Board grouping is presentation only: core research families, then their prerequisites.
    public int PolicyCategory(Policy policy) {
        var visited=new HashSet<int>();
        int Category(Policy p) {
            if(!visited.Add(p.Id))return 0;
            try {
            int id=p.Id%100;
            if(id is >=0 and <=2)return 1;
            if(id is >=3 and <=6)return 2;
            if(id is >=7 and <=10)return 3;
            if(id is >=11 and <=14 or >=36 and <=38 or 47)return 4;
            if(id is >=15 and <=19)return 5;
            var categories=p.RequiredPolicies.Select(id=>Policies.FirstOrDefault(x=>x.Id==id)).Where(x=>x is not null).Select(x=>Category(x!)).Distinct().ToArray();
            return categories.Length==1?categories[0]:0;
            } finally {visited.Remove(p.Id);}
        }
        return Category(policy);
    }
    public Plan CompleteProject(int side,int id,int targetLevel) {
        var p=Projects.Single(x=>x.Id==id);var restriction=ProjectRestriction(p,side);if(restriction!="")throw new InvalidOperationException(restriction);
        int current=Level(side,id),maximum=p.Repeating?100:1;
        if(targetLevel<=current || targetLevel>maximum)throw new InvalidOperationException($"Choose a level above {current}, up to {maximum}. Completed levels cannot be removed.");
        var field=Field(side,"Projects");var values=_doc.ListValue(field);values.AddRange(Enumerable.Repeat(id.ToString(Inv),targetLevel-current));
        var changes=new Dictionary<ManagementDocument.Field,string>{{field,JsonSerializer.Serialize(values)}};
        void Stock(int weapon,double amount) {
            var f=_doc.Records.Single(r=>r.Domain=="Weapons" && r.Side==side && r.Id==weapon).Fields.Single(f=>f.Key=="Stock");
            changes[f]=_doc.Validate(f,(_doc.Numeric(f.File,f.Line)+amount*(targetLevel-current)).ToString("R",Inv));
        }
        if(id==5){Stock(36,3000);Stock(86,2500);}
        if(id is 10 or 11) {
            if(_prefs is null)throw new InvalidOperationException("Machine-gun grant settings unavailable.");var lines=TextFileBuffer.Read(_prefs).Lines;
            int line=lines.FindIndex(s=>s.StartsWith("machine guns, adding number of guns to stock",StringComparison.OrdinalIgnoreCase));
            if(line<0 || line+3>=lines.Count)throw new InvalidOperationException("Machine-gun grant settings unavailable.");
            if(id==10){Stock(37,double.Parse(lines[line+1],Inv));Stock(38,double.Parse(lines[line+2],Inv));}else Stock(12,double.Parse(lines[line+3],Inv));
        }
        return new(changes,$"{p.Name}: level {current} → {targetLevel}. Direct completion; accumulated funds and treasury are preserved. Unlocks are recalculated by the game after loading/advancing."+(id is 5 or 10 or 11?" Includes the game's one-time weapon stock grant.":""));
    }
}
