using System.Numerics;
using System.Reflection;
using TruckLib;
using TruckLib.ScsMap;
using TruckLib.Sii;

namespace MautAssistent;

internal static class MapGateSource
{
    internal sealed record Trigger(float Width,float Length);
    static readonly PropertyInfo? kdop=typeof(MapItem).GetProperty("Kdop",BindingFlags.Instance|BindingFlags.NonPublic);
    internal static MapGateArea? Capture(TruckLib.ScsMap.Gate gate,IReadOnlyDictionary<string,Trigger?> profiles)
    {
        if(gate.Layer!=0||gate.Type!=GateType.TriggerActivated||gate.ActivationPoints.Count!=1)return null;
        var metadata=kdop?.GetValue(gate);
        var flags=metadata?.GetType().GetField("Flags",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(metadata);
        if(flags is not FlagField f||f.Bits!=0)return null; // only the default trigger-activated layout
        var point=gate.ActivationPoints[0];
        if(!profiles.TryGetValue(point.Trigger,out var profile)||profile==null)return null;
        var result=new MapGateArea(gate.Uid,gate.Node.Position,gate.Node.Rotation,point.Node.Position,point.Node.Rotation,profile.Width,profile.Length);
        return MapGateApproach.Valid(result)?result:null;
    }
    internal static IReadOnlyDictionary<string,Trigger?> ReadProfiles(IModelAssets assets)
    {
        // Read every candidate file before publishing. The inexpensive text
        // selection skips parsing unrelated large hookup graphs, but an include
        // or any competing declaration of a selected ID must still be parsed.
        var files=assets.FilesBelow("/unit/hookup").Where(p=>p.EndsWith(".sii",StringComparison.Ordinal)).Take(8193).ToArray();
        if(files.Length>8192)throw new InvalidDataException("Gate trigger file budget");
        long total=0;var texts=new List<(string Path,string Text)>();
        foreach(string path in files) {
            using var stream=assets.Files.Open(path);
            if(stream.Length>8*1024*1024||(total+=stream.Length)>64*1024*1024)throw new InvalidDataException("Gate trigger byte budget");
            using var reader=new StreamReader(stream);texts.Add((path,reader.ReadToEnd()));
        }
        // Preserve the pinned parser's required-include resolution, reject
        // placements it would silently skip, and bound recursive reads.
        var required=new RequiredFiles(assets.Files);
        var result=new Dictionary<string,Trigger?>(StringComparer.Ordinal);var units=new Dictionary<string,Unit>(StringComparer.Ordinal);
        var conflicting=new HashSet<string>(StringComparer.Ordinal);
        var read=new HashSet<string>(StringComparer.Ordinal);
        void Load((string Path,string Text) file) {
            if(!read.Add(file.Path))return;
            foreach(var unit in SiiFile.Open(file.Path,required).Units) {
                if(units.TryGetValue(unit.Name,out var prior)&&!Same(prior,unit))conflicting.Add(unit.Name);
                units.TryAdd(unit.Name,unit);
                if(unit.Class!="gate_item_trigger"&&!result.ContainsKey(unit.Name))continue;
                var parsed=Parse(unit);
                if(conflicting.Contains(unit.Name)||result.TryGetValue(unit.Name,out var old)&&!Equals(old,parsed))result[unit.Name]=null;
                else result.TryAdd(unit.Name,parsed);
            }
        }
        foreach(var file in texts.Where(f=>f.Text.Contains("gate_item_trigger",StringComparison.Ordinal)||f.Text.Contains("@include",StringComparison.Ordinal)))Load(file);
        foreach(var file in texts.Where(f=>!read.Contains(f.Path)&&result.Keys.Any(id=>f.Text.Contains(id,StringComparison.Ordinal))))Load(file);
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string,Trigger?>(result);
    }
    sealed class RequiredFiles(IFileSystem inner):IFileSystem
    {
        long bytes;int reads;
        public char DirectorySeparator=>inner.DirectorySeparator;
        // The pinned parser first tries an include as absolute, then relative.
        // Preserve that lookup; Open still requires the resolved file.
        public bool FileExists(string path)=>inner.FileExists(path);
        public bool DirectoryExists(string path)=>inner.DirectoryExists(path);
        public IList<string> GetFiles(string path)=>inner.GetFiles(path);
        public Stream Open(string path) {
            if(++reads>16384)throw new InvalidDataException("Gate catalog include/read budget");
            if(!FileExists(path))throw new InvalidDataException("Missing gate catalog reference: "+path);
            var stream=inner.Open(path);
            if(stream.Length>8*1024*1024||(bytes+=stream.Length)>64*1024*1024){stream.Dispose();throw new InvalidDataException("Gate catalog include byte budget");}
            return stream;
        }
        public string ReadAllText(string path)=>ReadAllText(path,System.Text.Encoding.UTF8);
        public string ReadAllText(string path,System.Text.Encoding encoding){using var stream=Open(path);using var reader=new StreamReader(stream,encoding);var text=reader.ReadToEnd();ValidateIncludes(text);return text;}
        public byte[] ReadAllBytes(string path){using var stream=Open(path);var result=new byte[checked((int)stream.Length)];stream.ReadExactly(result);ValidateIncludes(System.Text.Encoding.UTF8.GetString(result));return result;}
        public string GetParent(string path)=>inner.GetParent(path);
        static void ValidateIncludes(string text)
        {
            if(!text.Contains("@include",StringComparison.Ordinal))return;
            var remove=typeof(SiiFile).Assembly.GetType("TruckLib.Sii.SiiMatUtils")?.GetMethod("RemoveComments",BindingFlags.Static|BindingFlags.Public);
            string clean=remove?.Invoke(null,[text]) as string??throw new InvalidDataException("Include parser contract unavailable");
            foreach(string line in clean.Split('\n'))if(line.Contains("@include",StringComparison.Ordinal)&&
                !System.Text.RegularExpressions.Regex.IsMatch(line,"^@include \"[^\"\r\n]+\"[\\t \\r]*$"))
                throw new InvalidDataException("Unsupported gate catalog include placement");
        }
    }
    static bool Same(Unit a,Unit b)=>a.Class==b.Class&&a.Attributes.Count==b.Attributes.Count&&a.Attributes.All(x=>
        b.Attributes.TryGetValue(x.Key,out var y)&&Equals((object?)x.Value,(object?)y));
    internal static Trigger? Parse(Unit unit)
    {
        object? Get(string k)=>unit.Attributes.TryGetValue(k,out var v)?(object?)v:null;
        static double Number(object? v)=>v switch{int n=>n,uint n=>n,float n=>n,double n=>n,_=>double.NaN};
        if(unit.Class!="gate_item_trigger"||Get("one_way") is not true||Get("automatic_trigger") is not bool||
            Get("interaction_mode") is not string mode||mode is not ("player_only" or "ai_and_player")||
            Get("model") is not string model||model!="/model/activation/border.pmd"||
            Get("trigger_width") is not { } width||Get("trigger_length") is not { } length)return null;
        if(new[]{"truck_allowed","bus_allowed","car_allowed"}.Any(k=>Get(k) is { } allowed&&allowed is not bool))return null;
        // Unsupported action/shape/condition attributes cannot silently widen
        // this narrow border-approach contract. No interaction is emitted.
        if(unit.Attributes.Keys.Any(k=>k is not ("trigger_name" or "interaction_mode" or "automatic_trigger" or "one_way" or
            "model" or "animation" or "truck_allowed" or "bus_allowed" or "car_allowed" or "message_title" or
            "message_closed" or "message_wait" or "message_open" or "trigger_length" or "trigger_width")))return null;
        double w=Number(width),l=Number(length);
        return double.IsFinite(w)&&w is >=2 and <=10&&double.IsFinite(l)&&l is >=2 and <=30?new((float)w,(float)l):null;
    }
}
