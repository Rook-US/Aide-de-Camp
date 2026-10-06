using System.IO;
namespace AideDeCamp.Services;

public static class ManagementBatchPlanner
{
    public static List<ManagementDocument.Field> CommonFields(ManagementDocument doc,IReadOnlyList<ManagementDocument.Record> records) {
        if(records.Count==0)return new();
        return records[0].Fields.Where(f=>records.All(r=>r.Domain==records[0].Domain && r.Fields.Any(other=>other.Key==f.Key&&other.Kind==f.Kind&&other.Min==f.Min&&other.Max==f.Max)))
            .Where(f=>records.All(r=>{
                var field=r.Fields.Single(x=>x.Key==f.Key);
                if(field.Kind is "completion" or "fraction")return doc.Numeric(field.File,field.Line)>0;
                if(field.Key=="OrderQuantity")return doc.Numeric(field.File,field.Line)>0&&doc.Numeric(field.File,field.Line+2)>0;
                return true;
            })).ToList();
    }
    public static Dictionary<ManagementDocument.Field,string> Plan(ManagementDocument doc,IReadOnlyList<ManagementDocument.Record> records,IReadOnlyDictionary<string,string> checkedValues) {
        if(records.Count==0||checkedValues.Count==0)throw new InvalidOperationException("Select records and check at least one field.");
        var common=CommonFields(doc,records).Select(f=>f.Key).ToHashSet();
        if(checkedValues.Keys.Any(k=>!common.Contains(k)))throw new InvalidDataException("A checked field is not editable for every selected record.");
        var result=new Dictionary<ManagementDocument.Field,string>();
        foreach(var record in records)foreach(var (key,value) in checkedValues) {
            var field=record.Fields.Single(f=>f.Key==key);
            try{result.Add(field,doc.Validate(field,value));}catch(Exception ex){throw new InvalidDataException(record.Name+": "+ex.Message,ex);}
        }
        var before=doc.Capture();try{doc.Apply(result);}finally{doc.Restore(before);}return result;
    }
}
