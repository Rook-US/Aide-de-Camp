using System.Globalization;
using System.IO;
using System.Text.Json;

namespace AideDeCamp.Services;

// Each editable field is tied to a validated record and a loaded byte-preserving buffer.
// Validated counted progression lists may grow; scalar addresses stay relative to the loaded buffer.
// No guessed line searches or cached recruitment writes are used.
public sealed class ManagementDocument
{
    public sealed record Field(string File,int Line,string Key,string Label,string Kind="number",double Min=0,double Max=float.MaxValue,int? MirrorLine=null);
    public sealed record Record(string Domain,int Id,int Side,string Name,List<Field> Fields);
    public sealed record Stock(int Side,int WeaponId,int StockLine,int OrderLine,int StandardizationLine);
    private readonly Dictionary<string,TextFileBuffer> _buffers=new();
    private readonly Dictionary<string,List<string>> _loaded=new();
    private readonly Dictionary<(string File,int Line),string> _changes=new();
    private Dictionary<(string File,int Line),string> _saved=new();
    private Dictionary<(string File,int Line),Field> _lists=new();
    public List<Record> Records {get;}=new();
    public List<Stock> Stocks {get;}=new();
    public List<string> Notices {get;}=new();
    private IEnumerable<KeyValuePair<(string File,int Line),string>> Unsaved()=>Capture().Where(p=>!_saved.TryGetValue(p.Key,out var v)||v!=p.Value);
    public bool HasChanges=>Unsaved().Any();
    public int ChangedFields=>Unsaved().Count();
    static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
    public static ManagementDocument Load(string directory)
    {
        var doc=new ManagementDocument();
        var version=Path.Combine(directory,"version.dat");
        if(!File.Exists(version) || !float.TryParse(File.ReadLines(version).FirstOrDefault(),NumberStyles.Float,Inv,out var v) || v!=1.142f) {
            doc.Notices.Add("Management editing requires the mapped 1.142 save format.");return doc;
        }
        void Domain(string file,Action<List<string>> parse) {
            var recordCount=doc.Records.Count;var stockCount=doc.Stocks.Count;
            try {var path=Path.Combine(directory,file);if(!File.Exists(path))return;var buffer=TextFileBuffer.Read(path);parse(buffer.Lines);doc._buffers.Add(file,buffer);doc._loaded.Add(file,buffer.CloneLines());}
            catch(Exception ex) when(ex is FormatException or OverflowException or ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException) {
                doc.Records.RemoveRange(recordCount,doc.Records.Count-recordCount);doc.Stocks.RemoveRange(stockCount,doc.Stocks.Count-stockCount);doc.Notices.Add(file+": editing unavailable — "+ex.Message);
            }
        }
        Domain("commanders.txt",l=>{
            int n=Count(l,66);var ids=new HashSet<int>();
            for(int i=0;i<n;i++) {
                int p=1+66*i,id=Int(l[p]),side=Int(l[p+4]);if(!ids.Add(id))throw new InvalidDataException("Duplicate officer ID.");
                var f=new List<Field>{new("commanders.txt",p+5,"Experience","Experience (0–100)",Max:100),new("commanders.txt",p+11,"Veteran","Veteran","bool"),new("commanders.txt",p+12,"WestPoint","West Point","bool"),new("commanders.txt",p+55,"Political","Political officer","bool"),new("commanders.txt",p+13,"Branch","Historical branch (−1 none; 0 infantry, 1 cavalry, 2 artillery, 3 engineer, 4 navy)","integer",-1,4)};
                foreach(var (offset,key) in new[]{(6,"Fame"),(7,"Leadership"),(8,"Initiative"),(9,"Administration"),(10,"Cunning")})f.Insert(f.Count-4,new("commanders.txt",p+offset,key,key+" (0–100)",Max:100));
                string[] ranks={"Lieutenant","Captain / Navy commander","Major / Navy captain","Lieutenant Colonel / Navy flag officer","Colonel","Brigadier General","Major General","Lieutenant General","General"};
                for(int rank=1;rank<=9;rank++) f.Add(new("commanders.txt",p+17+(rank-1)*3,"Promotion"+rank,$"{ranks[rank-1]} promotion (YYYY-MM-DD; blank = unset)","date"));
                foreach(var field in f.Where(f=>f.Kind!="date")) {
                    // Preserve existing mod/game values outside the editor's permitted input range.
                    if(field.Key is "Fame" or "Leadership" or "Initiative" or "Administration" or "Cunning")Number(l[field.Line]);
                    else doc.Validate(field,l[field.Line]);
                }
                doc.Records.Add(new("Officers",id,side,l[p+3],f));
            }
        });
        Domain("ships.dat",l=>{
            int n=Count(l,23);var ids=new HashSet<int>();
            for(int i=0;i<n;i++) {int p=1+23*i,id=Int(l[p]);if(!ids.Add(id))throw new InvalidDataException("Duplicate vessel ID.");
                var f=new List<Field>{new("ships.dat",p+1,"Name","Ship name","text")};
                foreach(var (offset,key) in new[]{(5,"Condition"),(6,"Provisions"),(7,"Coal"),(8,"Ammunition")})f.Add(new("ships.dat",p+offset,key,key+" (%)",Max:100));
                f.Add(new("ships.dat",p+17,"ConstructionCompletion","Construction completed (%; 100 = finished)","completion",0,100));
                f.Add(new("ships.dat",p+18,"RepairRemaining","Repair work remaining (%; 0 = finished)","fraction",0,100));
                doc.Records.Add(new("Navy",id,Int(l[p+3]),l[p+1],f));
            }
        });
        Domain("nations.dat",l=>doc.ParseNations(l));
        doc._lists=doc.Records.SelectMany(r=>r.Fields).Where(f=>f.Kind is "ids" or "policies").ToDictionary(f=>(f.File,f.Line));
        doc._saved=doc.Capture();
        return doc;
    }
    static int Count(List<string> l,int size) {if(l.Count==0)throw new InvalidDataException("Empty file.");int n=Int(l[0]);if(n<0 || l.Count!=1L+(long)n*size)throw new InvalidDataException("Record size mismatch.");return n;}
    static int Int(string s)=>int.Parse(s,Inv);
    static float Number(string s) {float v=float.Parse(s,Inv);if(!float.IsFinite(v))throw new InvalidDataException("Non-finite value.");return v;}
    public string Value(Field f) {
        if(f.Kind=="completion")return (100*(1-Numeric(f.File,f.Line))).ToString("0.#####",Inv);
        if(f.Kind=="fraction")return (100*Numeric(f.File,f.Line)).ToString("0.#####",Inv);
        if(f.Kind is "ids" or "policies")return Line(f.File,f.Line);
        if(f.Kind!="date")return Line(f.File,f.Line);
        int d=Int(Line(f.File,f.Line)),m=Int(Line(f.File,f.Line+1)),y=Int(Line(f.File,f.Line+2));
        return d<=0 || m<=0 || y<=0?"":new DateTime(y,m,d).ToString("yyyy-MM-dd",Inv);
    }
    private string Baseline(string file,int line) {
        var field=_lists.GetValueOrDefault((file,line));
        return field is null?_buffers[file].Lines[line]:JsonSerializer.Serialize(_buffers[file].Lines.Skip(line+1).Take(Int(_buffers[file].Lines[line])*(field.Kind=="policies"?2:1)).ToArray());
    }
    public string Line(string file,int line)=>_changes.TryGetValue((file,line),out var value)?value:Baseline(file,line);
    public List<string> ListValue(Field field)=>JsonSerializer.Deserialize<List<string>>(Value(field))!;
    public double Numeric(string file,int line)=>Number(Line(file,line));
    public List<string>? OriginalLines(string file)=>_loaded.TryGetValue(file,out var l)?new(l):null;
    public List<string>? WorkingLines(string file) {
        if(!_buffers.TryGetValue(file,out var b))return null;var l=b.CloneLines();foreach(var (key,value) in _changes.Where(x=>x.Key.File==file).OrderByDescending(x=>x.Key.Line)) {
            var f=_lists.GetValueOrDefault(key);
            if(f is null)l[key.Line]=value;
            else {int width=f.Kind=="policies"?2:1;var values=JsonSerializer.Deserialize<List<string>>(value)!;l.RemoveRange(key.Line,1+Int(b.Lines[key.Line])*width);l.InsertRange(key.Line,new[]{(values.Count/width).ToString(Inv)}.Concat(values));}
        }return l;
    }
    private static IEnumerable<int> FieldLines(Field f)=>f.MirrorLine is int mirror?new[]{f.Line,mirror}:Enumerable.Range(f.Line,f.Kind=="date"?3:1);
    public Dictionary<(string File,int Line),string> Capture()=>Records.SelectMany(r=>r.Fields).SelectMany(f=>FieldLines(f).Select(line=>(f.File,Line:line))).Distinct().ToDictionary(k=>k,k=>Line(k.File,k.Line));
    public void Restore(Dictionary<(string File,int Line),string> state) {_changes.Clear();foreach(var p in state)if(Baseline(p.Key.File,p.Key.Line)!=p.Value)_changes.Add(p.Key,p.Value);}
    public void Apply(IReadOnlyDictionary<Field,string> values) {
        var before=Capture();try {
            var expanded=new Dictionary<Field,string>(values);
            foreach(var ship in Records.Where(r=>r.Domain=="Navy" && r.Fields.Any(f=>values.ContainsKey(f)&&f.Kind is "completion" or "fraction"))) {
                var condition=ship.Fields.Single(f=>f.Key=="Condition");double change=0;
                foreach(var timer in ship.Fields.Where(f=>values.ContainsKey(f)&&f.Kind is "completion" or "fraction")) {
                    double target=Number(Validate(timer,values[timer])),current=Number(Value(timer));
                    if(Numeric(timer.File,timer.Line)<=0 && (timer.Kind=="completion"?target<100:target>0))throw new InvalidDataException(ship.Name+": cannot restart construction or create a new repair job.");
                    change+=timer.Kind=="completion"?target-current:current-target;
                }
                if(!expanded.ContainsKey(condition))expanded[condition]=Math.Clamp(Numeric(condition.File,condition.Line)+change,0,100).ToString("R",Inv);
            }
            foreach(var (field,value) in expanded)Set(field,value);ValidateOrders();
        }catch{Restore(before);throw;}
    }
    public string Validate(Field f,string text) {
        if(f.Kind is "ids" or "policies") {
            var values=JsonSerializer.Deserialize<List<string>>(text)??throw new InvalidDataException("Missing progression list.");int width=f.Kind=="policies"?2:1;
            if(values.Count%width!=0 || values.Count>20000)throw new InvalidDataException("Invalid progression list size.");
            for(int i=0;i<values.Count;i+=width){Int(values[i]);if(width==2 && (Number(values[i+1])<0 || Number(values[i+1])>1))throw new InvalidDataException("Policy progress must be between zero and one.");}
            return JsonSerializer.Serialize(values);
        }
        if(text.IndexOfAny(new[]{'\r','\n','\0'})>=0)throw new InvalidDataException(f.Label+": invalid line break.");
        if(f.Kind=="text") {if(string.IsNullOrWhiteSpace(text) || text.Length>160)throw new InvalidDataException("Names must contain 1–160 characters.");return text.Trim();}
        if(f.Kind=="bool")return bool.Parse(text).ToString();
        if(f.Kind=="date") {if(string.IsNullOrWhiteSpace(text))return "";if(!DateTime.TryParseExact(text,"yyyy-MM-dd",Inv,DateTimeStyles.None,out var date) || date.Year<1700 || date.Year>2100)throw new InvalidDataException(f.Label+": enter a valid date from 1700 through 2100.");return date.ToString("yyyy-MM-dd",Inv);}
        if(f.Kind=="integer") {if(!int.TryParse(text,NumberStyles.Integer,Inv,out var n) || n<f.Min || n>f.Max)throw new InvalidDataException($"{f.Label}: enter an integer from {f.Min:N0} to {f.Max:N0}.");return n.ToString(Inv);}
        var v=Number(text);if(v<f.Min || v>f.Max)throw new InvalidDataException($"{f.Label}: range {f.Min:N0} to {f.Max:N0}.");return v.ToString("R",Inv);
    }
    void Set(Field f,string text) {
        if(!Records.Any(r=>r.Fields.Contains(f)))throw new InvalidOperationException("Unknown editable field.");
        var value=Validate(f,text);
        if(f.Kind is "completion" or "fraction") {
            if(Math.Abs(Number(Value(f))-Number(value))<0.000001)return;
            value=((float)(f.Kind=="completion"?1-Number(value)/100d:Number(value)/100d)).ToString("R",Inv);
            var key=(f.File,f.Line);if(Number(Baseline(f.File,f.Line))==Number(value))_changes.Remove(key);else _changes[key]=value;return;
        }
        if(f.Kind is "ids" or "policies") {var key=(f.File,f.Line);if(Baseline(f.File,f.Line)==value)_changes.Remove(key);else _changes[key]=value;return;}
        void Patch(int line,string s) {var key=(f.File,line);if(_buffers[f.File].Lines[line]==s)_changes.Remove(key);else _changes[key]=s;}
        if(Value(f)==value)return;
        if(f.Kind=="date") {
            if(value=="") {for(int j=0;j<3;j++)Patch(f.Line+j,"-1");}
            else {var date=DateTime.ParseExact(value,"yyyy-MM-dd",Inv);Patch(f.Line,date.Day.ToString(Inv));Patch(f.Line+1,date.Month.ToString(Inv));Patch(f.Line+2,date.Year.ToString(Inv));}
        } else {
            // Keep the original lexical representation when the numeric/bool value is unchanged.
            var original=_buffers[f.File].Lines[f.Line];
            bool same=f.Kind=="bool"?bool.Parse(original)==bool.Parse(value):f.Kind=="text"?original==value:f.Kind=="integer"?Int(original)==Int(value):Number(original)==Number(value);
            Patch(f.Line,same?original:value);
            if(f.MirrorLine is int mirror)Patch(mirror,value);
        }
    }
    void ValidateOrders() {foreach(var stock in Stocks)if(stock.OrderLine>=0 && _changes.ContainsKey(("nations.dat",stock.OrderLine+1))) {
        if(Numeric("nations.dat",stock.OrderLine+1)<Numeric("nations.dat",stock.OrderLine+5))throw new InvalidDataException("Order total cannot be less than the quantity already delivered.");
        if(Number(_buffers["nations.dat"].Lines[stock.OrderLine+1])<=0 || Numeric("nations.dat",stock.OrderLine+3)<=0)throw new InvalidDataException("This weapon has no active order to resize. Increase its stock directly instead.");
    }}
    public List<(string Name,TextFileBuffer Buffer,List<string> Lines)> Targets()=>_buffers.Where(x=>Unsaved().Any(k=>k.Key.File==x.Key)).Select(x=>{
        var edits=Records.SelectMany(r=>r.Fields).Where(f=>f.File==x.Key && f.Kind is "ids" or "policies").Where(f=>_changes.ContainsKey((f.File,f.Line))).Select(f=>(Start:f.Line,Removed:1+Int(x.Value.Lines[f.Line])*(f.Kind=="policies"?2:1),Added:1+ListValue(f).Count));
        return (x.Key,x.Value.WithSplices(edits),WorkingLines(x.Key)!);
    }).ToList();
    public void AcceptSaved() {_saved=Capture();}
    public IEnumerable<string> Review()=>Unsaved().OrderBy(x=>x.Key.File).ThenBy(x=>x.Key.Line).Select(x=>{
        var record=Records.First(r=>r.Fields.Any(f=>f.File==x.Key.File && FieldLines(f).Contains(x.Key.Line)));
        return $"{record.Domain}: {record.Name} [ID {record.Id}] — {x.Key.File}, original line {x.Key.Line+1}: {_saved.GetValueOrDefault(x.Key)} → {x.Value}";
    });
    void ParseNations(List<string> l) {
        int p=0;string phase="states";string Take(){if(p>=l.Count)throw new InvalidDataException("Truncated nations data.");return l[p++];}
        void Skip(int n){if(n<0 || (long)p+n>l.Count)throw new InvalidDataException("Invalid array length near line "+(p+1));p+=n;}
        int Array(int width=1){var raw=Take();if(!int.TryParse(raw,NumberStyles.Integer,Inv,out int n) || n<0 || n>100000)throw new InvalidDataException("Invalid count ("+phase+") near line "+p+": "+raw);Skip(checked(n*width));return n;}
        int states=Int(Take());if(states<1 || states>10000)throw new InvalidDataException("Invalid state count.");var ids=new HashSet<int>();
        for(int n=0;n<states;n++) {
            int id=Int(Take());if(!ids.Add(id))throw new InvalidDataException("Duplicate state ID.");Skip(1);if(Array()!=2)throw new InvalidDataException("Unsupported state support slots.");Skip(7);int pop=p;Number(Take());Skip(7);Array();Skip(4);Array();Skip(4);Array();Skip(1009);
            Records.Add(new("Economy",id,-1,$"State {id}",new(){new("nations.dat",pop,"Population","State population (shared by both factions)",Min:1,Max:1000000000)}));
        }
        int factions=Int(Take());if(factions<2 || factions>32)throw new InvalidDataException("Invalid faction count.");
        for(int side=0;side<factions;side++) {
            phase=$"faction {side} stock";
            Skip(10);Array();Array();int treasuryFirst=p;Number(Take());Skip(6);Array();
            int stockCount=Int(Take()),stockStart=p;Skip(stockCount);int treasuryFinal=p;Number(Take());Skip(2);Array();Array();Array(2);Skip(4);
            int policyLine=p;Array(2);int actsLine=p;Array();
            Records.Add(new("Progression",side,side,$"Faction {side} progression",new(){new("nations.dat",policyLine,"Policies","Policies and acts","policies"),new("nations.dat",actsLine,"PastActs","Previously activated acts","ids")}));
            Skip(3);Array();Array();Array();Array();
            Records.Add(new("Treasury",side,side,side==0?"Union treasury":side==1?"Confederate treasury":$"Faction {side} treasury",new(){new("nations.dat",treasuryFinal,"Treasury","National treasury balance ($)",Min:-100000000,Max:100000000000,MirrorLine:treasuryFirst)}));
            phase=$"faction {side} industry";Skip(27);Array();Array();Skip(122);Skip(16);Array(2);Skip(3);
            int fundsCount=Int(Take());if(fundsCount<6 || fundsCount>32)throw new InvalidDataException("Unrecognized subsidy funding layout.");
            for(int fund=0;fund<fundsCount;fund++){int line=p;Number(Take());if(fund<6)Records.Add(new("Funding",fund,side,new[]{"Politics","Economy","Agriculture","Industry","Military","Diplomacy"}[fund],new(){new("nations.dat",line,"Funds","Accumulated project funds ($)",Max:100000000000)}));}
            phase=$"faction {side} construction";Array(6);Skip(1);Trailing();int projectsLine=p;Array();
            Records.Single(r=>r.Domain=="Progression"&&r.Side==side).Fields.Add(new("nations.dat",projectsLine,"Projects","Completed project levels","ids"));Skip(2);
            phase=$"faction {side} orders";int orders=Int(Take()),orderStart=p;Skip(checked(orders*6));Skip(2);Trailing();
            int standards=Int(Take()),standardStart=p;phase=$"faction {side} standardization/reports stock={stockCount}@{stockStart} orders={orders}@{orderStart} standards={standards}@{standardStart}";Skip(standards);Skip(3);Array();Array();Array();Array(3);Skip(2);Array();Skip(1);Array();
            if(stockCount<0 || stockCount>10000 || orders!=stockCount || standards!=stockCount)throw new InvalidDataException("Weapon array sizes do not agree.");
            for(int w=0;w<stockCount;w++) {
                Number(l[stockStart+w]);int q=orderStart+6*w;Int(l[q+1]);Number(l[q+5]);Number(l[standardStart+w]);
                Stocks.Add(new(side,w,stockStart+w,q,standardStart+w));
                Records.Add(new("Weapons",w,side,$"Weapon {w}",new(){new("nations.dat",stockStart+w,"Stock","Stock on hand (pieces)",Max:100000000),new("nations.dat",q+1,"OrderQuantity","Existing order total (pieces; delivered amount preserved)","integer",0,100000000),new("nations.dat",standardStart+w,"StandardizationYear","Standardization start (campaign year)",Min:1700,Max:2100)}));
            }
        }
        if(!bool.TryParse(Take(),out _) || !bool.TryParse(Take(),out _))throw new InvalidDataException("Alliance boundary mismatch.");
        void Trailing(){Skip(1);Array();Array();Skip(1);}
    }
}
