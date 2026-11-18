using System.Collections;
using TruckLib;
using TruckLib.Sii;

namespace MautAssistent;

internal sealed record RailSignalProfile(string[] Types,string[] Models,int[] IdMap)
{
    internal string Type(uint id)=>Types.Length==0?"":Types[(int)Math.Min(id,(uint)Types.Length-1)];
    internal string Model(uint id)=>Models.Length==0?"":Models[(int)Math.Min(id,(uint)Models.Length-1)];
    internal int MappedId(uint id)=>IdMap.Length==0?-1:IdMap[(int)Math.Min(id,(uint)IdMap.Length-1)];
}

// Read definitions on independent archive streams, never on a driving Tick or
// through the native parser's shared, seekable streams. Country overrides are
// admitted only when every variant gives the same controller/member semantics.
internal sealed class RailCrossingProfiles(Func<IFileSystem?> provider)
{
    readonly object sync=new();
    IFileSystem? source;
    Task<IReadOnlyDictionary<string,RailSignalProfile>>? pending;
    internal IReadOnlyDictionary<string,RailSignalProfile>? Read()
    {
        var current=provider();
        lock(sync) {
            if(current==null){source=null;pending=null;return null;}
            if(!ReferenceEquals(source,current)) {
                source=current;
                var paths=IndependentAssets.SnapshotPaths(current);
                pending=paths==null?null:Task.Run(()=> {
                    using var owned=new IndependentAssets(paths);
                    return Load(owned.Files);
                });
                if(pending!=null)_=pending.ContinueWith(t=>{_=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            }
            return pending?.IsCompletedSuccessfully==true?pending.Result:null;
        }
    }

    internal static IReadOnlyDictionary<string,RailSignalProfile> Load(IFileSystem files)
    {
        var paths=files.GetFiles("/def/world/").Where(p=>p.StartsWith("/def/world/semaphore_profile",StringComparison.Ordinal)&&p.EndsWith(".sii",StringComparison.Ordinal)).Distinct().ToArray();
        if(paths.Length is <1 or >128)throw new InvalidDataException("Semaphore profile catalogue missing or oversized");
        var units=paths.SelectMany(p=>SiiFile.Open(p,files).Units).Where(u=>u.Class=="tr_semaphore_profile").ToArray();
        return FromUnits(units);
    }

    internal static IReadOnlyDictionary<string,RailSignalProfile> FromUnits(IEnumerable<Unit> definitions)
    {
        const string prefix="tr_sem_prof.";
        var all=definitions.ToArray();
        if(all.Length>8192)throw new InvalidDataException("Semaphore profile catalogue oversized");
        var duplicate=all.GroupBy(u=>u.Name,StringComparer.Ordinal).Where(g=>g.Count()!=1).Select(g=>g.Key).ToHashSet(StringComparer.Ordinal);
        var lookup=all.Where(u=>!duplicate.Contains(u.Name)).ToDictionary(u=>u.Name,StringComparer.Ordinal);
        object[] Values(Unit u,string name)=>u.Attributes.TryGetValue(name,out var value)&&(object?)value is IEnumerable seq and not string?seq.Cast<object>().ToArray():[];
        string Text(object? value)=>value switch {LinkPointer p=>p.Value,string s=>s,Token t=>t.ToString(),_=>""};
        RailSignalProfile? Resolve(string name,HashSet<string> seen)
        {
            if(seen.Count>=16||!seen.Add(name)||!lookup.TryGetValue(name,out var u))return null;
            RailSignalProfile? inherited=null;
            if(u.Attributes.TryGetValue("inherited",out var parent)) {
                inherited=Resolve(Text((object?)parent),seen);if(inherited==null)return null;
            }
            var types=Values(u,"type");var models=Values(u,"model");var mapped=Values(u,"id_map");
            if(models.Any(v=>v is not (string or Token)))return null;
            var t=u.Attributes.ContainsKey("type")?types.Select(Text).ToArray():inherited?.Types??[];
            var m=u.Attributes.ContainsKey("model")?models.Select(Text).ToArray():inherited?.Models??[];
            var ids=u.Attributes.ContainsKey("id_map")?mapped.Select(v=>v switch {int i=>i,uint i when i<=int.MaxValue=>(int)i,_=>-1}).ToArray():inherited?.IdMap??[];
            if(t.Length is <1 or >256||m.Length is <1 or >256||ids.Length is <1 or >256||t.Any(x=>x.Length==0)||ids.Any(x=>x is <0 or >255))return null;
            return new(t,m,ids);
        }
        var result=new Dictionary<string,RailSignalProfile>(StringComparer.Ordinal);
        foreach(string name in lookup.Keys.Where(n=>n.StartsWith(prefix,StringComparison.Ordinal)&&!n[prefix.Length..].Contains('.'))) {
            var variants=all.Where(u=>u.Name==name||u.Name.StartsWith(name+".",StringComparison.Ordinal)).ToArray();
            var profiles=variants.Select(u=>Resolve(u.Name,[])).ToArray();
            if(profiles.Length==0||profiles.Any(p=>p==null))continue;
            var first=profiles[0]!;
            // Default-index semantics repeat the last array entry (SCS 1.9).
            bool Same(RailSignalProfile p)=>Enumerable.Range(0,256).All(i=>first.Type((uint)i)==p.Type((uint)i)&&
                (first.Model((uint)i).Length>0)==(p.Model((uint)i).Length>0)&&first.MappedId((uint)i)==p.MappedId((uint)i));
            if(profiles.All(p=>Same(p!)))result[name[prefix.Length..]]=first;
        }
        return result;
    }
}
