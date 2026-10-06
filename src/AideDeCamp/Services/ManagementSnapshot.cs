using System.Globalization;
using System.IO;

namespace AideDeCamp.Services;

// Read-only projections. New write domains must join the main save transaction before editing.
public sealed class ManagementSnapshot
{
    public sealed record Officer(int Id, string Name, int Side, int Rank, int Branch, double Experience, double Fame,
        double Leadership, double Initiative, double Administration, double Cunning, bool Veteran, bool WestPoint,
        bool Political, string DateOfRank, string Status);
    public sealed record State(int Id, int Side, string Name, double Population, double Support,
        int Available, int Deficit, int Recruited, long Capacity, bool Recruitable) {
        public bool IsVolunteerEditorState=>Recruitable && (Id is >=0 and <=40 or 46 or 47 or 50 or 51);
    }
    public sealed record Ship(int Id, string Name, int Side, int FleetId, int TypeId, double Condition,
        string Status, double? TravelDays, double? ConstructionComplete, bool InPort,double ConstructionRemaining=0,double RepairRemaining=0);
    public List<Officer> Officers { get; } = new();
    public List<State> States { get; } = new();
    public List<Ship> Ships { get; } = new();
    public List<string> Notices { get; } = new();
    static double D(string s) { var v=double.Parse(s, CultureInfo.InvariantCulture); if(!double.IsFinite(v)) throw new InvalidDataException("Non-finite value."); return v; }
    static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);
    public static ManagementSnapshot Read(string directory, IReadOnlyDictionary<int,string> names, IReadOnlyDictionary<int,int> fleetSides, Func<string,List<string>?>? working = null)
    {
        var result=new ManagementSnapshot();
        var version=Path.Combine(directory,"version.dat");
        string? versionText=null;
        try { if(File.Exists(version)) versionText=File.ReadLines(version).FirstOrDefault(); }
        catch(IOException ex) { result.Notices.Add(ex.Message); }
        if(!double.TryParse(versionText,NumberStyles.Float,CultureInfo.InvariantCulture,out var saveVersion) || !double.IsFinite(saveVersion) || Math.Abs(saveVersion-1.142)>0.00001)
        { result.Notices.Add("Management records are supported for save version 1.142. This save has not been mapped."); return result; }
        void ReadDomain(string name, Action<List<string>> read) {
            try { var file=Path.Combine(directory,name); if(File.Exists(file)) read(working?.Invoke(name) ?? TextFileBuffer.Read(file).Lines); else result.Notices.Add(name+" is missing."); }
            catch(Exception ex) when(ex is FormatException or OverflowException or ArgumentException or InvalidDataException or IOException) { result.Notices.Add(name+": "+ex.Message); }
        }
        ReadDomain("commanders.txt", l => {
            if(l.Count==0) throw new InvalidDataException("Empty commander file.");
            int count=I(l[0]); if(count<0 || l.Count!=1L+66L*count) throw new InvalidDataException("Unsupported commander record layout.");
            var rows=new List<Officer>(); var ids=new HashSet<int>();
            for(int i=0;i<count;i++) {
                int p=1+66*i,id=I(l[p]),rank=I(l[p+59]); if(!ids.Add(id)) throw new InvalidDataException("Duplicate officer ID.");
                string date="Not recorded";
                if(rank>=1 && rank<=9) { int q=p+17+(rank-1)*3; if(I(l[q])>0 && I(l[q+1])>0 && I(l[q+2])>0) date=$"{l[q+2]}-{l[q+1].PadLeft(2,'0')}-{l[q].PadLeft(2,'0')}"; }
                rows.Add(new Officer(id,l[p+3],I(l[p+4]),rank,I(l[p+13]),D(l[p+5]),D(l[p+6]),D(l[p+7]),D(l[p+8]),D(l[p+9]),D(l[p+10]),
                    bool.Parse(l[p+11]),bool.Parse(l[p+12]),bool.Parse(l[p+55]),date,l[p+60]));
            }
            result.Officers.AddRange(rows);
        });
        ReadDomain("nations.dat", l => {
            int p=0; string Take() { if(p>=l.Count) throw new InvalidDataException("Truncated state record.");return l[p++]; }
            void Skip(int n) { if(n<0 || (long)p+n>l.Count) throw new InvalidDataException("Invalid array length.");p+=n; }
            void Array() => Skip(I(Take()));
            int count=I(Take()); if(count<0 || count>10000) throw new InvalidDataException("Invalid state count.");
            var ids=new HashSet<int>();var rows=new List<State>();
            for(int n=0;n<count;n++) {
                int id=I(Take()); if(!ids.Add(id)) throw new InvalidDataException("Duplicate state ID."); I(Take());
                int supports=I(Take());if(supports!=2) throw new InvalidDataException("Unsupported support array.");
                double[] support={D(Take()),D(Take())};Skip(3);
                int[] vol={I(Take()),I(Take())};Skip(2);double pop=D(Take());D(Take());
                int[] used={I(Take()),I(Take())};Skip(4);Array();Skip(4);Array();Skip(2);
                bool recruitable=bool.Parse(Take());bool.Parse(Take());Array();Skip(1000);I(Take());Skip(8);
                for(int side=0;side<2;side++) rows.Add(new State(id,side,names.GetValueOrDefault(id,$"State {id}"),pop,support[side]*100,
                    Math.Max(0,vol[side]),(int)Math.Min(int.MaxValue,Math.Max(0L,-(long)vol[side])),used[side],(long)vol[side]+used[side],recruitable));
            }
            if(I(Take())<=0) throw new InvalidDataException("Missing alliance section.");result.States.AddRange(rows);
        });
        ReadDomain("ships.dat", l => {
            if(l.Count==0) throw new InvalidDataException("Empty ship file.");
            int count=I(l[0]);if(count<0 || l.Count!=1L+23L*count) throw new InvalidDataException("Unsupported ship record layout.");
            var ids=new HashSet<int>();var rows=new List<Ship>();
            for(int i=0;i<count;i++) {
                int p=1+23*i,id=I(l[p]),fleet=I(l[p+2]),pool=I(l[p+3]);if(!ids.Add(id)) throw new InvalidDataException("Duplicate ship ID.");
                double build=D(l[p+17]),repair=D(l[p+18]),eta=D(l[p+16]);
                bool moving=Enumerable.Range(0,3).Any(j=>D(l[p+10+j])!=D(l[p+13+j]));
                bool returningToFleet=bool.Parse(l[p+22]);
                bool harbor=Enumerable.Range(0,3).Any(j=>D(l[p+19+j])!=0);
                string status=build>0?"Under construction":moving?(returningToFleet?"Returning to fleet":fleet<0 || harbor?"Returning to harbor":"Moving to fleet"):repair>0?"Repairing":harbor?"Harbor duty":fleet>=0?"With fleet":"In port";
                rows.Add(new Ship(id,l[p+1],fleet>=0?fleetSides.GetValueOrDefault(fleet,-1):pool,fleet,I(l[p+4]),D(l[p+5]),status,
                    moving?eta:null,build>0?100*(1-build):null,build>0 || repair>0 || fleet<0 || harbor,build,repair));
            }
            result.Ships.AddRange(rows);
        });
        return result;
    }
}
