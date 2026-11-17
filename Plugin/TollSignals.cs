using System.Numerics;
using System.Reflection;
using ETS2LA.Game.Data;
using ETS2LA.Game.Utils;
using TruckLib.Models.Ppd;

namespace MautAssistent;

internal static class TollSignals
{
    static readonly MethodInfo clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic)!;
    static readonly object groupSync=new();
    static readonly Dictionary<(int Id,Vector3 Anchor),Vector3[][]> liveGroups=[];
    static readonly Queue<(int Id,Vector3 Anchor)> groupOrder=[];

    internal static bool SameBooth(NavCurve a,NavCurve b)
    {
        var da=a.EndPosition-a.StartPosition;var db=b.EndPosition-b.StartPosition;
        return da.LengthSquared()>0 && db.LengthSquared()>0 &&
            Vector3.Dot(Vector3.Normalize(da),Vector3.Normalize(db))>.95 &&
            Vector3.Distance(a.StartPosition,b.StartPosition)<=1 && Vector3.Distance(a.EndPosition,b.EndPosition)<=1;
    }

    internal static IEnumerable<ParsedSemaphore> ForPath(PrefabPath input)
    {
        var path=PrefabRefinement.OriginalFor(input);
        var descriptor=path.Prefab.Descriptor;
        if(descriptor==null)yield break;
        var booths=new List<List<NavCurve>>();
        foreach(var c in descriptor.NavCurves.Where(c=>c.SemaphoreId>=0)) {
            var booth=booths.FirstOrDefault(g=>g[0].SemaphoreId==c.SemaphoreId&&g.All(other=>SameBooth(c,other)));
            if(booth==null)booths.Add([c]);else booth.Add(c);
        }
        Vector3[] Points(List<NavCurve> curves)=>curves.SelectMany(c=> {
            int count=Math.Clamp((int)Math.Ceiling(c.Length*2),4,600);
            return Enumerable.Range(0,count+1).Select(i=>PrefabUtils.InterpolateNavCurveOriented(c,(float)i/count).Position);
        }).ToArray();
        var samples=booths.Select(Points).ToArray();var emitted=new HashSet<int>();
        foreach(var curve in path.Curves.Where(c=>c.SemaphoreId>=0))
        {
            int booth=booths.FindIndex(g=>g.Contains(curve));
            if(booth<0||!emitted.Add(booth))continue;
            var candidates=descriptor.Semaphores.Where(s=>s.SemaphoreId==(uint)curve.SemaphoreId).Select(s=> {
                var near=booths.Select((g,i)=>(Index:i,Distance:g[0].SemaphoreId==curve.SemaphoreId?
                    samples[i].Min(p=>Vector3.Distance(p,s.Position)):float.PositiveInfinity)).OrderBy(x=>x.Distance).ToArray();
                bool unique=near[0].Distance<=4&&(near.Length==1||near[1].Distance-near[0].Distance>=.5);
                return (Signal:s,Distance:near[0].Distance,Booth:unique?near[0].Index:-1);
            }).Where(s=>s.Booth==booth).OrderBy(s=>s.Distance).ThenBy(s=>s.Signal.Position.X)
                .ThenBy(s=>s.Signal.Position.Y).ThenBy(s=>s.Signal.Position.Z).ToArray();
            if(candidates.Length==0)continue;
            var forward=Vector3.Normalize(curve.EndPosition-curve.StartPosition);
            if(!float.IsFinite(forward.X)||!float.IsFinite(forward.Y)||!float.IsFinite(forward.Z))continue;
            var along=candidates.Select(c=>Vector3.Dot(c.Signal.Position,forward)).ToArray();
            // Twin arms across one wide booth share a longitudinal plane.
            // Serial gates, different levels and distant reused IDs are not merged.
            if(along.Max()-along.Min()>1||candidates.Max(c=>c.Signal.Position.Y)-candidates.Min(c=>c.Signal.Position.Y)>1||
                candidates.Any(a=>candidates.Any(b=>Vector3.Distance(a.Signal.Position,b.Signal.Position)>9)))continue;
            var mapped=candidates.Select(c=>Create(path,curve,c.Signal)).ToArray();
            var components=new List<List<Vector3>>();
            foreach(var signal in mapped) {
                var point=signal.GetWorldOrientedPoint().Position;
                var component=components.FirstOrDefault(g=>g.All(p=>Vector3.Distance(p,point)<=1));
                if(component==null)components.Add([point]);else component.Add(point);
            }
            var chosen=mapped[0];var anchor=chosen.GetWorldOrientedPoint().Position;
            Remember((curve.SemaphoreId,anchor),components.Select(g=>g.ToArray()).ToArray());
            yield return chosen;
        }
    }

    static ParsedSemaphore Create(PrefabPath path,NavCurve curve,TruckLib.Models.Ppd.Semaphore signal)
    {
        var descriptor=(PrefabDescriptor)clone.Invoke(path.Prefab.Descriptor,null)!;descriptor.Semaphores=[signal];
        var prefab=(ParsedPrefab)clone.Invoke(path.Prefab,null)!;prefab.Descriptor=descriptor;
        var local=(PrefabPath)clone.Invoke(path,null)!;local.Prefab=prefab;local.Curves=[curve];
        return local.GetSemaphores().Single();
    }

    static void Remember((int Id,Vector3 Anchor) key,Vector3[][] components)
    {
        lock(groupSync) {
            if(!liveGroups.ContainsKey(key)) {
                while(liveGroups.Count>=128)liveGroups.Remove(groupOrder.Dequeue());
                groupOrder.Enqueue(key);
            }
            liveGroups[key]=components;
        }
    }

    internal static Vector3[][]? SnapshotComponents(int id,Vector3 anchor)
    {
        lock(groupSync)return liveGroups.TryGetValue((id,anchor),out var components)?components.Select(c=>c.ToArray()).ToArray():null;
    }

    internal static ETS2LA.Game.SDK.Semaphore? ReadLive(ETS2LA.Game.SDK.Semaphore[] samples,int id,Vector3 anchor)
    {
        Vector3[][]? components;
        lock(groupSync)components=liveGroups.GetValueOrDefault((id,anchor));
        if(components==null) {
            // Losing bounded map metadata must not silently turn a twin-arm
            // passage into a single-arm opening proof.
            return samples.Any(s=>s.type==ETS2LA.Game.SDK.SemaphoreType.GATE&&s.id==id&&Vector3.Distance(s.GetWorldCoordinates(),anchor)<=1)
                ?new(){id=id,position=anchor,type=ETS2LA.Game.SDK.SemaphoreType.GATE,state=-1}:null;
        }
        var states=new List<int>();int found=0;
        foreach(var component in components) {
            var matches=samples.Where(s=>s.type==ETS2LA.Game.SDK.SemaphoreType.GATE&&s.id==id&&
                component.Any(p=>Vector3.Distance(s.GetWorldCoordinates(),p)<=1)).ToArray();
            found+=matches.Length;
            int value=matches.Length>0?matches[0].state:-1;
            if(value is <0 or >3||matches.Any(s=>s.state!=value))value=-1;
            states.Add(value);
        }
        if(found==0)return null;
        int state=states.Any(s=>s<0)?-1:states.All(s=>s==3)?3:states.Any(s=>s==0)?0:states.Any(s=>s==1)?1:2;
        return new() {id=id,position=anchor,type=ETS2LA.Game.SDK.SemaphoreType.GATE,state=state};
    }
}
