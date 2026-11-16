using System.Collections;
using System.Numerics;
using TruckLib;
using TruckLib.Models;
using TruckLib.Sii;
namespace MautAssistent;

// Resolve actual geometry, never infer clearance from an attachment's name.
internal sealed class StaticHookupGeometry(IFileSystem files,IReadOnlyDictionary<string,Unit> units,IReadOnlySet<string> nonSolid,
    Func<string,(Model Model,byte[] Pmg)>? readModel=null)
{
    readonly Dictionary<string,(Model Model,byte[] Pmg)> models=new(StringComparer.Ordinal);
    // This is a spawn definition, not a solid part of the toll structure. Its
    // actual vehicles are checked separately from a fresh game-side scene.
    internal bool HasDynamicParked {get;private set;}
    readonly List<ObstacleSphere> movers=[];
    MoverEnvelopeEvidence? moverEvidence;
    internal IReadOnlyList<ObstacleSphere> Movers=>movers;
    internal void BeginInspection(){HasDynamicParked=false;movers.Clear();}
    (Model Model,byte[] Pmg) Load(string path)
    {
        if(!models.TryGetValue(path,out var cached)) {
            cached=readModel?.Invoke(path)??(Model.Open(path,files),files.ReadAllBytes(Path.ChangeExtension(path,".pmg")));
            if(models.Count>=128)models.Clear();models[path]=cached;
        }
        return cached;
    }
    internal IReadOnlyList<(Part Part,Matrix4x4 Transform)> Read(string hook,Locator locator)
    {
        var result=new List<(Part,Matrix4x4)>();int count=0;
        Visit(hook,Transform(locator),RadialScale(locator),new HashSet<string>(StringComparer.Ordinal),0,result,ref count);
        return result;
    }
    void Visit(string hook,Matrix4x4 placement,double radialScale,HashSet<string> stack,int depth,List<(Part,Matrix4x4)> result,ref int count)
    {
        if(depth>4||++count>64||!stack.Add(hook))throw new InvalidDataException("Zyklische oder zu tiefe Maut-Untermodelle: "+hook);
        try {
            if(TollModelEvidence.IsNonSolidReference(hook,nonSolid,units))return;
            if(!units.TryGetValue(hook,out var unit))throw new InvalidDataException("Unbekannte Untermodell-Definition: "+hook);
            if(unit.Class=="parked_vehicle") {
                if(!IsDynamicParked(unit))throw new InvalidDataException("Ungeprüfte physische Parkfahrzeug-Definition: "+hook);
                HasDynamicParked=true;
                return;
            }
            if(unit.Class=="mover_hookup") {
                moverEvidence??=new(files,Load);
                var sphere=moverEvidence.Envelope(unit);
                movers.Add(new(Vector3.Transform(sphere.Center,placement),(float)(sphere.Radius*radialScale+.01)));
                return;
            }
            if(unit.Class is "flare" or "flare_lamp" or "flare_blink") {
                var flarePaths=Paths(unit);
                if(flarePaths.Length!=1)throw new InvalidDataException("Unbekannte Flare-Modellliste");
                var flare=Load(flarePaths[0]).Model;
                if(!FlareEvidence.Validate(unit,flare,flarePaths[0],files))throw new InvalidDataException("Ungeprüfte Flare-Render-/Kollisionsdaten: "+hook);
                return;
            }
            if(unit.Class!="lod_model_hookup"||unit.Attributes.Keys.Any(k=>k is not ("model" or "lod_dist")))
                throw new InvalidDataException("Nicht unterstützte Untermodell-Definition: "+hook);
            var paths=Paths(unit);
            foreach(string path in paths) {
                var cached=Load(path);
                var model=cached.Model;
                if(model.Skeleton.Count!=0||model.Parts.Count==0||model.Variants.Count==0||model.Variants.Any(v=>
                    v.Attributes.Count!=model.Parts.Count||v.Attributes.Any(a=>a.Tag.ToString()!="visible"||a.Value>1)))
                    throw new InvalidDataException("Animiertes oder unbekanntes Untermodell: "+path);
                // All declared LODs and every possibly visible variant are
                // included. No arbitrary selection can hide a lower beam.
                var active=model.Parts.Where((p,i)=>model.Variants.Any(v=>v.Attributes[i].Value==1));
                foreach(var part in active) {
                    result.Add((part,placement));
                    foreach(var child in part.Locators) {
                        if(child.HookupOffset==-1)continue;
                        string? name=TollModelEvidence.ReadHookup(cached.Pmg,child.HookupOffset);
                        if(name==""||name!=null&&TollModelEvidence.IsNonSolidReference(name,nonSolid,units))continue;
                        if(name!=null&&units.TryGetValue(name,out var childUnit)&&TollDefinition.Read(childUnit,false).Known)continue;
                        if(name==null)throw new InvalidDataException("Ungültiger Untermodell-Hookup");
                        Visit(name,Transform(child)*placement,RadialScale(child)*radialScale,stack,depth+1,result,ref count);
                    }
                }
            }
        } finally {stack.Remove(hook);}
    }
    internal static bool IsDynamicParked(Unit unit)
    {
        if(unit.Class!="parked_vehicle"||unit.Attributes.Keys.Any(k=>k is not ("allowed_vehicle" or "low_poly_only" or "probability_day" or "probability_night" or "always_visible"))||
            !unit.Attributes.TryGetValue("low_poly_only",out var low)||(object?)low is not false||
            !unit.Attributes.TryGetValue("allowed_vehicle",out var allowed)||(object?)allowed is string||
            (object?)allowed is not IEnumerable sequence)return false;
        var vehicles=sequence.Cast<object>().Select(v=>v switch {string s=>s,Token t=>t.ToString(),_=>null}).ToArray();
        if(vehicles.Length is <1 or >2||vehicles.Any(v=>v is not ("car" or "truck"))||vehicles.Distinct(StringComparer.Ordinal).Count()!=vehicles.Length)return false;
        if(unit.Attributes.TryGetValue("always_visible",out var visible)&&(object?)visible is not bool)return false;
        foreach(string key in new[]{"probability_day","probability_night"}) {
            // Probability is optional spawn metadata. No value, including an
            // omitted default or zero, can remove the current scene requirement.
            if(!unit.Attributes.TryGetValue(key,out var raw))continue;
            double value=(object?)raw switch {int n=>n,uint n=>n,float n=>n,double n=>n,_=>double.NaN};
            if(!double.IsFinite(value)||value<0||value>1)return false;
        }
        // Even probability zero is not an absence proof; retain the dynamic
        // scene requirement whenever this typed spawn appears in active parts.
        return true;
    }
    internal static string[] Paths(Unit unit)
    {
        if(!unit.Attributes.TryGetValue("model",out var value))throw new InvalidDataException("Untermodell ohne Modellliste");
        object? raw=value;
        var items=raw is string one?new object[]{one}:raw is IEnumerable many?many.Cast<object>().ToArray():[];
        if(items.Length is <1 or >8||items.Any(v=>v is not string))throw new InvalidDataException("Ungültige Untermodell-Liste");
        var paths=items.Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        if(paths.Any(p=>!p.StartsWith('/')||!p.EndsWith(".pmd",StringComparison.Ordinal)||p.Contains("..")||p.Contains('\\')))
            throw new InvalidDataException("Ungültiger Untermodell-Pfad");
        return paths;
    }
    internal static Matrix4x4 Transform(Locator locator)
    {
        var p=locator.Position;var q=locator.Rotation;float norm=q.LengthSquared();
        if(!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)||!float.IsFinite(norm)||Math.Abs(norm-1)>.01||
            !float.IsFinite(locator.Scale)||locator.Scale<=0||locator.Scale>100)
            throw new InvalidDataException("Ungültige Untermodell-Transformation");
        return Matrix4x4.CreateScale(locator.Scale)*Matrix4x4.CreateFromQuaternion(q)*Matrix4x4.CreateTranslation(p);
    }
    internal static double RadialScale(Locator locator)
    {
        _=Transform(locator); // preserve the exact supported transform contract
        // For the raw quaternion matrix used by System.Numerics,
        // R(q)=(1-|q|²)I+|q|²R(q/|q|). Its operator norm is at most
        // 1+2max(0,|q|²-1). Unit rotations preserve spheres exactly; only
        // uniform scale and accepted quaternion roundoff enlarge a radius.
        return locator.Scale*(1+2*Math.Max(0,(double)locator.Rotation.LengthSquared()-1))*(1+0.000001);
    }
}
